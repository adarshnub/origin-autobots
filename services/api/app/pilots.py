"""Invite-only pilot registry. Cognito authenticates; this database grants product access."""
from __future__ import annotations

import sqlite3
from contextlib import closing
from datetime import UTC, datetime
from pathlib import Path


class PilotStore:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        with closing(self._connect()) as db:
            db.execute("""CREATE TABLE IF NOT EXISTS pilot_users (
                subject TEXT PRIMARY KEY, email TEXT NOT NULL UNIQUE,
                username TEXT, purpose TEXT, enabled INTEGER NOT NULL DEFAULT 1,
                invited_at TEXT NOT NULL, onboarded_at TEXT)""")

    def _connect(self):
        db = sqlite3.connect(self.path, timeout=5)
        db.execute("PRAGMA busy_timeout=5000")
        return db

    def add(self, subject: str, email: str) -> None:
        with closing(self._connect()) as db:
            db.execute("INSERT INTO pilot_users(subject,email,invited_at) VALUES(?,?,?)",
                       (subject, email.lower(), datetime.now(UTC).isoformat()))
            db.commit()

    def get(self, subject: str) -> dict | None:
        with closing(self._connect()) as db:
            row = db.execute("SELECT subject,email,username,purpose,enabled,invited_at,onboarded_at FROM pilot_users WHERE subject=?", (subject,)).fetchone()
        return dict(zip(("subject", "email", "username", "purpose", "enabled", "invited_at", "onboarded_at"), row)) if row else None

    def list(self) -> list[dict]:
        with closing(self._connect()) as db:
            rows = db.execute("SELECT subject,email,username,purpose,enabled,invited_at,onboarded_at FROM pilot_users ORDER BY invited_at DESC").fetchall()
        keys = ("subject", "email", "username", "purpose", "enabled", "invited_at", "onboarded_at")
        return [dict(zip(keys, row)) for row in rows]

    def onboard(self, subject: str, username: str, purpose: str) -> bool:
        with closing(self._connect()) as db:
            cursor = db.execute("""UPDATE pilot_users SET username=?,purpose=?,onboarded_at=?
                WHERE subject=? AND enabled=1 AND onboarded_at IS NULL""",
                (username.strip(), purpose.strip(), datetime.now(UTC).isoformat(), subject))
            db.commit()
        return cursor.rowcount == 1
