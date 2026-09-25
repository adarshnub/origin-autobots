from uuid import uuid4

import pytest

from services.api.app.storage import IdempotencyConflict, SQLiteTaskStore


def test_task_store_enforces_owner_devices_idempotency_and_stop_epoch(tmp_path) -> None:
    store = SQLiteTaskStore(tmp_path / "autobots.db")
    owner_device = uuid4()
    assert store.enroll_device("owner", owner_device, "Autobots workstation", "windows")
    assert store.owns_device("owner", owner_device)
    assert not store.owns_device("another-owner", owner_device)
    assert not store.enroll_device("another-owner", owner_device, "stolen", "windows")

    task, created = store.create_task("owner", owner_device, "Open Settings", "request-1")
    assert created
    retry, created = store.create_task("owner", owner_device, "Open Settings", "request-1")
    assert not created and retry.task_id == task.task_id
    with pytest.raises(IdempotencyConflict):
        store.create_task("owner", owner_device, "Delete files", "request-1")

    stopped = store.stop_task("owner", task.task_id)
    assert stopped is not None
    assert stopped.status == "stopped"
    assert stopped.epoch == 1
    assert store.get_task("another-owner", task.task_id) is None
