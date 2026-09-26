from __future__ import annotations

import hashlib
import json
import os
import sqlite3
from contextlib import closing
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from uuid import UUID, uuid4


class IdempotencyConflict(ValueError):
    pass


@dataclass(frozen=True)
class StoredTask:
    task_id: UUID
    owner_sub: str
    device_id: UUID
    instruction: str
    status: str
    epoch: int
    created_at: datetime
    updated_at: datetime


class SQLiteTaskStore:
    """Small single-owner journal; screenshots and model payloads are never persisted."""

    def __init__(self, path: str | Path | None = None) -> None:
        raw_path = path or os.getenv("AUTOBOTS_DATABASE_PATH", "./data/autobots.db")
        self.path = Path(raw_path).resolve()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with closing(self._connect()) as connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS devices (
                    device_id TEXT PRIMARY KEY,
                    owner_sub TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    platform TEXT NOT NULL,
                    enrolled_at TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_devices_owner ON devices(owner_sub);
                CREATE TABLE IF NOT EXISTS tasks (
                    task_id TEXT PRIMARY KEY,
                    owner_sub TEXT NOT NULL,
                    device_id TEXT NOT NULL,
                    instruction TEXT NOT NULL,
                    status TEXT NOT NULL,
                    epoch INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_tasks_owner_created ON tasks(owner_sub, created_at DESC);
                CREATE TABLE IF NOT EXISTS task_idempotency (
                    owner_sub TEXT NOT NULL,
                    idempotency_key TEXT NOT NULL,
                    request_hash TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    PRIMARY KEY(owner_sub, idempotency_key),
                    FOREIGN KEY(task_id) REFERENCES tasks(task_id)
                );
                CREATE TABLE IF NOT EXISTS task_events (
                    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id TEXT NOT NULL,
                    owner_sub TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    event_data TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(task_id) REFERENCES tasks(task_id)
                );
                """
            )
            connection.commit()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path, timeout=5, isolation_level=None)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("PRAGMA busy_timeout=5000")
        return connection

    def enroll_device(self, owner_sub: str, device_id: UUID, display_name: str, platform: str) -> bool:
        now = _timestamp()
        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            existing = connection.execute("SELECT owner_sub FROM devices WHERE device_id = ?", (str(device_id),)).fetchone()
            if existing and existing["owner_sub"] != owner_sub:
                connection.rollback()
                return False
            if existing:
                connection.execute(
                    "UPDATE devices SET display_name = ?, platform = ?, last_seen_at = ? WHERE device_id = ?",
                    (display_name, platform, now, str(device_id)),
                )
            else:
                connection.execute(
                    "INSERT INTO devices(device_id, owner_sub, display_name, platform, enrolled_at, last_seen_at) VALUES(?, ?, ?, ?, ?, ?)",
                    (str(device_id), owner_sub, display_name, platform, now, now),
                )
            connection.commit()
            return True

    def owns_device(self, owner_sub: str, device_id: UUID) -> bool:
        with closing(self._connect()) as connection:
            row = connection.execute(
                "SELECT 1 FROM devices WHERE owner_sub = ? AND device_id = ?",
                (owner_sub, str(device_id)),
            ).fetchone()
            return row is not None

    def create_task(self, owner_sub: str, device_id: UUID, instruction: str, idempotency_key: str) -> tuple[StoredTask, bool]:
        now = _timestamp()
        request_hash = hashlib.sha256(f"{device_id}\0{instruction}".encode("utf-8")).hexdigest()
        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            previous = connection.execute(
                "SELECT request_hash, task_id FROM task_idempotency WHERE owner_sub = ? AND idempotency_key = ?",
                (owner_sub, idempotency_key),
            ).fetchone()
            if previous:
                if previous["request_hash"] != request_hash:
                    connection.rollback()
                    raise IdempotencyConflict("An idempotency key cannot be reused for a different task.")
                row = connection.execute("SELECT * FROM tasks WHERE task_id = ? AND owner_sub = ?", (previous["task_id"], owner_sub)).fetchone()
                connection.commit()
                if row is None:
                    raise RuntimeError("Idempotency record refers to a missing task.")
                return _task(row), False

            task_id = uuid4()
            connection.execute(
                "INSERT INTO tasks(task_id, owner_sub, device_id, instruction, status, epoch, created_at, updated_at) VALUES(?, ?, ?, ?, 'queued', 0, ?, ?)",
                (str(task_id), owner_sub, str(device_id), instruction, now, now),
            )
            connection.execute(
                "INSERT INTO task_idempotency(owner_sub, idempotency_key, request_hash, task_id) VALUES(?, ?, ?, ?)",
                (owner_sub, idempotency_key, request_hash, str(task_id)),
            )
            connection.execute(
                "INSERT INTO task_events(task_id, owner_sub, event_type, event_data, created_at) VALUES(?, ?, 'created', '{}', ?)",
                (str(task_id), owner_sub, now),
            )
            row = connection.execute("SELECT * FROM tasks WHERE task_id = ?", (str(task_id),)).fetchone()
            connection.commit()
            if row is None:
                raise RuntimeError("Task creation did not persist.")
            return _task(row), True

    def get_task(self, owner_sub: str, task_id: UUID) -> StoredTask | None:
        with closing(self._connect()) as connection:
            row = connection.execute("SELECT * FROM tasks WHERE owner_sub = ? AND task_id = ?", (owner_sub, str(task_id))).fetchone()
            return _task(row) if row else None

    def stop_task(self, owner_sub: str, task_id: UUID) -> StoredTask | None:
        now = _timestamp()
        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT * FROM tasks WHERE owner_sub = ? AND task_id = ?", (owner_sub, str(task_id))).fetchone()
            if row is None:
                connection.commit()
                return None
            if row["status"] not in {"completed", "stopped", "failed", "blocked", "out_of_budget"}:
                connection.execute(
                    "UPDATE tasks SET status = 'stopped', epoch = epoch + 1, updated_at = ? WHERE owner_sub = ? AND task_id = ?",
                    (now, owner_sub, str(task_id)),
                )
                connection.execute(
                    "INSERT INTO task_events(task_id, owner_sub, event_type, event_data, created_at) VALUES(?, ?, 'stopped', '{}', ?)",
                    (str(task_id), owner_sub, now),
                )
            updated = connection.execute("SELECT * FROM tasks WHERE owner_sub = ? AND task_id = ?", (owner_sub, str(task_id))).fetchone()
            connection.commit()
            return _task(updated)

    def stop_all_owner_tasks(self, owner_sub: str) -> int:
        """Invalidate every nonterminal cloud task when the owner's pilot grant is revoked."""
        now = _timestamp()
        with closing(self._connect()) as connection:
            connection.execute("BEGIN IMMEDIATE")
            rows = connection.execute("""SELECT task_id FROM tasks WHERE owner_sub=?
                AND status NOT IN ('completed','stopped','failed','blocked','out_of_budget')""", (owner_sub,)).fetchall()
            for row in rows:
                connection.execute("""UPDATE tasks SET status='stopped',epoch=epoch+1,updated_at=?
                    WHERE owner_sub=? AND task_id=?""", (now, owner_sub, row["task_id"]))
                connection.execute("""INSERT INTO task_events(task_id,owner_sub,event_type,event_data,created_at)
                    VALUES(?,?,'access_revoked','{}',?)""", (row["task_id"], owner_sub, now))
            connection.commit()
        return len(rows)

    def record_event(self, owner_sub: str, task_id: UUID, event_type: str, event_data: dict[str, object]) -> bool:
        with closing(self._connect()) as connection:
            row = connection.execute("SELECT 1 FROM tasks WHERE owner_sub = ? AND task_id = ?", (owner_sub, str(task_id))).fetchone()
            if row is None:
                return False
            connection.execute(
                "INSERT INTO task_events(task_id, owner_sub, event_type, event_data, created_at) VALUES(?, ?, ?, ?, ?)",
                (str(task_id), owner_sub, event_type, json.dumps(event_data, separators=(",", ":")), _timestamp()),
            )
            return True


def _timestamp() -> str:
    return datetime.now(UTC).isoformat()


def _task(row: sqlite3.Row) -> StoredTask:
    return StoredTask(
        task_id=UUID(row["task_id"]),
        owner_sub=row["owner_sub"],
        device_id=UUID(row["device_id"]),
        instruction=row["instruction"],
        status=row["status"],
        epoch=row["epoch"],
        created_at=datetime.fromisoformat(row["created_at"]),
        updated_at=datetime.fromisoformat(row["updated_at"]),
    )
