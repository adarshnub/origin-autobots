from __future__ import annotations

import os


REQUIRED_INPUTS = (
    "AWS_PROFILE",
    "AWS_ACCOUNT_ID",
    "GCP_PROJECT_ID",
    "GCP_BILLING_ACCOUNT_ID",
    "OWNER_EMAIL",
    "AUTOBOTS_MAX_MODEL_COST_USD_PER_TASK",
    "AUTOBOTS_MAX_MODEL_COST_USD_PER_DAY",
    "AUTOBOTS_MAX_MODEL_COST_USD_PER_MONTH",
)


def main() -> int:
    missing = [name for name in REQUIRED_INPUTS if not os.environ.get(name)]
    print("Autobots cloud preflight (read-only; no cloud API calls)")
    for name in REQUIRED_INPUTS:
        print(f"{'SET' if os.environ.get(name) else 'MISSING'} {name}")
    if missing:
        print("\nCloud preflight is incomplete. No credentials or values were printed.")
        return 2
    print("\nRequired deployment inputs are present. Identity, permissions, quotas and budgets still need separate verification.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
