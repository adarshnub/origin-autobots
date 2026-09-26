"""Private usage reporting. Provider estimates and billing totals are never added together."""
from __future__ import annotations

import asyncio
import json
import os
import re
import sqlite3
import time
from contextlib import closing
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path


class UsageStore:
    def __init__(self, path: Path):
        self.path = path
        with closing(sqlite3.connect(path, timeout=5)) as db:
            db.execute("""CREATE TABLE IF NOT EXISTS provider_usage (
                request_id TEXT PRIMARY KEY, owner_sub TEXT NOT NULL, created_at TEXT NOT NULL,
                provider TEXT NOT NULL, operation TEXT NOT NULL, model TEXT,
                input_tokens INTEGER, output_tokens INTEGER, estimated_usd TEXT,
                outcome TEXT NOT NULL, latency_ms INTEGER NOT NULL)""")
            db.commit()

    def record(self, request_id, owner: str, operation: str, outcome: str, started: float,
               result=None, provider: str = "gcp") -> None:
        with closing(sqlite3.connect(self.path, timeout=5)) as db:
            db.execute("INSERT OR IGNORE INTO provider_usage VALUES (?,?,?,?,?,?,?,?,?,?,?)", (
                str(request_id), owner, datetime.now(UTC).isoformat(), provider, operation,
                getattr(result, "model_id", None), getattr(result, "input_tokens", None),
                getattr(result, "output_tokens", None),
                str(result.cost_usd) if result is not None else None,
                outcome, max(0, round((time.perf_counter() - started) * 1000))))
            db.commit()

    def summary(self, owner: str, inference, speech, now: datetime | None = None) -> dict:
        now = now or datetime.now(UTC)
        day, month = now.date().isoformat(), now.strftime("%Y-%m")
        with closing(sqlite3.connect(self.path, timeout=5)) as db:
            rows = db.execute("""SELECT provider,operation,model,input_tokens,output_tokens,
                estimated_usd,outcome,latency_ms FROM provider_usage
                WHERE owner_sub=? AND substr(created_at,1,7)=?""", (owner, month)).fetchall()
            first = db.execute("SELECT MIN(created_at) FROM provider_usage WHERE owner_sub=?", (owner,)).fetchone()[0]
            models: dict[tuple, dict] = {}
            for provider, operation, model, inp, out, cost, outcome, latency in rows:
                key = provider, operation, model
                item = models.setdefault(key, dict(provider=provider, operation=operation, model=model,
                    requests=0, input_tokens=0, output_tokens=0, estimated_usd=Decimal(0), unknown_cost_requests=0,
                    failed_requests=0, total_latency_ms=0))
                item["requests"] += 1
                item["input_tokens"] += inp or 0
                item["output_tokens"] += out or 0
                item["estimated_usd"] += Decimal(cost) if cost is not None else Decimal(0)
                item["unknown_cost_requests"] += cost is None
                item["failed_requests"] += outcome not in {"response", "transcribed", "no_speech"}
                item["total_latency_ms"] += latency
            for item in models.values():
                item["estimated_usd"] = str(item["estimated_usd"])
            channels = []
            for operation, ledger in (("desktop", inference), ("speech", speech)):
                # Legacy speech rows predate owner attribution. They are excluded from
                # per-user usage and remain in the global budget ledger.
                if operation == "desktop":
                    records = db.execute("""SELECT r.utc_day,r.reserved_usd,r.actual_usd,r.status
                        FROM inference_reservations r JOIN tasks t ON t.task_id=r.task_id
                        WHERE t.owner_sub=? AND r.utc_month=?""", (owner, month)).fetchall()
                else:
                    records = db.execute("""SELECT utc_day,reserved_usd,actual_usd,status
                        FROM speech_reservations WHERE utc_month=? AND owner_sub=?""", (month, owner)).fetchall()
                relevant = [r for r in records if r[3] != "released"]
                def amount(r):
                    return Decimal(r[1] if r[3] == "pending" else r[2] or "0")
                channels.append(dict(operation=operation, provider="gcp", currency="USD",
                    day_requests=sum(r[0] == day for r in relevant), month_requests=len(relevant),
                    day_budget_accounted_usd=str(sum((amount(r) for r in relevant if r[0] == day), Decimal(0))),
                    month_budget_accounted_usd=str(sum((amount(r) for r in relevant), Decimal(0))),
                    pending_requests=sum(r[3] == "pending" for r in relevant),
                    day_limit_usd=str(ledger.limits.per_day_usd), month_limit_usd=str(ledger.limits.per_month_usd)))
        return dict(as_of=now.isoformat(), utc_day=day, utc_month=month, currency="USD",
            token_tracking_since=first, channels=channels, models=list(models.values()),
            note="Budget accounted includes pending reservations and conservative charges for failed requests. "
                 "Token details cover requests since detailed tracking began. Older speech rows without user attribution remain in global limits. "
                 "Request costs are estimates, not cloud invoices; do not add them to GCP billing totals.")


_billing_cache: tuple[float, dict] | None = None
_billing_lock = asyncio.Lock()


async def aws_billing() -> dict:
    global _billing_cache
    async with _billing_lock:
        if _billing_cache and time.monotonic() < _billing_cache[0]:
            return _billing_cache[1]
        account = os.getenv("AUTOBOTS_AWS_ACCOUNT_ID", "")
        budget = os.getenv("AUTOBOTS_AWS_BUDGET_NAME", "autobots-dev-monthly-account-alert")
        base = dict(provider="aws", source="AWS Budgets", scope="Entire AWS account, including other projects",
                    status="unavailable", amount=None, currency="USD", as_of=None)
        if not re.fullmatch(r"\d{12}", account):
            return {**base, "note": "AWS billing account is not configured on the service."}
        process = None
        try:
            process = await asyncio.create_subprocess_exec(
                "aws", "budgets", "describe-budget", "--account-id", account, "--budget-name", budget,
                "--region", "us-east-1", "--output", "json", "--no-cli-pager",
                "--cli-connect-timeout", "5", "--cli-read-timeout", "10",
                stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
            stdout, _ = await asyncio.wait_for(process.communicate(), timeout=18)
            if process.returncode != 0:
                raise ValueError("Billing access unavailable")
            data = json.loads(stdout)["Budget"]
            actual = data["CalculatedSpend"]["ActualSpend"]
            amount = Decimal(actual["Amount"])
            if not amount.is_finite():
                raise ValueError("Invalid billing amount")
            result = {**base, "status": "available", "amount": str(amount), "currency": actual["Unit"],
                "as_of": data.get("LastUpdatedTime"), "retrieved_at": datetime.now(UTC).isoformat(),
                "monthly_limit": data.get("BudgetLimit", {}).get("Amount"),
                "note": "Month-to-date reported account spend. AWS billing is delayed; cached for one hour."}
        except (OSError, ValueError, KeyError, asyncio.TimeoutError):
            result = {**base, "note": "AWS billing could not be retrieved. No zero-cost value has been substituted."}
        finally:
            if process is not None and process.returncode is None:
                process.kill()
                await process.communicate()
        _billing_cache = (time.monotonic() + (3600 if result["status"] == "available" else 60), result)
        return result
