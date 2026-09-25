# Autobots by Origin Studios

Autobots is an owner-operated desktop assistant. The Windows 11 pilot signs into an owner-only Cognito account and autonomously carries out a submitted task in the currently visible Windows app. Gemini 3.5 Flash-Lite proposes one next action at a time; the local supervisor checks and dispatches each action under the task grant. There is no approval prompt between steps. Each task stops after 20 actions or five minutes, and STOP is available as a button and `Ctrl+Alt+Shift+S` hotkey.

This is a Windows visual-control pilot, not guaranteed compatibility with every application. It targets the foreground window at normal user integrity; elevated/UAC and secure-desktop prompts are outside scope, and some games, remote sessions, DRM surfaces and custom controls may not expose usable input/accessibility. Google Calendar scheduling and Google Meet joining use the visible browser session already signed into the intended account; there is no dedicated browser extension or Calendar API connector. The task instruction must include required event details and named invitees; if details are missing, Autobots stops and asks for them. Joining defaults to microphone and camera off. A site sign-in, verification challenge, or provider-required confirmation can still block a task. The shared Avalonia shell and OS abstractions leave room for future macOS/Linux adapters; no native support for those systems is implemented. The separate website remains a blank page reserved for later.

## Install the Windows development build

The deployment bundle is produced in `artifacts/` after the AWS and GCP dev stacks are applied. It includes a self-contained Windows x64 executable and the public API/Cognito settings for this deployment. Keep it private. See [Windows installation steps](docs/INSTALL_WINDOWS.md).

To rebuild the bundle from this workspace after deploying:

```powershell
.\scripts\package_windows_app.ps1
```

The first deployment's owner invitation is sent by:

```powershell
.\scripts\bootstrap_cognito_owner.ps1
```

Complete the temporary-password change in the browser at first sign-in. Cognito access and refresh tokens remain in app memory and are not written to disk.

## Current cloud setup

- AWS `ap-south-1`: one `t3.medium` API host, TLS endpoint, private/versioned S3 release and backup bucket, Cognito owner pool, CloudWatch alarms, and a $75 account budget with an automatic stop action for the API instance at $70 actual spend.
- Google Cloud: a dedicated Gemini inference service account and AWS workload identity federation. No long-lived service-account key or AWS static key is installed on the API host.
- Gemini 3.5 Flash-Lite uses application ledger limits of $0.25 per task, $0.50 per UTC day, and $20 per UTC month. These are API-side controls, not a GCP billing cap.
- Screenshots are not persisted by the API. Submitting a task authorizes repeated full-primary-display capture and upload for that task (up to five minutes or 20 actions), and autonomous steps in the visible app. Keep private information off screen; use STOP to cancel.

AWS cost is usage-dependent. The dev instance's current on-demand compute rate is about $32.70 per 730-hour month; the 32 GiB gp3 volume and public IPv4 add about $6.20/month before taxes, data transfer, and other account usage. AWS budget actions have billing-data delay and are not a hard account-wide spend cap.

## Local verification

Backend checks:

```powershell
.\.venv\Scripts\python.exe -m pytest
```

Desktop build and unit checks:

```powershell
.\.tools\dotnet\dotnet.exe test Autobots.slnx
.\.tools\dotnet\dotnet.exe build apps/desktop/shell/Autobots.Desktop/Autobots.Desktop.csproj
```

The website's one-page blank placeholder builds from `apps/website` with `npm ci` and `npm run build`.

The automated desktop tests use supervisor contracts and synthetic frames only; they never inject input into the developer's active desktop. The actual Windows UI, screen capture, global hotkey and first autonomous task still require a user-run integration check.

## Repository map

- `apps/desktop/` — Avalonia shell and cross-platform shared supervisor.
- `apps/website/` — blank landing-page placeholder.
- `services/api/` — Cognito-protected FastAPI control plane and low-cost Gemini provider.
- `packages/contracts/` — shared action schema and generated types.
- `platforms/` — shared OS interfaces and the Windows capture adapter.
- `infra/aws/dev/`, `infra/gcp/dev/` — separated Terraform roots for dev infrastructure.
- `docs/MASTER_PLAN.md` — current specification; `docs/implementation-status.md` records delivered work and limitations.
- `docs/archive/` — original planning documents preserved for reference.
