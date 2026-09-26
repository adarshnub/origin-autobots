from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class ClickAction(StrictModel):
    kind: Literal["click"]
    x: int = Field(ge=0, le=999)
    y: int = Field(ge=0, le=999)
    button: Literal["left", "middle", "right"]
    clicks: int = Field(default=1, ge=1, le=3)


class TypeTextAction(StrictModel):
    kind: Literal["type_text"]
    text: str = Field(min_length=1, max_length=4000)
    press_enter: bool = False


class KeyPressAction(StrictModel):
    kind: Literal["key_press"]
    key: str = Field(min_length=1, max_length=64)


class ScrollAction(StrictModel):
    kind: Literal["scroll"]
    x: int = Field(ge=0, le=999)
    y: int = Field(ge=0, le=999)
    delta_x: int = Field(ge=-1200, le=1200)
    delta_y: int = Field(ge=-1200, le=1200)


class WaitAction(StrictModel):
    kind: Literal["wait"]
    duration_ms: int = Field(ge=0, le=5000)


class MoveAction(StrictModel):
    kind: Literal["move"]
    x: int = Field(ge=0, le=999)
    y: int = Field(ge=0, le=999)


class DragAction(StrictModel):
    kind: Literal["drag"]
    x: int = Field(ge=0, le=999)
    y: int = Field(ge=0, le=999)
    to_x: int = Field(ge=0, le=999)
    to_y: int = Field(ge=0, le=999)


ProposedAction = Annotated[
    ClickAction | TypeTextAction | KeyPressAction | ScrollAction | WaitAction | MoveAction | DragAction,
    Field(discriminator="kind"),
]


class ActionEnvelope(StrictModel):
    schema_version: Literal[1]
    task_id: UUID
    device_id: UUID
    action_id: UUID
    observation_id: UUID
    lease_id: UUID
    epoch: int = Field(ge=0)
    sequence: int = Field(ge=1)
    action: ProposedAction
