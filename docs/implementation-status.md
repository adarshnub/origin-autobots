# Implementation status

Updated 26 September 2026.

## Latest — Calendar qualification, limits and website copy

- The owner raised desktop inference limits to $1/task, $5/UTC day and $100/UTC month. The running AWS API process was checked for those exact values; separate speech limits stayed unchanged. API 0.4.2 was deployed with sanitized provider-error diagnostics and passed its public live health check.
- The desktop now defaults to 100 actions per task and allows 5–200 in settings, with the existing 10-minute default. A transient API 502 now triggers at most two retries with fresh screenshots and no native input on the failed requests. The desktop build succeeded and 83 local .NET tests passed.
- A fresh 30 September 2026 Google Calendar test event was saved. Copied conference details independently showed the exact title, 2:00–2:15 PM Asia/Kolkata and Meet URL. The date/time adapter verified all four accessible fields before Save. Sharing to Amal Tgh remains at a Google Computer Use owner-confirmation handoff; no automated WhatsApp send is claimed.
- The landing hero now asks “What if your cursor had eyes and a brain?” and describes visible Windows screen, mouse and keyboard operation with local STOP. The Vite build passed and AWS CloudFront invalidation `I51T9WKO6405Y33B3ZA87BAU6J` completed for `https://autobots.origin-studio.in`. No browser visual inspection was run for this copy change.
- See [live qualification log](testing-2026-09-26.md) for exact run outcomes, commands, cloud changes, skipped checks and remaining work. The chronological sections below retain their original historical evidence.

## 26 September 2026 — live QA, text editing and usage reporting

The owner then stopped further Notepad testing and requested two live meeting flows: create/join/share a new Meet and schedule/share a later Meet with Amal TGH via WhatsApp desktop. The first run created a Meet URL but hit a Firefox camera/microphone permission prompt before the join or send. The app now exposes Resume and Stop during this handoff. Resume keeps the original task, device lease and instruction, waits for the owner to handle the prompt, then captures a fresh screen. It never treats the provider's confirmation request as permission for native input. The owner is handling the prompt in an active continuation run; see the live log for the eventual result. The scheduling test uses the owner-approved arbitrary values 27 September 2026, 11:00–11:15 IST. No message has been verified sent yet.

- Owner-authorized native runs use disposable files and the normal product task grant/lease. Full results, failures, commands, metrics and skipped checks: [live qualification log](testing-2026-09-26.md). Earlier no-live-test entries below are historical.
- Added opt-in local action diagnostics, measured pointer arrival before clicks, precise text-edit guidance and a File-menu fallback for intercepted shortcuts. Fixed local/server epoch confusion, composer Alt+Tab interference, repeated hotkey registration and `Super_L` key parsing. STOP and focus/lease validation remain enforced.
- Added the private desktop **Usage & costs** window and authenticated API 0.4.0 `/v1/usage`: model requests/tokens/estimated costs, failures, daily/monthly limits, separate speech accounting and existing AWS budget billing with timestamp. GCP invoices are unavailable because the project's BigQuery API/export is not enabled; this is labeled, not shown as zero.
- Regression evidence: 68 Python tests and 68 .NET tests passed; Windows bundle built. Nine-target live calibration passed with zero measured pixel error, 14 actions, 70.11 s. Button STOP observed in 48.77 ms with no subsequent executed actions. Longer editing results are recorded individually in the log.
- Cloud changes: API source artifact uploaded and deployed through SSM with live health success; one reviewed narrow IAM policy reads the existing budget; deployment supplies only account/budget identifiers. No resource replacement, budget increase, new dependency or public desktop release.
- Website adds three interactive workflow examples, including create/join Meet and invite Amal on WhatsApp. That communication workflow remains explicitly untested.
- Next: finish the current exact-content live run and website browser checks, then qualify voice and an explicitly scoped meeting workflow. GCP invoice integration needs an existing billing export or a separately scoped export setup.

## 26 September 2026 — custom website domain

- **Components:** Added optional custom-hostname support to the isolated website Terraform root, an account-restricted `us-east-1` AWS provider for ACM, a DNS-validated non-exportable RSA certificate, certificate validation, CloudFront alias/SNI TLS configuration and public DNS outputs. Public website URL: `https://autobots.origin-studio.in`; the existing CloudFront address remains available. Updated the infrastructure guide and master plan.
- **Authorization and scope:** The owner explicitly authorized connecting an Autobots subdomain using their signed-in GoDaddy session. The actual registrar domain is `origin-studio.in`. Only two CNAME records were added; the existing root `A`, `www`, nameservers and other records were preserved. No registrar API key or new AWS credentials were created or stored.
- **Commands and reviewed plans:** Saved and inspected `terraform plan -target=aws_acm_certificate.site -out=certificate-bootstrap.tfplan -input=false`, then applied that exact plan: **1 added, 0 changed, 0 destroyed**. After saving the validation CNAME in GoDaddy, saved and inspected a full `terraform plan -out=custom-domain.tfplan -input=false`: **1 add, 2 changes, 0 destroys** proposed, including a deferred bucket-policy read. Applied the saved full plan: **1 added, 1 changed, 0 destroyed**; the recalculated bucket policy needed no write. Commands used `.tools/terraform/terraform.exe -chdir=infra/aws/website`. Bootstrap targeting was followed by the complete plan/apply.
- **Deployment evidence:** ACM `describe-certificate` reported `ISSUED` and export `DISABLED` for certificate `105f4ea8-398a-48b8-b8a7-555e93c25ca3`. CloudFront `get-distribution` reported `Deployed` for `E21MANSPJEWFLG`, alias `autobots.origin-studio.in`, the issued certificate and minimum TLS `TLSv1.2_2021`. GoDaddy displayed nine records after the two additions and confirmed both saves. `Resolve-DnsName -Name autobots.origin-studio.in -Type CNAME -Server ns63.domaincontrol.com -DnsOnly` returned `d39k5o9aaxn0fs.cloudfront.net`, TTL 3600.
- **DNS records added:** `autobots` → `d39k5o9aaxn0fs.cloudfront.net`; `_3fb14074b114cea6a5bbfc4010f3a592.autobots` → `_a9c9180a77e90eda208bbf69f802282b.wzccmgtwzk.acm-validations.aws`. Both are CNAMEs with an observed one-hour TTL. Retain the validation CNAME for certificate renewal.
- **Cloud resources changed:** One ACM certificate created and the existing CloudFront distribution updated. Terraform also tracks certificate validation. No new S3 origin, hosted zone, server, domain registration or application release was created. Existing website objects and desktop/API/authentication configuration were not changed.
- **Tests executed and limitations:** None. App/browser testing, HTTP requests and UI behavior checks were skipped under the owner's instructions. Registrar UI inspection, AWS deployment status and an authoritative DNS lookup supplied configuration evidence only; they do not prove global DNS propagation or rendered website behavior.
- **Measured metrics:** Certificate creation took 6 seconds, certificate-validation resource completion took 3 seconds, and CloudFront modification took 2 minutes 47 seconds. No app performance measurements were collected.
- **Next task:** No required configuration work remains for the custom hostname. Keep both DNS records in place and allow cached DNS responses to expire.

## 26 September 2026 — viewport-triggered demo playback

- **Components:** The task walkthrough automatically starts once on first viewport entry with at least 15% visibility in an active tab. Offscreen playback remains suspended and resumes on return if still playing. Manual pause, STOP and chapter selection suppress automatic restarts; reduced-motion visitors retain manual playback.
- **Commands and evidence:** `npm exec vite build` completed in 988 ms; `scripts/deploy_website_aws.ps1` uploaded the production artifacts successfully; `aws cloudfront wait invalidation-completed` completed for `IDSCGR9MJOI6ZY11XZ5B9AD6Y2` on `E21MANSPJEWFLG`. Published at `https://d39k5o9aaxn0fs.cloudfront.net/#in-action`.
- **Tests executed:** None. Browser/app checks and type checking were skipped under the owner's existing instructions; viewport behavior is implemented but not runtime-tested.
- **Cloud resources changed:** Updated objects in the existing website S3 origin and invalidated CloudFront. No infrastructure or desktop application changes.
- **Metrics and limitations:** Main page JS 28.90 kB (7.66 kB gzip); existing shared 3D chunk still emits the build size warning. No runtime measurements were collected.
- **Next task:** Owner review of the published autoplay behavior.

## 26 September 2026 — eyes branding, voice demo and curated resume portfolio

- **Components:** Added one vector eyes mark with generated website favicon/touch assets and multi-resolution Windows ICO/PNG resources. Updated desktop branding in the executable/window/tray resources, active pilot and pointer caption, and added a shaded companion illustration to the empty activity view. WebGL robot chest panels now carry the eyes mark. Existing functional action icons retain their meanings.
- **Website experience:** Added distinct winding scroll paths and nine waypoints per bot, later group formations, Orbit/V/Explore controls and walkthrough-aware flanking positions. Replaced the old screenshot slideshow with a seven-chapter interactive simulation of voice instruction, browser opening, Google Calendar creation, meeting-link retrieval, WhatsApp contact selection/message composition, and completion. Includes a persistent pilot, agent cursor, waveform, transcript, chapter navigation, pause/replay and simulation STOP. The reconstructed app screens are labeled as previews/simulation.
- **Developer content:** Role changed to AI Engineer at Ant Venture.ai. Removed six previously displayed projects at the owner's request; 16 public GitHub entries remain. Read the owner's local resume and added 10 professional contributions, historical roles and additional technologies. Excluded Ant Venture project details, Rahma, private repositories, Datacom, Git City, Pe-Do, Petgram and personal portfolio projects. Resume contact details and the PDF itself were not published. See [source notes](developer-project-sources.md).
- **Commands actually executed:** `python -X utf8 scripts/generate_brand_assets.py`; `npm exec vite build`; `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package_windows_app.ps1 -ReleaseName Autobots-Windows-Eyes-Pilot -NoRestore`; `scripts/deploy_website_aws.ps1`; `aws cloudfront wait invalidation-completed --distribution-id E21MANSPJEWFLG --id I1THRFTMEYMSIYRTK0NOLI7H2M`. The first Windows packaging invocation failed because PowerShell splatted a scalar string into characters; changed the argument collection to an explicit string array and reran successfully. No restore or dependency installation was needed.
- **Artifact evidence:** Final Vite production build completed in 1.60 seconds. The website upload succeeded and CloudFront invalidation `I1THRFTMEYMSIYRTK0NOLI7H2M` completed. Public site: `https://d39k5o9aaxn0fs.cloudfront.net`. Windows compilation and packaging completed; private unsigned artifact: `artifacts/Autobots-Windows-Eyes-Pilot-20260926-152013.zip`, SHA-256 `1aa14f4cf7cf55a15a582d5ed16d1e7ab713163a159585afcf88f6e656be6668`. The bundle was not launched or installed over an existing app.
- **Tests actually executed:** None. App/browser tests, UI inspection, type checking, live desktop tasks, microphone capture, real Calendar creation, actual WhatsApp messages and HTTP checks were skipped. Build/deployment evidence does not establish runtime interaction success. The walkthrough uses fictional content and a non-navigable example meeting string.
- **Measured metrics and limits:** Main page JS 28.54 kB (7.55 kB gzip), developer page JS 23.99 kB (7.61 kB gzip), existing shared WebGL JS 1,201.27 kB (333.63 kB gzip). Vite still emits its shared-chunk size warning; no performance benchmark was run. The desktop companion is a static shaded illustration; the website robots are live Three.js geometry.
- **Cloud resources changed:** Static objects in the existing S3 website origin and one invalidation on the existing CloudFront distribution. No infrastructure, API service or authentication settings were changed. The Windows bundle remains local and private; no public app release or model call occurred.
- **Next task:** Owner visual review and, if desired, replacing the local Windows pilot using the new bundle. Live workflow qualification remains separate. Details: [brand and walkthrough notes](website-brand-and-walkthrough.md).

## 26 September 2026 — expanded developer portfolio

- **Components:** Updated the developer page with the owner's current company, Ant Venture.ai, a fuller bio, GitHub account `adarshnub`, background and technology sections, three project spotlights, and 22 selected project summaries. Added category filters, counts, text search, an empty-state reset, technology tags and direct repository links. Added page-specific responsive CSS and updated title/description metadata.
- **Content evidence:** Read the public GitHub profile, repository metadata, READMEs and selected source files. All six pinned repositories are represented. Project summaries distinguish prototypes and frontend work; see [developer project sources](developer-project-sources.md). The owner correction supersedes the older TGH Tech reference. No private repository access was used.
- **Run commands:** `npm exec vite build` in `apps/website`; `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy_website_aws.ps1`; `aws cloudfront wait invalidation-completed --distribution-id E21MANSPJEWFLG --id IAZCKG5ELQJNVA3Z4YE6IFL9NF`.
- **Evidence:** Production build completed in 1.01 seconds. Website upload exited successfully and CloudFront invalidation `IAZCKG5ELQJNVA3Z4YE6IFL9NF` completed. Developer page URL: `https://d39k5o9aaxn0fs.cloudfront.net/developer/index.html`.
- **Tests actually executed:** None. App/browser testing, type checking, accessibility audits, live demo visits and HTTP checks were skipped at the owner's request. Production compilation generated deployment artifacts; it is not runtime/UI evidence.
- **Limitations and measured metrics:** Repository descriptions reflect documentation/source rather than independently exercised projects. Developer JS is 21.05 kB (6.81 kB gzip); developer CSS is 10.90 kB (2.75 kB gzip). The existing shared 3D JS chunk remains 1,200.02 kB (333.18 kB gzip) and emits Vite's size warning. No runtime metrics were collected.
- **Cloud resources changed:** Uploaded static objects to the existing website S3 origin and invalidated the existing CloudFront distribution. No infrastructure resources were created or altered. No dependency installation, release publication, desktop control or model call was performed.
- **Next task:** Owner review of the expanded public profile and project selection. No required implementation work remains for this portfolio update.

## 26 September 2026 — flying robot website redesign

- **Components:** Replaced the active landing page and developer-page design with a light lavender product layout and locally hosted Manrope fonts. Added three procedural 3D helper robots with rounded bodies, binocular eyes, blinking, cursor tracking, arm motion, jet effects, task cards, and section-based scroll flight paths. The current React/Vite and React Three Fiber stack already supports these features; no Next.js migration or package installation was needed.
- **App images:** Copied the existing app-home, working-preview, settings-preview and floating-pilot screenshots into `apps/website/src/assets`. The home image is a packaged-app screenshot; activity/settings images are existing disarmed UI previews. The website identifies the activity as sample UI preview content. Added screenshot selection, a timed walkthrough, and an expandable image dialog. No desktop input or live app task was performed to obtain these assets.
- **Run commands:** `npm exec vite build`, `scripts/deploy_website_aws.ps1`, and `aws cloudfront wait invalidation-completed` for `I8I79ZYFYT29D2I0VNTZVX3AUN` on distribution `E21MANSPJEWFLG`.
- **Evidence:** Vite production build completed in 5.60 seconds; the upload script exited successfully; the CloudFront invalidation completed. The updated public URL remains `https://d39k5o9aaxn0fs.cloudfront.net`.
- **Tests actually executed:** None. Browser/UI testing remains skipped at the owner's earlier request. Source screenshots were viewed before including them; runtime appearance is not claimed verified.
- **Limitations:** Windows still links to private pilot access until an approved public download URL exists. The screenshots show app UI and sample activity, not a recorded successful automation task. The main shared JS chunk is approximately 1.20 MB (333 KB gzip); no runtime performance measurement was made.
- **Cloud resources changed:** Updated website objects in the existing S3 origin and invalidated CloudFront. No infrastructure resources were created, changed or destroyed.
- **Next task:** Owner visual review of the redesigned site; a live task recording and public Windows release remain separate work.

## 26 September 2026 — public website AWS deployment

- **Components:** Added an isolated Terraform root at `infra/aws/website` for a private, versioned and encrypted S3 origin with public-access blocking, CloudFront origin access control, a read-only distribution-scoped bucket policy, and an HTTPS CloudFront distribution. Added `scripts/deploy_website_aws.ps1` to upload the built pages and assets and invalidate CloudFront.
- **Run commands:** `npm exec vite build` in `apps/website`; Terraform init using the already installed AWS provider; saved Terraform plan and reviewed it before applying; Terraform apply of the saved plan; `scripts/deploy_website_aws.ps1`; `aws cloudfront wait invalidation-completed`.
- **Build/deployment evidence:** Vite generated both `dist/index.html` and `dist/developer/index.html` in 3.35 seconds. The reviewed Terraform plan had **8 adds, 0 changes, 0 destroys** in AWS account `214422569127`; apply reported the same. CloudFront distribution `E21MANSPJEWFLG` reached deployed state, the upload script exited successfully, and invalidation `I4UE6JR7VU8O2NQTM9OTS1BUW1` completed. Public distribution URL: `https://d39k5o9aaxn0fs.cloudfront.net`.
- **Tests actually executed:** None. The Vite production build was required to publish files; no application tests were run.
- **Skipped checks:** No browser visit, HTTP response check, UI review, download click or live app test, per the owner's request.
- **Limitations:** The Windows card still requests access because the existing unsigned, owner-only pilot ZIP is not a public release. This CloudFront URL uses the AWS managed domain; no custom domain was purchased. CloudFront and S3 usage can incur charges under the existing AWS account budget; the account's EC2 stop action does not stop these resources.
- **Cloud resources changed:** One website S3 bucket and its public-access, ownership, encryption, versioning and bucket policy resources; one CloudFront origin access control; one CloudFront distribution. No existing API resources were changed.
- **Measured metrics:** Build duration 3.35 seconds; CloudFront creation 2 minutes 48 seconds. No page performance or user experience metrics were measured.
- **Next task:** Publish a separately approved public Windows release and configure `VITE_WINDOWS_DOWNLOAD_URL` if a direct download should appear. A visual and HTTP review remains pending by owner preference.

## 26 September 2026 — public website implementation

- **Components:** Replaced the blank Vite page with a product landing page, one continuous React Three Fiber scene driven by scroll and cursor position, an interactive illustrated task walkthrough, product controls and getting-started sections, platform cards, and a separate `/developer/index.html` page. The developer copy uses the public LinkedIn profile supplied by the owner. The Windows card requests private pilot access until a public release URL is configured.
- **Run commands:** None. The owner explicitly requested implementation without testing or checks.
- **Tests actually executed:** None.
- **Skipped checks:** TypeScript/build, browser layout, responsive behavior, accessibility, WebGL fallback, download link, deployment and live site checks were not run at the owner's request.
- **Limitations:** The demo is illustrative; there is no recorded live task footage. The existing Windows ZIP is private and unsigned and is not bundled into the public site. The LinkedIn page exposes only limited profile details. Direct download requires `VITE_WINDOWS_DOWNLOAD_URL` at build time and a separately approved public artifact. Current site changes are local code only; no public website URL has been established in this pass.
- **Cloud resources changed:** None.
- **Measured metrics:** None; no run or benchmark was performed.
- **Next task:** Build and review the site, prepare a public Windows release if desired, configure its download URL, then publish the generated site to the intended static host.

## 26 September 2026 — desktop pilot overhaul

### Delivered in this pass (local code; API changes not yet deployed)

- **Whole-desktop tasks.** A task is no longer pinned to the window that was active at submission. Each step observes the primary display and whichever window is active, so Autobots can use the Start menu, the taskbar, browsers, VS Code and other normal-integrity apps. Every one-use action capability is still bound to its own screenshot, the window active in that screenshot, the display layout and the task lease; if focus or layout changes first, nothing is sent and the task re-observes.
- **Visible agent pointer.** The real Windows pointer glides to each target along an eased arc (speed configurable) before clicking, and a click-through indicator follows it: pulsing halo, spinner while the model is thinking, click ripple, scroll chevrons and a caption with the model's stated intent. It replaces the bee animation. The indicator and the pilot bar are hidden for the instant of each Autobots screenshot (not capture-excluded from other software) and the pilot bar moves out of the way of pointer targets.
- **Premium Windows UI.** New dark Windows 11 shell: native dark title bar, branded header, large composer with mic and Start/Stop, suggestion chips, live activity feed, "how it works" card, status bar and a settings sheet (steps and minutes per task, pointer speed, cursor halo, voice auto-start, stop-on-pause, spoken language, keep running in tray). Tasks run from a floating, non-activating **pilot bar** with live step, intent and STOP; results appear there with an Open button. Closing the window keeps Autobots in the tray (Talk, Open, Stop, Quit).
- **Voice (push-to-talk with Gemini).** `Ctrl+Alt+Space` from any app, the mic button or the tray starts listening; pressing again, Done, or a 1.4-second pause finishes (45-second cap). Audio is 16 kHz mono WinMM capture held in memory. The new `POST /v1/transcriptions` route transcribes one clip with Gemini (`gemini-3.5-flash-lite` by default), never stores audio, uses a separate speech budget ledger and cannot start a task. The transcript is shown with a 3-second countdown (Start now, Edit, Cancel) before it becomes the owner's task.
- **Reliability fixes that made tasks fail before:**
  - Tasks died whenever focus moved to another app (the old window pin); now handled per step.
  - Full-resolution PNG screenshots could exceed the 3 MB upload limit; observations are now JPEG, scaled to fit 1440×900 (the size recommended for Gemini computer use) with coordinates still mapped to physical display pixels.
  - The API rejected documented desktop functions (`double_click`, `triple_click`, `move`, `drag_and_drop`, `hotkey`, `take_screenshot`, and `type` with `press_enter`); all are now mapped to the shared contract, and unsupported ones are excluded from the tool configuration.
  - The model had no memory between steps; the device now sends a bounded step history (executed and rejected steps, labelled as untrusted data) and the active window title.
  - One rejected proposal stopped the whole task; pre-dispatch rejections are now fed back and the task re-observes (at most three in a row).
  - Key presses lacked the Windows key, chords such as `Control+L`, punctuation, media keys and extended-key flags; a platform-neutral `KeyChord` parser now handles them and blocks security/system chords (Win+L, Ctrl+Alt+Del, Ctrl+Shift+Esc, graphics reset, Autobots' own shortcuts, Alt+F4 on the desktop).
  - Thinking level `MINIMAL` with 256 output tokens could truncate responses because thinking tokens count toward the limit; the provider now uses `LOW` thinking and 1,024 output tokens.
  - Placeholder text showed `?` in place of punctuation.
- **Safety preserved.** Owner-submitted grant, local supervisor validation, one input lease, offline STOP (button, pilot bar, tray, `Ctrl+Alt+Shift+S`), held-input release, password/unverifiable text-field refusal, no replay of uncertain actions (an action interrupted after input reached Windows stops the task as uncertain), lock screen/UAC handoff, refusal to act on Autobots' own windows, and honest reporting of administrator windows that Windows would silently block.
- **Contracts.** `action-envelope.schema.json` adds `move`, `drag`, `click.clicks` (1–3) and `type_text.press_enter`, with matching Python, C# and TypeScript types and new fixtures.

### Verification run in this pass

- `.venv/Scripts/python.exe -m pytest` — **62 passed**, one existing Starlette deprecation warning.
- `.tools/dotnet/dotnet.exe test Autobots.slnx` — **60 passed**.
- `.tools/dotnet/dotnet.exe build Autobots.slnx` — succeeded with zero warnings (warnings are errors).
- `.tools/dotnet/dotnet.exe build apps/desktop/shell/Autobots.Desktop/Autobots.Desktop.csproj -p:OS=Unix` — shared shell compiled without the Windows adapter.
- Launched the debug build against the deployed API configuration and captured only the Autobots windows with `PrintWindow`: main window, settings sheet, and pilot-bar states (working, listening, voice confirm, result) via the developer `--ui-preview <state>` switch. Rendered the pointer indicator with `--render-pointer-preview <png>`. The app detected that the deployed API (0.2) does not yet advertise transcription and disabled voice with an explanation.
- Packaged the self-contained bundle with `scripts/package_windows_app.ps1 -ReleaseName Autobots-Windows-Pilot`: `artifacts/Autobots-Windows-Pilot-20260926-110829.zip`, SHA-256 `4365a84dc0890cbdab82bb6764a9baccbbbe0544e4f1b53d34b371814c6df5ed`. The packaged executable launched without a system .NET install and rendered the main window.
- Tests use fake providers, synthetic frames and fixtures. No native input was sent to the developer desktop, no task ran, no microphone audio was recorded and no model request was made.

### Not verified / SKIPPED

- **SKIPPED — live end-to-end task.** No authenticated task has been run with the new client; pointer motion, typing pacing, UI Automation text-field checks, Start-menu launches, the visual-settle heuristic and HUD repositioning have not been exercised on a real desktop.
- **SKIPPED — API deployment.** The API changes (0.3.0: step history, desktop-actions-v2, transcription, intent) are local only. The deployed dev API is still 0.2; with it the new app works in a reduced mode (no step history, no new action kinds, no voice). Deploying requires an approved rebuild of `artifacts/autobots-api.zip`, a reviewed Terraform plan for the S3 release object and the SSM deployment script.
- **SKIPPED — live Gemini behaviour.** Transcription quality, the exact desktop function names emitted by `gemini-3.5-flash-lite`, `excluded_predefined_functions` acceptance by Vertex and `LOW` thinking cost have not been probed live.
- **SKIPPED — microphone capture, the global `Ctrl+Alt+Space` key response and the tray menu.** (Both global shortcuts registered at startup without error.)

### Known limits

- Observations cover the primary display only. Physical owner input during a task is not detected (the plan's pause-on-interference remains future work); bringing the Autobots window forward stops the task.
- Screenshot-based control can misread a screen; step/time limits and STOP reduce exposure but do not guarantee correct outcomes. Prompt-injection immunity is not claimed.
- Default limits are now 40 actions and 10 minutes (owner-adjustable 5–100 and 1–30). With `LOW` thinking a step is expected to cost roughly $0.001–0.002, so the existing $0.50/day API ledger allows roughly 250–500 steps per day; this estimate has not been measured.

## Earlier delivered work (25 September 2026)

- Autobots by Origin Studios has a Windows-first Avalonia shell with shared OS abstractions, preserving extension points for macOS and Linux.
- The API uses Cognito owner authentication, device/task ownership, task epochs and a persistent SQLite inference ledger. Gemini 3.5 Flash-Lite calls disable SDK automatic tool execution and return one proposal at a time. The API can also report completion or stop for missing details.
- The current browser workflows target Google Calendar event scheduling and Google Meet joining through an already signed-in browser. No browser extension or direct Calendar API integration exists. The model prompt asks for missing event details and named invitees, defaults Meet joins to camera/microphone off, and prohibits meeting recording.
- The separate landing-page project remains a blank placeholder.
- AWS dev infrastructure is deployed in `ap-south-1` with the small EC2 stack, private S3 releases/backups, owner-only Cognito, CloudWatch alarms, a $75 account budget and an automatic stop action for the Autobots EC2 at $70 actual spend. GCP Vertex access uses AWS workload identity federation; no static AWS key or service-account key was created.
- API inference ledger limits are $0.25 per task, $0.50 per UTC day and $20 per UTC month. These are API-side request controls, not a GCP billing cap.
- The previous live API artifact was deployed through a reviewed Terraform plan that updated only the private S3 release object, followed by the SSM deployment and a public `/healthz` check.
- Previous live checks confirmed AWS-to-GCP workload identity federation and a synthetic Gemini proposal. The hosted Autobots sign-in requires the owner to enter the temporary password from the invitation or complete account recovery.
- The app is unsigned. The planned Windows.Graphics.Capture picker, update delivery and production PostgreSQL migration are not complete. AWS's $75 budget action is delayed and not a hard cap. Gemini computer use is preview and uses global Vertex processing, which does not guarantee India-only data residency. macOS/Linux remain extension points; no native adapters or packages are implemented or tested.

## Cloud resources changed in this pass

None. No Terraform plan or apply, SSM command, S3 upload or model request was made.

## Next task

1. Owner approval to deploy API 0.3.0 (rebuild the source archive, review the Terraform plan for the S3 release object, run `scripts/deploy_api_dev.ps1`, confirm `/healthz` lists `transcription`).
2. Package the Windows build with `scripts/package_windows_app.ps1` and run one owner-supervised task in a non-sensitive app (for example "Open Notepad and type hello"), recording steps, cost and STOP behaviour.
3. Probe voice with one short owner clip and record transcription latency and cost.

See [capability findings](capabilities.md), [Windows install steps](INSTALL_WINDOWS.md), and the [master plan](MASTER_PLAN.md).
