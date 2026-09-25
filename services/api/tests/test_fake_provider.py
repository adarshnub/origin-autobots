import asyncio
from uuid import uuid4

from services.api.app.fake_provider import FakeModelProvider


def test_fake_provider_returns_a_typed_non_mutating_wait_proposal() -> None:
    provider = FakeModelProvider(wait_duration_ms=250)
    task_id, device_id, observation_id, lease_id = (uuid4() for _ in range(4))
    result = asyncio.run(provider.propose_wait(
        task_id=task_id,
        device_id=device_id,
        observation_id=observation_id,
        lease_id=lease_id,
        epoch=0,
        sequence=1,
    ))

    assert result.action.kind == "wait"
    assert result.action.duration_ms == 250
    assert result.task_id == task_id
