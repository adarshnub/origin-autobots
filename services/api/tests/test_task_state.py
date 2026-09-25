from uuid import UUID

import pytest

from packages.contracts.generated.python.action import ActionEnvelope, ClickAction
from services.api.app.task_state import ActionLeaseGuard, InvalidTransition, StaleAction, TaskStateMachine, TaskStatus


TASK_ID = UUID("11111111-1111-4111-8111-111111111111")
DEVICE_ID = UUID("22222222-2222-4222-8222-222222222222")
OBSERVATION_ID = UUID("44444444-4444-4444-8444-444444444444")
LEASE_ID = UUID("55555555-5555-4555-8555-555555555555")


def envelope(*, sequence: int = 1, epoch: int = 0, action_id: UUID = UUID("33333333-3333-4333-8333-333333333333")) -> ActionEnvelope:
    return ActionEnvelope(
        schema_version=1,
        task_id=TASK_ID,
        device_id=DEVICE_ID,
        action_id=action_id,
        observation_id=OBSERVATION_ID,
        lease_id=LEASE_ID,
        epoch=epoch,
        sequence=sequence,
        action=ClickAction(kind="click", x=10, y=10, button="left"),
    )


def test_task_state_follows_observe_plan_execute_verify_path() -> None:
    machine = TaskStateMachine()
    for state in (TaskStatus.OBSERVING, TaskStatus.PLANNING, TaskStatus.VALIDATING, TaskStatus.EXECUTING, TaskStatus.VERIFYING, TaskStatus.COMPLETED):
        machine.transition(state)
    assert machine.history[-1] is TaskStatus.COMPLETED


def test_terminal_state_cannot_be_reopened() -> None:
    machine = TaskStateMachine(status=TaskStatus.COMPLETED)
    with pytest.raises(InvalidTransition):
        machine.transition(TaskStatus.OBSERVING)


def test_stop_advances_epoch_and_is_idempotent() -> None:
    machine = TaskStateMachine(status=TaskStatus.EXECUTING)
    assert machine.stop() == 1
    assert machine.status is TaskStatus.STOPPED
    assert machine.stop() == 1


def test_lease_guard_rejects_duplicates_stale_observations_and_old_epochs() -> None:
    guard = ActionLeaseGuard(TASK_ID, DEVICE_ID, LEASE_ID, OBSERVATION_ID)
    first = envelope()
    guard.accept(first)
    with pytest.raises(StaleAction, match="duplicate"):
        guard.accept(first)
    with pytest.raises(StaleAction, match="sequence"):
        guard.accept(envelope(sequence=1, action_id=UUID("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")))

    guard.invalidate(1)
    with pytest.raises(StaleAction, match="epoch"):
        guard.accept(envelope(sequence=2))
