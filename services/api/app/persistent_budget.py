from __future__ import annotations

import re
import sqlite3
from contextlib import closing
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from uuid import UUID, uuid4

from services.api.app.budget import BudgetExceeded, BudgetLimits, _money, _utc


_LEDGER_TABLES = {"inference_reservations", "speech_reservations"}


class SQLiteBudgetLedger:
    """SQLite-backed spend ledger with serialized reservations across API workers.

    Each ledger table is accounted separately, so speech transcription never consumes the
    desktop-action inference budget and vice versa.
    """

    def __init__(self, path: str | Path, limits: BudgetLimits = BudgetLimits(), *, table: str = "inference_reservations") -> None:
        if table not in _LEDGER_TABLES or not re.fullmatch(r"[a-z_]+", table):
            raise ValueError("Unknown budget ledger table")
        self.path = Path(path)
        self.limits = limits
        self.table = table
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with closing(self._connect()) as connection:
            connection.execute(
                f"""
                CREATE TABLE IF NOT EXISTS {self.table} (
                    reservation_id TEXT PRIMARY KEY,
                    task_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    utc_day TEXT NOT NULL,
                    utc_month TEXT NOT NULL,
                    reserved_usd TEXT NOT NULL,
                    actual_usd TEXT,
                    status TEXT NOT NULL CHECK(status IN ('pending', 'reconciled', 'released')),
                    owner_sub TEXT
                )
                """
            )
            columns = {row[1] for row in connection.execute(f"PRAGMA table_info({self.table})")}
            if "owner_sub" not in columns:
                connection.execute(f"ALTER TABLE {self.table} ADD COLUMN owner_sub TEXT")
            connection.execute(f"CREATE INDEX IF NOT EXISTS ix_{self.table}_owner ON {self.table}(owner_sub)")

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path, timeout=5, isolation_level=None)
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA busy_timeout=5000")
        return connection

    def reserve(self, task_id: UUID, estimate_usd: Decimal | int | float | str, now: datetime | None = None,
                *, owner_sub: str | None = None, lifetime_limit_usd: Decimal | None = None) -> UUID:
        estimate = _money(estimate_usd)
        if estimate <= 0:
            raise ValueError("Reservation must be greater than zero")
        current = _utc(now or datetime.now(UTC))
        utc_day = current.date().isoformat()
        utc_month = current.strftime("%Y-%m")
        reservation_id = uuid4()

        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            task_total = _total(connection, self.table, "task_id = ?", (str(task_id),))
            day_total = _total(connection, self.table, "utc_day = ?", (utc_day,))
            month_total = _total(connection, self.table, "utc_month = ?", (utc_month,))
            if lifetime_limit_usd is not None:
                if not owner_sub:
                    connection.rollback()
                    raise ValueError("A pilot lifetime limit requires an owner subject")
                lifetime_total = self._lifetime_total(connection, owner_sub)
                if lifetime_total + estimate > lifetime_limit_usd:
                    connection.rollback()
                    raise BudgetExceeded("Pilot lifetime AI budget would be exceeded")
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
                f"INSERT INTO {self.table}(reservation_id, task_id, created_at, utc_day, utc_month, reserved_usd, status, owner_sub) VALUES(?, ?, ?, ?, ?, ?, 'pending', ?)",
                (str(reservation_id), str(task_id), current.isoformat(), utc_day, utc_month, str(estimate), owner_sub),
            )
            connection.commit()
        return reservation_id

    def reconcile(self, reservation_id: UUID, actual_usd: Decimal | int | float | str) -> Decimal:
        actual = _money(actual_usd)
        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute(
                f"SELECT reserved_usd FROM {self.table} WHERE reservation_id = ? AND status = 'pending'",
                (str(reservation_id),),
            ).fetchone()
            if row is None:
                connection.rollback()
                raise KeyError("Reservation is missing, expired, or already reconciled")
            connection.execute(
                f"UPDATE {self.table} SET actual_usd = ?, status = 'reconciled' WHERE reservation_id = ?",
                (str(actual), str(reservation_id)),
            )
            connection.commit()
            return actual - Decimal(row[0])

    def release(self, reservation_id: UUID) -> None:
        with closing(self._connect()) as connection:
            cursor = connection.execute(
                f"UPDATE {self.table} SET status = 'released' WHERE reservation_id = ? AND status = 'pending'",
                (str(reservation_id),),
            )
            if cursor.rowcount != 1:
                raise KeyError("Reservation is missing or already completed")

    def lifetime_spend(self, owner_sub: str) -> Decimal:
        with closing(self._connect()) as connection:
            return self._lifetime_total(connection, owner_sub)

    @staticmethod
    def _lifetime_total(connection: sqlite3.Connection, owner_sub: str) -> Decimal:
        existing = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        return sum((_total(connection, ledger, "owner_sub = ?", (owner_sub,))
                    for ledger in _LEDGER_TABLES if ledger in existing), Decimal(0))


def _total(connection: sqlite3.Connection, table: str, predicate: str, values: tuple[str, ...]) -> Decimal:
    rows = connection.execute(
        f"SELECT reserved_usd, actual_usd, status FROM {table} WHERE {predicate}",
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
