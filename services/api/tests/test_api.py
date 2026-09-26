from fastapi.testclient import TestClient
import base64
import pytest
from decimal import Decimal
from types import SimpleNamespace
from uuid import uuid4

from services.api.app import main
from services.api.app.auth import OwnerPrincipal
from services.api.app.budget import BudgetLimits
from services.api.app.persistent_budget import SQLiteBudgetLedger
from services.api.app.storage import SQLiteTaskStore
from packages.contracts.generated.python.action import ActionEnvelope, ClickAction


client = TestClient(main.app)


@pytest.mark.parametrize("stop_during_request", [False, True])
def test_successive_tasks_keep_local_epoch_and_discard_cloud_stop(monkeypatch, tmp_path, stop_during_request):
    """A device epoch spans tasks; the independent server task epoch starts at zero for each task."""
    owner = OwnerPrincipal(subject="epoch-test-owner", username="owner")
    task_store = SQLiteTaskStore(tmp_path / "epochs.db")
    monkeypatch.setattr(main, "store", task_store)
    monkeypatch.setattr(main, "budget_ledger", SQLiteBudgetLedger(
        task_store.path, BudgetLimits(Decimal("1"), Decimal("5"), Decimal("50"))))
    monkeypatch.setenv("AUTOBOTS_LIVE_AI_ENABLED", "true")
    main.app.dependency_overrides[main._current_owner] = lambda: owner
    device_id = uuid4()
    task_store.enroll_device(owner.subject, device_id, "Synthetic desktop", "windows")

    class FakeProvider:
        async def propose_action(self, **kwargs):
            if stop_during_request:
                task_store.stop_task(owner.subject, kwargs["task_id"])
            return SimpleNamespace(
                envelope=ActionEnvelope(schema_version=1, task_id=kwargs["task_id"],
                    device_id=device_id, action_id=uuid4(), observation_id=kwargs["observation_id"],
                    lease_id=kwargs["lease_id"], epoch=kwargs["epoch"], sequence=kwargs["sequence"],
                    action=ClickAction(kind="click", x=500, y=500, button="left")),
                model_id="fake", input_tokens=10, output_tokens=1, cost_usd=Decimal("0.000001"))

    monkeypatch.setattr(main, "_live_provider", FakeProvider)
    try:
        for local_epoch in (0, 1, 7):
            task, _ = task_store.create_task(owner.subject, device_id, "Synthetic long workflow", str(uuid4()))
            lease_id = uuid4()
            for sequence in range(1, 41 if not stop_during_request else 2):
                response = client.post(f"/v1/tasks/{task.task_id}/proposals", json={
                    "device_id": str(device_id), "observation_id": str(uuid4()),
                    "lease_id": str(lease_id), "epoch": local_epoch, "sequence": sequence,
                    "image_mime_type": "image/png",
                    "image_base64": base64.b64encode(b"\x89PNG\r\n\x1a\n" + b"0" * 32).decode("ascii"),
                })
                if stop_during_request:
                    assert response.status_code == 409
                    assert response.json()["detail"]["code"] == "task_stopped_while_model_was_running"
                else:
                    assert response.status_code == 200, response.json()
                    assert response.json()["proposal"]["epoch"] == local_epoch
                    assert response.json()["proposal"]["sequence"] == sequence
                    assert response.json()["execution"] == "not_executed"
            task_store.stop_task(owner.subject, task.task_id)
    finally:
        main.app.dependency_overrides.clear()


def test_health_is_available_without_cloud_credentials() -> None:
    response = client.get("/healthz")
    assert response.status_code == 200
    body = response.json()
    assert {key: body[key] for key in ("status", "service", "mode", "native_input")} == {
        "status": "ok", "service": "autobots-api", "mode": "mock", "native_input": "local-device-only",
    }
    assert body["api_version"] == main.API_VERSION
    assert {"step-history", "desktop-actions-v2", "transcription"} <= set(body["features"])


def test_device_enrollment_fails_closed_until_auth_is_configured() -> None:
    response = client.post("/v1/devices/enroll", json={
        "device_id": "9701b73b-0bd8-47f7-bce6-a07b364335ac",
        "display_name": "Test device",
        "platform": "windows",
    })
    assert response.status_code == 503
    assert response.json()["detail"]["code"] == "authentication_not_configured"


def test_task_creation_fails_closed_until_auth_is_configured() -> None:
    response = client.post("/v1/tasks", json={
        "device_id": "9701b73b-0bd8-47f7-bce6-a07b364335ac",
        "instruction": "Test task",
    })
    assert response.status_code == 503
    assert response.json()["detail"]["code"] == "authentication_not_configured"


def test_owner_task_lifecycle_uses_enrolled_device_and_idempotency(monkeypatch, tmp_path) -> None:
    owner = OwnerPrincipal(subject="test-owner", username="owner")
    original_store = main.store
    main.store = SQLiteTaskStore(tmp_path / "tasks.db")
    main.app.dependency_overrides[main._current_owner] = lambda: owner
    try:
        device_id = uuid4()
        enrolled = client.post("/v1/devices/enroll", json={
            "device_id": str(device_id), "display_name": "Test desktop", "platform": "windows",
        })
        assert enrolled.status_code == 200

        payload = {"device_id": str(device_id), "instruction": "Open the calculator"}
        headers = {"Idempotency-Key": "request-123"}
        created = client.post("/v1/tasks", json=payload, headers=headers)
        retried = client.post("/v1/tasks", json=payload, headers=headers)
        assert created.status_code == retried.status_code == 200
        assert created.json()["task_id"] == retried.json()["task_id"]

        stopped = client.post(f"/v1/tasks/{created.json()['task_id']}/stop")
        assert stopped.status_code == 200
        assert stopped.json()["status"] == "stopped"
        assert stopped.json()["epoch"] == 1
    finally:
        main.app.dependency_overrides.clear()
        main.store = original_store


def test_live_proposal_is_authenticated_budgeted_and_never_executed(monkeypatch, tmp_path) -> None:
    owner = OwnerPrincipal(subject="test-owner", username="owner")
    original_store = main.store
    original_budget = main.budget_ledger
    main.store = SQLiteTaskStore(tmp_path / "live.db")
    main.budget_ledger = SQLiteBudgetLedger(
        main.store.path,
        BudgetLimits(Decimal("1.00"), Decimal("5.00"), Decimal("50.00")),
    )
    main.app.dependency_overrides[main._current_owner] = lambda: owner
    monkeypatch.setenv("AUTOBOTS_LIVE_AI_ENABLED", "true")
    device_id = uuid4()
    task_id = uuid4()
    observation_id = uuid4()
    lease_id = uuid4()
    action_id = uuid4()
    png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL8WQAAAABJRU5ErkJggg==")

    class FakeProvider:
        calls = 0
        complete_next = False
        needs_input_next = False

        last_history = None
        last_title = None

        async def propose_action(self, **kwargs):
            self.calls += 1
            self.last_history = list(kwargs["history"])
            self.last_title = kwargs["foreground_title"]
            assert kwargs["image_bytes"] == png
            if self.needs_input_next:
                return SimpleNamespace(
                    envelope=None,
                    completed=False,
                    needs_input=True,
                    completion_message="Which date and time should I use?",
                    model_id="gemini-3.5-flash-lite",
                    input_tokens=20,
                    output_tokens=5,
                    cost_usd=Decimal("0.000010"),
                )
            if self.complete_next:
                return SimpleNamespace(
                    envelope=None,
                    completed=True,
                    completion_message="The requested view is visible.",
                    model_id="gemini-3.5-flash-lite",
                    input_tokens=20,
                    output_tokens=5,
                    cost_usd=Decimal("0.000010"),
                )
            envelope = ActionEnvelope(
                schema_version=1,
                task_id=kwargs["task_id"],
                device_id=kwargs["device_id"],
                action_id=action_id,
                observation_id=kwargs["observation_id"],
                lease_id=kwargs["lease_id"],
                epoch=kwargs["epoch"],
                sequence=kwargs["sequence"],
                action=ClickAction(kind="click", x=500, y=500, button="left"),
            )
            return SimpleNamespace(envelope=envelope, intent="Open the synthetic target", model_id="gemini-3.5-flash-lite", input_tokens=20, output_tokens=5, cost_usd=Decimal("0.000010"))

    provider = FakeProvider()
    monkeypatch.setattr(main, "_live_provider", lambda: provider)
    try:
        assert main.store.enroll_device("test-owner", device_id, "Test desktop", "windows")
        task, created = main.store.create_task("test-owner", device_id, "Click the center of the synthetic image", "live-probe")
        assert created
        task_id = task.task_id
        response = client.post(
            f"/v1/tasks/{task_id}/proposals",
            json={
                "device_id": str(device_id),
                "observation_id": str(observation_id),
                "lease_id": str(lease_id),
                "epoch": 0,
                "sequence": 1,
                "image_mime_type": "image/png",
                "image_base64": base64.b64encode(png).decode("ascii"),
            },
        )
        assert response.status_code == 200
        assert response.json()["status"] == "proposal"
        assert response.json()["execution"] == "not_executed"
        assert response.json()["proposal"]["action"] == {"kind": "click", "x": 500, "y": 500, "button": "left", "clicks": 1}
        assert response.json()["intent"] == "Open the synthetic target"
        assert provider.calls == 1
        assert provider.last_history == []

        provider.complete_next = True
        completion_response = client.post(
            f"/v1/tasks/{task_id}/proposals",
            json={
                "device_id": str(device_id),
                "observation_id": str(uuid4()),
                "lease_id": str(lease_id),
                "epoch": 0,
                "sequence": 2,
                "image_mime_type": "image/png",
                "image_base64": base64.b64encode(png).decode("ascii"),
                "history": [{"step": 1, "action": "click (500, 500)", "outcome": "executed"}],
                "foreground_title": "Synthetic window",
            },
        )
        assert completion_response.status_code == 200
        assert [(entry.step, entry.outcome) for entry in provider.last_history] == [(1, "executed")]
        assert provider.last_title == "Synthetic window"
        assert completion_response.json()["status"] == "completed"
        assert completion_response.json()["proposal"] is None
        assert completion_response.json()["completion_message"] == "The requested view is visible."

        provider.needs_input_next = True
        clarification_response = client.post(
            f"/v1/tasks/{task_id}/proposals",
            json={
                "device_id": str(device_id),
                "observation_id": str(uuid4()),
                "lease_id": str(lease_id),
                "epoch": 0,
                "sequence": 3,
                "image_mime_type": "image/png",
                "image_base64": base64.b64encode(png).decode("ascii"),
            },
        )
        assert clarification_response.status_code == 200
        assert clarification_response.json()["status"] == "needs_input"
        assert clarification_response.json()["proposal"] is None
        assert clarification_response.json()["completion_message"] == "Which date and time should I use?"
    finally:
        main.app.dependency_overrides.clear()
        main.store = original_store
        main.budget_ledger = original_budget


def _wav(seconds: float, sample_rate: int = 16_000) -> bytes:
    import struct

    frames = int(seconds * sample_rate)
    data = b"\x00\x00" * frames
    header = b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE"
    header += b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, sample_rate, sample_rate * 2, 2, 16)
    header += b"data" + struct.pack("<I", len(data))
    return header + data


def test_step_history_is_bounded_and_typed(monkeypatch, tmp_path) -> None:
    owner = OwnerPrincipal(subject="test-owner", username="owner")
    original_store = main.store
    main.store = SQLiteTaskStore(tmp_path / "history.db")
    main.app.dependency_overrides[main._current_owner] = lambda: owner
    monkeypatch.setenv("AUTOBOTS_LIVE_AI_ENABLED", "true")
    try:
        device_id = uuid4()
        assert main.store.enroll_device("test-owner", device_id, "Test desktop", "windows")
        task, _ = main.store.create_task("test-owner", device_id, "Open Notepad", "history-key")
        base = {
            "device_id": str(device_id),
            "observation_id": str(uuid4()),
            "lease_id": str(uuid4()),
            "epoch": 0,
            "sequence": 1,
            "image_mime_type": "image/png",
            "image_base64": base64.b64encode(b"\x89PNG\r\n\x1a\n" + b"0" * 32).decode("ascii"),
        }
        invalid_outcome = client.post(f"/v1/tasks/{task.task_id}/proposals", json={
            **base, "history": [{"step": 1, "action": "click", "outcome": "approved_by_page"}],
        })
        assert invalid_outcome.status_code == 422
        too_long = client.post(f"/v1/tasks/{task.task_id}/proposals", json={
            **base, "history": [{"step": index + 1, "action": "wait", "outcome": "executed"} for index in range(16)],
        })
        assert too_long.status_code == 422
    finally:
        main.app.dependency_overrides.clear()
        main.store = original_store


def test_voice_transcription_is_owner_scoped_budgeted_and_not_task_creating(monkeypatch, tmp_path) -> None:
    owner = OwnerPrincipal(subject="test-owner", username="owner")
    original_store = main.store
    original_speech = main.speech_ledger
    main.store = SQLiteTaskStore(tmp_path / "voice.db")
    main.speech_ledger = SQLiteBudgetLedger(
        main.store.path,
        BudgetLimits(Decimal("0.01"), Decimal("0.00206"), Decimal("1.00")),
        table="speech_reservations",
    )
    main.app.dependency_overrides[main._current_owner] = lambda: owner
    monkeypatch.setenv("AUTOBOTS_LIVE_AI_ENABLED", "true")

    class FakeTranscriber:
        calls = 0

        async def transcribe(self, **kwargs):
            self.calls += 1
            assert kwargs["mime_type"] == "audio/wav"
            return SimpleNamespace(
                transcript="Open Notepad and type hello" if self.calls == 1 else None,
                model_id="gemini-3.5-flash-lite",
                input_tokens=64,
                output_tokens=8,
                cost_usd=Decimal("0.000040"),
            )

    transcriber = FakeTranscriber()
    monkeypatch.setattr(main, "_live_transcriber", lambda: transcriber)
    try:
        device_id = uuid4()
        payload = {
            "device_id": str(device_id),
            "audio_mime_type": "audio/wav",
            "audio_base64": base64.b64encode(_wav(1.5)).decode("ascii"),
        }
        not_enrolled = client.post("/v1/transcriptions", json=payload)
        assert not_enrolled.status_code == 403

        assert main.store.enroll_device("test-owner", device_id, "Test desktop", "windows")
        transcribed = client.post("/v1/transcriptions", json=payload)
        assert transcribed.status_code == 200
        assert transcribed.json()["status"] == "transcribed"
        assert transcribed.json()["transcript"] == "Open Notepad and type hello"
        assert transcribed.json()["audio_seconds"] == 1.5

        silent = client.post("/v1/transcriptions", json=payload)
        assert silent.status_code == 200
        assert silent.json() == {**silent.json(), "status": "no_speech", "transcript": None}

        # The daily speech limit is independent from the action-inference ledger.
        exhausted = client.post("/v1/transcriptions", json=payload)
        assert exhausted.status_code == 429
        assert exhausted.json()["detail"]["code"] == "speech_budget_exhausted"
        assert transcriber.calls == 2

        invalid = client.post("/v1/transcriptions", json={**payload, "audio_base64": base64.b64encode(b"RIFF" + b"x" * 80).decode("ascii")})
        assert invalid.status_code == 422
        assert invalid.json()["detail"]["code"] == "invalid_audio"
    finally:
        main.app.dependency_overrides.clear()
        main.store = original_store
        main.speech_ledger = original_speech


def test_transcription_fails_closed_without_live_ai_or_auth(monkeypatch) -> None:
    monkeypatch.setenv("AUTOBOTS_LIVE_AI_ENABLED", "false")
    response = client.post("/v1/transcriptions", json={
        "device_id": str(uuid4()),
        "audio_mime_type": "audio/wav",
        "audio_base64": base64.b64encode(_wav(1.0)).decode("ascii"),
    })
    assert response.status_code == 503
