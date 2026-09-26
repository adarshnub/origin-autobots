using Autobots.Contracts;
using Autobots.Platform;

namespace Autobots.Desktop;

/// <summary>Describes proposed actions for the owner (HUD and timeline) and for the model's step history.</summary>
public static class ActionNarration
{
    public static string Headline(ProposedAction action) => action switch
    {
        ClickAction { Button: PointerButton.Right } => "Right-clicking",
        ClickAction { Clicks: 2 } => "Double-clicking",
        ClickAction { Clicks: 3 } => "Triple-clicking",
        ClickAction => "Clicking",
        TypeTextAction { PressEnter: true } => "Typing and submitting",
        TypeTextAction => "Typing",
        KeyPressAction key => KeyChord.TryParse(key.Key, out var chord, out _) ? $"Pressing {chord}" : "Pressing a key",
        ScrollAction { DeltaY: > 0 } => "Scrolling down",
        ScrollAction { DeltaY: < 0 } => "Scrolling up",
        ScrollAction { DeltaX: > 0 } => "Scrolling right",
        ScrollAction => "Scrolling left",
        MoveAction => "Pointing",
        DragAction => "Dragging",
        WaitAction { DurationMs: 0 } => "Looking again",
        WaitAction => "Waiting for the app",
        _ => "Working"
    };

    public static string PastTense(ProposedAction action) => action switch
    {
        ClickAction { Button: PointerButton.Right } => "Right-clicked",
        ClickAction { Clicks: 2 } => "Double-clicked",
        ClickAction { Clicks: 3 } => "Triple-clicked",
        ClickAction => "Clicked",
        TypeTextAction text => $"Typed “{Clip(text.Text, 48)}”{(text.PressEnter ? " and pressed Enter" : string.Empty)}",
        KeyPressAction key => KeyChord.TryParse(key.Key, out var chord, out _) ? $"Pressed {chord}" : "Pressed a key",
        ScrollAction scroll => scroll.DeltaY != 0 ? $"Scrolled {(scroll.DeltaY > 0 ? "down" : "up")}" : $"Scrolled {(scroll.DeltaX > 0 ? "right" : "left")}",
        MoveAction => "Pointed at an item",
        DragAction => "Dragged an item",
        WaitAction { DurationMs: 0 } => "Looked at the screen again",
        WaitAction wait => $"Waited {wait.DurationMs / 1000d:0.#} s",
        _ => "Performed an action"
    };

    public static string Glyph(ProposedAction action) => action switch
    {
        ClickAction or MoveAction or DragAction => Icons.Cursor,
        TypeTextAction or KeyPressAction => Icons.Keyboard,
        ScrollAction => Icons.Scroll,
        WaitAction => Icons.Clock,
        _ => Icons.Sparkle
    };

    public static PointerActivity Activity(ProposedAction action) => action switch
    {
        ClickAction => PointerActivity.Clicking,
        TypeTextAction or KeyPressAction => PointerActivity.Typing,
        ScrollAction => PointerActivity.Scrolling,
        DragAction => PointerActivity.Dragging,
        MoveAction => PointerActivity.Moving,
        WaitAction => PointerActivity.Waiting,
        _ => PointerActivity.Moving
    };

    /// <summary>A compact, model-facing record of what the device did (normalized coordinates).</summary>
    public static string ForHistory(ProposedAction action, string? intent)
    {
        var summary = action switch
        {
            ClickAction click => $"{ClickName(click)} at ({click.X}, {click.Y})",
            TypeTextAction text => $"type \"{Clip(text.Text, 120)}\"{(text.PressEnter ? " then Enter" : string.Empty)}",
            KeyPressAction key => $"press {(KeyChord.TryParse(key.Key, out var chord, out _) ? chord.ToString() : Clip(key.Key, 32))}",
            ScrollAction scroll => $"scroll ({scroll.DeltaX}, {scroll.DeltaY}) at ({scroll.X}, {scroll.Y})",
            MoveAction move => $"move pointer to ({move.X}, {move.Y})",
            DragAction drag => $"drag from ({drag.X}, {drag.Y}) to ({drag.ToX}, {drag.ToY})",
            WaitAction wait => $"wait {wait.DurationMs} ms",
            _ => "unknown action"
        };
        if (!string.IsNullOrWhiteSpace(intent))
            summary += $" (intent: {Clip(intent, 120)})";
        return Clip(summary, 290);
    }

    public static string Clip(string value, int length)
    {
        var singleLine = string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= length ? singleLine : singleLine[..(length - 1)] + "…";
    }

    private static string ClickName(ClickAction click) => (click.Button, click.Clicks) switch
    {
        (PointerButton.Right, _) => "right-click",
        (PointerButton.Middle, _) => "middle-click",
        (_, 2) => "double-click",
        (_, 3) => "triple-click",
        _ => "click"
    };
}
