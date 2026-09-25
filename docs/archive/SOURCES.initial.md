# Primary research sources

Reviewed on **25 September 2026**. These references support platform capabilities, constraints and integration choices, not a claim that the proposed application has been built or benchmarked. Model availability, SDK interfaces, quotas, prices and account policies must be rechecked during M0. References in `MASTER_PLAN.md` use the IDs below.

| ID | Primary source | Relevance |
|---|---|---|
| S01 | Google Cloud, Gemini 3.8 Flash — `https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-8-flash` | Model ID, image/function/computer-use support, thinking settings and locations |
| S02 | Google Cloud, Gemini 3.1 Pro — `https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-1-pro` | Preview status and planning model capabilities; built-in computer-use not supported |
| S03 | Google Cloud, Computer use — `https://docs.cloud.google.com/vertex-ai/generative-ai/docs/computer-use` | Desktop environment, execution loop, prompt-injection detection and required confirmations |
| S04 | Microsoft, SendInput — `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput` | Real input injection and integrity-level restrictions |
| S05 | Microsoft, Interactive Services — `https://learn.microsoft.com/en-us/windows/win32/services/interactive-services` | Service/session boundaries and interactive desktop limitations |
| S06 | Microsoft, .NET support policy — `https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core` | .NET 10 LTS lifecycle |
| S07 | Microsoft, Screen capture — `https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture` | Native Windows capture and user-consent/support considerations |
| S08 | Chrome, Native messaging — `https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging` | Extension-to-native host communications and origin restrictions |
| S09 | Google, Gen AI Python SDK — `https://github.com/googleapis/python-genai` | Current project-based client setup, version pinning, function-call handling |
| S10 | Google, Thought signatures — `https://ai.google.dev/gemini-api/docs/thought-signatures` | Preserve required provider state across function-calling turns |
| S11 | AWS, Restrict access to an S3 origin — `https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-restricting-access-to-s3.html` | Private S3 origins behind CloudFront |
| S12 | AWS, Systems Manager Session Manager — `https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager.html` | Administration without public SSH |
| S13 | Google Cloud, AWS/Azure Workload Identity Federation — `https://docs.cloud.google.com/iam/docs/workload-identity-federation-with-other-clouds` | AWS role federation, short-lived credentials and EC2 IMDSv2 setup |
| S14 | Google Cloud, Budgets and budget alerts — `https://docs.cloud.google.com/billing/docs/how-to/budgets` | Distinction between alerts-only budgets and supported spend-cap features |
| S15 | Microsoft, SetWindowDisplayAffinity — `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity` | Capture exclusion for owned windows and its limitations |
| S16 | Chrome, Remote debugging switch changes — `https://developer.chrome.com/blog/remote-debugging-port` | Default-profile debugging restrictions and dedicated profiles |
| S17 | Playwright, Auto-waiting — `https://playwright.dev/docs/actionability` | Structured browser actionability and waiting behavior |
| S18 | Microsoft, VS Code Extension API — `https://code.visualstudio.com/api/references/vscode-api` | Document edits, visible editors, diagnostics and terminal shell integration |
| S19 | Microsoft, Creating a Pseudoconsole session — `https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session` | Agent-owned visible console hosting |
| S20 | Microsoft, TerminateJobObject — `https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-terminatejobobject` | Cancellation of an owned process group |
| S21 | OWASP, LLM Prompt Injection Prevention Cheat Sheet — `https://cheatsheetseries.owasp.org/cheatsheets/LLM_Prompt_Injection_Prevention_Cheat_Sheet.html` | Trust separation, least privilege, action validation and limits of model guardrails |
| S22 | Chrome, downloads API — `https://developer.chrome.com/docs/extensions/reference/api/downloads` | Download observation/control; do not assume event notification is pre-transfer prevention |
| S23 | Microsoft, CryptProtectData — `https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata` | Windows-protected storage and user/machine binding |
| S24 | AWS Cognito, Authorization endpoint — `https://docs.aws.amazon.com/cognito/latest/developerguide/authorization-endpoint.html` | Authorization code flow and PKCE |
| S25 | AWS Cognito, Adding MFA — `https://docs.aws.amazon.com/cognito/latest/developerguide/user-pool-settings-mfa.html` | Owner/admin authentication configuration |
| S26 | Microsoft, Sign an MSIX package — `https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview` | Package signing and certificate trust |
| S27 | Google Cloud, Speech-to-Text supported languages — `https://docs.cloud.google.com/speech-to-text/docs/speech-to-text-supported-languages` | Model/location-specific recognition language availability |
| S28 | Google Cloud, Generative AI pricing — `https://cloud.google.com/gemini-enterprise-agent-platform/generative-ai/pricing` | Dated model rates, region/service tier and future scheduled changes |
| S29 | Microsoft, Teams meeting recording policies — `https://learn.microsoft.com/en-us/microsoftteams/meeting-recording` | Recording availability and policy restrictions |
| S30 | Microsoft, Loopback Recording — `https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording` | System-audio capture mechanism and scope |
| S31 | Google AI for Developers, Computer use — `https://ai.google.dev/gemini-api/docs/computer-use` | Additional desktop action definitions and provider-specific features; not a substitute for checking GCP support |

## Interpretation notes

- Google Cloud documentation now uses Gemini Enterprise Agent Platform terminology in places formerly documented as Vertex AI. This plan still uses GCP-managed, project-billed inference, not a personal Gemini subscription.
- A model being generally available does not make every associated tool generally available. In particular, the computer-use feature has its own preview status and terms.
- `global` is an endpoint selection, not an India-only data residency claim.
- A listed API method does not imply it works for every third-party application, browser profile, remote workspace or terminal configuration.
- Measurements in the plan are acceptance targets; no latency or completion-rate benchmark was run in the user's environment.
