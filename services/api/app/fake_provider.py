from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol
from uuid import UUID, uuid4

from packages.contracts.generated.python.action import ActionEnvelope, WaitAction


class ModelProvider(Protocol):
    async def propose_wait(self, *, task_id: UUID, device_id: UUID, observation_id: UUID, lease_id: UUID, epoch: int, sequence: int) -> ActionEnvelope: ...


@dataclass(frozen=True)
class FakeModelProvider:
    """Deterministic provider used by tests and local orchestration only."""

    wait_duration_ms: int = 0

    async def propose_wait(self, *, task_id: UUID, device_id: UUID, observation_id: UUID, lease_id: UUID, epoch: int, sequence: int) -> ActionEnvelope:
        return ActionEnvelope(
            schema_version=1,
            task_id=task_id,
            device_id=device_id,
            action_id=uuid4(),
            observation_id=observation_id,
            lease_id=lease_id,
            epoch=epoch,
            sequence=sequence,
            action=WaitAction(kind="wait", duration_ms=self.wait_duration_ms),
        )
