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
    assert len(schema["properties"]["action"]["oneOf"]) == 5


def test_out_of_range_coordinate_is_rejected() -> None:
    payload = json.loads((FIXTURES / "action-invalid-coordinate.v1.json").read_text(encoding="utf-8"))
    with pytest.raises(ValidationError):
        ActionEnvelope.model_validate(payload)


def test_unknown_fields_are_rejected() -> None:
    payload = json.loads((FIXTURES / "action-click.v1.json").read_text(encoding="utf-8"))
    payload["model_says_approved"] = True
    with pytest.raises(ValidationError):
        ActionEnvelope.model_validate(payload)
