from __future__ import annotations

import base64
import binascii
import os
from decimal import Decimal
from functools import lru_cache
from typing import Annotated, Literal
from uuid import UUID

from fastapi import Depends, FastAPI, Header, HTTPException, status
from pydantic import BaseModel, ConfigDict, Field

from packages.contracts.generated.python.action import ActionEnvelope
from services.api.app.auth import OwnerPrincipal, require_owner
from services.api.app.budget import BudgetExceeded, BudgetLimits
from services.api.app.google_provider import (
    GoogleCloudModelProvider,
    OwnerConfirmationRequired,
    ProposalUnavailable,
)
from services.api.app.persistent_budget import SQLiteBudgetLedger
from services.api.app.storage import IdempotencyConflict, SQLiteTaskStore, StoredTask


app = FastAPI(
    title="Autobots by Origin Studios API",
    version="0.2.0",
    description="Owner-only control plane. The local supervisor remains authoritative for all desktop input.",
)

class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


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


class ActionProposalRequest(StrictModel):
    device_id: UUID
    observation_id: UUID
    lease_id: UUID
    epoch: int = Field(ge=0)
    sequence: int = Field(ge=1)
    image_mime_type: Literal["image/png", "image/jpeg"]
    image_base64: str = Field(min_length=16, max_length=4_000_000)


class ActionProposalResponse(StrictModel):
    status: Literal["proposal", "completed", "needs_input"]
    proposal: ActionEnvelope | None
    completion_message: str | None = None
    model_id: str
    input_tokens: int
    output_tokens: int
    actual_cost_usd: str
    execution: Literal["not_executed"] = "not_executed"


def _budget_limits() -> BudgetLimits:
    return BudgetLimits(
        per_task_usd=Decimal(os.getenv("AUTOBOTS_MAX_MODEL_COST_USD_PER_TASK", "0.25")),
        per_day_usd=Decimal(os.getenv("AUTOBOTS_MAX_MODEL_COST_USD_PER_DAY", "0.50")),
        per_month_usd=Decimal(os.getenv("AUTOBOTS_MAX_MODEL_COST_USD_PER_MONTH", "20.00")),
    )


store = SQLiteTaskStore()
budget_ledger = SQLiteBudgetLedger(store.path, _budget_limits())


def _current_owner(authorization: Annotated[str | None, Header()] = None) -> OwnerPrincipal:
    return require_owner(authorization)


@lru_cache(maxsize=1)
def _live_provider() -> GoogleCloudModelProvider:
    project = os.getenv("GOOGLE_CLOUD_PROJECT", "").strip()
    if not project:
        raise HTTPException(status_code=503, detail={"code": "model_project_not_configured"})
    return GoogleCloudModelProvider(
        project=project,
        location=os.getenv("GOOGLE_CLOUD_LOCATION", "global"),
        model_id=os.getenv("AUTOBOTS_MODEL_ID", "gemini-3.5-flash-lite"),
    )


@app.get("/healthz", tags=["health"])
async def health() -> dict[str, str]:
    live = os.getenv("AUTOBOTS_LIVE_AI_ENABLED", "false").lower() == "true"
    return {
        "status": "ok",
        "service": "autobots-api",
        "mode": "live" if live else "mock",
        "native_input": "local-device-only",
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
    if os.getenv("AUTOBOTS_LIVE_AI_ENABLED", "false").lower() != "true":
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
        reservation = budget_ledger.reserve(task_id, Decimal("0.01"))
    except BudgetExceeded as error:
        raise HTTPException(status_code=429, detail={"code": "model_budget_exhausted"}) from error

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
        )
    except OwnerConfirmationRequired as error:
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        store.record_event(principal.subject, task_id, "provider_confirmation_required", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=409, detail={"code": "owner_confirmation_required"}) from error
    except ProposalUnavailable as error:
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        store.record_event(principal.subject, task_id, "proposal_rejected", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=422, detail={"code": "proposal_unavailable"}) from error
    except HTTPException:
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        raise
    except Exception as error:
        if reservation:
            budget_ledger.reconcile(reservation, Decimal("0.01"))
        store.record_event(principal.subject, task_id, "provider_error", {"epoch": request.epoch, "sequence": request.sequence})
        raise HTTPException(status_code=502, detail={"code": "model_provider_error"}) from error

    if reservation:
        budget_ledger.reconcile(reservation, result.cost_usd)
    latest_task = store.get_task(principal.subject, task_id)
    if latest_task is None or latest_task.status not in {"queued", "active"} or latest_task.epoch != request.epoch:
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
        model_id=result.model_id,
        input_tokens=result.input_tokens,
        output_tokens=result.output_tokens,
        actual_cost_usd=str(result.cost_usd),
    )


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
