from types import SimpleNamespace

import pytest
from fastapi import HTTPException

from services.api.app import auth


def _configure(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("AWS_REGION", "ap-south-1")
    monkeypatch.setenv("COGNITO_USER_POOL_ID", "ap-south-1_testpool")
    monkeypatch.setenv("COGNITO_APP_CLIENT_ID", "test-client")
    monkeypatch.setenv("AUTOBOTS_OWNER_SUB", "owner-subject")
    monkeypatch.setattr(auth, "_jwk_client", lambda _url: SimpleNamespace(get_signing_key_from_jwt=lambda _token: SimpleNamespace(key="test-key")))


def test_missing_cognito_configuration_fails_closed(monkeypatch: pytest.MonkeyPatch) -> None:
    for key in ("AWS_REGION", "AWS_DEFAULT_REGION", "COGNITO_USER_POOL_ID", "COGNITO_APP_CLIENT_ID"):
        monkeypatch.delenv(key, raising=False)
    with pytest.raises(HTTPException) as error:
        auth.require_owner("Bearer token")
    assert error.value.status_code == 503


def test_owner_access_token_must_match_client_and_owner_group(monkeypatch: pytest.MonkeyPatch) -> None:
    _configure(monkeypatch)
    monkeypatch.setattr(auth.jwt, "decode", lambda *_args, **_kwargs: {
        "sub": "owner-subject",
        "username": "owner",
        "client_id": "test-client",
        "token_use": "access",
        "cognito:groups": [auth.OWNER_GROUP],
    })

    assert auth.require_owner("Bearer valid-token") == auth.OwnerPrincipal("owner-subject", "owner")


def test_non_owner_token_is_forbidden(monkeypatch: pytest.MonkeyPatch) -> None:
    _configure(monkeypatch)
    monkeypatch.setattr(auth.jwt, "decode", lambda *_args, **_kwargs: {
        "sub": "other-subject",
        "client_id": "test-client",
        "token_use": "access",
        "cognito:groups": [],
    })

    with pytest.raises(HTTPException) as error:
        auth.require_owner("Bearer valid-token")
    assert error.value.status_code == 403


def test_pilot_can_be_member_but_never_owner(monkeypatch: pytest.MonkeyPatch) -> None:
    _configure(monkeypatch)
    monkeypatch.setattr(auth.jwt, "decode", lambda *_args, **_kwargs: {
        "sub": "pilot-subject", "client_id": "test-client", "token_use": "access",
        "cognito:groups": [auth.PILOT_GROUP],
    })
    assert auth.require_member("Bearer valid-token").role == "pilot"
    with pytest.raises(HTTPException) as error:
        auth.require_owner("Bearer valid-token")
    assert error.value.status_code == 403
