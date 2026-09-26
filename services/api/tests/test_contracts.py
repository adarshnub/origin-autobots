import json
from pathlib import Path
from uuid import UUID

import pytest
from pydantic import ValidationError

from packages.contracts.generated.python.action import ActionEnvelope


ROOT = Path(__file__).resolve().parents[3]
FIXTURES = ROOT / "packages" / "contracts" / "fixtures"


def test_click_fixture_matches_shared_contract() -> None:
    payload = json.loads((FIXTURES / "action-click.v1.json").read_text(encoding="utf-8"))
    action = ActionEnvelope.model_validate(payload)
    assert action.action.kind == "click"
    assert action.action.x == 420
    assert isinstance(action.action_id, UUID)


def test_shared_schema_is_valid_json_and_defines_required_envelope_fields() -> None:
    schema_file = ROOT / "packages" / "contracts" / "schemas" / "action-envelope.schema.json"
    schema = json.loads(schema_file.read_text(encoding="utf-8"))
    assert schema["$schema"].endswith("2020-12/schema")
    assert set(schema["required"]) == {
        "schema_version", "task_id", "device_id", "action_id", "observation_id", "lease_id", "epoch", "sequence", "action"
    }
    kinds = {option["properties"]["kind"]["const"] for option in schema["properties"]["action"]["oneOf"]}
    assert kinds == {"click", "type_text", "key_press", "scroll", "wait", "move", "drag"}


def test_python_contract_matches_every_schema_action_kind() -> None:
    schema_file = ROOT / "packages" / "contracts" / "schemas" / "action-envelope.schema.json"
    schema = json.loads(schema_file.read_text(encoding="utf-8"))
    for option in schema["properties"]["action"]["oneOf"]:
        allowed = set(option["properties"])
        kind = option["properties"]["kind"]["const"]
        model = {
            "click": "ClickAction", "type_text": "TypeTextAction", "key_press": "KeyPressAction", "scroll": "ScrollAction",
            "wait": "WaitAction", "move": "MoveAction", "drag": "DragAction",
        }[kind]
        from packages.contracts.generated.python import action as generated

        assert set(getattr(generated, model).model_fields) == allowed, kind


def test_extended_action_fixtures_match_shared_contract() -> None:
    drag = ActionEnvelope.model_validate(json.loads((FIXTURES / "action-drag.v1.json").read_text(encoding="utf-8")))
    assert (drag.action.kind, drag.action.to_x) == ("drag", 800)
    typed = ActionEnvelope.model_validate(json.loads((FIXTURES / "action-type-enter.v1.json").read_text(encoding="utf-8")))
    assert typed.action.press_enter is True
    click = ActionEnvelope.model_validate(json.loads((FIXTURES / "action-click.v1.json").read_text(encoding="utf-8")))
    assert click.action.clicks == 1


def test_click_count_is_bounded() -> None:
    payload = json.loads((FIXTURES / "action-click.v1.json").read_text(encoding="utf-8"))
    payload["action"]["clicks"] = 4
    with pytest.raises(ValidationError):
        ActionEnvelope.model_validate(payload)


def test_out_of_range_coordinate_is_rejected() -> None:
    payload = json.loads((FIXTURES / "action-invalid-coordinate.v1.json").read_text(encoding="utf-8"))
    with pytest.raises(ValidationError):
        ActionEnvelope.model_validate(payload)


def test_unknown_fields_are_rejected() -> None:
    payload = json.loads((FIXTURES / "action-click.v1.json").read_text(encoding="utf-8"))
    payload["model_says_approved"] = True
    with pytest.raises(ValidationError):
        ActionEnvelope.model_validate(payload)
