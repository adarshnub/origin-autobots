from fastapi.testclient import TestClient
import base64
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


def test_health_is_available_without_cloud_credentials() -> None:
    response = client.get("/healthz")
    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "autobots-api", "mode": "mock", "native_input": "local-device-only"}


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

        async def propose_action(self, **kwargs):
            self.calls += 1
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
            return SimpleNamespace(envelope=envelope, model_id="gemini-3.5-flash-lite", input_tokens=20, output_tokens=5, cost_usd=Decimal("0.000010"))

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
        assert response.json()["proposal"]["action"] == {"kind": "click", "x": 500, "y": 500, "button": "left"}
        assert provider.calls == 1

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
            },
        )
        assert completion_response.status_code == 200
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
