# Autobots website

The React/Vite website publishes four pages: landing, developer, pilot portal, and [downloads](https://autobots.origin-studio.in/downloads/index.html). The landing page has a continuous React Three Fiber canvas, illustrative walkthrough and product screenshots. The portal uses Cognito authorization code with PKCE; the API enforces owner-only invites and invite-only pilot access.

## Build and deploy

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_website_pilot.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/deploy_website_aws.ps1
```

The build script reads the API and public Cognito client values from the existing Terraform outputs. The deploy script uploads HTML and hashed assets to the existing AWS S3 website bucket and invalidates CloudFront. Release ZIPs are **not** part of Vite's `dist/` output.

## Windows releases

Versioned ZIPs live under `s3://<website-bucket>/downloads/windows/<version>/` and are served by CloudFront. The small `downloads/releases.json` catalog, also in S3, drives the downloads page. Release notes are reviewed as JSON in `releases/windows/`. App ZIPs are local ignored `artifacts/` files until published; `.gitignore` excludes them.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package_windows_app.ps1 -ReleaseName Autobots-Windows-0.5.0 -NoRestore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish_windows_release.ps1 -ZipPath artifacts/Autobots-Windows-0.5.0-<stamp>.zip -NotesPath releases/windows/0.5.0.json
```

The release publisher checks the local SHA-256 sidecar, AWS account and existing version key. It refuses to replace a published version with different bytes, then adds its notes to the public catalog without exposing the checksum. Add a new metadata file and increment the app version for each later release. Old published versions remain downloadable. The unsigned public ZIP contains only public API/Cognito endpoints; account sign-in still requires an owner invitation.
