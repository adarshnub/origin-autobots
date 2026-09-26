namespace Autobots.Platform;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8
}

/// <summary>
/// A platform-neutral key press such as <c>Control+L</c>, <c>Meta</c> or <c>ArrowDown</c>.
/// <see cref="Key"/> is either a canonical named key (for example <c>Enter</c>, <c>F5</c>, <c>Meta</c>)
/// or a single printable character. Platform adapters translate it to native key codes.
/// </summary>
public sealed record KeyChord(KeyModifiers Modifiers, string Key)
{
    private static readonly IReadOnlyDictionary<string, KeyModifiers> ModifierNames = new Dictionary<string, KeyModifiers>(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = KeyModifiers.Control, ["control"] = KeyModifiers.Control, ["ctl"] = KeyModifiers.Control,
        ["lctrl"] = KeyModifiers.Control, ["rctrl"] = KeyModifiers.Control, ["controlleft"] = KeyModifiers.Control,
        ["controlright"] = KeyModifiers.Control, ["leftcontrol"] = KeyModifiers.Control, ["rightcontrol"] = KeyModifiers.Control,
        // Autobots drives Windows first: a macOS Command shortcut means the equivalent Control shortcut.
        ["cmd"] = KeyModifiers.Control, ["command"] = KeyModifiers.Control,
        ["shift"] = KeyModifiers.Shift, ["lshift"] = KeyModifiers.Shift, ["rshift"] = KeyModifiers.Shift,
        ["shiftleft"] = KeyModifiers.Shift, ["shiftright"] = KeyModifiers.Shift,
        ["alt"] = KeyModifiers.Alt, ["option"] = KeyModifiers.Alt, ["opt"] = KeyModifiers.Alt, ["lalt"] = KeyModifiers.Alt,
        ["ralt"] = KeyModifiers.Alt, ["altleft"] = KeyModifiers.Alt, ["altright"] = KeyModifiers.Alt,
        ["meta"] = KeyModifiers.Meta, ["win"] = KeyModifiers.Meta, ["windows"] = KeyModifiers.Meta, ["super"] = KeyModifiers.Meta,
        ["os"] = KeyModifiers.Meta, ["lwin"] = KeyModifiers.Meta, ["rwin"] = KeyModifiers.Meta, ["metaleft"] = KeyModifiers.Meta,
        ["metaright"] = KeyModifiers.Meta, ["start"] = KeyModifiers.Meta, ["windowskey"] = KeyModifiers.Meta
    };

    private static readonly IReadOnlyDictionary<string, string> NamedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = "Enter", ["return"] = "Enter", ["numpadenter"] = "Enter", ["kpenter"] = "Enter",
        ["tab"] = "Tab", ["esc"] = "Escape", ["escape"] = "Escape",
        ["space"] = "Space", ["spacebar"] = "Space",
        ["backspace"] = "Backspace", ["back"] = "Backspace", ["bksp"] = "Backspace",
        ["delete"] = "Delete", ["del"] = "Delete", ["forwarddelete"] = "Delete",
        ["insert"] = "Insert", ["ins"] = "Insert",
        ["home"] = "Home", ["end"] = "End",
        ["pageup"] = "PageUp", ["pgup"] = "PageUp", ["prior"] = "PageUp",
        ["pagedown"] = "PageDown", ["pgdn"] = "PageDown", ["pgdown"] = "PageDown", ["next"] = "PageDown",
        ["arrowup"] = "ArrowUp", ["up"] = "ArrowUp", ["uparrow"] = "ArrowUp",
        ["arrowdown"] = "ArrowDown", ["down"] = "ArrowDown", ["downarrow"] = "ArrowDown",
        ["arrowleft"] = "ArrowLeft", ["left"] = "ArrowLeft", ["leftarrow"] = "ArrowLeft",
        ["arrowright"] = "ArrowRight", ["right"] = "ArrowRight", ["rightarrow"] = "ArrowRight",
        ["capslock"] = "CapsLock", ["caps"] = "CapsLock",
        ["printscreen"] = "PrintScreen", ["prtsc"] = "PrintScreen", ["printscr"] = "PrintScreen", ["snapshot"] = "PrintScreen",
        ["contextmenu"] = "ContextMenu", ["apps"] = "ContextMenu", ["application"] = "ContextMenu",
        ["volumeup"] = "VolumeUp", ["audiovolumeup"] = "VolumeUp",
        ["volumedown"] = "VolumeDown", ["audiovolumedown"] = "VolumeDown",
        ["volumemute"] = "VolumeMute", ["audiovolumemute"] = "VolumeMute", ["mute"] = "VolumeMute",
        ["mediaplaypause"] = "MediaPlayPause", ["playpause"] = "MediaPlayPause",
        ["medianexttrack"] = "MediaNext", ["medianext"] = "MediaNext",
        ["mediaprevioustrack"] = "MediaPrevious", ["mediaprevious"] = "MediaPrevious", ["mediatrackprevious"] = "MediaPrevious",
        ["mediastop"] = "MediaStop",
        ["minus"] = "-", ["hyphen"] = "-", ["dash"] = "-", ["equal"] = "=", ["equals"] = "=", ["plus"] = "+",
        ["comma"] = ",", ["period"] = ".", ["dot"] = ".", ["slash"] = "/", ["backslash"] = "\\",
        ["semicolon"] = ";", ["quote"] = "'", ["apostrophe"] = "'", ["backquote"] = "`", ["grave"] = "`",
        ["backtick"] = "`", ["bracketleft"] = "[", ["bracketright"] = "]"
    };

    public static IReadOnlyCollection<string> ModifierOnlyKeys { get; } = ["Control", "Shift", "Alt", "Meta"];

    public bool IsModifierOnly => ModifierOnlyKeys.Contains(Key);

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(Key switch
        {
            "Meta" => "Win",
            "Control" => "Ctrl",
            { Length: 1 } character => character.ToUpperInvariant(),
            var named => named
        });
        return string.Join('+', parts);
    }

    /// <summary>Parses a model key description. Unknown keys fail closed.</summary>
    public static bool TryParse(string? text, out KeyChord chord, out string error)
    {
        chord = new KeyChord(KeyModifiers.None, string.Empty);
        error = string.Empty;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 64)
            return Fail("The key description is empty or too long.", out error);

        var pieces = SplitChord(value);
        if (pieces.Count is 0 or > 4)
            return Fail("A key combination may contain at most three modifiers and one key.", out error);

        var modifiers = KeyModifiers.None;
        string? key = null;
        KeyModifiers? lastModifier = null;
        foreach (var piece in pieces)
        {
            if (ModifierNames.TryGetValue(Compact(piece), out var modifier))
            {
                if (modifiers.HasFlag(modifier))
                    return Fail("A modifier key is repeated.", out error);
                modifiers |= modifier;
                lastModifier = modifier;
                continue;
            }
            if (key is not null)
                return Fail("A key combination may contain only one non-modifier key.", out error);
            key = NormalizeKey(piece);
            if (key is null)
                return Fail($"The key '{Sanitize(piece)}' is not supported.", out error);
        }

        if (key is null)
        {
            // A lone modifier, such as the Windows key that opens Start.
            if (pieces.Count != 1 || lastModifier is null)
                return Fail("A key combination needs a key besides its modifiers.", out error);
            chord = new KeyChord(KeyModifiers.None, lastModifier.Value switch
            {
                KeyModifiers.Control => "Control",
                KeyModifiers.Shift => "Shift",
                KeyModifiers.Alt => "Alt",
                _ => "Meta"
            });
            return true;
        }

        chord = new KeyChord(modifiers, key);
        return true;
    }

    /// <summary>
    /// Returns why this chord is never sent by Autobots, or null when it is allowed. These shortcuts lock
    /// the session, open security surfaces, reset the display driver or trigger Autobots' own controls.
    /// </summary>
    public string? BlockedReason()
    {
        var control = Modifiers.HasFlag(KeyModifiers.Control);
        var alt = Modifiers.HasFlag(KeyModifiers.Alt);
        var shift = Modifiers.HasFlag(KeyModifiers.Shift);
        var meta = Modifiers.HasFlag(KeyModifiers.Meta);
        if (meta && Key == "l")
            return "Locking the PC is not allowed during a task.";
        if (control && alt && Key == "Delete")
            return "The Windows security screen shortcut is not allowed.";
        if (control && shift && !alt && Key == "Escape")
            return "Opening Task Manager is not allowed during a task.";
        if (meta && control && shift && Key == "b")
            return "Resetting the graphics driver is not allowed.";
        if (control && alt && shift && Key == "s")
            return "Autobots' STOP shortcut is reserved for the owner.";
        if (control && alt && !shift && Key == "Space")
            return "Autobots' talk shortcut is reserved for the owner.";
        return null;
    }

    private static List<string> SplitChord(string value)
    {
        var pieces = new List<string>();
        var remaining = value;
        // "Ctrl++" or a lone "+" names the plus key itself.
        var trailingPlus = remaining == "+" || remaining.EndsWith("++", StringComparison.Ordinal);
        if (trailingPlus)
            remaining = remaining[..^1].TrimEnd('+');
        foreach (var piece in remaining.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            pieces.Add(piece);
        if (trailingPlus)
            pieces.Add("+");
        return pieces;
    }

    private static string? NormalizeKey(string piece)
    {
        if (piece.Length == 1)
        {
            var character = piece[0];
            if (char.IsAsciiLetter(character))
                return char.ToLowerInvariant(character).ToString();
            return character is >= '!' and <= '~' ? character.ToString() : null;
        }

        var compact = Compact(piece);
        if (NamedKeys.TryGetValue(compact, out var named))
            return named;
        if (compact.Length is 2 or 3 && compact[0] is 'f' or 'F' && int.TryParse(compact.AsSpan(1), out var function) && function is >= 1 and <= 24)
            return $"F{function}";
        // Playwright-style codes such as KeyA and Digit1.
        if (compact.Length == 4 && compact.StartsWith("key", StringComparison.OrdinalIgnoreCase) && char.IsAsciiLetter(compact[3]))
            return char.ToLowerInvariant(compact[3]).ToString();
        if (compact.Length == 6 && compact.StartsWith("digit", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(compact[5]))
            return compact[5].ToString();
        return null;
    }

    private static string Compact(string piece) =>
        string.Concat(piece.Where(character => character is not (' ' or '_' or '-')));

    private static string Sanitize(string value) =>
        new(value.Where(character => !char.IsControl(character)).Take(24).ToArray());

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
