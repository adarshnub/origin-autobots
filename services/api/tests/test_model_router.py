from services.api.app.model_router import ModelRouteState, select_model


def test_flash_lite_is_the_default_and_recovery_is_bounded() -> None:
    state = ModelRouteState(max_ordinary_retries=2, max_recovery_turns=2)
    assert select_model(state, failed_attempts=0, budget_available=True).model_id == "gemini-3.5-flash-lite"
    assert select_model(state, failed_attempts=3, budget_available=True).model_id == "gemini-3.8-flash"
    assert select_model(state, failed_attempts=3, budget_available=True).model_id == "gemini-3.8-flash"
    assert select_model(state, failed_attempts=3, budget_available=True).model_id is None


def test_router_never_uses_more_models_after_budget_exhaustion() -> None:
    state = ModelRouteState()
    assert select_model(state, failed_attempts=0, budget_available=False).model_id is None
