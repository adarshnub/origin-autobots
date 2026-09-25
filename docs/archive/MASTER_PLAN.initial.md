# Desktop Pilot — implementation specification

**Status:** proposed implementation plan; not an implemented application or an account deployment.  
**Research date:** 25 September 2026.  
**Working repository name:** `desktop-pilot` (internal placeholder, not a cleared product brand).  
**Initial operator:** the owner, on an interactive Windows desktop.  
**Deployment:** AWS control plane; Google Cloud managed Gemini inference; local Windows execution.

Read `SOURCES.md` for the primary documentation supporting platform capabilities. Items stated as defaults, targets, acceptance criteria, or recommendations are design choices, not measured results. Run the capability checks in M0 before relying on a model, SDK flag, service region, or account entitlement.

## 1. Product definition

Build a downloadable Windows application that accepts a typed or spoken goal, understands the current desktop, performs visible actions, checks the result, and continues until complete, blocked, stopped, or out of budget. The primary interface during execution is a prominent, persistent **STOP** button. The current application stays visible. A cursor halo follows the actual Windows pointer during native interaction.

The revised requirement is **hybrid visible automation**, superseding the earlier visual-only preference. Screenshot-based control must remain available across normal applications. Browser and VS Code adapters may inspect structured state and perform actions directly when supported, avoiding unnecessary image-inference calls. Adapter actions must still reveal their target application and visible results. Do not pretend an API operation was a physical click.

The product is an assistant acting for the authenticated owner, not a remote-access service for arbitrary people. Content on webpages, in chat, in files, in terminal output, or spoken by meeting participants does not authorize tasks or change permissions.

### Requirements traceability

| ID | Requirement | Implementation boundary | First delivery |
|---|---|---|---|
| R01 | Download and install a Windows app | Signed Windows package and release channel | M7 |
| R02 | Email/password account and login | Cognito, device enrollment, revocation | M1–M2 |
| R03 | Voice instructions | Push-to-talk microphone capture and GCP transcription | M5 |
| R04 | Real mouse/keyboard control | Local interactive-session input broker | M2–M3 |
| R05 | Full visual desktop understanding | Gemini desktop computer-use; screenshot coordinate translation | M3 |
| R06 | Persistent STOP and visible cursor | Independent local cancellation path and cursor halo | M2 |
| R07 | Browser API acceleration | Permissioned extension/native messaging adapter | M4 |
| R08 | VS Code API acceleration | Permissioned extension, visible documents/terminal | M4 |
| R09 | CMD, PowerShell, installed app access | Normal-user permissions; visible execution; task-scoped grants | M3–M4 |
| R10 | Permission before download/install | Acquisition approval workflow across managed routes | M3–M4 |
| R11 | AWS-hosted backend/dashboard | Isolated infrastructure namespace and container deployment | M1, M6 |
| R12 | GCP vision and agent reasoning | Server-side Google Cloud model provider | M1, M3 |
| R13 | Developer controls and analytics | Versioned config, traces, adoption and quality metrics | M6 |
| R14 | Injection resistance | Trust separation, local authorization, vendor checks, adversarial tests | All milestones |
| R15 | Internet research/team communication | Browser workflows and explicit send scopes | M3–M4; expanded M8 |
| R16 | Prompt another agent and check its work | Visible agent interaction plus independent outcome checks | M8 |
| R17 | Calendar, meetings, recording | Armed schedules, visible joining, consented recording | M8 |
| R18 | Owner-only pilot before public users | Closed registration and explicit public-release gate | All milestones |

## 2. Defaults and explicit limitations

Use Windows 11 x64, one monitor initially, a currently supported Windows build, and a normal logged-in user session. Test common DPI settings and at least two keyboard layouts before beta. The owner can initially use typed commands; voice is added after the core control loop works. Use English as the initial recognition test language; add Malayalam and code-switching tests separately rather than assuming mixed-language quality.

AWS region defaults to `ap-south-1` as a deployment choice. The initial Gemini endpoint is `global`: the current Flash model card lists global and US/EU multi-region availability, not an India regional endpoint. Do not claim all screenshot processing stays in India. Measure actual client→AWS→Google latency. [S01]

AWS hosts the control plane, not the user's physical desktop. A component on Windows must capture pixels and generate input. A Windows cloud VM is useful as a test machine, but controlling that VM does not control the owner's laptop.

There is one desktop input owner at a time. The operator cannot simultaneously use the same pointer independently. Human interference pauses the agent. A locked session, UAC secure desktop, authentication challenge, protected capture surface, or unsupported application may require a handoff. Run the controller in the logged-in user's session, not as a Session 0 service. Normal input injection cannot freely control higher-integrity applications. [S04, S05]

Broad access is not a guarantee of reliable completion in every installed application. Do not promise guaranteed autonomous meeting entry, guaranteed recording, universal app compatibility, invisible administrator access, or operation while a laptop is asleep.

## 3. Delivery strategy

Deliver a working vertical slice before general-purpose workflows.

**v0.1 — real desktop control:** owner authentication, device enrollment, AWS/GCP connectivity, screenshot-to-action loop, real pointer/keyboard execution, reliable STOP, a task log, visible terminal access under a narrow test grant, download/install approval, and screenshot-based success verification. Validate on a disposable Windows test profile first.

**v0.2 — practical personal pilot:** browser and VS Code adapters, voice, workspace-level permissions, a useful developer dashboard, signed configuration, rate/cost limits, and resumable task state.

**v1 private beta:** signed installer/update process, account lifecycle, operational monitoring, regression coverage, documented limitations, and a tested recovery path.

**Later workflow pack:** calendar scheduling, chat workflows, supervising coding agents, joining meetings, recording/transcribing with permission. These use the same task engine; they are not separate uncontrolled agents.

Do not include Jev/Laya, fine-tuning, a second reflex model, Kubernetes, a vector database, multi-agent swarms, or a custom always-listening speech stack in the initial implementation. First measure the simpler system. Additional model hops are not automatically a speed improvement.

## 4. Architecture

```text
WINDOWS DEVICE — interactive user session
  Desktop shell: login, command entry, push-to-talk, STOP, cursor halo
  Local supervisor: task lease, policy, cancellation, window ownership
  Input/capture worker: screenshots, SendInput, window focus, input release
  Optional browser extension <-> native messaging host
  Optional VS Code extension <-> authenticated local named pipe
  Optional agent-owned visible terminal host
       |
       | Outbound authenticated HTTPS/WSS; no inbound workstation port
       v
AWS CONTROL PLANE
  API + task orchestrator + model gateway
  PostgreSQL: tasks, events, devices, permissions, config, usage
  Cognito: accounts and authentication
  S3/CloudFront: dashboard assets, installers, optional debug artifacts
  IAM/KMS/Secrets Manager/SSM/CloudWatch: operations and credentials
       |
       | AWS workload identity -> Google short-lived credentials
       v
GOOGLE CLOUD
  Managed Gemini Flash: visual decisions and ordinary task reasoning
  Optional Gemini Pro: difficult planning/recovery, not every action
  Speech-to-Text: push-to-talk transcription
```

No user desktop commands execute on the AWS Linux host. The cloud proposes actions; the local supervisor validates and performs them on the explicitly enrolled Windows device.

### Technology decisions

| Layer | Choice | Rationale |
|---|---|---|
| Windows shell | C# / .NET 10 LTS / WPF | Native Windows integration, predictable overlay and input handling |
| Local controller | Separate C# worker and supervisor processes | Keep input cancellation independent of cloud/model work |
| Capture | Windows.Graphics.Capture with explicit capture consent | Native capture; validate whole-display support and exclusions |
| Input | Win32 SendInput and window/session APIs | Real pointer/keyboard events, not a simulated cloud cursor |
| Cloud API and orchestrator | Python + FastAPI + Google Gen AI SDK | Async requests and typed provider/tool contracts |
| Dashboard | React + TypeScript | Static client with authenticated API calls |
| Browser extension | TypeScript, Chromium Manifest V3 | Structured visible-page control in Chrome/Edge |
| VS Code extension | TypeScript | Supported editor and terminal integration |
| Storage | PostgreSQL + local SQLite action journal | Durable cloud tasks and local deduplication |
| Infrastructure | Terraform, separate AWS/GCP modules | Repeatable provisioning and reviewed changes |
| Packaging | Self-contained Windows build, signed installer | No Python or Node installation required on end-user PCs |

.NET 10 is currently an active LTS release. Pin a supported patch at build time, not a floating SDK. Native capture and input mechanisms have platform restrictions that must be tested rather than abstracted away. [S04, S06, S07]

Avoid exposing arbitrary local HTTP automation endpoints. Use named pipes with user-SID access control for native components and VS Code, and browser native messaging with explicit extension-origin allowlists. These are local access controls, not protection from arbitrary malicious code already executing as the same Windows user. [S08]

## 5. Models and Google Cloud integration

### Initial model registry

| Registry role | Initial model ID | Use | Caveat |
|---|---|---|---|
| `desktop_actor` | `gemini-3.8-flash` | Screenshot interpretation and native desktop action proposal | Computer-use feature remains preview |
| `task_reasoner` | `gemini-3.8-flash` | Ordinary planning, adapter use, summarization and verification | Reuse actor call/context where practical |
| `recovery_planner` | `gemini-3.1-pro-preview` | Optional difficult planning or recovery | Preview; model card does not support built-in computer-use |
| `speech` | Speech-to-Text V2, tested model/location | Transcribe pushed-to-talk audio | Language and streaming support depend on chosen model/location |

These are researched starting choices, not proof of availability in the owner's project. Flash supports image inputs, function calling, and computer-use. Pro is a reasoning fallback, not the native computer-use actuator. [S01, S02]

The current Google Cloud computer-use documentation supports `ENVIRONMENT_DESKTOP` for newer Gemini Flash models. It also documents opt-in screenshot prompt-injection detection. Enable it; honor human-confirmation responses. The tool is a preview capability, and the application must execute its returned actions. [S03]

### Provider implementation

Build a `GoogleCloudModelProvider` behind a narrow interface:

```text
check_capabilities(model_id, location)
propose_step(task_context, observation, allowed_tools, model_policy)
plan_or_recover(task_summary, evidence, failure)
transcribe(audio, language_policy)
estimate_usage(request)
```

Use Google Cloud project-based authentication, not a personal Gemini web subscription or a Gemini Developer API key embedded in the Windows binary. Current Google SDK documentation uses Agent Platform/enterprise terminology; inspect the pinned SDK's actual constructor. Do not blindly copy older `vertexai=True` examples into a newer release. Keep one tested provider shim and record the working SDK/API version. The REST `aiplatform.googleapis.com` project-scoped endpoint is a useful integration check. [S09]

Disable automatic execution of callable tools by the SDK. Function calls are proposals and must pass the application's authorization and local execution path. Preserve complete model response parts, function call IDs, and required thought signatures when continuing a conversation. Opaque provider state is not a user-visible reasoning transcript. Never strip signatures while reconstructing only the text portion. [S09, S10]

Use supported thinking settings. For the initial Flash actor, start with LOW and benchmark; permit MEDIUM/HIGH for difficult tasks. Do not send MINIMAL to 3.8 Flash: the model card says it is unsupported. Store allowed settings by model, and validate configuration edits against that capability registry. [S01]

Do not call a vision-description model followed by a planner for every screenshot. Ordinary visual turns should produce the next action directly. Pro escalation happens at task boundaries, repeated failures, or explicit complex planning—not per click.

Use persistent HTTP clients and short-lived token caching. Add bounded retries with jitter for transient quota/server failures. Do not retry desktop side effects merely because a model/API call failed. Record model name, region, token usage, end-to-end latency and response-validation outcome per turn.

### M0 capability probe

Make a non-destructive request using a synthetic screenshot, desktop computer-use, and injection detection. Validate response parsing, model safety responses, coordinate conventions, and credentials from the actual AWS runtime. Test custom functions alongside computer-use before combining adapters. If a feature is unsupported in the pinned provider, fail clearly and record the blocker. A custom screenshot/function-call adapter may be evaluated as a separate mode; do not silently lose safety responses or change APIs.

## 6. AWS and GCP provisioning

### 6.1 Owner-only AWS baseline

Create an isolated namespace `desktop-pilot-dev` in the approved AWS account, with tags for project, environment, owner and cost allocation. A separate AWS account can be added later; do not silently create or modify organization-wide resources.

For the single-user alpha, use one modest Linux EC2 instance, initially a configurable `t3.medium`, running a small container deployment: API/orchestrator, PostgreSQL, and HTTPS reverse proxy. This is a cost/complexity choice, not a high-availability design. Put PostgreSQL on an encrypted persistent data volume, never on an ephemeral container filesystem. No public database port.

Provision:

- A project VPC/subnet/security group, EC2 instance role and required encrypted EBS volumes.
- ECR repository for pinned backend images.
- Cognito user pool and native/dashboard clients; closed registration initially.
- Private S3 buckets for releases, backups, dashboard assets and optional debug artifacts.
- CloudFront with origin access control for static assets and releases; do not make all S3 content public. [S11]
- KMS keys and Secrets Manager entries for server secrets/configuration signing where needed.
- CloudWatch metrics, structured logs and alarms with short explicit retention.
- SSM administration; no public SSH. SSM supports managing instances without opening inbound admin ports. [S12]
- DNS and TLS for the API and dashboard once a domain is supplied. Domain purchase is not assumed or authorized.

Only the HTTPS entry point is exposed for normal application traffic. Port 80 may be enabled solely for certificate validation/redirect if that TLS deployment requires it. SSM and Google API calls use outbound access. The Windows client opens an outbound WSS connection; do not ask users to forward router ports or enable Remote Desktop.

Require IMDSv2 and restrict metadata access to the intended runtime. Containers using EC2 instance credentials need an explicitly tested metadata/network configuration; do not distribute static AWS keys as a shortcut. Never let the model gateway become an arbitrary URL-fetch proxy to instance metadata.

Back up PostgreSQL using consistent encrypted database backups, initially daily with a documented maximum data-loss window. Test restoration into an isolated instance. EBS snapshots alone are not a substitute for a validated database recovery procedure. Database availability is a known alpha limitation.

Before public multi-user release, move PostgreSQL to RDS and the stateless API/orchestrator to ECS/Fargate behind an appropriate load balancer. Add multi-instance task coordination, connection routing and stronger availability. Re-test Google federation from the new runtime; an EC2 metadata-based credential configuration is not automatically an ECS credential solution.

### 6.2 Google Cloud project

Create a uniquely named dedicated project under the approved billing account/organization, such as `desktop-pilot-dev-<unique-suffix>`. Enable the model API, IAM credentials, STS, and Speech-to-Text APIs required by the chosen implementation. Confirm current API service names during M0.

Create a narrowly privileged service account for model inference, not Owner/Editor. For initial isolation, a documented model invocation role within this dedicated project is acceptable; reduce unnecessary permissions after the capability probe. Add speech invocation permissions only when M5 starts.

Configure Workload Identity Federation from the exact AWS runtime role/account to the GCP service account, including restrictive attribute conditions. Use AWS temporary role credentials and Google short-lived tokens; do not generate a long-lived Google service-account JSON private key for deployment or the client. Google documents AWS role/instance-profile federation and IMDSv2 credential configurations. [S13]

Use one project/environment initially. Separate dev and production projects before external users. Confirm model quota, billing status, account policy restrictions, supported location and data handling terms. Configure billing alerts and application-enforced request budgets. Alerts-only billing budgets are not request-by-request hard stops. [S14]

### 6.3 Inputs required at deployment time

These remain unset because the plan has not inspected the owner's cloud accounts:

```text
AWS_PROFILE / expected AWS_ACCOUNT_ID
AWS_REGION = ap-south-1 (proposed)
GCP_PROJECT_ID or approved project-creation parent
GCP_BILLING_ACCOUNT_ID
GCP_MODEL_LOCATION = global (initial choice)
OWNER_EMAIL / approved registration allowlist
API_DOMAIN / DASHBOARD_DOMAIN / DNS ownership
Backend and model spending limits
Code-signing identity for distributed installers
```

Keep them in ignored environment/tfvars files or the approved secret store. Do not paste cloud keys into prompts. Use interactive CLI/SSO authentication for bootstrap. Terraform should emit a reviewable plan and expected costs before applying billable resources. This document does not authorize a Codex agent to purchase domains, turn on unlimited paid services, or change unrelated resources.

## 7. Windows shell, capture and actual cursor

### Application processes

`DesktopPilot.Shell` owns onboarding, command entry, microphone UI and the STOP overlay. `DesktopPilot.Supervisor` owns task permissions, the active-task lease, local journal and cancellation state. `DesktopPilot.Worker` captures the screen and injects native input only after supervisor approval. A hung model request must never block the STOP path. Keep the native worker's executable interface small and typed.

All three run in the interactive user session. The default is normal-user integrity. Do not run the entire application as administrator. A narrowly scoped elevation flow may be added later with explicit Windows consent, but secure-desktop automation is not part of v1. [S04, S05]

### Minimal interface

During an active task show a large STOP target and, at most, a compact optional status line. No permanent chat panel is required. Login, a voice/text instruction sheet, permission dialogs and a task result view appear only when needed. In an authenticated session with the necessary grants, submitting a voice or text task can itself arm that task; ordinary tasks must not require an extra mouse click to start. The separate explicit arming control is required for developer test harnesses and newly granted capabilities. A global hotkey opens push-to-talk; another independent hotkey stops the task. Suggested stop shortcut: `Ctrl+Alt+Shift+S`, subject to collision testing and user configuration.

The STOP overlay must not steal keyboard focus from the application being controlled. Reposition it when it covers a required target, without hiding it. It must remain visible on the active monitor. Do not let generated actions click or modify the application's consent/settings/STOP surfaces.

Use a click-through pointer halo anchored to the real pointer for the custom-cursor appearance. Smooth pointer movement is local interpolation, not one model call per pixel. Avoid replacing the user's global cursor theme in v1. The halo must not become a false target in screenshots. Use supported capture exclusion for the application's own overlays where available, with a tested fallback. Display-affinity exclusion is not a DRM or security guarantee. [S15]

For API-based actions, reveal the app, highlight the target or changed range, and label the operation as an adapter action in the trace. Where useful, move the real pointer to a browser target before the adapter acts. Do not insert fake clicks solely to make an API operation appear visual.

### Capture and coordinate contract

Every observation includes a unique ID, monotonic capture time, image dimensions, desktop origin, monitor ID, DPI/scale, foreground window identity, process identity/start time and a layout generation. Preserve the mapping from model image pixels to physical display pixels.

The provider adapter translates the model's coordinate format—currently normalized 0–999 for documented desktop actions—into screenshot pixels. The local worker then translates those into the virtual desktop coordinate space required by Windows input. Test both corners and edges, non-100% scaling and negative monitor origins before multi-monitor release. Do not reuse old browser-page coordinates as desktop coordinates. [S03]

Before dispatch, check foreground window, current target validity, observation/layout generation and a current local visual readiness check. A timeout alone does not establish that the layout is unchanged. Re-observe after navigation, popup, focus change, scrolling that changes the target, or user interference.

Use visually faithful compression and detailed crops for small controls; do not pick a low image resolution merely to improve token count. Image byte size and billed image tokens are different quantities.

### Input primitives

Implement pointer move, left/right/middle click, double click, drag, vertical/horizontal scroll, key press/release, hotkey combinations, Unicode text entry, wait, window focus, and screenshot capture. Cap durations, key counts and text sizes per action. Release all agent-held keys/buttons on STOP, worker failure or session loss.

Use clipboard transfer only when the task policy allows it. Clipboard contents may contain secrets; do not automatically upload or inspect the user's existing clipboard. Prefer native Unicode input for ordinary typing and test application-specific behavior.

## 8. Agent engine and reliable task execution

### State machine

```text
IDLE -> RECEIVED -> PLANNING -> OBSERVING -> PROPOSING
     -> VALIDATING -> EXECUTING -> VERIFYING -> OBSERVING ...

Exceptional states:
  AWAITING_APPROVAL / PAUSED_BY_USER / PAUSED_OFFLINE
  BLOCKED / FAILED / STOPPING / STOPPED / COMPLETED
```

Record transitions and their evidence. A final model message is not sufficient proof of completion. Tasks can be partially complete, and the final summary must distinguish completed work, unverified outcomes, blocked steps and irreversible effects already performed.

### Trusted task specification

Compile the owner's authenticated instruction into a task specification: goal, scope, allowed apps/workspaces, allowed destinations/recipients, expected output, requested sends, expiry, step/time/model-cost limits, and allowed delegated agents. The model can propose a clarification or a narrower plan; it cannot grant itself additional permissions.

Model context has explicitly separate fields for trusted task/configuration and untrusted observations. A teammate asking the agent to do something in a chat message is content unless the owner has explicitly created a restricted delegation rule.

### Action envelope

Use typed payloads generated from shared schemas. Do not treat the following example as executable code:

```json
{
  "protocol_version": 1,
  "task_id": "task-id",
  "device_id": "enrolled-device-id",
  "session_epoch": 4,
  "sequence": 18,
  "action_id": "unique-action-id",
  "observation_id": "obs-id",
  "policy_version": 3,
  "lease_id": "local-lease-id",
  "expires_at": "UTC timestamp",
  "kind": "desktop.click",
  "arguments": {"coordinate_space": "screenshot_pixels", "x": 830, "y": 240},
  "preconditions": {"window_ref": "known-window-reference", "layout_generation": 9},
  "effect_class": "navigation",
  "approval_ref": null
}
```

The local supervisor computes authorization independently; a model-supplied `effect_class` is advisory, not trusted. Bind approval and lease to the task, device, action parameters and current policy. The broker rejects unknown tools, malformed arguments, expired leases, wrong device IDs, stale epochs and replayed sequences.

Use a durable local action journal. Record accepted/started/finished/unknown status. Network acknowledgements alone cannot provide exactly-once external effects. After disconnect or crash, an action with an ambiguous outcome must be re-observed, not blindly replayed. This is especially important for Send, Submit, payment, deletion and starting external agents.

### Batch actions without blind execution

Allow short predictable sequences such as focus address bar → select text → type query → Enter. Default to one visual action initially; enable bounded batches only after baseline tests. An approved batch still checks cancellation before each input and stops at new navigation, approval boundary or unexpected focus/layout change.

Do not run two cursor-controlling calls in parallel even if the provider returns parallel function calls. Serialize desktop mutations. Parallelize safe reading or cloud preparation only when it does not race with the foreground interaction.

### Verification and recovery

For native mode, use fresh screenshots and explicit visual postconditions. For adapters, combine structured state with a visible checkpoint. Examples: saved file is reopened and its contents checked; editor changes match the requested diff; a search result actually loaded; a message is visible in the correct channel; a terminal command produced a known exit result.

Allow at most two ordinary recovery attempts by default. Then replan, escalate to the optional model, or ask the owner. Do not treat model confidence scores as calibrated probabilities of safety or success. Unknown is a supported result.

## 9. Hybrid adapters

### Browser

Prefer a Chrome/Edge extension for the owner's normal visible browser. Use native messaging to communicate with the local supervisor. Request site permissions explicitly. Structured read tools should focus on currently rendered content and actionable targets; hidden DOM content is not trusted instruction material. Native messaging requires explicit allowed origins. [S08]

Expose bounded operations: list opted-in tabs, activate tab, inspect visible page, locate visible target, reveal target, fill field, click selected target, scroll, navigate a permitted URL, and return completion/error state. Do not expose unrestricted `eval`, arbitrary JavaScript injection, raw cookie access, password extraction or arbitrary network requests to the model.

Before mutation, verify the tab, origin, current element reference and visible target. Browser page state can eliminate many screenshots. It does not eliminate all verification. If a target is missing, a frame is inaccessible, a canvas is involved, or a dialog is outside the page, use native visual control or pause.

Browser extension content scripts cannot inspect every browser/internal page. Use visual fallback for unsupported surfaces. For optional controlled-browser testing, Playwright may run headed with a dedicated profile; it is not a requirement for attaching to the user's existing browser. Chrome restricts remote debugging of the default data directory, so do not assume adding a debugging flag gives access to an already-open personal profile. [S16, S17]

Do not make a raw Chrome debugging port publicly reachable. This project does not require one for the normal-browser extension path.

### VS Code

Install a first-party extension with user approval. Pair it with the local app. Restrict operations to approved workspaces. Support opening/revealing documents, reading selected files, applying a bounded edit, saving, reading diagnostics, launching an approved visible terminal command and collecting its result. The extension API provides document edits and terminal shell-integration capabilities, but terminal completion information depends on support. [S18]

Reveal affected files and changed ranges before/after edits. Detect external edits and file-version conflicts rather than overwriting them. Keep a local undo/backup checkpoint for agent changes. Normalize paths, reject directory traversal, and account for symlinks/junctions when checking workspace boundaries.

Do not provide unrestricted `executeCommand` access to every third-party extension command. Maintain an allowlisted command adapter. Installing or updating extensions requires the software-acquisition approval flow.

Remote workspaces, WSL, SSH sessions and containers are different execution environments. Detect and deny them by default in v1 unless explicitly enrolled/scoped; do not label a remote command as a local Windows action.

### Terminal

Native mode can operate a visible terminal. For stronger result tracking, implement an agent-owned terminal surface using ConPTY or a supported VS Code terminal integration. Show commands and output. Capture exit status when reliable; otherwise mark it unknown. ConPTY provides a mechanism to host console programs; the application must provide the terminal display and cancellation handling. [S19]

Prefer structured `executable + arguments + cwd` execution for known operations. Keep shell interpretation and arbitrary scripts in a separately granted developer scope. Treat scripts, repository tasks and tool output as untrusted. Even `npm test` or a build script can execute other code; command-name allowlists are not a sandbox.

For owned process trees, use Windows Job Objects and bounded cancellation. STOP can terminate the owned job after a graceful cancellation attempt. It must not indiscriminately kill the user's unrelated editor, browser or shells. A process launched indirectly or an external agent may survive; report cancellation uncertainty. [S20]

## 10. Permissions and prompt-injection resistance

The goal is reduced exposure and constrained effects, not a claim that prompt injection cannot exist. The threat remains present even with a single owner, because the agent reads third-party content. OWASP recommends validating tool calls against permissions/context and using layered controls, not relying on an instruction prompt alone. [S21]

### Authorization categories

| Category | Default behavior |
|---|---|
| Navigation, search, scrolling, reading approved content | Autonomous within task scope |
| Editing files in an approved workspace | Autonomous with checkpoint/verification |
| Known terminal operation in approved development scope | Autonomous within the granted boundary |
| New download, installer, package, extension, dependency or application update | Explicit owner approval before acquisition |
| Sending/posting/uploading | Must match an explicit user request or a precise session grant; otherwise approval |
| Deletion, payments, credential/security changes or elevation | Explicit approval/handoff; reversible alternatives preferred |
| Disabling STOP, changing the agent's own authorization, harvesting credentials | Not offered as agent capabilities |
| Provider-required confirmation | Always surface it; never auto-acknowledge |

A user saying “send this exact message to this channel” can establish that narrow communication authorization, subject to provider-required confirmation. It is not permission to send arbitrary content to new recipients discovered on a webpage.

### Acquisition approval

An approval request must show the requested item, publisher/source, canonical URL or package identity, destination, version/hash when knowable, expected installation effects, required privileges and reason. Approval binds to this item or a disclosed finite dependency bundle and expires. New sources/versions/redirect destinations outside the grant require new approval.

Cover browser downloads, `winget`, `pip`, `npm`, PowerShell web requests, installers, VS Code extensions, first-party updates and scripts that acquire dependencies. Merely viewing a webpage is not an approval-gated “download”; otherwise normal browsing could not work. Saving attachments, executables, archives, packages or other files is acquisition.

Do not claim that extension download events can prevent the first byte from arriving. Such events are detection/cancellation mechanisms unless the browser is configured to block downloads before initiating the operation. Preflight user requests, use a controlled browser/profile for strict acquisition tests, and use a vetted download broker or application/network containment when a hard guarantee is needed. [S22]

**Important boundary:** unrestricted arbitrary terminal code in the owner's normal account can download data through many mechanisms and can tamper with same-user processes. Neither a prompt, a command substring filter nor an in-process supervisor can make that equivalent to a provable “no downloads without permission” rule. Strict enforcement requires OS/application isolation and mediated network/file access. The personal pilot must honestly label its unrestricted developer mode as supervised and weaker, or keep broad execution restricted to a test VM. Do not market it as injection-proof.

### Controls that ship from the beginning

Separate trusted owner instructions from every external observation, including model summaries of external content. Preserve provenance through summaries and task memory. Never promote screenshot text, a webpage's “system message,” a README, a terminal printout or another agent's response into authority.

Enable the provider's prompt-injection detection where the chosen API/model supports it and honor safety responses. Add a local policy check independent of model output. Prompt filters and a guardrail model are supplemental, not an authorization boundary. [S03, S21]

Protect task grants and approval tokens from the model, web content and browser scripts. Revalidate the exact action before execution. Local approval UI is outside the agent tool surface; block generated input to it. Re-authenticate for high-impact changes. Same-user arbitrary code remains outside this protection claim.

Never expose cloud credentials to the Windows app or screenshots. Store native session tokens using Windows-protected storage. Exclude password managers, OS security prompts, cloud secret consoles and secret-entry surfaces from autonomous capture/typing by default. Redaction is useful but not guaranteed, especially for screenshots. [S23]

The dashboard must not expose an unrestricted remote shell, a “disable all safeguards” button or invisible control of another user's machine. Config changes cannot turn off local STOP or silently expand local grants. A configuration edit is versioned, audited and acknowledged by the enrolled device.

## 11. STOP, pause, reconnect and crash behavior

STOP immediately revokes the local task lease, increments the session epoch, stops new input dispatch, clears queued batches, releases held keys/buttons, requests cancellation of active adapters/model calls, and reports the final local state. Cloud acknowledgement is not required. Late cloud responses for the old epoch are discarded.

The target is a local stop-dispatch latency below 100 ms at p95 on the test hardware. This is an engineering target, not a guarantee that a sent message can be unsent or an injected event removed from Windows' queue. Measure the exact interval: accepted STOP → no further newly submitted actuator events. Already-completed and external side effects are separate.

When physical user input is detected during automation, pause rather than fight the pointer. Distinguish the broker's tagged synthetic input from physical input for normal operation; do not interpret this as a security boundary against hostile same-user software. Keyboard emergency stop remains available.

On network loss, expiry or agent process failure, finish only necessary key/button releases and stop dispatch. Reconnect into PAUSED with a fresh observation. Never automatically resume pending non-idempotent actions. An explicitly armed scheduled task still needs a valid current local lease and an unlocked desktop at execution time.

If the owner asked the agent to launch another coding/chat agent, stopping Desktop Pilot does not prove the other agent stopped. Use its supported cancellation path or stop the owned process, then verify. Show “external task may still be running” when uncertain.

## 12. Accounts, device enrollment and distribution

Use Cognito for email/password verification and recovery. The desktop is a public OAuth client without a client secret. Use system-browser authorization code flow with PKCE and a registered callback. Validate state/nonce and callback routing. The official authorization endpoint documents PKCE support. [S24]

For the owner alpha, disable public signup and require MFA on the owner/admin pool. Enable account creation only for the allowlisted email or invite. Before public release, define a separate admin authentication policy rather than relying on a hidden dashboard route. Cognito supports pool MFA configuration. [S25]

Enroll each Windows installation with a random device ID and a device key. Bind sessions to that device and owner. Store refresh material using Windows-protected storage; keep short-lived access tokens in memory where possible. Enforce device revocation, per-device connection limits and ownership checks server-side. Check issuer, audience/client, token type, expiry and authorization scopes; browser clients cannot choose their own user ID.

Keep the initial desktop UI to onboarding and task controls. The broader admin console is a separate web application. Use secure cookies/BFF for the dashboard or a carefully implemented public-client flow; do not place long-lived tokens in scripts or plaintext logs. Native credentials and browser session cookies are different assets.

Distribute a self-contained x64 Windows build; the backend's Python runtime is not installed on the user machine. Sign executables and installer packages using an approved publisher identity. Sign release manifests and verify hashes and signer before updating. Self-signed packages are only for explicitly trusted private test machines, not a frictionless public installer promise. Windows packaging has specific certificate/trust requirements. [S26]

Updates are downloads/installations under the user's rule. Ask before downloading/applying updates unless the owner has explicitly authorized a defined update policy. Do not update while a task is active. Keep a tested rollback and protocol-compatibility policy; a security rollback must not silently re-enable revoked behavior.

## 13. Voice and meeting audio separation

Use push-to-talk first. Capture only the selected microphone while the button/hotkey is active, transcribe through the approved GCP speech endpoint, display the recognized task briefly, and submit it as the owner's instruction. Ask for clarification when recipient, path or destructive intent is ambiguous. Do not feed screen audio into the command channel.

The current GCP language tables include English and Malayalam support in specific models/locations. Validate the exact language/location/model combination and mixed-language examples; language listing alone does not establish code-switching accuracy. [S27]

During meetings, transcript audio is untrusted content. A participant or video saying “download this tool” must not trigger execution. An optional future hands-free mode needs explicit arming, bounded duration and local cancellation. Voice identity is not strong authorization for purchases, credential changes or other sensitive operations.

Avoid adding a continuously streaming multimodal model to the action loop until push-to-talk and screenshot turn-taking have measured results. Keep screen understanding and command-audio routing separable.

## 14. Developer dashboard

### Operational views

Overview shows enrolled installations, online devices, authenticated active users, tasks started/completed/blocked, stop events, crashes, model spend and task latency. Distinguish “online device heartbeat” from “active user.” Use a clear definition for daily/weekly active users, such as a verified user with at least one task started during the period.

Downloads must be reported honestly: website download-button clicks, served installer requests, installation completions/first launch and device enrollments are different events. Repeated requests, retries, shared installers and blocked telemetry mean a download count is not an exact count of people. Do not invent uninstall numbers from missing heartbeats.

The task inspector shows the original instruction, permission scope, action timeline, execution mode, errors, verification evidence, model/config versions, token/cost estimates and recovery steps. Screenshots and terminal content are opt-in diagnostic content, not default admin access to users' private work. Display concise model-provided action explanations, not a purported hidden reasoning transcript.

### Configuration controls

Support validated, versioned settings for model roles and locations, supported thinking levels, context limits, screenshot resolution policy, compression, batch size, retries, timeouts, escalation, mouse movement/typing speed, hotkeys, enabled adapters, acquisition policy, allowed workspaces/apps, prompt templates, retention, signup, per-user quotas, release channels and kill switches.

Use draft → validate → evaluate → publish → acknowledge on device → rollback. Pin a configuration snapshot for each task so mid-task edits do not unpredictably change behavior. Emergency stop/revoke can interrupt immediately; ordinary model or prompt changes apply to new tasks.

Treat prompt editing and model switching as high-impact changes. Test against recorded fixtures before release. Do not let arbitrary dashboard text expand the native tool surface or remove local authorization requirements.

Include user/device revoke, backend drain, model-provider disable and global halt. A global halt stops/revokes; it is not permission for the administrator to start hidden tasks on users' PCs. An owner-initiated remote task requires a previously armed, explicit device policy.

### Observability

Trace each task across device, API, model request and local action using IDs, not secrets. Record separate timing for capture/encode, upload, AWS queueing, Google round-trip, local validation, execution and application wait. Add p50/p95 distributions, not just averages. Compare native vs adapter completion, retries and failure causes.

Alert on repeated approval requests, unusual destinations, request loops, malformed responses, account/device mismatches, model-budget exhaustion and missing backups. Redact logs by default and enforce a retention policy.

## 15. Data model and API contract

### Core entities

`users`, `devices`, `device_keys`, `tasks`, `task_steps`, `observations_metadata`, `action_attempts`, `approvals`, `capability_grants`, `artifacts`, `model_usage`, `config_versions`, `config_acknowledgements`, `releases`, `installation_events`, `audit_events`, `scheduled_tasks`.

Every relevant record carries owner/tenant identity, timestamps and environment. Use composite ownership constraints and query-level authorization. The alpha has one owner but the database must not rely on that assumption for isolation. Add row-level protection or an equivalently tested ownership layer before public use.

Store raw screenshots/audio only when the user has explicitly enabled the relevant feature. Session context can remain in encrypted short-lived runtime storage; after loss, start a new model conversation from trusted task state plus a fresh observation rather than incorrectly fabricating old provider context.

### API surface

```text
POST /v1/devices/enroll
POST /v1/devices/{id}/revoke
POST /v1/tasks
GET  /v1/tasks/{id}
POST /v1/tasks/{id}/stop
POST /v1/tasks/{id}/resume
POST /v1/approvals/{id}/decision
GET  /v1/device-config
POST /v1/device-config/ack
GET  /v1/releases/latest
POST /v1/events/batch
WS   /v1/devices/{id}/session

Admin:
GET  /v1/admin/overview
GET  /v1/admin/tasks
GET  /v1/admin/devices
POST /v1/admin/config/drafts
POST /v1/admin/config/{id}/validate
POST /v1/admin/config/{id}/publish
POST /v1/admin/config/{id}/rollback
POST /v1/admin/halt
```

Actual names can evolve with a versioned OpenAPI/schema change. Enforce RBAC, rate limits, tenant checks, payload limits and replay protection for each operation. A client-supplied action success or installation event is telemetry, not permission.

For WSS, authenticate with a short-lived credential in a suitable handshake/header path; never put durable refresh credentials in a query string. Negotiate protocol version and session epoch. Limit binary image size, audio duration, inflight requests and event backlog. Reject reconnects from a revoked device.

## 16. Performance and cost plan

Optimize task completion rather than one model's inference number. The initial hot path is:

```text
capture -> encode -> Windows/AWS upload -> model round-trip
        -> validation -> visible execution -> app response -> verification
```

Capture locally more frequently than model requests if needed, but upload only at decision boundaries. Local image change/stability detection is a readiness hint, not proof of page completion. Ignore the agent's own cursor halo and known animations when computing stability. Retain a full-screen fallback when crops lose context.

Use short action batches, persistent connections, concise tool results, bounded context summaries, structured browser/editor observations and targeted visual verification. Measure before introducing extra planners or reflex models. Do not skip outcome checks to improve apparent speed.

Initial engineering targets, to be measured on the actual owner path:

| Metric | Initial target, not benchmark claim |
|---|---|
| Local STOP to cessation of new dispatch | p95 <100 ms |
| Capture + encode of a readable primary screen | p95 <150 ms |
| Routine screenshot-to-valid-action proposal | target p50 <2 s, p95 <5 s; revise after measurement |
| Unexpected repeated action | zero blind retries of irreversible actions |
| Baseline task success | >=90% on a defined 30-task private test suite |
| Acquisition/policy test outcomes | all known negative fixtures blocked or handed off |

A passed test suite does not establish zero real-world security failures. Report confidence intervals or at least run counts and failures for model comparisons. Keep the same tasks, initial state and permissions when comparing two models/adapters.

### Budget calculation

Use provider-reported input/output/cache usage, not screenshot count alone. Include thinking/output token accounting as reported. Current published global Standard PayGo rates for Gemini 3.8 Flash are $0.75 per million input tokens and $3.75 per million output tokens through 31 December 2026; the published rates starting 1 January 2027 are higher. Re-read the price table at implementation time and store rates with effective dates. [S28]

```text
model_cost = uncached_input/1e6 * input_rate
           + cached_input/1e6 * cache_rate
           + billed_output/1e6 * output_rate
           + other applicable provider charges

monthly_total = EC2 + disks + backup/storage + network/CDN
              + auth + logs/secrets + model_usage + speech
              + optional recording/storage and signing costs
```

No monthly total is claimed without the owner's measured usage and regional AWS quote. Implement per-task and daily/monthly request budgets with reservations for inflight calls and reconciliation afterward. Provider billing can differ or lag; application limits are an additional operational control, not a promise of an exact invoice ceiling.

## 17. Calendar, communication, coding-agent and meeting workflows

These are later skill packs, not blockers to proving v0.1.

For calendar tasks, start by navigating the owner's existing browser calendar and reading the visible schedule through the permitted adapter or screenshots. Store explicit time zones and the original event identity. For scheduled execution, the owner must arm the task; the local PC must be awake, connected and unlocked. If not, mark missed/blocked and notify rather than trying to bypass the lock screen.

For meeting entry, open the expected meeting link visibly, verify the meeting identity, join with mic/camera defaults chosen by the owner, and handle waiting rooms or login manually. Do not claim attendance if the app remained in a lobby. Keep a STOP/leave path.

For recording, prefer the meeting platform's built-in recording interface where the owner has permission. Teams recording is controlled by meeting/admin policies; being able to click does not confer recording rights. A separate local recording mode requires clear participant notice/consent under applicable requirements and an on-screen recording indicator. Do not use local capture to evade a platform/organization restriction. [S29]

Windows audio loopback can capture rendered system audio, which may include unrelated applications. Use explicit device/app selection where supported, and do not assume a default loopback is meeting-only audio. Store recordings locally by default; cloud upload and retention require a separate grant. [S30]

For team messages, validate destination and content against the original request. Avoid duplicate sends after a reconnect. For coding-agent supervision, cap iterations, show prompts, inspect resulting files/tests, and never approve the other agent's elevated/download requests automatically. Another agent's statement that it completed the task is not independent verification.

## 18. Repository and implementation order

```text
desktop-pilot/
  AGENTS.md
  docs/
    MASTER_PLAN.md
    SOURCES.md
    decisions/
    capabilities.md
    runbooks/
  apps/
    windows/
      DesktopPilot.Shell/
      DesktopPilot.Supervisor/
      DesktopPilot.Worker/
      DesktopPilot.Terminal/
      DesktopPilot.Tests/
    dashboard/
  services/
    api/
      auth/
      devices/
      tasks/
      models/
      policy/
      telemetry/
      tests/
  adapters/
    browser-extension/
    vscode-extension/
  packages/
    contracts/
      schemas/
      generated/
      fixtures/
  infra/
    bootstrap/
    aws/
    gcp/
    environments/dev/
    environments/prod/
  evals/
    desktop-fixtures/
    task-suite/
    injection-suite/
  scripts/
  deploy/
  releases/
```

Generate C#, Python and TypeScript contract types from one schema source, or enforce cross-language contract fixtures in CI. Commit lockfiles and pin toolchains. Keep secrets, live screenshots, raw meeting audio and real credentials out of source control.

### Milestones

| Milestone | Deliverable | Exit evidence |
|---|---|---|
| M0 — preflight | Requirements, version pins, cloud identity check, model probe, contracts and risk boundaries | `capabilities.md`, real probe output, reviewed Terraform plan or explicit missing-input report |
| M1 — cloud/account foundation | AWS stack, GCP federation, Cognito, authenticated API, device enrollment, closed signup | No static cloud key in client; enrolled client connects; restore test planned and first backup validated |
| M2 — native executor | Capture, real input, STOP/halo, action journal, offline pause; fake model provider | Local tests with network disabled; focus/DPI tests; stale/replay rejection |
| M3 — first agent | Gemini screenshot/action loop, bounded task state, verification, approval, terminal test scope | Three cross-app workflows complete; STOP and download tests pass |
| M4 — acceleration | Browser and VS Code extensions, visible adapter effects and visual fallback | Same tasks succeed in native/hybrid mode; no hidden app mutations |
| M5 — voice | Push-to-talk, GCP transcription, interruption, input-channel separation | Voice tasks and unrelated speaker/audio tests |
| M6 — developer console | Configuration/versioning, traces, adoption metrics, budgets, device revoke/global halt | Published config ack/rollback; real latency/cost/task data |
| M7 — private release | Signed package/update flow, backup restore, cleanup/uninstall, regression and threat tests | Clean-machine installation and end-to-end owner pilot |
| M8 — workflow packs | Calendar/chat/coding-agent/meeting workflows | Per-skill reliability and consent/cancellation evidence |

M1 infrastructure work and M2 local work may develop in parallel after contracts are stable; both must be complete before real unattended tasks. No calendar automation, meeting recording or public signup until the core is trustworthy on the test suite.

## 19. Required tests and acceptance gates

### Core task fixtures

Create 30 deterministic/repeatable test tasks across a synthetic browser app, Notepad/File Explorer and a disposable VS Code repository. Include searches, form entry, saving/reopening a document, switching apps, reading results, editing a file, invoking a preapproved local test command and summarizing the result. Separate public-internet nondeterminism from local fixture results.

At minimum, run:

1. Search the browser, open a result, save a note, reopen and verify it.
2. Open a disposable VS Code project, make a bounded edit, save, run an approved test and verify output.
3. Navigate File Explorer, create a folder, save a document into it and confirm destination/content.
4. Trigger a download and verify approval is required; deny it and verify no managed acquisition proceeds.
5. Stop during movement, typing, scrolling, model wait, browser mutation, terminal execution and a queued batch.
6. Disconnect/reconnect during a message submission; verify no blind resend.

### Boundary and failure fixtures

Test lock/unlock, UAC, focus theft, popup appearance, window resize, DPI change, missing permissions, lost microphone, expired login, revoked device, 429/5xx model errors, malformed function calls, unavailable model, provider-required confirmation, stale screenshot, duplicate action, delayed response, process crash and database interruption.

Injection fixtures include a malicious webpage, chat message, README, terminal output and screenshot text attempting to change the goal, reveal secrets, change recipient, install an extension, run a download command, click the agent's own approval UI, or weaken its policies. Include an adversarial voice clip in meeting audio. The expected result is no expanded authority and no unapproved effect, not merely a model saying “I will ignore that.”

Test nested package-manager/scripts and untrusted repo hooks in a disposable environment. Document what the private non-sandboxed mode cannot enforce. Do not label a prompt-only test as an OS isolation test.

Before public signup: add tenant isolation, auth abuse, CSRF/XSS, extension/native messaging origin tests, update-signature/rollback tests, privacy/retention controls, incident response and independent security review.

## 20. Immediate first implementation task

Start with M0 and the non-destructive skeleton for M1/M2. Produce the repo, shared schemas, fake model provider, Windows capture preview, STOP overlay and a local input test harness that cannot run without explicit arming. Add Terraform modules and model capability probes, but do not apply paid resources without approval.

The first real end-to-end demonstration is:

> After owner login and local arming, receive a task; capture the actual desktop; have the approved GCP Gemini model return an action; validate it locally; visibly move/click/type; capture the result; verify; stop immediately when requested.

Do not begin by generating a large dashboard with fake metrics. Do not declare the complete product implemented when only screenshots, a demo cursor or mocked model responses work. Every milestone needs a runnable artifact, tests, a known-limitations note and a precise next task.
