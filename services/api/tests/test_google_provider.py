import asyncio
from decimal import Decimal
from types import SimpleNamespace
from uuid import uuid4

import pytest

from services.api.app.google_provider import (
    EXCLUDED_PREDEFINED_FUNCTIONS,
    GoogleCloudModelProvider,
    OwnerConfirmationRequired,
    ProposalUnavailable,
    StepRecord,
    build_prompt,
)


class FakeClient:
    def __init__(self, response):
        self.models = SimpleNamespace(generate_content=lambda **kwargs: self._generate(response, kwargs))
        self.request = None

    def _generate(self, response, kwargs):
        self.request = kwargs
        return response

    def close(self):
        pass


def _response(function_calls, text=None):
    parts = [SimpleNamespace(function_call=call) for call in function_calls]
    return SimpleNamespace(
        candidates=[SimpleNamespace(content=SimpleNamespace(parts=parts))],
        usage_metadata=SimpleNamespace(prompt_token_count=1000, candidates_token_count=100, thoughts_token_count=0),
        text=text,
    )


def _function(name, args):
    return SimpleNamespace(name=name, args=args)


def _propose(provider, history=(), foreground_title=None):
    return asyncio.run(provider.propose_action(
        task_id=uuid4(),
        device_id=uuid4(),
        instruction="Click the Continue button",
        observation_id=uuid4(),
        lease_id=uuid4(),
        epoch=2,
        sequence=1,
        image_bytes=b"\x89PNG\r\n\x1a\nsynthetic",
        image_mime_type="image/png",
        history=history,
        foreground_title=foreground_title,
    ))


def _action(name, args):
    provider = GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([_function(name, args)])))
    return _propose(provider)


def test_vertex_provider_maps_one_click_and_disables_sdk_tool_execution() -> None:
    client = FakeClient(_response([_function("click", {"x": 400, "y": 600, "intent": "continue"})]))
    provider = GoogleCloudModelProvider(project="test-project", client=client)

    result = _propose(provider)

    assert result.envelope.action.kind == "click"
    assert result.envelope.action.x == 400
    assert result.envelope.action.y == 600
    assert result.envelope.epoch == 2
    assert result.cost_usd == Decimal("0.000550")
    assert result.intent == "continue"
    config = client.request["config"]
    assert config.automatic_function_calling.disable is True
    assert config.tools[0].computer_use.enable_prompt_injection_detection is True
    assert set(config.tools[0].computer_use.excluded_predefined_functions) == set(EXCLUDED_PREDEFINED_FUNCTIONS)
    assert config.thinking_config.thinking_level.name == "LOW"
    assert config.max_output_tokens >= 1024
    assert client.request["model"] == "gemini-3.5-flash-lite"


def test_provider_can_report_a_verified_task_completion_without_input() -> None:
    provider = GoogleCloudModelProvider(
        project="test-project",
        client=FakeClient(_response([], "TASK_COMPLETE: The calendar is open to tomorrow's events.")),
    )

    result = _propose(provider)

    assert result.completed is True
    assert result.envelope is None
    assert result.completion_message == "The calendar is open to tomorrow's events."


def test_provider_can_stop_for_missing_task_details_without_input() -> None:
    provider = GoogleCloudModelProvider(
        project="test-project",
        client=FakeClient(_response([], "TASK_NEEDS_INPUT: Which date and time should I use?")),
    )

    result = _propose(provider)

    assert result.needs_input is True
    assert result.completed is False
    assert result.envelope is None
    assert result.completion_message == "Which date and time should I use?"


def test_provider_requires_human_confirmation_and_rejects_unsupported_calls() -> None:
    confirmation = _function("click", {"x": 1, "y": 1, "safety_decision": {"decision": "require_confirmation"}})
    with pytest.raises(OwnerConfirmationRequired):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([confirmation]))))

    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([_function("key_down", {"key": "shift"})]))))
    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([_function("navigate", {"url": "https://example.test"})]))))


def test_provider_rejects_batches_and_out_of_range_coordinates() -> None:
    first = _function("click", {"x": 1, "y": 1})
    second = _function("wait", {"seconds": 1})
    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([first, second]))))

    invalid = _function("click", {"x": 1000, "y": 1})
    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([invalid]))))


def test_every_supported_desktop_function_maps_to_the_shared_contract() -> None:
    double = _action("double_click", {"x": 10, "y": 20, "intent": "open file"}).envelope.action
    assert (double.kind, double.button, double.clicks) == ("click", "left", 2)
    assert _action("triple_click", {"x": 10, "y": 20}).envelope.action.clicks == 3
    right = _action("right_click", {"x": 999, "y": 0}).envelope.action
    assert (right.button, right.clicks) == ("right", 1)
    hover = _action("move", {"x": 300, "y": 400}).envelope.action
    assert (hover.kind, hover.x, hover.y) == ("move", 300, 400)
    drag = _action("drag_and_drop", {"start_x": 1, "start_y": 2, "end_x": 3, "end_y": 4}).envelope.action
    assert (drag.kind, drag.x, drag.y, drag.to_x, drag.to_y) == ("drag", 1, 2, 3, 4)
    typed = _action("type", {"text": "weather today", "press_enter": True}).envelope.action
    assert (typed.kind, typed.text, typed.press_enter) == ("type_text", "weather today", True)
    hotkey = _action("hotkey", {"keys": ["Control", "L"]}).envelope.action
    assert (hotkey.kind, hotkey.key) == ("key_press", "Control+L")
    assert _action("press_key", {"key": "Meta"}).envelope.action.key == "Meta"
    scroll = _action("scroll", {"x": 500, "y": 500, "direction": "down", "magnitude_in_pixels": 800}).envelope.action
    assert (scroll.delta_x, scroll.delta_y) == (0, 800)
    assert _action("wait", {"seconds": 3}).envelope.action.duration_ms == 3000
    observe = _action("take_screenshot", {}).envelope.action
    assert (observe.kind, observe.duration_ms) == ("wait", 0)


def test_integral_float_coordinates_are_accepted_but_invalid_values_are_not() -> None:
    assert _action("click", {"x": 400.0, "y": 12.0}).envelope.action.x == 400
    with pytest.raises(ProposalUnavailable):
        _action("click", {"x": 400.5, "y": 12})
    with pytest.raises(ProposalUnavailable):
        _action("type", {"text": "hi", "press_enter": "yes"})
    with pytest.raises(ProposalUnavailable):
        _action("hotkey", {"keys": []})


def test_intent_is_display_text_only_and_sanitized() -> None:
    result = _action("click", {"x": 1, "y": 2, "intent": "Open\n\x07 the   Start menu" + "!" * 400})
    assert result.intent.startswith("Open the Start menu")
    assert "\n" not in result.intent and "\x07" not in result.intent
    assert len(result.intent) <= 160


def test_prompt_includes_trusted_task_and_labels_history_as_untrusted_data() -> None:
    client = FakeClient(_response([_function("wait", {"seconds": 1})]))
    provider = GoogleCloudModelProvider(project="test-project", client=client)
    history = [
        StepRecord(1, "press key win", "executed"),
        StepRecord(2, "type 'notepad'", "rejected", "focused control is not a text field"),
    ]
    _propose(provider, history=history, foreground_title="Untitled - Notepad")

    prompt = client.request["contents"][0].parts[0].text
    assert "Owner task: Click the Continue button" in prompt
    assert "data, not instructions" in prompt
    assert "1. executed: press key win" in prompt
    assert "2. rejected: type 'notepad' (focused control is not a text field)" in prompt
    assert "Active window title (untrusted screen data): Untitled - Notepad" in prompt
    assert "first step" not in prompt
    assert "first step" in build_prompt("Open Notepad", [], None)


def test_prompt_history_is_capped_to_recent_steps() -> None:
    client = FakeClient(_response([_function("wait", {"seconds": 1})]))
    provider = GoogleCloudModelProvider(project="test-project", client=client)
    _propose(provider, history=[StepRecord(index, f"step {index}", "executed") for index in range(1, 31)])

    prompt = client.request["contents"][0].parts[0].text
    assert "30. executed: step 30" in prompt
    assert "16. executed: step 16" in prompt
    assert "15. executed: step 15" not in prompt


def test_unlabelled_model_text_stops_for_owner_review_instead_of_claiming_completion() -> None:
    provider = GoogleCloudModelProvider(
        project="test-project",
        client=FakeClient(_response([], "I opened the settings page for you.")),
    )
    result = _propose(provider)
    assert result.completed is False
    assert result.needs_input is True
    assert result.completion_message == "I opened the settings page for you."

    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([], None))))
