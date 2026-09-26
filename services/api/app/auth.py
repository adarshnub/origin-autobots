from __future__ import annotations

import os
from dataclasses import dataclass
from functools import lru_cache

import jwt
from fastapi import HTTPException, status
from jwt import PyJWKClient


OWNER_GROUP = "autobots-owners"
PILOT_GROUP = "autobots-pilots"


@dataclass(frozen=True)
class OwnerPrincipal:
    subject: str
    username: str
    role: str = "owner"


@dataclass(frozen=True)
class CognitoSettings:
    region: str
    user_pool_id: str
    app_client_id: str
    owner_group: str = OWNER_GROUP

    @property
    def issuer(self) -> str:
        return f"https://cognito-idp.{self.region}.amazonaws.com/{self.user_pool_id}"

    @property
    def jwks_url(self) -> str:
        return f"{self.issuer}/.well-known/jwks.json"


def configured_cognito() -> CognitoSettings | None:
    region = os.getenv("AWS_REGION") or os.getenv("AWS_DEFAULT_REGION")
    pool_id = os.getenv("COGNITO_USER_POOL_ID")
    client_id = os.getenv("COGNITO_APP_CLIENT_ID")
    if not all((region, pool_id, client_id)):
        return None
    return CognitoSettings(region=region, user_pool_id=pool_id, app_client_id=client_id)


@lru_cache(maxsize=8)
def _jwk_client(url: str) -> PyJWKClient:
    return PyJWKClient(url, timeout=5, cache_jwk_set=True, lifespan=300)


def require_owner(authorization: str | None) -> OwnerPrincipal:
    principal = require_member(authorization)
    if principal.role != "owner" or not os.getenv("AUTOBOTS_OWNER_SUB") or principal.subject != os.getenv("AUTOBOTS_OWNER_SUB"):
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Owner access is required.")
    return principal


def require_member(authorization: str | None) -> OwnerPrincipal:
    settings = configured_cognito()
    if settings is None:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail={"code": "authentication_not_configured", "message": "Protected API access is disabled until Cognito is configured."},
        )
    if not authorization or not authorization.startswith("Bearer "):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="A Cognito bearer token is required.")

    token = authorization.removeprefix("Bearer ").strip()
    if not token:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="A Cognito bearer token is required.")

    try:
        signing_key = _jwk_client(settings.jwks_url).get_signing_key_from_jwt(token).key
        claims = jwt.decode(
            token,
            signing_key,
            algorithms=["RS256"],
            issuer=settings.issuer,
            options={"verify_aud": False, "require": ["exp", "iat", "sub", "token_use", "client_id"]},
            leeway=30,
        )
    except Exception as error:
        # Provider/JWKS errors are intentionally generic; never include the token or URL in client logs.
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="The Cognito session is invalid or expired.") from error

    groups = claims.get("cognito:groups") or []
    if claims.get("token_use") != "access" or claims.get("client_id") != settings.app_client_id:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="The Cognito access token is for another client.")
    if settings.owner_group in groups and claims["sub"] == os.getenv("AUTOBOTS_OWNER_SUB"):
        role = "owner"
    elif PILOT_GROUP in groups:
        role = "pilot"
    else:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="This account is not enrolled in the Autobots pilot.")

    return OwnerPrincipal(subject=str(claims["sub"]), username=str(claims.get("username", "")), role=role)
