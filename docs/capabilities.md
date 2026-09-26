# Capability and toolchain findings

## Custom website hostname — 26 September 2026

The owner's existing GoDaddy session exposed the editable DNS zone for `origin-studio.in`. Added only the Autobots CNAME and ACM validation CNAME; the main website's root `A` record, `www` and nameservers remain intact. The existing AWS CLI identity and installed Terraform/provider were sufficient; no dependency installation, registrar API credentials or new AWS keys were needed. ACM in `us-east-1` reported the non-exportable certificate `ISSUED`, and CloudFront `E21MANSPJEWFLG` reported `Deployed` with alias `autobots.origin-studio.in`. The authoritative GoDaddy DNS server returned the expected CloudFront CNAME with TTL 3600. The public address is `https://autobots.origin-studio.in`. No application, HTTP or rendered-page tests were run, and global DNS propagation is not claimed. See [implementation evidence](implementation-status.md) and [domain configuration guide](../infra/aws/website/README.md).

## Eyes brand and illustrated voice workflow — 26 September 2026

The website now uses the eyes brand mark consistently with native Windows icon resources, and the desktop empty activity view has a shaded helper illustration. The existing WebGL helpers have distinct curved scroll paths, later formations and manual Orbit/V controls. The landing walkthrough is a locally rendered simulation of voice instruction, Google Calendar creation and WhatsApp sharing to the owner-specified contact; no real app workflow is claimed verified. The developer page now lists the role AI Engineer, 16 curated public projects and 10 resume-based professional contributions. Requested exclusions and employer project details are omitted. See [brand and walkthrough notes](website-brand-and-walkthrough.md) and [portfolio sources](developer-project-sources.md). No app/browser tests were run.

## Developer portfolio expansion — 26 September 2026

The developer page now identifies Ant Venture.ai as the current company using the owner's correction, and links GitHub account `adarshnub`. Public GitHub documentation and selected source files provided the basis for 22 project summaries, categorized filtering, text search, and three featured projects. The existing React/Vite dependencies were sufficient; no installation or framework migration was needed. See [portfolio source notes](developer-project-sources.md). Only the production build and AWS publication workflow were run; application and browser testing remain skipped at the owner's request.

## Website implementation note — 26 September 2026

The formerly blank `apps/website` project now contains a two-page Vite/React product site. The landing page uses the existing pinned React Three Fiber/Three.js packages for a continuous scroll and cursor responsive scene, and includes an illustrative task walkthrough. The separate developer page uses publicly visible details from the owner-supplied LinkedIn profile. A production build was generated and uploaded to a new AWS CloudFront distribution at `https://d39k5o9aaxn0fs.cloudfront.net`; the distribution and cache invalidation completed. The owner requested no browser or application checks, so actual rendered behavior was not verified. The private unsigned Windows pilot ZIP is not exposed as a public download.

Checked 25 September 2026; desktop behavior updated 26 September 2026. This records what was exercised in the current implementation pass and distinguishes local probes from end-to-end user sign-in.

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

- The Windows x64 app is published as a self-contained ZIP with the deployed API URL and public Cognito client ID. It contains no cloud credentials. The current local bundle is `artifacts/Autobots-Windows-Pilot-20260926-110829.zip` (SHA-256 `4365a84d…6df5ed`); it has been launched locally but not yet used for a live task.
- Cognito uses authorization-code flow with PKCE and a loopback callback on `127.0.0.1:53682`. Access/refresh tokens stay in process memory.
- Screen capture uses GDI `CopyFromScreen` of the primary display while a task holds the local lease. Observations are JPEG (quality 85) scaled to fit 1440×900; normalized 0–999 model coordinates map to physical display pixels, the display origin and the virtual desktop. Autobots' pilot bar and pointer indicator are hidden (moved off-screen / hidden, then a compositor flush) for the instant of each capture; they are not capture-excluded from other software. The planned Windows.Graphics.Capture picker is not implemented.
- A task may act in any normal-integrity window on the interactive desktop. Each one-use capability binds to the task, observation, the window active at capture, and the display layout; the executor re-checks focus, layout and cancellation before and during paced input. A pre-dispatch failure sends nothing and the task re-observes; a failure after input reached Windows stops the task as uncertain.
- `WindowsInputController` moves the real pointer along an eased arc (170–650 ms, scaled by the pointer-speed setting) before clicking, sends multi-clicks as one batch, drags with a held and tracked left button, scrolls in wheel notches, types short text at a visible pace (newline → Enter, tab → Tab) and sends key chords with scan codes and extended-key flags. Text entry requires a focused, non-password Edit/Document/ComboBox, an editable value control, or a keyboard-focusable text-pattern control (for example a terminal). Held keys/buttons are tracked and released on STOP or failure.
- Pointer targets are refused when they fall on Autobots' own (non-click-through) windows or on a process with higher integrity than Autobots (reported instead of silently dropped by Windows). The lock screen and UAC secure desktop are detected through the input desktop name and handed back to the owner.
- `Ctrl+Alt+Shift+S` (STOP) and `Ctrl+Alt+Space` (talk) are registered as global shortcuts on dedicated message-pump threads; STOP is also on the pilot bar, the main window and the tray menu. If another app holds a shortcut, the app says so and keeps the buttons.
- Push-to-talk records the default microphone through WinMM (16 kHz, mono, 16-bit) in memory, with an energy-based pause detector and a 45-second cap. Windows shows its microphone-in-use indicator during capture. If desktop apps are denied microphone access, capture fails or yields silence and the app points the owner to Windows privacy settings.
- Visible browser interactions use the same screenshot/action loop; there is no browser extension, DOM adapter or Google Calendar API connector. Some custom apps, games, remote sessions and DRM surfaces may block or poorly expose input. Literal support for every installed app is not claimed.
- Not implemented: pausing on physical owner input, multi-monitor observation, app signing, update delivery, and native macOS/Linux capture/input adapters (shared interfaces and a non-Windows compile target exist).

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
