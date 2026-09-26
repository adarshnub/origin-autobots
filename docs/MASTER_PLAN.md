# Autobots by Origin Studios — implementation specification

**Status:** active implementation plan; code and test status live in `implementation-status.md`.  
**Initial platform:** Windows 11 x64, one monitor, interactive standard-user session.  
**Future platforms:** macOS and Linux, using shared contracts/core with OS-specific adapters.  
**Cloud:** AWS control plane and Google Cloud managed Gemini inference.  
**Initial access:** owner only; closed registration.

The original plan remains in `archive/MASTER_PLAN.initial.md`. This specification supersedes its WPF-only shell, Windows-only project shape, product name and model defaults. Other security, privacy, release and milestone boundaries continue to apply unless revised here.

## Product behavior

Autobots accepts an owner instruction that grants a bounded task, observes the current desktop, proposes one action at a time, validates each action locally, performs it visibly, then checks the result. It ends completed, needs input, blocked, stopped, failed, paused or out of budget. The active task always has a local STOP path independent of cloud availability.

The first product slice is a typed or spoken instruction and an authenticated screenshot-to-action loop on Windows. Submitting an instruction in the local app is the owner's task-scoped grant: the local Windows pilot executes one validated action at a time without a separate confirmation at each step, then observes again. A task may use any normal-integrity application on the interactive desktop (Start menu, taskbar, browsers, VS Code and other installed apps); each action is bound to the window that was active in its own screenshot. The task is limited to the stated request and to owner-configured step and time limits (default 40 actions and 10 minutes, bounded to 5–100 actions and 1–30 minutes); the local owner can STOP it at any time. A spoken instruction is captured only while the owner holds the push-to-talk session open, is transcribed by Gemini, and is shown with a short countdown (cancel or edit) before it becomes the task. Structured browser and VS Code adapters, dashboard operations, and workflow packs follow as separate milestones. The app must keep mock mode useful while credentials are absent.

### Identity and scope

- Display name: **Autobots by Origin Studios**.
- Keep internal namespaces consistent with `Autobots` / `autobots`.
- The landing-page application is a separate public website project. Its product showcase, illustrated walkthrough, developer profile and platform roadmap were commissioned after the initial blank placeholder. It has no public signup or analytics. The owner-only, unsigned Windows pilot is not a public download.
- Keep the original handoff intact under `docs/archive/`.

## Architecture

```text
Desktop — interactive user session
  Avalonia shell and STOP surface
  Shared task supervisor, policy, lease/epoch and local journal
  Platform adapter loaded for the current operating system
  Optional browser and editor adapters
       | outbound authenticated HTTPS/WSS
AWS control plane
  Dev: API, task orchestration, model gateway, SQLite journal and Cognito
  Future multi-instance production: managed PostgreSQL and expanded control plane
       | AWS role federation / short-lived credentials
Google Cloud
  Gemini inference, then push-to-talk speech in a later milestone
```

### Shared application versus platform code

Use Avalonia and .NET 10 for the desktop shell. Keep task lifecycle, authorization decisions, action contracts, cancellation, telemetry and provider interfaces in OS-independent libraries. Put capture, native input, foreground-window inspection, session status, global shortcuts, secure storage, overlays and local IPC behind explicit platform interfaces.

Implement and qualify Windows native services first. macOS and Linux projects are planned extension points, not fake implementations. Capability negotiation must report unsupported APIs or permissions. Linux must account for display-session differences; macOS must account for screen-recording and accessibility permissions. Neither platform may claim support until native capture, input, STOP, permission prompts, package signing and release tests pass on real hardware.

### Repository boundaries

- `apps/desktop`: Avalonia UI and process entrypoint.
- `platforms/abstractions`: interfaces, capabilities and shared observation metadata.
- `platforms/windows`: Windows capture, input, hotkeys, storage and session checks.
- `platforms/macos`, `platforms/linux`: future implementations, added only when work starts.
- `services/api`: FastAPI routes, auth, device/task orchestration, providers and budgets.
- `packages/contracts`: source JSON Schema, generated language types and shared fixtures.
- `adapters`: permissioned browser and VS Code extensions.
- `apps/dashboard`: authenticated owner/admin console.
- `apps/website`: independent public landing page and developer page.
- `infra`: separate AWS/GCP Terraform and isolated environments.
- `evals`: synthetic desktop tasks, quality comparisons and adversarial fixtures.

## Model routing and cost

Google Cloud is the first provider behind a replaceable `ModelProvider` interface. The deployed dev pilot enables live inference only behind owner authentication; local mock mode remains useful when credentials are absent. Google documents Gemini 3.5 Flash-Lite as accepting images and supporting function calling and preview computer use. Capability and response-shape checks remain required for the selected project and pinned SDK. The bounded probe uses a one-pixel synthetic image, disables SDK tool execution and never executes a returned proposal. See [capabilities](capabilities.md).

### Initial routing policy

1. Prefer deterministic local postcondition checks and structured adapter observations where available.
2. Use `gemini-3.5-flash-lite` for routine screenshot interpretation and action proposals, subject to the capability probe. The desktop computer-use tool runs with `LOW` thinking, excludes predefined functions the executor does not implement (`key_down`, `key_up`, `mouse_down`, `mouse_up`, `take_screenshot`), and receives a bounded history of the steps the device executed or rejected. History entries are untrusted data, never authorization.
3. The current pilot makes one Flash-Lite proposal call per observation. A proposal the local supervisor or executor rejects before any input is sent is recorded in the history and the task re-observes (at most three consecutive rejections). Do not automatically retry a request or route to a stronger model; stop and report uncertain or unavailable results for owner review.
4. Defer stronger models and Pro models until a later capability and cost comparison justifies them.
5. Do not add a separate image-description call before each action. A visual turn should propose the next bounded action directly.
6. Compare cost per successfully completed task against a Flash baseline before broadening the routed default.

### Budget controls

The deployed pilot ledger limits inference to **$0.25 per task, $0.50 per UTC day and $20 per UTC calendar month**. Voice transcription uses a separate ledger with defaults of **$0.01 per request, $0.10 per UTC day and $2 per UTC month**. These are API-side request controls, not Google Cloud billing guarantees. Reserve estimated request cost before dispatch, reconcile provider-reported usage, account for in-flight requests after crashes, and block a call that would exceed a limit. Do not let the model or remote dashboard silently raise a limit.

Record provider, model, service tier, input/output/cache usage, estimated cost, route reason, latency, retry count and validation result. Keep hosting, speech, storage and egress accounting separate. Pricing changes over time; store rates with effective dates and refresh them before enabling live traffic.

### Image and token efficiency

- Capture locally at the cadence needed by the worker; upload at decision boundaries.
- Prefer fresh cropped/window images when they preserve task context; keep full-screen fallback.
- Preserve physical display origin, crop origin, image dimensions, DPI/scale and the transform back to native coordinates.
- Use image-change detection as a readiness hint, never as proof that an action completed.
- Avoid repeated uploads of unchanged frames while an app is waiting.
- Keep bounded context with trusted task summaries; never fabricate provider continuation state.
- Use structured browser/editor state after those adapters are installed, followed by a visible result checkpoint.

## Safety and execution invariants

- All input runs on the enrolled local device in the logged-in user session, normal integrity by default.
- Exactly one task owns the physical input lease.
- Local supervisor validates task/device ownership, lease, epoch, sequence, observation freshness, schema and task grant before execution. Each action receives a one-use, short-lived local capability; the model and API cannot mint one.
- STOP immediately revokes the local lease, advances epoch, cancels pending dispatch, releases held inputs and rejects late results. Cloud acknowledgement is not required.
- Native input is disarmed until the owner submits a task in the local UI (typed, or a displayed voice transcript the owner lets start). The one-use authorization is then created by the supervisor for each current proposal, expires within 15 seconds and binds to the task, action, screenshot, the window active in that screenshot and display generation. The executor re-checks focus, layout and cancellation during paced pointer motion and typing.
- Text input fails closed for password controls and for focused controls that Windows UI Automation cannot verify as ordinary editable text controls.
- The initial Windows adapter works at the signed-in user's normal integrity. It does not control UAC/secure-desktop surfaces or higher-integrity processes.
- App startup and test harness start disarmed. Unit tests never control the developer desktop.
- Model SDK tool execution stays disabled. Provider calls produce proposals that pass the local policy path.
- Webpages, screenshots, chat, files, terminal output, meeting speech and other agents are untrusted data, not authorization.
- If the provider requires confirmation, or a site presents login, MFA, CAPTCHA or another security challenge, stop and report the handoff. The agent cannot approve its own permission UI.
- Do not retry uncertain non-idempotent effects. Re-observe and report unknown outcomes.
- New downloads, packages, extensions, installers and updates require a scoped acquisition grant.
- Sending invites or messages is allowed only when the owner task explicitly requests it and identifies the intended recipients and content/purpose. Never infer recipients or expand a send from page content. Deletion, payment, credential/security changes, new software acquisition and elevation require the explicit scoped grant described in the archived plan; otherwise stop and ask.
- Raw screenshot/audio persistence remains opt-in. No cloud keys in the desktop client.
- The same-user supervisor is not tamper-proof against arbitrary same-user code. Do not claim injection immunity.

## Interfaces and contracts

`packages/contracts/schemas/action-envelope.schema.json` is the source of truth. It carries schema version, task/device/action/observation IDs, lease ID, epoch, monotonic sequence and one typed action. The provider adapter normalizes provider-specific coordinates; OS-specific handles do not cross the platform boundary.

Minimum actions: click (one to three clicks, any button), type text (optionally followed by Enter), key press (including the Windows key and chords), scroll, pointer move, drag and bounded wait. Each proposal binds to the screenshot/observation from which it was produced. Action results distinguish executed, rejected (nothing sent), interrupted/unknown (some input sent) and cancelled. Task grants bind the owner's submitted instruction to a device, lease, expiry and bounded action budget.

The current API exposes public health (with an advertised feature list) plus Cognito-protected device enrollment, task create/read/stop, action-proposal and voice-transcription routes. It verifies Cognito access-token issuer, client ID, expiry and `autobots-owners` group. The API never executes input: it returns one action proposal (with the model's short stated intent for display), a completion report, or a request for missing details per observation; the local Windows supervisor owns validation and dispatch. Transcription accepts one bounded push-to-talk WAV clip (at most 60 seconds), is budgeted in a separate ledger, never stores audio and cannot create or start a task. Revocation, remote approval records, config, releases and admin routes remain future work.

## Infrastructure

Keep AWS as the control plane and GCP as the model provider. The dev pilot uses one AWS EC2 API host, Cognito owner-only login, a private versioned S3 bucket and SQLite with daily S3 backups. It uses AWS runtime identity federation for Google credentials; there are no static cloud keys. A multi-instance production service should move the journal to managed PostgreSQL and complete restore/revoke/retention qualification before release.

Terraform must isolate `dev` from `prod`, verify expected account/project IDs, expose a reviewed plan, and support dry-run. Do not apply billable resources or create accounts/domains without explicit deployment inputs and authorization. No cloud resource is considered present until verified in the approved account.

## UI and future website

The current Windows shell supports Cognito sign-in, typed and spoken task entry, task-granted repeated display capture, an autonomous bounded observe/propose/validate/act loop, local lease/focus/layout validation, native input at normal user integrity, and STOP through a button, the floating pilot bar, the tray menu and a global hotkey. It executes without per-action approval and stops to request missing task details or on provider/security handoff. The main window is a dark Windows 11 design with a composer, suggestions, a live activity feed and a settings sheet (step/time limits, pointer speed, voice and tray options). While a task runs the main window minimizes and a non-activating, always-on-top pilot bar shows the current step, the model's stated intent and STOP. The real Windows pointer travels visibly to each target along an eased path and a click-through indicator (halo, click ripple, caption) follows it. The pilot bar and pointer indicator are hidden for the instant of each Autobots screenshot and move away from pointer targets; they are never capture-excluded from other software. Push-to-talk uses `Ctrl+Alt+Space` or the mic button, records the default microphone only until the owner finishes or pauses (at most 45 seconds), and shows a live level meter. Closing the window keeps Autobots in the tray so the hotkey remains available. The adapter uses visual screen interaction across apps, including browsers; the intended Google Calendar workflows create/schedule events and join existing Google Meet calls through a browser already signed into the intended account. Meeting joins default to microphone and camera off and do not record. There is no browser extension or direct Calendar API connector, so web workflows depend on the active browser session, and this path has not been tested end to end. Literal support for every installed app is not promised: UAC/secure desktop, the lock screen, processes running as administrator and applications that block or do not expose usable input remain outside scope and are reported as a handoff. Pausing on physical owner input, multi-monitor observation, browser/VS Code structured adapters and native macOS/Linux implementations remain future work behind the platform boundary.

`apps/website` is an independently buildable landing page with a continuous scroll-responsive 3D scene, a clearly marked illustrative demo, a Windows private-pilot access point, macOS/Linux roadmap cards and a separate developer page. Public signup and analytics remain disabled. A direct Windows download requires a separately approved public release artifact and configured URL.

## Milestones

| Milestone | Deliverable | Exit evidence |
|---|---|---|
| M0 — project foundation | Repo structure, pins, contracts, fake provider, task states, health API, preflight scripts, docs, website placeholder | Schema/fixture validation and deterministic state tests; toolchain and missing credential report |
| M1 — account/cloud skeleton | Terraform modules, Cognito, device enrollment/auth, API ownership and budget ledger | Reviewed plan; no static key in client; auth/revoke tests; first backup/restore evidence |
| M2 — local Windows supervisor | Capture preview, coordinate mapping, disarmed input harness, STOP/hotkey/halo, journal and local IPC | Offline STOP and stale/replay/focus/DPI/crash tests on a disposable profile |
| M3 — first real agent | Approved GCP Flash-Lite screenshot loop, local policy validation, bounded stronger fallback and postcondition checks | At least three cross-app tasks with action/cost/verification evidence; STOP and acquisition tests |
| M4 — cost-saving adapters | Visible browser and VS Code APIs with screenshot fallback | Same fixture set compared across native and hybrid modes; task success and cost recorded |
| M5 — voice | Push-to-talk and separately budgeted transcription | Task mapping, ambiguity and unrelated audio isolation checks |
| M6 — developer console | Real traces, budget/config management, device revoke, rollback and halt | Versioned config ack/rollback and real usage metrics |
| M7 — private Windows release | Signed installer/update, backup recovery, privacy cleanup and regression gate | Clean-machine install/update, signature failure, restore and uninstall tests |
| M8 — workflow packs | Calendar, communications, coding-agent supervision and meetings | Separate scoped permission, reliability, consent and cancellation evidence |

M1 and M2 can progress independently after contract stability. Do not enable real automation until both local STOP and authenticated cloud prerequisites pass.

## Acceptance gates

Create a deterministic 30-task synthetic suite covering browser, Notepad/File Explorer and a disposable VS Code repo. Record build, OS, screen/DPI, model/SDK/location, config, run count, result, time and cost. Keep quality tests separate from deterministic executor tests.

Minimum gates include: no input while disarmed; coordinate accuracy at 100/125/150% DPI; STOP while input/model/network is blocked; stale epochs and duplicate IDs rejected; lock/UAC/focus loss handoff; crash or disconnect yields unknown for uncertain effects; one input owner; model 429/5xx and malformed response bounded; missing budget blocks inference; denied acquisition causes no managed download; user/device mismatch is rejected; external prompt injection cannot expand authorization; tampered updates fail verification.

Private suite target is at least 90% success on the defined task set, with confidence/run count reported. All mandatory negative fixtures must block or request handoff. A passing fixture suite does not prove universal application support or injection immunity.

## Deployment inputs

The dev deployment uses the explicitly approved signed-in AWS account, GCP project, owner email and spend inputs stored in ignored local Terraform variables. Any new environment, production deployment, public dashboard/domain, signing identity or wider model budget requires its own reviewed inputs and plan. Keep credentials out of repository files, the desktop app and logs.
