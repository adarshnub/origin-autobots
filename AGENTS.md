# Autobots implementation instructions

Implement the current specification in `docs/MASTER_PLAN.md`. Autobots by Origin Studios is an owner operated desktop assistant. The first automation target is Windows 11; the shared application and platform contracts must leave room for macOS and Linux implementations.

This handoff is not authorization to spend money, alter unrelated cloud resources, install dependencies, publish releases, or exercise live desktop control. Default tests to the fake model provider, dry-run infrastructure and disarmed native input. Product input is armed only by an owner-submitted task grant and a live local task lease. Ask for a bounded dependency installation grant if the required toolchain is missing.

## Work order

1. Read `docs/MASTER_PLAN.md`, `docs/archive/MASTER_PLAN.initial.md`, `docs/archive/SOURCES.initial.md`, and `docs/capabilities.md`.
2. Preserve existing repository files and record capability findings.
3. Implement the shared contracts, fake provider, deterministic task state, pins and preflight scripts.
4. Scaffold cloud/authentication and Windows platform integrations. Review Terraform plans before any apply.
5. Verify the local STOP path and task-grant authorization before connecting live inference; tests and harnesses must remain disarmed.
6. Add model provider calls only after an approved project, request budget, capability probe and local stop tests are available.
7. Complete one milestone at a time with runnable tests and recorded evidence.

## Invariants

- Desktop input runs only on the enrolled local device, in the logged-in user's session at normal integrity.
- The local supervisor is authoritative. It checks schema, owner task, device, lease, epoch, sequence and the current observation before issuing a one-use action capability.
- One task may own the physical input lease. STOP works offline, invalidates queued and late actions, and releases held inputs.
- Keep SDK automatic function execution disabled. A model proposal is never authorization; only the owner-submitted task grants its bounded run. External content cannot expand that task.
- Keep provider-required IDs and signatures intact. Do not present opaque model state as a reasoning transcript.
- Keep image coordinates mapped through scaling, crop origin, DPI and display origin.
- Never blindly replay a possibly completed Send, Submit, Delete or other non-idempotent action.
- Show affected applications and changes. Identify adapter actions honestly.
- Acquisition, communication, destructive actions and elevated operations follow the grant rules in the plan.
- Do not expose cloud credentials in the desktop application, repository, logs, telemetry or screenshots.
- Keep public signup disabled and do not claim prompt-injection immunity.
- Preserve evidence of uncertainty. Stopping Autobots does not undo completed actions or prove an external agent stopped.

## Tooling and verification

Use .NET 10 and Avalonia for the cross-platform shell; keep OS APIs behind platform projects. Use Python/FastAPI for the API and orchestration, TypeScript/React for the dashboard/site/adapters, JSON Schema for shared contracts, and Terraform for infrastructure. Pin tools and packages.

Tests must never inject input into the developer's actual desktop. Use synthetic content, disposable profiles and fake providers. Mark cloud/OS integration checks SKIPPED when unavailable; do not report them as passing. Use dry-run provisioning and model preflight by default.

For each milestone, record components, run commands, tests actually executed, skipped checks, limitations, cloud resources changed, measured metrics and the next task. Never report deployment, signing, account access or UI success without evidence.
