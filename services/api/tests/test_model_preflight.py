from types import SimpleNamespace

import pytest

from scripts.preflight.model_capabilities import run_live_probe


def test_probe_builds_one_non_executing_synthetic_image_request(monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]) -> None:
    genai = pytest.importorskip("google.genai")

    function_call = SimpleNamespace(
        name="click",
        args={"x": 500, "y": 500, "intent": "synthetic probe", "safety_decision": {"decision": "require_confirmation"}},
    )
    response = SimpleNamespace(
        candidates=[SimpleNamespace(content=SimpleNamespace(parts=[SimpleNamespace(function_call=function_call)]))],
        usage_metadata=SimpleNamespace(prompt_token_count=12, candidates_token_count=4),
    )

    class FakeModels:
        request = None

        def generate_content(self, **kwargs: object) -> SimpleNamespace:
            self.request = kwargs
            return response

    models = FakeModels()

    class FakeClient:
        def __init__(self, **kwargs: object) -> None:
            self.closed = False
            self.models = models
            self.options = kwargs

        def close(self) -> None:
            self.closed = True

    client = FakeClient()
    client_options: dict[str, object] = {}

    def make_client(**kwargs: object) -> FakeClient:
        client_options.update(kwargs)
        return client

    monkeypatch.setattr(genai, "Client", make_client)

    result = run_live_probe(project="synthetic-project", location="global", model="gemini-3.5-flash-lite", max_cost_usd=0.01)

    assert result == 0
    assert client.closed
    assert models.request is not None
    assert models.request["model"] == "gemini-3.5-flash-lite"
    config = models.request["config"]
    assert config.automatic_function_calling.disable is True
    assert config.max_output_tokens == 256
    assert client_options["http_options"].retry_options.attempts == 1
    report = capsys.readouterr().out
    assert "no proposal was executed" in report
    assert "Recognized proposal shapes: 1" in report
    assert "none were acknowledged" in report


def test_probe_rejects_an_allowance_above_the_one_request_cap(capsys: pytest.CaptureFixture[str]) -> None:
    assert run_live_probe(project="synthetic-project", location="global", model="gemini-3.5-flash-lite", max_cost_usd=0.051) == 2
    assert "no greater than $0.05" in capsys.readouterr().err
