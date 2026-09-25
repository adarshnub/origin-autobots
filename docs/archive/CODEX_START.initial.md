# Codex implementation starting prompt

## Place the handoff files

In a new repository directory, place `AGENTS.md` at the root. Place `MASTER_PLAN.md`, `SOURCES.md`, and `acceptance-tests.md` inside `docs/`. Place `config.example.yaml` inside `config/`.

These files contain a plan, not an implemented or deployed application. The account IDs, domain, owner email, credentials and spending limits are deliberately not filled in.

## Paste this as the initial task

```text
Read AGENTS.md, docs/MASTER_PLAN.md, docs/SOURCES.md and
config/config.example.yaml before implementing Desktop Pilot.

Implement M0 and scaffold the M1/M2 foundations only. The final architecture
is a Windows-native local controller, an AWS-hosted control plane and
Google Cloud managed Gemini models. The current design permits visible
browser and VS Code API adapters, but those come after the native loop.

First inspect the repository and installed development tools. Preserve
existing files and report any naming conflicts. Record current supported
SDK/model capabilities and version pins in docs/capabilities.md.

Create:
1. The repository structure and typed cross-language action contracts.
2. A fake model provider and deterministic task-state tests.
3. A native Windows shell with capture preview, a prominent STOP overlay,
   a cursor halo and a disarmed-by-default action test harness.
4. A local supervisor that validates lease/epoch/sequence, blocks stale
   or replayed actions and stops without needing any cloud response.
5. A minimal FastAPI health endpoint and authenticated-device interface
   scaffold, without bypassing authentication for a public deployment.
6. Terraform modules and an environment-input checklist for an isolated
   AWS development stack and GCP workload identity federation.
7. An executable, non-destructive Google Cloud capability-probe script
   for the approved account. Run it only after credentials and a test
   spending limit have been supplied.
8. Build/test instructions and a milestone status report separating real
   tests from mocks and unrun integration checks.

Do not apply Terraform, enable billable cloud resources, publish installers,
open public signup, install dependencies or exercise live desktop control
without the corresponding explicit approval. List missing dependencies
and request a bounded installation grant rather than silently installing.

Do not implement meeting attendance/recording, Jev/Laya, a multi-agent swarm
or a full admin dashboard yet. Do not give the model automatic tool
execution or unrestricted authority to expand permissions. Show any
platform/model incompatibility honestly. Complete and test the first
vertical slice before broadening scope.
```

## First real demonstration after the foundation

Once M1–M3 prerequisites are met, use a disposable Windows profile, a synthetic browser page, and a test folder:

> Open the test page in the browser, copy its heading into Notepad, save the note in the approved folder, reopen it and verify the contents.

Record real model latency, each executed action, verification evidence, and STOP behavior. Only then add the browser and VS Code acceleration paths and compare against the visual baseline.
