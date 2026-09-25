from datetime import UTC, datetime
from decimal import Decimal
from uuid import uuid4

import pytest

from services.api.app.budget import BudgetExceeded, BudgetLedger, BudgetLimits


NOW = datetime(2026, 9, 25, 12, 0, tzinfo=UTC)


def test_inflight_reservations_count_toward_all_limits() -> None:
    ledger = BudgetLedger(BudgetLimits(Decimal("2"), Decimal("2"), Decimal("3")))
    ledger.reserve(uuid4(), "1.00", NOW)
    with pytest.raises(BudgetExceeded, match="Daily"):
        ledger.reserve(uuid4(), "1.01", NOW)


def test_reconciliation_releases_reservation_and_records_actual_cost() -> None:
    task_id = uuid4()
    ledger = BudgetLedger()
    reservation = ledger.reserve(task_id, "0.20", NOW)
    assert ledger.reconcile(reservation.reservation_id, "0.12") == Decimal("-0.080000")
    assert ledger.snapshot(task_id, NOW)["task_reserved_or_spent_usd"] == Decimal("0.120000")


def test_actual_cost_above_estimate_is_recorded_and_duplicate_reconcile_fails() -> None:
    task_id = uuid4()
    ledger = BudgetLedger()
    reservation = ledger.reserve(task_id, "0.10", NOW)
    assert ledger.reconcile(reservation.reservation_id, "0.30") == Decimal("0.200000")
    with pytest.raises(KeyError):
        ledger.reconcile(reservation.reservation_id, "0.01")


def test_daily_limit_blocks_spend_across_tasks() -> None:
    ledger = BudgetLedger(BudgetLimits(Decimal("1"), Decimal("0.50"), Decimal("5")))
    first = ledger.reserve(uuid4(), "0.40", NOW)
    ledger.reconcile(first.reservation_id, "0.40")
    with pytest.raises(BudgetExceeded, match="Daily"):
        ledger.reserve(uuid4(), "0.11", NOW)


def test_naive_timestamps_are_rejected() -> None:
    ledger = BudgetLedger()
    with pytest.raises(ValueError, match="timezone"):
        ledger.reserve(uuid4(), "0.01", datetime(2026, 9, 25))


def test_task_limit_follows_task_across_midnight() -> None:
    task_id = uuid4()
    ledger = BudgetLedger(BudgetLimits(Decimal("1"), Decimal("5"), Decimal("50")))
    first = ledger.reserve(task_id, "0.60", NOW)
    ledger.reconcile(first.reservation_id, "0.60")
    tomorrow = datetime(2026, 9, 26, 1, 0, tzinfo=UTC)
    with pytest.raises(BudgetExceeded, match="Per-task"):
        ledger.reserve(task_id, "0.41", tomorrow)
