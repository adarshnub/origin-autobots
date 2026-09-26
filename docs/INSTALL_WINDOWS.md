# Install and use Autobots on Windows

This self-contained Windows x64 development bundle connects to the owner-only Autobots API and private account sign-in configured for this deployment. AWS Cognito manages the Autobots account behind the sign-in page. Keep the ZIP private because its configuration identifies the private development service. The executable is unsigned.

## First run

1. Extract the ZIP to a folder you control and run `Autobots.Desktop.exe`.
2. Choose **Connect account** and sign in to your Autobots account in the browser. It is separate from Google or other app sign-ins. Complete the temporary-password change if this is your first sign-in.
3. Sign in to the websites and apps you want Autobots to use yourself (for browser tasks, the intended Google account in that browser).
4. Type a task, or press the mic (or `Ctrl+Alt+Space` from any app) and say it. Choose **Start** (or `Ctrl+Enter`). Submitting authorizes screen capture and mouse/keyboard input for that task only.
5. Autobots minimizes itself and a pilot bar appears at the bottom of the screen. Watch the glowing cursor move, click and type; the pilot bar shows each step and what the AI intends to do. A screenshot of the primary display is sent to the AI before each step; the API does not store screenshots.
6. Use **Stop** on the pilot bar, the tray menu or `Ctrl+Alt+Shift+S` at any time. When the task ends, the pilot bar shows the result; **Open** brings back the main window and its activity feed. A reported completion is the AI's reading of the screen, not proof that an external change succeeded.

## Voice

Press `Ctrl+Alt+Space` (or the mic button, or **Talk to Autobots** in the tray) and speak. Press it again, choose **Done**, or pause briefly to finish; recordings are capped at 45 seconds and kept only in memory. The Gemini transcript is shown with a 3-second countdown: **Start** begins immediately, the pencil opens it for editing, and ✕ cancels. Turn off automatic start in Settings to always confirm first. Voice needs Windows to allow desktop apps to use the microphone (Settings > Privacy & security > Microphone) and an Autobots service version that supports transcription.

## Settings

Open the gear in the header to change steps and minutes per task (defaults 40 and 10), pointer speed, the cursor halo, voice options and whether closing the window keeps Autobots in the tray. Keeping it in the tray keeps the talk and STOP shortcuts available; use **Quit Autobots** in the tray menu to exit.

## STOP and limits

STOP invalidates the local task lease, cancels pending model work, and releases input Autobots still holds. It cannot undo an action already delivered to another app. A global shortcut may be unavailable if another application registered it; the app reports that and keeps the buttons.

Autobots can use any app on the desktop that runs at your normal user level, including the Start menu, taskbar, browsers and VS Code. Each action is tied to the window that was active in its screenshot; if focus changes first, nothing is sent and Autobots looks again. It will not type into password fields or controls it cannot verify as text fields, and it does not act on UAC prompts, the lock screen or apps running as administrator — it stops and hands those back to you. Bringing the Autobots window to the front during a task also stops it. Only the primary display is observed. Some games, remote sessions, DRM surfaces and custom controls may not accept input reliably.

This pilot uses screenshot-based browser interaction; it has no browser extension or direct Google Calendar API integration. For events, give the title, date, start time and time zone, duration and invitees; Autobots asks when details are missing. It joins meetings with microphone and camera off unless you say otherwise and never records.

The API runs on one development EC2 instance with SQLite. Local STOP works without the API, but cloud task-stop acknowledgement, model steps and transcription need the network. Sign-in tokens stay in app memory and are not written to disk; settings (limits and toggles only) are stored in `%LOCALAPPDATA%\Origin Studios\Autobots\settings.json`.

To rebuild the bundle after updating the deployed API, run `scripts/package_windows_app.ps1` from PowerShell in the repository. The API URL and public Cognito app-client ID come from Terraform outputs; the app bundle contains no cloud credentials.

The owner Autobots account is managed by Cognito and was created by `scripts/bootstrap_cognito_owner.ps1`; its temporary-password invitation was sent to the owner email configured in the private Terraform variables.
