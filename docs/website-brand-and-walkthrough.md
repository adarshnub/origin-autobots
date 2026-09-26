# Eyes branding and simulated task walkthrough

Implemented 26 September 2026 at the owner's request.

## Brand assets

- `assets/brand/autobots-eyes.svg` is the code-owned eyes mark. `scripts/generate_brand_assets.py` exports the same vector to the website SVG/touch icon and Windows PNG/ICO resources. ICO sizes: 16, 24, 32, 48, 64, 128 and 256 pixels.
- `assets/brand/bot-companion.svg` is a shaded companion illustration matching the website's binocular helper theme. It is exported to native PNG resources for the desktop empty activity view and the illustrated website interface. The illustration is static; the website flight scene remains actual interactive Three.js geometry.
- Existing application icon wiring applies the new ICO to the Windows executable, window and tray. The active floating pilot uses the eyes mark; the pointer caption has a miniature pair of eyes. Stop, microphone, warning and other action/state glyphs retain their meanings.
- The 3D robots carry the eyes mark on their chest panels. Header/footer branding and both browser favicons use the same source mark.

## Scene behavior

- Nine distinct waypoints per bot replace the repeated side-switching pattern. Section progress is normalized so landing and developer pages both reach the final formations.
- Deterministic winding curves vary each bot's route and remain reversible when scrolling backward. Scroll velocity contributes a small bank angle; the later waypoints gather the bots into a triangular formation.
- The download section offers Explore, Orbit and V formation controls. Bots flank the walkthrough while it is in view. Motion uses mutable refs in the render loop, honors reduced-motion input, and the canvas stops rendering while the document is hidden.
- Mobile keeps two smaller helpers; reduced motion disables idle flight and pointer response. No performance measurements or device tests were performed.

## Walkthrough

`TaskWalkthrough.tsx` contains seven chapters: voice instruction, browser opening, Calendar event creation, meeting link retrieval, WhatsApp contact selection, message composition/sharing, and completion. The sample recipient is the owner-specified **Amal TGH**; avatar initials and all surrounding content are fictional. The sample event uses 29 September 2026, 15:00–15:30 IST. The displayed `meet.google.com/demo-preview` string is illustrative text, not a navigable live meeting.

The Calendar and WhatsApp windows are hand-built React/CSS illustrations. They are not screenshots, account access evidence or a recording of a successful desktop task. The persistent pilot, moving agent cursor, waveform, transcript, event form and message bubble are rendered locally. Play/pause, chapter selection, replay and STOP only control simulation state. STOP freezes the current state and replay begins a new preview. No microphone, calendar, WhatsApp or model API is used. Playback pauses when the document is hidden and its timer suspends when the walkthrough is offscreen.

The updated hero and pilot examples are labeled interface previews. Earlier screenshots remain preserved in source assets but are no longer rendered by the active landing page. The page explicitly identifies the new workflow as an interactive simulation.

The walkthrough automatically starts on its first entry into view, once at least 15% of its container is visible and the browser tab is active. Its existing timer suspends offscreen and continues on re-entry when playback is still enabled. Manual pause, STOP and chapter selection prevent a later automatic restart. A hidden tab does not begin playback; visitors who prefer reduced motion keep manual playback controls.

## Scope

Production builds generate website files and a private unsigned Windows bundle. No browser or app tests, live desktop task, microphone capture, external message, account access, public Windows release, dependency installation or model request is part of this work. Website publication updates only the existing S3/CloudFront site.
