# Reviewed WhatsApp Desktop paste

`Invoke-ReviewedWhatsAppPaste.ps1` pastes an owner-reviewed UTF-8 message into the WhatsApp Desktop chat composer. It does not press Enter or Send. It is a manual handoff for a message that the owner already has in a local text file, including a message drafted by AI.

```powershell
pwsh -File scripts/whatsapp/Invoke-ReviewedWhatsAppPaste.ps1 `
  -Recipient 'Amal Tgh' `
  -MessageFile 'C:\path\to\reviewed-message.txt'
```

1. Read the displayed recipient and full message. Type `PASTE` to confirm.
2. Open the intended chat in **WhatsApp Desktop** and click its message field.
3. Press `Ctrl+Alt+Shift+F12` within 30 seconds. The helper checks that WhatsApp Desktop is foreground, then sends only `Ctrl+V` to that same window.
4. Check the pasted text and chat recipient in WhatsApp, then press Send there.

The script changes the Windows clipboard. It does not inspect or verify the WhatsApp chat name or the focused field because WhatsApp Desktop does not expose those through the available UI Automation tree. It cannot establish whether the paste appeared; the owner must check the draft. The current Autobots API pauses on Google's `require_confirmation` response and does not export the proposed text to this helper. The helper must not be launched automatically in response to that safety decision. For a Google-flagged proposal, the owner must confirm the action before it runs. A separate first-party messaging connector would need its own account setup and recipient, delivery, and duplicate-send controls.

The script uses Windows PowerShell's built-in clipboard command and Win32 input APIs; it installs no packages and performs no cloud calls. It requires a signed-in, interactive standard-user session.
