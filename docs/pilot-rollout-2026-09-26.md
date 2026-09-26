# Invite-only pilot and Windows v0.5.0 rollout — 26 September 2026

## Components

- `services/api`: Cognito owner/pilot roles; owner-subject-only invitation endpoints; SQLite invite/profile registry; $10 lifetime per pilot across desktop and speech request reservations; public signup remains disabled.
- `apps/website`: owner portal at `/pilot/index.html`, public downloads page at `/downloads/index.html`, versioned release catalog loaded from S3.
- Windows desktop v0.5.0: exact-text Notepad typing guard, Unicode character pacing and UI Automation readback, honest WhatsApp completion result, profile/budget error messages.
- AWS: existing Cognito pool, EC2 API, S3 website bucket and CloudFront distribution. No new database instance was created. SQLite on the single EC2 host already backs device/task history and now holds pilot profiles and reservation ownership.

## Run commands and results

- `.venv/Scripts/python.exe -m pytest`: **72 passed**, one upstream Starlette deprecation warning; fake provider and no native desktop input.
- `.tools/dotnet/dotnet.exe test apps/desktop/tests/Autobots.Core.Tests/Autobots.Core.Tests.csproj --no-restore`: **90 passed**, zero skipped; no developer desktop input.
- `scripts/build_website_pilot.ps1`: TypeScript and Vite build **passed**, four HTML entrypoints. Existing large landing scene chunk reported a size warning.
- Reviewed `terraform plan` in `infra/aws/dev`, then applied saved `artifacts/pilot-infra-final.tfplan`: **2 added, 4 changed in place, 0 destroyed**. Additions were `autobots-pilots` group and API role policy restricted to `AdminCreateUser`/`AdminAddUserToGroup` on the existing pool. Changes were invite email template, portal callback/logout URLs, EC2 user-data drift for already approved global limits, and the API S3 release object. EC2 instance was not replaced.
- `scripts/deploy_api_dev.ps1`: SSM deploy and public API health **passed**. `/healthz` reports API **0.5.0** and `invite-only-pilots`.
- `scripts/package_windows_app.ps1 -ReleaseName Autobots-Windows-0.5.0 -NoRestore`: self-contained unsigned Windows x64 ZIP built. Local ignored artifact: `artifacts/Autobots-Windows-0.5.0-20260926-221708.zip` (113,231,653 bytes), SHA-256 `38ca4579eeb651cb88c3bb460c6a30d04eb51f3baf20578d1f5e2fb6ee4e8805`. Inspection found 10 entries and only `apiBaseUrl`, `cognitoHostedUiBaseUrl` and `cognitoClientId` in runtime configuration; no credential-named files.
- `scripts/publish_windows_release.ps1`: uploaded immutable v0.5.0 ZIP and SHA-256 file to the website S3 bucket, published the S3 release catalog, and updated the previous direct-link alias to the same bytes. No app binary was added to Git. Release notes are the small reviewed file `releases/windows/0.5.0.json`.
- `scripts/deploy_website_aws.ps1`: uploaded four pages and assets; CloudFront invalidation `I97CFTOJ42S6VP5XWYS9U8XSU6` completed. A later release-catalog/alias invalidation was requested by the release script.
- Public HTTP checks: landing, portal, downloads page, release catalog, versioned ZIP and checksum returned **200**; versioned ZIP reported 113,231,653 bytes; catalog listed `0.5.0` with the local ZIP SHA-256. Anonymous owner route returned **401**. CORS preflight from `https://autobots.origin-studio.in` returned **200** with the allowed origin, methods and authorization header.
- A read-only SSM check on the live API process returned `owner_subject_configured`; the configured owner subject was not printed or logged. The direct legacy download alias was updated to the same v0.5.0 bytes.
- Browser rendering: the public downloads page displayed its hero, release list heading, v0.5.0 changelog, matching checksum and download links in Chrome at a desktop viewport. A later scroll/full-page screenshot call timed out in the browser connection; mobile visual rendering was not checked.

## Live text issue

The previous Notepad-to-WhatsApp run was reported complete after 18 actions, but independent Notepad UI Automation and clipboard readback contained only 122 incorrect characters instead of the requested 177-character story. The revised Windows input sends paced Unicode characters and checks the blank Notepad document text before another action can use it. A fresh owner-submitted Notepad-only run read back **177/177 exact characters**. That run's result banner incorrectly flagged the negative instruction “Do not ... WhatsApp”; the guard was corrected and covered by local .NET tests. This evidence covers exact text entry in that Notepad condition. The full WhatsApp send remains unverified and provider confirmation is still honored.

## Skipped and limitations

- No real pilot email was supplied; no invitation email was sent and the full temporary-password, first-login and dashboard flow was not exercised with a real invited account. Owner-only access and role separation are covered by fake-token API tests; authenticated live owner portal UI is **unverified**.
- The publicly downloaded v0.5.0 ZIP was not installed on another user's device. Its contents, build and URL were checked; Windows signing is unavailable and the build remains unsigned.
- The $10 lifetime amount is an API request-dispatch guard across both model ledgers, with a $0.05 desktop reservation before each pilot call. Provider-reported actual cost can exceed a reservation; it is not a hard GCP billing cap. Global $1/task, $5/day and $100/month inference controls and separate speech service caps still apply.
- The development API uses one EC2 host and SQLite with the existing daily backup process. Multi-instance database failover and a real restore drill were not completed for this rollout. GCP invoice export remains unavailable; request costs are estimates.
- No external WhatsApp delivery was performed or claimed by this rollout. Google Computer Use confirmation handoffs are preserved.

## Public links

- Downloads: `https://autobots.origin-studio.in/downloads/index.html`
- Pilot portal: `https://autobots.origin-studio.in/pilot/index.html`
- v0.5.0 ZIP: `https://autobots.origin-studio.in/downloads/windows/0.5.0/Autobots-Windows-0.5.0.zip`
- Release catalog: `https://autobots.origin-studio.in/downloads/releases.json`

Next: the owner enters a real pilot email in the portal; observe Cognito invitation delivery, first password change, profile completion, desktop sign-in and a bounded task before claiming that new-user path passed end to end. Later releases use a new app version, a new `releases/windows/<version>.json` file and a new immutable S3 key.
