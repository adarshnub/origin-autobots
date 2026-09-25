from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum
from uuid import UUID

from packages.contracts.generated.python.action import ActionEnvelope


class TaskStatus(StrEnum):
    CREATED = "created"
    OBSERVING = "observing"
    PLANNING = "planning"
    VALIDATING = "validating"
    EXECUTING = "executing"
    VERIFYING = "verifying"
    AWAITING_APPROVAL = "awaiting_approval"
    PAUSED = "paused"
    COMPLETED = "completed"
    STOPPED = "stopped"
    FAILED = "failed"
    BUDGET_EXHAUSTED = "budget_exhausted"


_TRANSITIONS: dict[TaskStatus, frozenset[TaskStatus]] = {
    TaskStatus.CREATED: frozenset({TaskStatus.OBSERVING, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.OBSERVING: frozenset({TaskStatus.PLANNING, TaskStatus.PAUSED, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.PLANNING: frozenset({TaskStatus.VALIDATING, TaskStatus.AWAITING_APPROVAL, TaskStatus.PAUSED, TaskStatus.STOPPED, TaskStatus.FAILED, TaskStatus.BUDGET_EXHAUSTED}),
    TaskStatus.VALIDATING: frozenset({TaskStatus.EXECUTING, TaskStatus.OBSERVING, TaskStatus.AWAITING_APPROVAL, TaskStatus.PAUSED, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.EXECUTING: frozenset({TaskStatus.VERIFYING, TaskStatus.PAUSED, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.VERIFYING: frozenset({TaskStatus.OBSERVING, TaskStatus.COMPLETED, TaskStatus.AWAITING_APPROVAL, TaskStatus.PAUSED, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.AWAITING_APPROVAL: frozenset({TaskStatus.OBSERVING, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.PAUSED: frozenset({TaskStatus.OBSERVING, TaskStatus.STOPPED, TaskStatus.FAILED}),
    TaskStatus.COMPLETED: frozenset(),
    TaskStatus.STOPPED: frozenset(),
    TaskStatus.FAILED: frozenset(),
    TaskStatus.BUDGET_EXHAUSTED: frozenset(),
}


class InvalidTransition(ValueError):
    pass


class StaleAction(ValueError):
    pass


@dataclass
class TaskStateMachine:
    """Deterministic orchestration state; it never performs desktop input."""

    status: TaskStatus = TaskStatus.CREATED
    epoch: int = 0
    _history: list[TaskStatus] = field(default_factory=lambda: [TaskStatus.CREATED])

    @property
    def history(self) -> tuple[TaskStatus, ...]:
        return tuple(self._history)

    def transition(self, next_status: TaskStatus) -> None:
        if next_status not in _TRANSITIONS[self.status]:
            raise InvalidTransition(f"Cannot move task from {self.status} to {next_status}")
        self.status = next_status
        self._history.append(next_status)

    def stop(self) -> int:
        if self.status not in {TaskStatus.COMPLETED, TaskStatus.STOPPED, TaskStatus.FAILED, TaskStatus.BUDGET_EXHAUSTED}:
            self.transition(TaskStatus.STOPPED)
            self.epoch += 1
        return self.epoch


@dataclass
class ActionLeaseGuard:
    """Reject stale, replayed, cross-task and cross-device action proposals."""

    task_id: UUID
    device_id: UUID
    lease_id: UUID
    observation_id: UUID
    epoch: int = 0
    last_sequence: int = 0
    _action_ids: set[UUID] = field(default_factory=set)

    def validate(self, envelope: ActionEnvelope) -> None:
        checks = (
            (envelope.task_id == self.task_id, "wrong task"),
            (envelope.device_id == self.device_id, "wrong device"),
            (envelope.lease_id == self.lease_id, "expired or wrong lease"),
            (envelope.epoch == self.epoch, "stale epoch"),
            (envelope.observation_id == self.observation_id, "stale observation"),
            (envelope.action_id not in self._action_ids, "duplicate action id"),
            (envelope.sequence > self.last_sequence, "replayed or out-of-order sequence"),
        )
        for passed, reason in checks:
            if not passed:
                raise StaleAction(reason)

    def accept(self, envelope: ActionEnvelope) -> None:
        self.validate(envelope)
        self.last_sequence = envelope.sequence
        self._action_ids.add(envelope.action_id)

    def invalidate(self, next_epoch: int) -> None:
        if next_epoch <= self.epoch:
            raise ValueError("The task epoch must increase when invalidating a lease")
        self.epoch = next_epoch
