from __future__ import annotations

import sqlite3
from contextlib import closing
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from uuid import UUID, uuid4

from services.api.app.budget import BudgetExceeded, BudgetLimits, _money, _utc


class SQLiteBudgetLedger:
    """SQLite-backed inference ledger with serialized reservations across API workers."""

    def __init__(self, path: str | Path, limits: BudgetLimits = BudgetLimits()) -> None:
        self.path = Path(path)
        self.limits = limits
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with closing(self._connect()) as connection:
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS inference_reservations (
                    reservation_id TEXT PRIMARY KEY,
                    task_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    utc_day TEXT NOT NULL,
                    utc_month TEXT NOT NULL,
                    reserved_usd TEXT NOT NULL,
                    actual_usd TEXT,
                    status TEXT NOT NULL CHECK(status IN ('pending', 'reconciled', 'released'))
                )
                """
            )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path, timeout=5, isolation_level=None)
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA busy_timeout=5000")
        return connection

    def reserve(self, task_id: UUID, estimate_usd: Decimal | int | float | str, now: datetime | None = None) -> UUID:
        estimate = _money(estimate_usd)
        if estimate <= 0:
            raise ValueError("Reservation must be greater than zero")
        current = _utc(now or datetime.now(UTC))
        utc_day = current.date().isoformat()
        utc_month = current.strftime("%Y-%m")
        reservation_id = uuid4()

        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            task_total = _total(connection, "task_id = ?", (str(task_id),))
            day_total = _total(connection, "utc_day = ?", (utc_day,))
            month_total = _total(connection, "utc_month = ?", (utc_month,))
            if task_total + estimate > self.limits.per_task_usd:
                connection.rollback()
                raise BudgetExceeded("Per-task inference budget would be exceeded")
            if day_total + estimate > self.limits.per_day_usd:
                connection.rollback()
                raise BudgetExceeded("Daily inference budget would be exceeded")
            if month_total + estimate > self.limits.per_month_usd:
                connection.rollback()
                raise BudgetExceeded("Monthly inference budget would be exceeded")
            connection.execute(
                "INSERT INTO inference_reservations(reservation_id, task_id, created_at, utc_day, utc_month, reserved_usd, status) VALUES(?, ?, ?, ?, ?, ?, 'pending')",
                (str(reservation_id), str(task_id), current.isoformat(), utc_day, utc_month, str(estimate)),
            )
            connection.commit()
        return reservation_id

    def reconcile(self, reservation_id: UUID, actual_usd: Decimal | int | float | str) -> Decimal:
        actual = _money(actual_usd)
        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute(
                "SELECT reserved_usd FROM inference_reservations WHERE reservation_id = ? AND status = 'pending'",
                (str(reservation_id),),
            ).fetchone()
            if row is None:
                connection.rollback()
                raise KeyError("Reservation is missing, expired, or already reconciled")
            connection.execute(
                "UPDATE inference_reservations SET actual_usd = ?, status = 'reconciled' WHERE reservation_id = ?",
                (str(actual), str(reservation_id)),
            )
            connection.commit()
            return actual - Decimal(row[0])

    def release(self, reservation_id: UUID) -> None:
        with closing(self._connect()) as connection:
            cursor = connection.execute(
                "UPDATE inference_reservations SET status = 'released' WHERE reservation_id = ? AND status = 'pending'",
                (str(reservation_id),),
            )
            if cursor.rowcount != 1:
                raise KeyError("Reservation is missing or already completed")


def _total(connection: sqlite3.Connection, predicate: str, values: tuple[str, ...]) -> Decimal:
    rows = connection.execute(
        f"SELECT reserved_usd, actual_usd, status FROM inference_reservations WHERE {predicate}",
        values,
    ).fetchall()
    total = sum(
        (
            Decimal(reserved if status == "pending" else actual or "0")
            for reserved, actual, status in rows
            if status in {"pending", "reconciled"}
        ),
        Decimal(0),
    )
    return total.quantize(Decimal("0.000001"))
