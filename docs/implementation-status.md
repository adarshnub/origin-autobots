# Implementation status

Updated 25 September 2026.

## Delivered

- Autobots by Origin Studios has a Windows-first Avalonia shell with shared OS abstractions, preserving extension points for macOS and Linux.
- Submitting a task in the signed-in desktop app is the task-scoped grant. The Windows client runs each locally validated proposal without an approval dialog, then captures a fresh observation. Tasks are limited to 20 actions or five minutes, and local STOP remains available through a button and `Ctrl+Alt+Shift+S`.
- Repeated full-primary-display images are sent to the API during an active task. The API does not persist screenshots. The shell discloses this before task submission.
- The API uses Cognito owner authentication, device/task ownership, task epochs and a persistent SQLite inference ledger. Gemini 3.5 Flash-Lite calls disable SDK automatic tool execution and return one proposal at a time. The API can also report completion or stop for missing details.
- The one-use Windows action capability is created locally and bound to the current task, action, observation, foreground window and display layout. Input uses the logged-in user's normal integrity; password and unidentified text-entry controls are rejected. The desktop button and global shortcut cancel local work and release held inputs.
- The current browser workflows target Google Calendar event scheduling and Google Meet joining through an already signed-in browser. No browser extension or direct Calendar API integration exists. The model prompt asks for missing event details and named invitees, defaults Meet joins to camera/microphone off, and prohibits meeting recording.
- The separate landing-page project remains a blank placeholder.
- AWS dev infrastructure is deployed in `ap-south-1` with the small EC2 stack, private S3 releases/backups, owner-only Cognito, CloudWatch alarms, a $75 account budget and an automatic stop action for the Autobots EC2 at $70 actual spend. GCP Vertex access uses AWS workload identity federation; no static AWS key or service-account key was created.
- API inference ledger limits are $0.25 per task, $0.50 per UTC day and $20 per UTC month. These are API-side request controls, not a GCP billing cap.
- Rebuilt the live API source artifact and applied a reviewed Terraform plan that updated only the private S3 release object (0 resources added, 1 updated, 0 destroyed). The SSM deployment and public `/healthz` check succeeded; `/openapi.json` confirms the live API includes `proposal`, `completed`, and `needs_input` response states. A final AWS plan reported no changes.
- Built the self-contained Windows bundle at `artifacts/Autobots-Windows-20260925-200227.zip`. SHA-256: `3d2da0aafa9351335726ff3b28bf487b6c6985afe35354c4c947639998ffe1cf`. The archive contains the executable, public runtime configuration and current install guide.

## Verification

- `.venv/Scripts/python.exe -m pytest -q` — 35 passed; one Starlette deprecation warning.
- `.tools/dotnet/dotnet.exe test Autobots.slnx` — 7 passed.
- `.tools/dotnet/dotnet.exe build apps/desktop/shell/Autobots.Desktop/Autobots.Desktop.csproj --configuration Release` — passed with zero warnings or errors.
- Restored and built the shared shell with `-p:OS=Unix` — passed with zero warnings. This is a shared-code compile only; no macOS/Linux platform adapter is present.
- The test suites use synthetic frames and fake providers. They do not inject input into the developer's active desktop.
- Previous live checks confirmed AWS-to-GCP workload identity federation and a synthetic Gemini proposal. The current autonomous Calendar/Meet flow has not been run on a signed-in desktop.

## Known limits and remaining checks

- This is screenshot-based visual control. It has no structured browser/Calendar connector or deterministic semantic classifier for the meaning of every screen action. The model can misunderstand a page or click; task limits and STOP reduce exposure but do not guarantee safe or correct outcomes. External screen content must not be treated as authorization, and prompt-injection immunity is not claimed.
- Calendar creation and Meet joining depend on the intended Google account already being signed in. The model prompt gives conservative instructions, but Google UI behavior and end-to-end completion have not been verified. MFA, CAPTCHA, provider-required confirmation, UAC and secure-desktop prompts can still stop progress.
- No actual screen capture, global hotkey registration, UI Automation field check, or native input was exercised on the developer's desktop. The first Windows task must be manually tried by the owner in a suitable, non-sensitive app/account.
- The app is unsigned. The planned Windows.Graphics.Capture picker, capture exclusion, update delivery and production PostgreSQL migration are not complete.
- AWS's $75 budget action is delayed and not a hard cap. EC2 stop leaves storage, public IP, transfer, taxes, other AWS use, and Google AI charges separate. The API ledger only controls usage routed through Autobots.
- Gemini computer use is preview and uses global Vertex processing, which does not guarantee India-only data residency.
- macOS/Linux remain extension points; no native adapters or packages are implemented or tested.

See [capability findings](capabilities.md), [Windows install steps](INSTALL_WINDOWS.md), and the [master plan](MASTER_PLAN.md).
