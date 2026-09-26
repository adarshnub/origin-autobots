from decimal import Decimal

from fastapi.testclient import TestClient

from services.api.app import main
from services.api.app.auth import OwnerPrincipal
from services.api.app.pilots import PilotStore
from services.api.app.persistent_budget import SQLiteBudgetLedger


def test_invite_profile_and_lifetime_usage_without_real_email(monkeypatch, tmp_path):
    path = tmp_path / "pilot.db"
    monkeypatch.setattr(main, "pilots", PilotStore(path))
    monkeypatch.setattr(main, "budget_ledger", SQLiteBudgetLedger(path))
    monkeypatch.setattr(main, "speech_ledger", SQLiteBudgetLedger(path, table="speech_reservations"))
    monkeypatch.setenv("AUTOBOTS_OWNER_EMAIL", "owner@example.com")
    owner = OwnerPrincipal("owner-sub", "owner")
    pilot = OwnerPrincipal("pilot-sub", "pilot", "pilot")

    async def fake_invite(email):
        assert email == "pilot@example.com"
        return pilot.subject

    monkeypatch.setattr(main, "invite", fake_invite)
    client = TestClient(main.app)
    main.app.dependency_overrides[main._current_admin] = lambda: owner
    main.app.dependency_overrides[main._current_member] = lambda: pilot
    try:
        denied_owner = client.post("/v1/admin/pilots", json={"email": "owner@example.com"})
        assert denied_owner.status_code == 409
        created = client.post("/v1/admin/pilots", json={"email": "Pilot@Example.com"})
        assert created.status_code == 201, created.json()
        assert client.post("/v1/admin/pilots", json={"email": "pilot@example.com"}).status_code == 409
        me = client.get("/v1/pilot/me")
        assert me.status_code == 200 and me.json()["profile"]["onboarded_at"] is None
        assert client.post("/v1/pilot/profile", json={"username": "  ", "purpose": "  " * 10}).status_code == 422
        done = client.post("/v1/pilot/profile", json={"username": "Pilot", "purpose": "Help me organize my meetings"})
        assert done.status_code == 200
        assert client.get("/v1/pilot/me").json()["profile"]["username"] == "Pilot"
        assert client.get("/v1/admin/pilots").json()["pilots"][0]["lifetime_limit_usd"] == "10.00"
        reservation = main.budget_ledger.reserve(__import__("uuid").uuid4(), Decimal("0.25"), owner_sub=pilot.subject,
                                                  lifetime_limit_usd=Decimal("10"))
        main.budget_ledger.reconcile(reservation, Decimal("0.20"))
        assert client.get("/v1/pilot/me").json()["lifetime_used_usd"] == "0.200000"
    finally:
        main.app.dependency_overrides.clear()
