# Capability and toolchain findings

Checked 25 September 2026. This records what was exercised in the current implementation pass and distinguishes local probes from end-to-end user sign-in.

## Local environment

| Capability | Verified state | Evidence or limit |
|---|---|---|
| .NET | Pinned .NET SDK 10.0.100 installed under `.tools/dotnet`. | Windows shell and solution tests built locally. |
| Python | Python 3.11 virtual environment under `.venv`. | FastAPI, PyJWT, Google Gen AI SDK 2.25.0 and test dependencies installed. |
| Node.js / npm | Available locally. | Website dependencies installed and production build passed. |
| Terraform | Pinned Terraform 1.16.4 under `.tools/terraform`. | Both cloud roots validated and applied using reviewed saved plans. |
| AWS identity | Signed-in AWS account resolved to the expected account. | Deployed one API instance and owner-only Cognito resources; no static access keys were created. |
| GCP identity | Active `gcloud` user has project access. The machine's default ADC identity is different and lacks project service-list permissions. | Terraform used a short-lived access token from the signed-in `gcloud` user for plan/apply. No token was written to repo files. |

## Desktop and app behavior

- The Windows x64 app is published as a self-contained ZIP with the deployed API URL and public Cognito client ID. It contains no cloud credentials. The current task-scoped autonomous build is `artifacts/Autobots-Windows-20260925-200227.zip`; its sidecar hash was checked.
- Cognito uses authorization-code flow with PKCE and a loopback callback on `127.0.0.1:53682`. Access/refresh tokens stay in process memory.
- The screen-capture adapter is a GDI `CopyFromScreen` prototype. Submitting a task authorizes repeated full-primary-display uploads for at most five minutes and 20 actions. The planned Windows.Graphics.Capture picker and capture exclusion are not implemented.
- The API returns one `not_executed` proposal, a model completion report, or a request for missing task details per observation; it never sends native input. The desktop locally validates each proposal, creates a one-use action capability from the active owner task grant, sends input, and observes again. There is no per-action confirmation dialog.
- `WindowsInputController` uses `SendInput` only with a fresh one-use capability tied to the task, observation, foreground window and display layout. It rechecks these before dispatch and before each input batch. Unicode typing is limited to a focused, accessible non-password edit, combo box or document. It tracks its own held keys/buttons and releases them on interruption.
- `Ctrl+Alt+Shift+S` is registered as an offline global STOP shortcut on a dedicated message-pump thread; the visible STOP button remains available as a fallback. Input is sent at the signed-in user's normal integrity level. UAC/secure-desktop surfaces and higher-integrity apps are unsupported.
- Visible browser interactions are possible through the same autonomous screenshot/action loop, but there is no browser extension, DOM adapter or Google Calendar API connector. The model prompt supports creating Google Calendar events and joining existing Google Meet calls through an already signed-in browser, with missing details stopping the task. The prompt says to join muted with camera off unless the owner asked otherwise and never to start a recording. This path has not been tested end to end. Some custom apps, games, remote sessions and DRM surfaces may block or poorly expose input. Literal support for every installed app is not claimed.
- Voice, global STOP overlay, cursor halo, app signing, update delivery, and native macOS/Linux capture/input adapters remain future work. Shared OS interfaces and non-Windows .NET target selection are present as extension points.

## Cloud and model checks

- The deployed AWS API `/healthz` endpoint returned `status=ok`, `mode=live`; its OpenAPI schema includes the autonomous task response status `needs_input`. Its security group allows public TCP 80/443 only; there is no public SSH or database ingress.
- A read-only SSM check on the EC2 host exchanged its instance role credentials for a short-lived GCP token through the deployed workload identity federation. No model request was part of this check.
- A separate local GCP probe used a synthetic 1×1 image and Gemini 3.5 Flash-Lite. It returned zero calls for a no-action prompt and one normalized click proposal for a center-click prompt. Neither proposal was executed.
- Gemini computer use is preview. The selected model is the lowest-cost model currently documented as supporting computer use; the cheaper Flash-Lite generation does not support this tool. It uses global Vertex processing, which is not an India-only data-residency guarantee.
- The API's SQLite ledger limits inference to $0.25/task, $0.50/day and $20/month. They protect requests routed through this API; they do not cap other GCP project spend or guarantee the provider's final bill.
- AWS has a $75 monthly account budget and an automatic action to stop the Autobots EC2 instance at $70 actual spend. Budget updates are delayed, and this action does not stop other account resources or remove persistent storage/IP charges.

## Current model rates and sources

The published global Standard PayGo table lists Gemini 3.5 Flash-Lite at $0.30/1M input tokens and $2.50/1M output tokens. Rates can change; the API's recorded estimate is not an owner-account quote. [Flash-Lite model page](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-5-flash-lite), [computer-use supported models](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/computer-use), [Vertex pricing](https://cloud.google.com/gemini-enterprise-agent-platform/generative-ai/pricing).

## Verification gaps

- The owner Cognito account was created and an invitation sent. The owner must complete its temporary-password change, then perform the first authenticated app session. The full browser-to-desktop sign-in and proposal request has not yet been completed by the owner.
- The desktop code compiles and supervisor tests use synthetic frames, but the app was not opened against the developer's active desktop. No real screen capture, global hotkey registration, Windows UI Automation field check, or native input was exercised. The repo rule forbids injecting test input into the developer's active desktop.
- WIF token exchange is verified; an authenticated proposal request from the deployed API through Vertex still needs the owner to sign in and approve one synthetic-safe or user-chosen screen upload.
- macOS/Linux targets are architectural extension points only. No native capture, permissions or packaging tests were run on those systems.
