from __future__ import annotations

from dataclasses import dataclass


@dataclass
class ModelRouteState:
    cheap_model: str = "gemini-3.5-flash-lite"
    recovery_model: str = "gemini-3.8-flash"
    max_ordinary_retries: int = 2
    max_recovery_turns: int = 3
    recovery_enabled: bool = True
    recovery_turns_used: int = 0


@dataclass(frozen=True)
class RouteDecision:
    model_id: str | None
    reason: str


def select_model(state: ModelRouteState, *, failed_attempts: int, budget_available: bool) -> RouteDecision:
    if not budget_available:
        return RouteDecision(None, "model budget unavailable")
    if failed_attempts <= state.max_ordinary_retries:
        return RouteDecision(state.cheap_model, "default low-cost route")
    if not state.recovery_enabled:
        return RouteDecision(None, "recovery route disabled")
    if state.recovery_turns_used >= state.max_recovery_turns:
        return RouteDecision(None, "recovery turn cap reached")
    state.recovery_turns_used += 1
    return RouteDecision(state.recovery_model, "bounded recovery after repeated failure")
