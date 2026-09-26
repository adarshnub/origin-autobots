from datetime import UTC, datetime
from decimal import Decimal
from uuid import uuid4

import pytest

from services.api.app.budget import BudgetExceeded, BudgetLimits
from services.api.app.persistent_budget import SQLiteBudgetLedger


def test_budget_reservations_survive_restart_and_reconcile_exactly(tmp_path) -> None:
    path = tmp_path / "budget.db"
    now = datetime(2026, 9, 25, 10, tzinfo=UTC)
    limits = BudgetLimits(per_task_usd=Decimal("0.02"), per_day_usd=Decimal("0.02"), per_month_usd=Decimal("0.02"))
    task_a, task_b = uuid4(), uuid4()
    ledger = SQLiteBudgetLedger(path, limits)
    reservation = ledger.reserve(task_a, Decimal("0.01"), now)

    restarted = SQLiteBudgetLedger(path, limits)
    with pytest.raises(BudgetExceeded):
        restarted.reserve(task_b, Decimal("0.02"), now)

    assert restarted.reconcile(reservation, Decimal("0.003")) == Decimal("-0.007")
    next_reservation = restarted.reserve(task_b, Decimal("0.017"), now)
    restarted.reconcile(next_reservation, Decimal("0.017"))
    with pytest.raises(BudgetExceeded):
        restarted.reserve(task_b, Decimal("0.000001"), now)


def test_speech_and_inference_ledgers_are_accounted_separately(tmp_path) -> None:
    path = tmp_path / "shared.db"
    now = datetime(2026, 9, 26, 10, tzinfo=UTC)
    limits = BudgetLimits(per_task_usd=Decimal("0.01"), per_day_usd=Decimal("0.01"), per_month_usd=Decimal("0.01"))
    inference = SQLiteBudgetLedger(path, limits)
    speech = SQLiteBudgetLedger(path, limits, table="speech_reservations")

    inference.reconcile(inference.reserve(uuid4(), Decimal("0.01"), now), Decimal("0.01"))
    with pytest.raises(BudgetExceeded):
        inference.reserve(uuid4(), Decimal("0.001"), now)
    speech.reconcile(speech.reserve(uuid4(), Decimal("0.005"), now), Decimal("0.005"))


def test_unknown_ledger_tables_are_rejected(tmp_path) -> None:
    with pytest.raises(ValueError):
        SQLiteBudgetLedger(tmp_path / "x.db", table="inference_reservations; DROP TABLE tasks")


def test_pilot_lifetime_cap_combines_speech_and_inference_across_months(tmp_path) -> None:
    path = tmp_path / "pilot.db"
    limits = BudgetLimits(Decimal("2"), Decimal("100"), Decimal("100"))
    inference = SQLiteBudgetLedger(path, limits)
    speech = SQLiteBudgetLedger(path, limits, table="speech_reservations")
    pilot = "pilot-sub"
    first = datetime(2026, 9, 26, tzinfo=UTC)
    later = datetime(2026, 10, 2, tzinfo=UTC)
    for _ in range(9):
        inference.reconcile(inference.reserve(uuid4(), "1", first, owner_sub=pilot,
                                              lifetime_limit_usd=Decimal("10")), "1")
    speech.reconcile(speech.reserve(uuid4(), "0.99", later, owner_sub=pilot,
                                    lifetime_limit_usd=Decimal("10")), "0.99")
    assert inference.lifetime_spend(pilot) == Decimal("9.990000")
    with pytest.raises(BudgetExceeded):
        inference.reserve(uuid4(), "0.05", later, owner_sub=pilot, lifetime_limit_usd=Decimal("10"))
    # The owner has no per-user cap and cannot consume this pilot's allowance.
    inference.reserve(uuid4(), "0.05", later, owner_sub="owner-sub")
