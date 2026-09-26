from __future__ import annotations

import base64
import binascii
from collections import deque
import logging
import os
from threading import Lock
import time
from decimal import Decimal
from functools import lru_cache
from typing import Annotated, Literal
from uuid import UUID, uuid4

from fastapi import Depends, FastAPI, Header, HTTPException, Request, status
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, ConfigDict, Field

from packages.contracts.generated.python.action import ActionEnvelope
from services.api.app.auth import OwnerPrincipal, require_member, require_owner
from services.api.app.cognito_invites import InviteUnavailable, disable_user, invite
from services.api.app.pilots import PilotApplicationStore, PilotStore
from services.api.app.budget import BudgetExceeded, BudgetLimits
from services.api.app.google_provider import (
    GoogleCloudModelProvider,
    OwnerConfirmationRequired,
    ProposalUnavailable,
    StepRecord,
)
from services.api.app.persistent_budget import SQLiteBudgetLedger
from services.api.app.speech import GeminiTranscriber, InvalidAudio, inspect_wave
from services.api.app.storage import IdempotencyConflict, SQLiteTaskStore, StoredTask
from services.api.app.usage import UsageStore, aws_billing


API_VERSION = "0.6.0"
# Advertised so the desktop client can detect an older deployed API and omit newer request fields.
API_FEATURES = ("step-history", "desktop-actions-v2", "transcription", "usage-reporting", "calendar-field-observation", "invite-only-pilots", "pilot-applications", "pilot-revocation")

app = FastAPI(
    title="Autobots by Origin Studios API",
    version=API_VERSION,
    description="Invite-only control plane. The local supervisor remains authoritative for all desktop input.",
)
app.add_middleware(CORSMiddleware, allow_origins=["https://autobots.origin-studio.in"],
                   allow_methods=["GET", "POST"], allow_headers=["Authorization", "Content-Type"],
                   allow_credentials=False)
logger = logging.getLogger(__name__)

class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class PilotInviteRequest(StrictModel):
    email: str = Field(min_length=5, max_length=254, pattern=r"^[^\s@,]+@[^\s@,]+\.[^\s@,]+$")


class PilotProfileRequest(StrictModel):
    username: str = Field(min_length=2, max_length=60)
    purpose: str = Field(min_length=10, max_length=500)


class PilotApplicationRequest(StrictModel):
    email: str = Field(min_length=5, max_length=254, pattern=r"^[^\s@,]+@[^\s@,]+\.[^\s@,]+$")
    name: str = Field(min_length=2, max_length=80)
    profession: str = Field(min_length=2, max_length=80)
    industry: str = Field(min_length=2, max_length=80)
    purpose: str = Field(min_length=10, max_length=500)
    website: str = Field(default="", max_length=200)


class DeviceEnrollmentRequest(StrictModel):
    device_id: UUID
    display_name: str = Field(min_length=1, max_length=80)
    platform: Literal["windows", "macos", "linux"]


class DeviceEnrollmentResponse(StrictModel):
    device_id: UUID
    enrolled: bool


class CreateTaskRequest(StrictModel):
    device_id: UUID
    instruction: str = Field(min_length=1, max_length=4000)


class TaskResponse(StrictModel):
    task_id: UUID
    device_id: UUID
    instruction: str
    status: str
    epoch: int
    created_at: str
    updated_at: str


class StepHistoryEntry(StrictModel):
    """A step already handled by the enrolled device, reported as untrusted context for the model."""

    step: int = Field(ge=1, le=1000)
    action: str = Field(min_length=1, max_length=300)
    outcome: Literal["executed", "rejected", "failed", "owner_resumed", "verified"]
    detail: str | None = Field(default=None, max_length=300)


class CalendarFieldObservation(StrictModel):
    """Only four date/time values from the active Calendar editor; untrusted observation data."""

    start_date: str = Field(min_length=1, max_length=64)
    start_time: str = Field(min_length=1, max_length=64)
    end_date: str = Field(min_length=1, max_length=64)
    end_time: str = Field(min_length=1, max_length=64)


class ActionProposalRequest(StrictModel):
    device_id: UUID
    observation_id: UUID
    lease_id: UUID
    epoch: int = Field(ge=0)
    sequence: int = Field(ge=1)
    image_mime_type: Literal["image/png", "image/jpeg"]
    image_base64: str = Field(min_length=16, max_length=4_000_000)
    history: list[StepHistoryEntry] = Field(default_factory=list, max_length=15)
    foreground_title: str | None = Field(default=None, max_length=300)
    calendar_fields: CalendarFieldObservation | None = None


class ActionProposalResponse(StrictModel):
    status: Literal["proposal", "completed", "needs_input"]
    proposal: ActionEnvelope | None
    completion_message: str | None = None
    intent: str | None = None
    model_id: str
    input_tokens: int
    output_tokens: int
    actual_cost_usd: str
    execution: Literal["not_executed"] = "not_executed"


class TranscriptionRequest(StrictModel):
    device_id: UUID
    audio_mime_type: Literal["audio/wav"]
    audio_base64: str = Field(min_length=64, max_length=3_000_000)
    language_hint: str | None = Field(default=None, max_length=40, pattern=r"^[A-Za-z][A-Za-z ,()-]*$")


class TranscriptionResponse(StrictModel):
    status: Literal["transcribed", "no_speech"]
    transcript: str | None
    audio_seconds: float
    model_id: str
    input_tokens: int
    output_tokens: int
    actual_cost_usd: str


def _budget_limits() -> BudgetLimits:
    return BudgetLimits(
        per_task_usd=Decimal(os.getenv("AUTOBOTS_MAX_MODEL_COST_USD_PER_TASK", "1.00")),
        per_day_usd=Decimal(os.getenv("AUTOBOTS_MAX_MODEL_COST_USD_PER_DAY", "5.00")),
        per_month_usd=Decimal(os.getenv("AUTOBOTS_MAX_MODEL_COST_USD_PER_MONTH", "100.00")),
    )


def _speech_budget_limits() -> BudgetLimits:
    return BudgetLimits(
        per_task_usd=Decimal(os.getenv("AUTOBOTS_MAX_SPEECH_COST_USD_PER_REQUEST", "0.01")),
        per_day_usd=Decimal(os.getenv("AUTOBOTS_MAX_SPEECH_COST_USD_PER_DAY", "0.10")),
        per_month_usd=Decimal(os.getenv("AUTOBOTS_MAX_SPEECH_COST_USD_PER_MONTH", "2.00")),
    )


store = SQLiteTaskStore()
budget_ledger = SQLiteBudgetLedger(store.path, _budget_limits())
speech_ledger = SQLiteBudgetLedger(store.path, _speech_budget_limits(), table="speech_reservations")
pilots = PilotStore(store.path)
pilot_applications = PilotApplicationStore(store.path)
_SPEECH_RESERVATION_USD = Decimal("0.002")
_PILOT_LIFETIME_USD = Decimal("10.00")
_application_lock = Lock()
_application_attempts: dict[str, deque[float]] = {}


def _limit_public_application(client_host: str) -> None:
    """Bound anonymous form traffic without storing addresses in the database or logs."""
    now = time.monotonic()
    with _application_lock:
        if len(_application_attempts) > 2048:
            for key, entries in list(_application_attempts.items()):
                if not entries or entries[-1] < now - 3600:
                    del _application_attempts[key]
        if client_host not in _application_attempts and len(_application_attempts) >= 4096:
            raise HTTPException(status_code=429, detail={"code": "application_rate_limited"})
        recent = _application_attempts.setdefault(client_host, deque())
        while recent and recent[0] < now - 3600:
            recent.popleft()
        if len(recent) >= 5:
            raise HTTPException(status_code=429, detail={"code": "application_rate_limited"})
        recent.append(now)


def _pilot_access(subject: str) -> dict:
    profile = pilots.get(subject)
    if not profile:
        raise HTTPException(status_code=403, detail={"code": "pilot_not_invited"})
    if not profile["enabled"]:
        raise HTTPException(status_code=403, detail={"code": "pilot_revoked"})
    return profile


def _current_owner(authorization: Annotated[str | None, Header()] = None) -> OwnerPrincipal:
    principal = require_member(authorization)
    if principal.role == "pilot":
        profile = _pilot_access(principal.subject)
        if not profile["onboarded_at"]:
            raise HTTPException(status_code=403, detail={"code": "pilot_profile_required"})
    return principal


def _current_member(authorization: Annotated[str | None, Header()] = None) -> OwnerPrincipal:
    return require_member(authorization)


def _current_admin(authorization: Annotated[str | None, Header()] = None) -> OwnerPrincipal:
    return require_owner(authorization)


@app.get("/v1/pilot/me", tags=["pilot"])
async def pilot_me(principal: Annotated[OwnerPrincipal, Depends(_current_member)]) -> dict:
    if principal.role == "owner":
        return {"role": "owner", "subject": principal.subject}
    profile = _pilot_access(principal.subject)
    return {"role": "pilot", "profile": profile,
            "lifetime_limit_usd": str(_PILOT_LIFETIME_USD),
            "lifetime_used_usd": str(budget_ledger.lifetime_spend(principal.subject))}


@app.post("/v1/pilot/applications", tags=["pilot"], status_code=202)
async def apply_for_pilot(request: PilotApplicationRequest, browser: Request) -> dict:
    _limit_public_application(browser.client.host if browser.client else "unknown")
    if request.website:
        return {"status": "received"}
    fields = (request.name.strip(), request.profession.strip(), request.industry.strip(), request.purpose.strip())
    if any(len(value) < (10 if index == 3 else 2) for index, value in enumerate(fields)):
        raise HTTPException(status_code=422, detail={"code": "application_fields_required"})
    pilot_applications.submit(request.email.strip().lower(), *fields)
    # The same response for new and existing emails avoids account enumeration.
    return {"status": "received"}


@app.get("/v1/admin/pilot-applications", tags=["admin"])
async def admin_pilot_applications(principal: Annotated[OwnerPrincipal, Depends(_current_admin)]) -> dict:
    return {"applications": pilot_applications.list()}


@app.post("/v1/admin/pilot-applications/{application_id}/approve", tags=["admin"])
async def approve_pilot_application(
    application_id: UUID, principal: Annotated[OwnerPrincipal, Depends(_current_admin)]
) -> dict:
    application = pilot_applications.get(str(application_id))
    if not application or application["status"] not in {"pending", "rejected"}:
        raise HTTPException(status_code=409, detail={"code": "application_not_pending"})
    if application["email"] == os.getenv("AUTOBOTS_OWNER_EMAIL", "").lower():
        raise HTTPException(status_code=409, detail={"code": "owner_cannot_be_invited"})
    existing = pilots.get_by_email(application["email"])
    if existing and not existing["enabled"]:
        raise HTTPException(status_code=409, detail={"code": "pilot_access_revoked"})
    if not pilot_applications.claim_approval(str(application_id), principal.subject):
        raise HTTPException(status_code=409, detail={"code": "application_not_pending"})
    if existing:
        pilot_applications.approve(str(application_id), existing["subject"])
        return {"status": "approved", "email": application["email"], "invitation": "already_invited"}
    try:
        subject = await invite(application["email"])
        pilots.add(subject, application["email"])
        pilot_applications.approve(str(application_id), subject)
    except Exception as error:
        # The external invitation might have succeeded. Keep 'approving' for manual
        # review instead of retrying a possibly completed non-idempotent email.
        logger.error("pilot_approval_uncertain class=%s", type(error).__name__)
        raise HTTPException(status_code=503, detail={"code": "application_approval_uncertain"}) from error
    return {"status": "approved", "email": application["email"], "invitation": "sent"}


@app.post("/v1/admin/pilot-applications/{application_id}/reject", tags=["admin"])
async def reject_pilot_application(
    application_id: UUID, principal: Annotated[OwnerPrincipal, Depends(_current_admin)]
) -> dict:
    if not pilot_applications.reject(str(application_id), principal.subject):
        raise HTTPException(status_code=409, detail={"code": "application_not_pending"})
    return {"status": "rejected"}


@app.post("/v1/admin/pilots/{subject}/revoke", tags=["admin"])
async def revoke_pilot_access(
    subject: UUID, principal: Annotated[OwnerPrincipal, Depends(_current_admin)]
) -> dict:
    if str(subject) == principal.subject:
        raise HTTPException(status_code=403, detail={"code": "owner_cannot_be_revoked"})
    profile = pilots.revoke(str(subject))
    if not profile:
        raise HTTPException(status_code=404, detail={"code": "pilot_not_found"})
    stopped_tasks = store.stop_all_owner_tasks(str(subject))
    try:
        await disable_user(profile["email"])
        pilots.mark_cognito_disabled(str(subject))
        provider_signout = "completed"
    except InviteUnavailable as error:
        logger.error("pilot_cognito_disable_pending class=%s", type(error).__name__)
        provider_signout = "pending"
    return {"status": "revoked", "provider_signout": provider_signout, "stopped_tasks": stopped_tasks}


@app.post("/v1/pilot/profile", tags=["pilot"])
async def pilot_profile(request: PilotProfileRequest,
                        principal: Annotated[OwnerPrincipal, Depends(_current_member)]) -> dict:
    if len(request.username.strip()) < 2 or len(request.purpose.strip()) < 10:
        raise HTTPException(status_code=422, detail={"code": "pilot_profile_invalid"})
    if principal.role != "pilot" or not pilots.onboard(principal.subject, request.username, request.purpose):
        raise HTTPException(status_code=403, detail={"code": "pilot_profile_unavailable"})
    return {"status": "complete"}


@app.get("/v1/admin/pilots", tags=["admin"])
async def admin_pilots(principal: Annotated[OwnerPrincipal, Depends(_current_admin)]) -> dict:
    return {"pilots": [{**profile, "lifetime_limit_usd": str(_PILOT_LIFETIME_USD),
                        "lifetime_used_usd": str(budget_ledger.lifetime_spend(profile["subject"]))}
                       for profile in pilots.list()]}


@app.post("/v1/admin/pilots", tags=["admin"], status_code=201)
async def admin_invite_pilot(request: PilotInviteRequest,
                             principal: Annotated[OwnerPrincipal, Depends(_current_admin)]) -> dict:
    email = request.email.strip().lower()
    if email == os.getenv("AUTOBOTS_OWNER_EMAIL", "").lower():
        raise HTTPException(status_code=409, detail={"code": "owner_cannot_be_invited"})
    if any(profile["email"] == email for profile in pilots.list()):
        raise HTTPException(status_code=409, detail={"code": "pilot_already_invited"})
    try:
        subject = await invite(email)
        pilots.add(subject, email)
    except InviteUnavailable as error:
        raise HTTPException(status_code=503, detail={"code": "invitation_unavailable"}) from error
    except Exception as error:
        logger.error("pilot_registration_failed class=%s", type(error).__name__)
        raise HTTPException(status_code=503, detail={"code": "invitation_record_unavailable"}) from error
    return {"email": email, "status": "invited"}


@lru_cache(maxsize=1)
def _live_provider() -> GoogleCloudModelProvider:
    project = os.getenv("GOOGLE_CLOUD_PROJECT", "").strip()
    if not project:
        raise HTTPException(status_code=503, detail={"code": "model_project_not_configured"})
    return GoogleCloudModelProvider(
        project=project,
        location=os.getenv("GOOGLE_CLOUD_LOCATION", "global"),
        model_id=os.getenv("AUTOBOTS_MODEL_ID", "gemini-3.5-flash-lite"),
        thinking_level=os.getenv("AUTOBOTS_MODEL_THINKING_LEVEL", "LOW"),
    )


@lru_cache(maxsize=1)
def _live_transcriber() -> GeminiTranscriber:
    project = os.getenv("GOOGLE_CLOUD_PROJECT", "").strip()
    if not project:
        raise HTTPException(status_code=503, detail={"code": "model_project_not_configured"})
    return GeminiTranscriber(
        project=project,
        location=os.getenv("GOOGLE_CLOUD_LOCATION", "global"),
        model_id=os.getenv("AUTOBOTS_TRANSCRIPTION_MODEL_ID", os.getenv("AUTOBOTS_MODEL_ID", "gemini-3.5-flash-lite")),
    )


def _live_ai_enabled() -> bool:
    return os.getenv("AUTOBOTS_LIVE_AI_ENABLED", "false").lower() == "true"


@app.get("/healthz", tags=["health"])
async def health() -> dict[str, object]:
    return {
        "status": "ok",
        "service": "autobots-api",
        "mode": "live" if _live_ai_enabled() else "mock",
        "native_input": "local-device-only",
        "api_version": API_VERSION,
        "features": list(API_FEATURES),
    }


@app.post("/v1/devices/enroll", tags=["devices"], response_model=DeviceEnrollmentResponse)
async def enroll_device(
    request: DeviceEnrollmentRequest,
    principal: Annotated[OwnerPrincipal, Depends(_current_owner)],
) -> DeviceEnrollmentResponse:
    enrolled = store.enroll_device(principal.subject, request.device_id, request.display_name, request.platform)
    if not enrolled:
        raise HTTPException(status_code=409, detail={"code": "device_owned_by_another_user"})
    return DeviceEnrollmentResponse(device_id=request.device_id, enrolled=True)


@app.post("/v1/tasks", tags=["tasks"], response_model=TaskResponse)
async def create_task(
    request: CreateTaskRequest,
    principal: Annotated[OwnerPrincipal, Depends(_current_owner)],
    idempotency_key: Annotated[str | None, Header(alias="Idempotency-Key")] = None,
) -> TaskResponse:
    if not idempotency_key or len(idempotency_key) > 128:
        raise HTTPException(status_code=400, detail={"code": "idempotency_key_required"})
    if not store.owns_device(principal.subject, request.device_id):
        raise HTTPException(status_code=403, detail={"code": "device_not_enrolled"})
    try:
        task, _created = store.create_task(principal.subject, request.device_id, request.instruction, idempotency_key)
    except IdempotencyConflict as error:
        raise HTTPException(status_code=409, detail={"code": "idempotency_conflict"}) from error
    if principal.role == "pilot":
        try:
            _pilot_access(principal.subject)
        except HTTPException:
            store.stop_task(principal.subject, task.task_id)
            raise
    return _task_response(task)


@app.get("/v1/tasks/{task_id}", tags=["tasks"], response_model=TaskResponse)
async def get_task(
    task_id: UUID,
    principal: Annotated[OwnerPrincipal, Depends(_current_owner)],
) -> TaskResponse:
    task = store.get_task(principal.subject, task_id)
    if task is None:
        raise HTTPException(status_code=404, detail={"code": "task_not_found"})
    return _task_response(task)


@app.post("/v1/tasks/{task_id}/stop", tags=["tasks"], response_model=TaskResponse)
async def stop_task(
    task_id: UUID,
    principal: Annotated[OwnerPrincipal, Depends(_current_owner)],
) -> TaskResponse:
    task = store.stop_task(principal.subject, task_id)
    if task is None:
        raise HTTPException(status_code=404, detail={"code": "task_not_found"})
    return _task_response(task)


@app.post("/v1/tasks/{task_id}/proposals", tags=["tasks"], response_model=ActionProposalResponse)
async def propose_action(
    task_id: UUID,
    request: ActionProposalRequest,
    principal: Annotated[OwnerPrincipal, Depends(_current_owner)],
) -> ActionProposalResponse:
    if not _live_ai_enabled():
        raise HTTPException(status_code=503, detail={"code": "live_inference_disabled"})
    task = store.get_task(principal.subject, task_id)
    if task is None:
        raise HTTPException(status_code=404, detail={"code": "task_not_found"})
    if task.status not in {"queued", "active"} or task.device_id != request.device_id:
        raise HTTPException(status_code=409, detail={"code": "task_state_or_device_mismatch"})
    if request.observation_id.int == 0 or request.lease_id.int == 0:
        raise HTTPException(status_code=422, detail={"code": "observation_and_lease_ids_required"})
    try:
        image = base64.b64decode(request.image_base64, validate=True)
    except (ValueError, binascii.Error) as error:
        raise HTTPException(status_code=422, detail={"code": "invalid_image_encoding"}) from error
    if not image or len(image) > 3_000_000 or not _image_header_matches(image, request.image_mime_type):
        raise HTTPException(status_code=422, detail={"code": "invalid_or_oversized_image"})

    reservation = None
    try:
        reservation = budget_ledger.reserve(
            task_id, Decimal("0.05") if principal.role == "pilot" else Decimal("0.01"),
            owner_sub=principal.subject,
            lifetime_limit_usd=_PILOT_LIFETIME_USD if principal.role == "pilot" else None)
    except BudgetExceeded as error:
        raise HTTPException(status_code=429, detail={"code": "model_budget_exhausted"}) from error

    usage = UsageStore(store.path)
    request_started = time.perf_counter()
    try:
        provider = _live_provider()
        result = await provider.propose_action(
            task_id=task_id,
            device_id=request.device_id,
            instruction=task.instruction,
            observation_id=request.observation_id,
            lease_id=request.lease_id,
            epoch=request.epoch,
            sequence=request.sequence,
            image_bytes=image,
            image_mime_type=request.image_mime_type,
            history=[StepRecord(entry.step, entry.action, entry.outcome, entry.detail) for entry in request.history],
            foreground_title=request.foreground_title,
            calendar_fields=request.calendar_fields.model_dump() if request.calendar_fields is not None else None,
        )
    except OwnerConfirmationRequired as error:
        usage.record(reservation, principal.subject, "desktop", "confirmation_required", request_started)
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        store.record_event(principal.subject, task_id, "provider_confirmation_required", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=409, detail={"code": "owner_confirmation_required", "reason": _safe_reason(error)}) from error
    except ProposalUnavailable as error:
        usage.record(reservation, principal.subject, "desktop", "rejected", request_started)
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        store.record_event(principal.subject, task_id, "proposal_rejected", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=422, detail={"code": "proposal_unavailable", "reason": _safe_reason(error)}) from error
    except HTTPException:
        usage.record(reservation, principal.subject, "desktop", "unavailable", request_started)
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        raise
    except Exception as error:
        # The provider error text can contain request or response content. Log only its type and
        # numeric status so a failed live run is diagnosable without exposing screen data.
        provider_status = getattr(error, "code", None)
        if not isinstance(provider_status, int):
            provider_status = getattr(error, "status_code", None)
        logger.warning("desktop_provider_error class=%s status=%s", type(error).__name__,
                       provider_status if isinstance(provider_status, int) else "unknown")
        usage.record(reservation, principal.subject, "desktop", "provider_error", request_started)
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        store.record_event(principal.subject, task_id, "provider_error", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=502, detail={"code": "model_provider_error"}) from error

    usage.record(reservation, principal.subject, "desktop", "response", request_started, result)
    if reservation:
        budget_ledger.reconcile(reservation, result.cost_usd)
    if principal.role == "pilot":
        _pilot_access(principal.subject)
    latest_task = store.get_task(principal.subject, task_id)
    # The device's lease epoch spans multiple tasks; a new server task starts at epoch zero.
    # Detect cloud STOP against the server snapshot taken before inference, while preserving the
    # caller's local epoch in the proposal for the authoritative local supervisor to validate.
    if latest_task is None or latest_task.status not in {"queued", "active"} or latest_task.epoch != task.epoch:
        store.record_event(principal.subject, task_id, "proposal_discarded_after_stop", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=409, detail={"code": "task_stopped_while_model_was_running"})
    completed = bool(getattr(result, "completed", False))
    needs_input = bool(getattr(result, "needs_input", False))
    if completed or needs_input:
        terminal_status = "needs_input" if needs_input else "completed"
        store.record_event(
            principal.subject,
            task_id,
            "task_needs_input" if needs_input else "task_completion_proposed",
            {
                "status": terminal_status,
                "model": result.model_id,
                "observation_id": str(request.observation_id),
                "epoch": request.epoch,
                "sequence": request.sequence,
                "input_tokens": result.input_tokens,
                "output_tokens": result.output_tokens,
                "cost_usd": str(result.cost_usd),
            },
        )
    else:
        if result.envelope is None:
            raise HTTPException(status_code=502, detail={"code": "model_provider_invalid_completion"})
        store.record_event(
            principal.subject,
            task_id,
            "action_proposed",
            {
                "model": result.model_id,
                "action_id": str(result.envelope.action_id),
                "action_kind": result.envelope.action.kind,
                "observation_id": str(request.observation_id),
                "epoch": request.epoch,
                "sequence": request.sequence,
                "input_tokens": result.input_tokens,
                "output_tokens": result.output_tokens,
                "cost_usd": str(result.cost_usd),
            },
        )
    return ActionProposalResponse(
        status="needs_input" if needs_input else "completed" if completed else "proposal",
        proposal=result.envelope,
        completion_message=getattr(result, "completion_message", None),
        intent=getattr(result, "intent", None),
        model_id=result.model_id,
        input_tokens=result.input_tokens,
        output_tokens=result.output_tokens,
        actual_cost_usd=str(result.cost_usd),
    )


@app.post("/v1/transcriptions", tags=["voice"], response_model=TranscriptionResponse)
async def transcribe_voice_instruction(
    request: TranscriptionRequest,
    principal: Annotated[OwnerPrincipal, Depends(_current_owner)],
) -> TranscriptionResponse:
    """Transcribes one owner push-to-talk clip. The audio is not persisted and cannot start a task by itself."""
    if not _live_ai_enabled():
        raise HTTPException(status_code=503, detail={"code": "live_inference_disabled"})
    if not store.owns_device(principal.subject, request.device_id):
        raise HTTPException(status_code=403, detail={"code": "device_not_enrolled"})
    try:
        audio = base64.b64decode(request.audio_base64, validate=True)
    except (ValueError, binascii.Error) as error:
        raise HTTPException(status_code=422, detail={"code": "invalid_audio_encoding"}) from error
    try:
        wave = inspect_wave(audio)
    except InvalidAudio as error:
        raise HTTPException(status_code=422, detail={"code": "invalid_audio", "reason": str(error)}) from error

    try:
        reservation = speech_ledger.reserve(
            uuid4(), _SPEECH_RESERVATION_USD, owner_sub=principal.subject,
            lifetime_limit_usd=_PILOT_LIFETIME_USD if principal.role == "pilot" else None)
    except BudgetExceeded as error:
        raise HTTPException(status_code=429, detail={"code": "speech_budget_exhausted"}) from error
    usage = UsageStore(store.path)
    request_started = time.perf_counter()
    try:
        result = await _live_transcriber().transcribe(
            audio_bytes=audio,
            mime_type=request.audio_mime_type,
            language_hint=request.language_hint,
        )
    except HTTPException:
        # Configuration errors are raised before any provider request, so nothing was spent.
        usage.record(reservation, principal.subject, "speech", "unavailable", request_started)
        speech_ledger.release(reservation)
        raise
    except Exception as error:
        # The provider may have billed a failed request; keep the conservative reservation.
        usage.record(reservation, principal.subject, "speech", "provider_error", request_started)
        speech_ledger.reconcile(reservation, _SPEECH_RESERVATION_USD)
        raise HTTPException(status_code=502, detail={"code": "transcription_provider_error"}) from error
    usage.record(reservation, principal.subject, "speech", "transcribed" if result.transcript else "no_speech", request_started, result)
    speech_ledger.reconcile(reservation, result.cost_usd)
    if principal.role == "pilot":
        _pilot_access(principal.subject)
    return TranscriptionResponse(
        status="transcribed" if result.transcript else "no_speech",
        transcript=result.transcript,
        audio_seconds=round(wave.duration_seconds, 2),
        model_id=result.model_id,
        input_tokens=result.input_tokens,
        output_tokens=result.output_tokens,
        actual_cost_usd=str(result.cost_usd),
    )


@app.get("/v1/usage", tags=["usage"])
async def usage_summary(principal: Annotated[OwnerPrincipal, Depends(_current_owner)]) -> dict:
    report = UsageStore(store.path).summary(principal.subject, budget_ledger, speech_ledger)
    report["billing"] = [await aws_billing(), {
        "provider": "gcp", "source": "Cloud Billing export", "scope": "Autobots GCP project",
        "status": "not_connected", "amount": None, "currency": "USD", "as_of": None,
        "note": "GCP invoice totals require a configured Cloud Billing export. Gemini request usage is tracked separately above.",
    }]
    report["other_apis"] = {"status": "none_configured", "note": "No other billable AI API provider is configured. Google Meet and WhatsApp examples use their websites, not paid API integrations."}
    return report


def _safe_reason(error: Exception) -> str:
    text = "".join(character for character in str(error) if character.isprintable())
    return text[:200]


def _image_header_matches(image: bytes, mime_type: str) -> bool:
    if mime_type == "image/png":
        return image.startswith(b"\x89PNG\r\n\x1a\n")
    if mime_type == "image/jpeg":
        return image.startswith(b"\xff\xd8\xff")
    return False


def _task_response(task: StoredTask) -> TaskResponse:
    return TaskResponse(
        task_id=task.task_id,
        device_id=task.device_id,
        instruction=task.instruction,
        status=task.status,
        epoch=task.epoch,
        created_at=task.created_at.isoformat(),
        updated_at=task.updated_at.isoformat(),
    )
