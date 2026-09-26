import asyncio
import time
from decimal import Decimal
from types import SimpleNamespace
from uuid import uuid4

from fastapi.testclient import TestClient

from services.api.app import main, usage
from services.api.app.auth import OwnerPrincipal
from services.api.app.budget import BudgetLimits
from services.api.app.persistent_budget import SQLiteBudgetLedger
from services.api.app.storage import SQLiteTaskStore


def setup_ledgers(path):
    tasks = SQLiteTaskStore(path)
    limits = BudgetLimits(Decimal("1"), Decimal("5"), Decimal("50"))
    return tasks, SQLiteBudgetLedger(path, limits), SQLiteBudgetLedger(path, limits, table="speech_reservations")


def test_usage_preserves_owner_scope_unknown_costs_and_reservations(tmp_path):
    tasks, inference, speech = setup_ledgers(tmp_path / "usage.db")
    reports = usage.UsageStore(tasks.path)
    for owner in ("owner-a", "owner-b"):
        device = uuid4()
        tasks.enroll_device(owner, device, "Synthetic", "windows")
        task, _ = tasks.create_task(owner, device, "Synthetic", str(uuid4()))
        reservation = inference.reserve(task.task_id, Decimal("0.01"))
        inference.reconcile(reservation, Decimal("0.001234"))
        result = SimpleNamespace(model_id="fake", input_tokens=100, output_tokens=20, cost_usd=Decimal("0.001234"))
        reports.record(reservation, owner, "desktop", "response", time.perf_counter(), result)
        reports.record(reservation, owner, "desktop", "response", time.perf_counter(), result)
        if owner == "owner-a":
            pending = inference.reserve(task.task_id, Decimal("0.01"))
            reports.record(pending, owner, "desktop", "provider_error", time.perf_counter())
    released = speech.reserve(uuid4(), Decimal("0.01"))
    speech.release(released)
    data = reports.summary("owner-a", inference, speech)
    assert sum(m["requests"] for m in data["models"]) == 2
    assert sum(m["input_tokens"] for m in data["models"]) == 100
    assert sum(m["unknown_cost_requests"] for m in data["models"]) == 1
    assert sum(m["failed_requests"] for m in data["models"]) == 1
    assert data["channels"][0]["month_budget_accounted_usd"] == "0.011234"
    assert data["channels"][0]["pending_requests"] == 1
    assert data["channels"][1]["month_requests"] == 0
    assert reports.summary("stranger", inference, speech)["models"] == []


def test_private_usage_endpoint(monkeypatch, tmp_path):
    tasks, inference, speech = setup_ledgers(tmp_path / "usage.db")
    monkeypatch.setattr(main, "store", tasks)
    monkeypatch.setattr(main, "budget_ledger", inference)
    monkeypatch.setattr(main, "speech_ledger", speech)
    async def billing():
        return {"provider": "aws", "status": "unavailable", "amount": None}
    monkeypatch.setattr(main, "aws_billing", billing)
    client = TestClient(main.app)
    assert client.get("/v1/usage").status_code in (401, 503)
    main.app.dependency_overrides[main._current_owner] = lambda: OwnerPrincipal(subject="owner", username="owner")
    try:
        response = client.get("/v1/usage")
        assert response.status_code == 200
        data = response.json()
        assert data["billing"][0]["amount"] is None
        assert data["billing"][1]["status"] == "not_connected"
        assert data["other_apis"]["status"] == "none_configured"
        assert data["models"] == []
    finally:
        main.app.dependency_overrides.clear()


def test_aws_unavailable_is_not_zero(monkeypatch):
    monkeypatch.setattr(usage, "_billing_cache", None)
    monkeypatch.delenv("AUTOBOTS_AWS_ACCOUNT_ID", raising=False)
    result = asyncio.run(usage.aws_billing())
    assert result["status"] == "unavailable"
    assert result["amount"] is None


def test_aws_budget_is_cached_and_labeled_account_wide(monkeypatch):
    monkeypatch.setattr(usage, "_billing_cache", None)
    monkeypatch.setenv("AUTOBOTS_AWS_ACCOUNT_ID", "123456789012")
    calls = []
    class Process:
        returncode = 0
        async def communicate(self):
            return b'{"Budget":{"CalculatedSpend":{"ActualSpend":{"Amount":"12.345","Unit":"USD"}},"LastUpdatedTime":"2026-09-26T00:00:00Z","BudgetLimit":{"Amount":"75"}}}', b""
    async def create(*args, **kwargs):
        calls.append(args)
        return Process()
    monkeypatch.setattr(usage.asyncio, "create_subprocess_exec", create)
    async def twice():
        return await usage.aws_billing(), await usage.aws_billing()
    first, second = asyncio.run(twice())
    assert first == second
    assert len(calls) == 1
    assert first["amount"] == "12.345"
    assert "Entire AWS account" in first["scope"]
    assert first["as_of"] == "2026-09-26T00:00:00Z"
