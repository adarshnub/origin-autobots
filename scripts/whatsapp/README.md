# Reviewed WhatsApp Desktop paste

`Invoke-ReviewedWhatsAppPaste.ps1` prepares an owner-reviewed UTF-8 message on the Windows clipboard. The owner pastes it into WhatsApp Desktop and decides whether to send it. This manual handoff accepts a message the owner already has in a local text file, including a message drafted by AI.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/whatsapp/Invoke-ReviewedWhatsAppPaste.ps1 `
  -Recipient 'Amal Tgh' `
  -MessageFile 'C:\path\to\reviewed-message.txt'
```

1. Read the displayed recipient and full message. Type `COPY` to put the exact message on the clipboard.
2. Open the intended chat in **WhatsApp Desktop**, click its message field and press `Ctrl+V` yourself.
3. Check the pasted text and chat recipient in WhatsApp, then press Send there.

The script changes the Windows clipboard. It does not inspect or control WhatsApp. The current Autobots API pauses on Google's `require_confirmation` response and does not export the proposed text to this helper. The helper must not be launched automatically in response to that safety decision. For a Google-flagged proposal, the owner must confirm the action before it runs. A separate first-party messaging connector would need its own account setup and recipient, delivery, and duplicate-send controls.

The script uses Windows PowerShell's built-in clipboard command; it installs no packages, performs no cloud calls and injects no desktop input. It requires a signed-in, interactive Windows session.
