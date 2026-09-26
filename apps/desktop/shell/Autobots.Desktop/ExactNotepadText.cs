namespace Autobots.Desktop;

/// <summary>Extracts a single exact line that the owner explicitly asked to type in Notepad.</summary>
public static class ExactNotepadText
{
    public static string? FromOwnerInstruction(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return null;
        var lines = instruction.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("Open Notepad and type this exact ", StringComparison.OrdinalIgnoreCase))
                continue;
            var separator = line.IndexOf(':');
            if (separator < 0) return null;
            var value = line[(separator + 1)..].Trim();
            if (value.Length == 0 && index + 1 < lines.Length)
                value = lines[index + 1].Trim();
            return value.Length is > 0 and <= 4000 ? value : null;
        }
        return null;
    }

    public static bool IsNotepad(string? processName) =>
        processName is not null &&
        (processName.Equals("Notepad", StringComparison.OrdinalIgnoreCase) ||
         processName.Equals("Notepad.exe", StringComparison.OrdinalIgnoreCase));
}
