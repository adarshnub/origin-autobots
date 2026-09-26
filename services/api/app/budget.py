from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, date, datetime
from decimal import Decimal
from threading import RLock
from uuid import UUID, uuid4


class BudgetExceeded(RuntimeError):
    pass


def _money(value: Decimal | int | float | str) -> Decimal:
    result = Decimal(str(value))
    if not result.is_finite() or result < 0:
        raise ValueError("Cost must be a finite, non-negative amount")
    return result.quantize(Decimal("0.000001"))


@dataclass(frozen=True)
class BudgetLimits:
    per_task_usd: Decimal = Decimal("1.00")
    per_day_usd: Decimal = Decimal("5.00")
    per_month_usd: Decimal = Decimal("100.00")

    def __post_init__(self) -> None:
        if min(self.per_task_usd, self.per_day_usd, self.per_month_usd) <= 0:
            raise ValueError("Every model budget limit must be greater than zero")


@dataclass(frozen=True)
class BudgetReservation:
    reservation_id: UUID
    task_id: UUID
    amount_usd: Decimal
    reserved_at: datetime

    @property
    def utc_day(self) -> date:
        return self.reserved_at.date()

    @property
    def utc_month(self) -> tuple[int, int]:
        return self.reserved_at.year, self.reserved_at.month


class BudgetLedger:
    """Thread-safe in-memory reservation ledger for local tests and routing logic.

    Production must persist reservations and actual usage transactionally before
    enabling live model calls.
    """

    def __init__(self, limits: BudgetLimits = BudgetLimits()) -> None:
        self._limits = limits
        self._lock = RLock()
        self._reservations: dict[UUID, BudgetReservation] = {}
        self._task_spend: dict[UUID, Decimal] = {}
        self._day_spend: dict[date, Decimal] = {}
        self._month_spend: dict[tuple[int, int], Decimal] = {}

    def reserve(self, task_id: UUID, estimate_usd: Decimal | int | float | str, now: datetime | None = None) -> BudgetReservation:
        estimate = _money(estimate_usd)
        if estimate <= 0:
            raise ValueError("Reservation must be greater than zero")
        current = _utc(now or datetime.now(UTC))
        reservation = BudgetReservation(uuid4(), task_id, estimate, current)

        with self._lock:
            task_total = self._task_spend.get(task_id, Decimal(0))
            day_total = self._day_spend.get(reservation.utc_day, Decimal(0))
            month_total = self._month_spend.get(reservation.utc_month, Decimal(0))
            for pending in self._reservations.values():
                if pending.utc_day == reservation.utc_day:
                    day_total += pending.amount_usd
                if pending.utc_month == reservation.utc_month:
                    month_total += pending.amount_usd
                if pending.task_id == task_id:
                    task_total += pending.amount_usd

            if task_total + estimate > self._limits.per_task_usd:
                raise BudgetExceeded("Per-task inference budget would be exceeded")
            if day_total + estimate > self._limits.per_day_usd:
                raise BudgetExceeded("Daily inference budget would be exceeded")
            if month_total + estimate > self._limits.per_month_usd:
                raise BudgetExceeded("Monthly inference budget would be exceeded")
            self._reservations[reservation.reservation_id] = reservation
        return reservation

    def reconcile(self, reservation_id: UUID, actual_usd: Decimal | int | float | str) -> Decimal:
        actual = _money(actual_usd)
        with self._lock:
            reservation = self._reservations.pop(reservation_id, None)
            if reservation is None:
                raise KeyError("Reservation is missing, expired, or already reconciled")
            self._task_spend[reservation.task_id] = self._task_spend.get(reservation.task_id, Decimal(0)) + actual
            self._day_spend[reservation.utc_day] = self._day_spend.get(reservation.utc_day, Decimal(0)) + actual
            self._month_spend[reservation.utc_month] = self._month_spend.get(reservation.utc_month, Decimal(0)) + actual
        return actual - reservation.amount_usd

    def release(self, reservation_id: UUID) -> None:
        with self._lock:
            if self._reservations.pop(reservation_id, None) is None:
                raise KeyError("Reservation is missing or already completed")

    def snapshot(self, task_id: UUID, now: datetime | None = None) -> dict[str, Decimal]:
        current = _utc(now or datetime.now(UTC))
        day = current.date()
        month = (current.year, current.month)
        with self._lock:
            task_spend = self._task_spend.get(task_id, Decimal(0))
            day_spend = self._day_spend.get(day, Decimal(0))
            month_spend = self._month_spend.get(month, Decimal(0))
            for pending in self._reservations.values():
                if pending.utc_day == day:
                    day_spend += pending.amount_usd
                if pending.utc_month == month:
                    month_spend += pending.amount_usd
                if pending.task_id == task_id:
                    task_spend += pending.amount_usd
        return {"task_reserved_or_spent_usd": task_spend, "day_reserved_or_spent_usd": day_spend, "month_reserved_or_spent_usd": month_spend}


def _utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        raise ValueError("Budget timestamps must include a timezone")
    return value.astimezone(UTC)
