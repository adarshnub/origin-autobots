"""Narrow Cognito administrator operations; all commands use the instance role."""
from __future__ import annotations

import asyncio
import json
import os


class InviteUnavailable(RuntimeError):
    pass


async def _cognito(*arguments: str) -> dict:
    region = os.getenv("AWS_REGION") or os.getenv("AWS_DEFAULT_REGION")
    pool = os.getenv("COGNITO_USER_POOL_ID")
    if not region or not pool:
        raise InviteUnavailable("Cognito is not configured")
    try:
        process = await asyncio.create_subprocess_exec(
            "aws", "cognito-idp", *arguments, "--user-pool-id", pool, "--region", region,
            "--output", "json", "--no-cli-pager", "--cli-connect-timeout", "5", "--cli-read-timeout", "10",
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL)
        stdout, _ = await asyncio.wait_for(process.communicate(), timeout=20)
        if process.returncode != 0:
            raise InviteUnavailable("Cognito could not complete the account operation")
        return json.loads(stdout or b"{}")
    except (OSError, asyncio.TimeoutError, json.JSONDecodeError) as error:
        raise InviteUnavailable("Cognito account management is unavailable") from error


async def invite(email: str) -> str:
    """Send Cognito's one-time password email and assign the restricted pilot group."""
    result = await _cognito("admin-create-user", "--username", email,
                            "--user-attributes", f"Name=email,Value={email}", "Name=email_verified,Value=true",
                            "--desired-delivery-mediums", "EMAIL")
    attributes = {item["Name"]: item["Value"] for item in result.get("User", {}).get("Attributes", [])}
    subject = attributes.get("sub")
    if not subject:
        raise InviteUnavailable("Cognito did not return the invited user identity")
    await _cognito("admin-add-user-to-group", "--username", email, "--group-name", "autobots-pilots")
    return subject


async def disable_user(email: str) -> None:
    """Disable future sign-in and invalidate the user's Cognito sessions."""
    await _cognito("admin-disable-user", "--username", email)
    await _cognito("admin-user-global-sign-out", "--username", email)
