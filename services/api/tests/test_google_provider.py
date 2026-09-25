import asyncio
from decimal import Decimal
from types import SimpleNamespace
from uuid import uuid4

import pytest

from services.api.app.google_provider import (
    GoogleCloudModelProvider,
    OwnerConfirmationRequired,
    ProposalUnavailable,
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


def _propose(provider):
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
    ))


def test_vertex_provider_maps_one_click_and_disables_sdk_tool_execution() -> None:
    client = FakeClient(_response([_function("click", {"x": 400, "y": 600, "intent": "continue"})]))
    provider = GoogleCloudModelProvider(project="test-project", client=client)

    result = _propose(provider)

    assert result.envelope.action.kind == "click"
    assert result.envelope.action.x == 400
    assert result.envelope.action.y == 600
    assert result.envelope.epoch == 2
    assert result.cost_usd == Decimal("0.000550")
    config = client.request["config"]
    assert config.automatic_function_calling.disable is True
    assert config.tools[0].computer_use.enable_prompt_injection_detection is True
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
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([_function("double_click", {"x": 1, "y": 1})]))))


def test_provider_rejects_batches_and_out_of_range_coordinates() -> None:
    first = _function("click", {"x": 1, "y": 1})
    second = _function("wait", {"seconds": 1})
    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([first, second]))))

    invalid = _function("click", {"x": 1000, "y": 1})
    with pytest.raises(ProposalUnavailable):
        _propose(GoogleCloudModelProvider(project="test-project", client=FakeClient(_response([invalid]))))
