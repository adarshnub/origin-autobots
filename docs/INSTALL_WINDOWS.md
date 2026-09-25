# Install and use Autobots on Windows

This self-contained Windows x64 development bundle connects to the owner-only Autobots API and Cognito sign-in configured by Terraform. Keep the ZIP private because its configuration identifies the private development service. The executable is unsigned.

## First run

1. Extract the ZIP to a folder you control and run `Autobots.Desktop.exe`.
2. Choose **Sign in with Cognito** and complete the owner account's temporary-password change if this is the first sign-in.
3. Open the Windows app you want Autobots to use. For browser tasks, sign in to the intended Google account in that browser yourself and put the relevant page in view.
4. Enter a specific task and choose **Start autonomous Windows task**. That submission authorizes screen capture and native input for that task.
5. Autobots uploads a full primary-display image before each model step, then executes the validated action without a confirmation dialog. It stops after five minutes or 20 actions. The API does not persist screenshots.
6. Watch the task status and use **STOP** or `Ctrl+Alt+Shift+S` to cancel. After stopping, check the target app. Model-reported completion is a status report, not proof that an external change succeeded.

The task needs an active internet connection for inference. Text entry is blocked when Windows accessibility reports a password field, an unidentified field, or a non-text control. Sign in to websites and apps yourself before starting; expired logins, MFA, CAPTCHA, provider-required confirmation, UAC, and secure-desktop prompts may stop the task for attention.

## STOP and limits

Use the red **STOP** button or `Ctrl+Alt+Shift+S`. STOP invalidates the local task lease, cancels pending model work, and releases input Autobots still holds. It cannot undo an action already delivered to another app. The global shortcut may be unavailable if another application has registered it; the app reports that and retains the button.

The Windows adapter targets the foreground window captured for each step and runs as the signed-in user without elevation. It rejects a changed foreground window or display layout. It cannot control UAC/secure-desktop prompts or higher-integrity apps. Some games, remote sessions, DRM surfaces and custom controls may not accept input reliably. This pilot uses screenshot-based browser interaction; it does not include a browser extension or direct Google Calendar API integration. Google Calendar event creation and Google Meet joining run through a browser already signed into the intended account. Specify the event title, date, start time and time zone, duration, and intended invitees in the task; Autobots stops and asks if required details are ambiguous. It joins with microphone and camera off unless the task explicitly says otherwise. It does not record meetings.

Each task is limited to 20 actions and five minutes. Gemini may misread a screen or report completion incorrectly; verify results in the target app.

The API runs on one development EC2 instance with SQLite. Local STOP works without API availability, but cloud task-stop acknowledgement and later model observations require the network. Sign-in tokens stay in app memory and are not written to disk.

To rebuild the bundle after updating the deployed API, run `scripts/package_windows_app.ps1` from PowerShell in the repository. The API URL and public Cognito app-client ID come from Terraform outputs; the app bundle contains no cloud credentials.

The owner Cognito user was created by `scripts/bootstrap_cognito_owner.ps1`; its temporary-password invitation was sent to the owner email configured in the private Terraform variables.
