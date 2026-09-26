# Autobots website

This React/Vite project builds a two-page public site: `/index.html` and `/developer/index.html`. The active entry points use `Landing.tsx`, `BotsScene.tsx`, `ui.tsx`, and `design.css`. A single fixed React Three Fiber canvas animates three helper robots along section-based scroll flight paths, with cursor tracking and idle expressions. The product gallery uses screenshots of the actual app and disarmed sample UI states, not a recorded live task. Screenshot assets and the OFL-licensed Manrope fonts are included under `src/assets/`.

The Windows pilot remains private and unsigned. By default the Windows card links to Adarsh's LinkedIn profile to request access. When a public release is approved and hosted, set `VITE_WINDOWS_DOWNLOAD_URL` to its download URL at build time. The site never bundles the owner-only ZIP from `artifacts/`.

From `apps/website`, the existing scripts are `npm run dev` and `npm run build`. Both pages use absolute links, so the static host should serve the site from its root. Copy both generated HTML files and the `assets/` directory from `dist/` when publishing.

The current AWS CloudFront distribution URL is `https://d39k5o9aaxn0fs.cloudfront.net`. Infrastructure lives in `infra/aws/website`; `scripts/deploy_website_aws.ps1` uploads a prepared `dist/` and requests cache invalidation. The public URL was assigned by CloudFront and deployed on 26 September 2026. Browser rendering was not checked at the owner's request.
