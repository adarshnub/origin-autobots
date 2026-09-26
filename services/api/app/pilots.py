"""Invite-only pilot registry. Cognito authenticates; this database grants product access."""
from __future__ import annotations

import sqlite3
from contextlib import closing
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4


class PilotStore:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        with closing(self._connect()) as db:
            db.execute("""CREATE TABLE IF NOT EXISTS pilot_users (
                subject TEXT PRIMARY KEY, email TEXT NOT NULL UNIQUE,
                username TEXT, purpose TEXT, enabled INTEGER NOT NULL DEFAULT 1,
                invited_at TEXT NOT NULL, onboarded_at TEXT)""")
            columns = {row[1] for row in db.execute("PRAGMA table_info(pilot_users)")}
            if "revoked_at" not in columns:
                db.execute("ALTER TABLE pilot_users ADD COLUMN revoked_at TEXT")
            if "revocation_pending" not in columns:
                db.execute("ALTER TABLE pilot_users ADD COLUMN revocation_pending INTEGER NOT NULL DEFAULT 0")
            db.commit()

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
            row = db.execute("SELECT subject,email,username,purpose,enabled,invited_at,onboarded_at,revoked_at,revocation_pending FROM pilot_users WHERE subject=?", (subject,)).fetchone()
        return dict(zip(("subject", "email", "username", "purpose", "enabled", "invited_at", "onboarded_at", "revoked_at", "revocation_pending"), row)) if row else None

    def get_by_email(self, email: str) -> dict | None:
        with closing(self._connect()) as db:
            row = db.execute("SELECT subject FROM pilot_users WHERE email=?", (email.strip().lower(),)).fetchone()
        return self.get(row[0]) if row else None

    def list(self) -> list[dict]:
        with closing(self._connect()) as db:
            rows = db.execute("SELECT subject,email,username,purpose,enabled,invited_at,onboarded_at,revoked_at,revocation_pending FROM pilot_users ORDER BY invited_at DESC").fetchall()
        keys = ("subject", "email", "username", "purpose", "enabled", "invited_at", "onboarded_at", "revoked_at", "revocation_pending")
        return [dict(zip(keys, row)) for row in rows]

    def revoke(self, subject: str) -> dict | None:
        with closing(self._connect()) as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute("SELECT email FROM pilot_users WHERE subject=?", (subject,)).fetchone()
            if row is None:
                db.commit()
                return None
            db.execute("""UPDATE pilot_users SET enabled=0, revoked_at=COALESCE(revoked_at,?),
                revocation_pending=1 WHERE subject=?""", (datetime.now(UTC).isoformat(), subject))
            db.commit()
        return self.get(subject)

    def mark_cognito_disabled(self, subject: str) -> None:
        with closing(self._connect()) as db:
            db.execute("UPDATE pilot_users SET revocation_pending=0 WHERE subject=? AND enabled=0", (subject,))
            db.commit()

    def onboard(self, subject: str, username: str, purpose: str) -> bool:
        with closing(self._connect()) as db:
            cursor = db.execute("""UPDATE pilot_users SET username=?,purpose=?,onboarded_at=?
                WHERE subject=? AND enabled=1 AND onboarded_at IS NULL""",
                (username.strip(), purpose.strip(), datetime.now(UTC).isoformat(), subject))
            db.commit()
        return cursor.rowcount == 1


class PilotApplicationStore:
    """Public requests for access; an application never creates an account by itself."""

    _fields = ("id", "email", "name", "profession", "industry", "purpose", "status",
               "applied_at", "reviewed_at", "reviewer_sub", "approved_subject")

    def __init__(self, path: str | Path):
        self.path = Path(path)
        with closing(self._connect()) as db:
            db.execute("""CREATE TABLE IF NOT EXISTS pilot_applications (
                id TEXT PRIMARY KEY, email TEXT NOT NULL UNIQUE, name TEXT NOT NULL,
                profession TEXT NOT NULL, industry TEXT NOT NULL, purpose TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('pending','approving','approved','rejected')),
                applied_at TEXT NOT NULL, reviewed_at TEXT, reviewer_sub TEXT, approved_subject TEXT)""")
            db.execute("CREATE INDEX IF NOT EXISTS ix_pilot_applications_status ON pilot_applications(status, applied_at DESC)")
            db.commit()

    def _connect(self):
        db = sqlite3.connect(self.path, timeout=5)
        db.execute("PRAGMA busy_timeout=5000")
        return db

    def submit(self, email: str, name: str, profession: str, industry: str, purpose: str) -> None:
        with closing(self._connect()) as db:
            db.execute("""INSERT INTO pilot_applications
                (id,email,name,profession,industry,purpose,status,applied_at)
                VALUES(?,?,?,?,?,?,'pending',?)
                ON CONFLICT(email) DO UPDATE SET name=excluded.name,
                    profession=excluded.profession, industry=excluded.industry,
                    purpose=excluded.purpose, status='pending', applied_at=excluded.applied_at,
                    reviewed_at=NULL, reviewer_sub=NULL
                WHERE pilot_applications.status='rejected'""",
                (str(uuid4()), email.lower(), name, profession, industry, purpose, datetime.now(UTC).isoformat()))
            db.commit()

    def get(self, application_id: str) -> dict | None:
        with closing(self._connect()) as db:
            row = db.execute("SELECT " + ",".join(self._fields) + " FROM pilot_applications WHERE id=?", (application_id,)).fetchone()
        return dict(zip(self._fields, row)) if row else None

    def list(self) -> list[dict]:
        with closing(self._connect()) as db:
            rows = db.execute("SELECT " + ",".join(self._fields) + " FROM pilot_applications ORDER BY applied_at DESC").fetchall()
        return [dict(zip(self._fields, row)) for row in rows]

    def claim_approval(self, application_id: str, reviewer_sub: str) -> dict | None:
        now = datetime.now(UTC).isoformat()
        with closing(self._connect()) as db:
            db.execute("BEGIN IMMEDIATE")
            cursor = db.execute("""UPDATE pilot_applications SET status='approving',reviewed_at=?,reviewer_sub=?
                WHERE id=? AND status IN ('pending','rejected')""", (now, reviewer_sub, application_id))
            db.commit()
        return self.get(application_id) if cursor.rowcount == 1 else None

    def approve(self, application_id: str, subject: str) -> None:
        with closing(self._connect()) as db:
            db.execute("""UPDATE pilot_applications SET status='approved',approved_subject=?
                WHERE id=? AND status='approving'""", (subject, application_id))
            db.commit()

    def reject(self, application_id: str, reviewer_sub: str) -> bool:
        with closing(self._connect()) as db:
            cursor = db.execute("""UPDATE pilot_applications SET status='rejected',reviewed_at=?,reviewer_sub=?
                WHERE id=? AND status='pending'""", (datetime.now(UTC).isoformat(), reviewer_sub, application_id))
            db.commit()
        return cursor.rowcount == 1
