using Autobots.Contracts;

namespace Autobots.Desktop;

/// <summary>
/// Detects a Calendar date/time picker loop. The guard only rejects a model suggestion;
/// it never edits the page or grants input by itself.
/// </summary>
public sealed class CalendarFieldLoopGuard
{
    private const int MaximumPickerClicksWithoutTyping = 5;
    private int _pickerClicks;

    public void Reset() => _pickerClicks = 0;

    public const string RecoveryHint =
        "Calendar date/time picker clicks are looping. No click sent. Close the popup with Escape, " +
        "focus one field, press Ctrl+A, type its full date or time, then Tab to commit. " +
        "Set start date, start time, end date, then end time; check all four visible values before Save.";

    public bool CanExecute(ProposedAction action, string? foregroundTitle, string? intent)
    {
        if (!IsCalendarEditor(foregroundTitle))
        {
            _pickerClicks = 0;
            return true;
        }

        return action is not ClickAction || !IsDateOrTimeIntent(intent) || _pickerClicks < MaximumPickerClicksWithoutTyping;
    }

    public void RecordExecuted(ProposedAction action, string? foregroundTitle, string? intent)
    {
        if (!IsCalendarEditor(foregroundTitle))
        {
            _pickerClicks = 0;
            return;
        }

        if (action is TypeTextAction)
            _pickerClicks = 0;
        else if (action is ClickAction && IsDateOrTimeIntent(intent))
            _pickerClicks++;
    }

    private static bool IsCalendarEditor(string? title) =>
        title?.Contains("Google Calendar", StringComparison.OrdinalIgnoreCase) == true &&
        (title.Contains("Event details", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Create event", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Edit event", StringComparison.OrdinalIgnoreCase));

    private static bool IsDateOrTimeIntent(string? intent) =>
        intent?.Contains("date", StringComparison.OrdinalIgnoreCase) == true ||
        intent?.Contains("time", StringComparison.OrdinalIgnoreCase) == true ||
        intent?.Contains("calendar popup", StringComparison.OrdinalIgnoreCase) == true;
}
