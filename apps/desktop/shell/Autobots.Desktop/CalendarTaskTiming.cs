using System.Globalization;
using System.Text.RegularExpressions;

namespace Autobots.Desktop;

/// <summary>Extracts only explicit, unambiguous Calendar start/end values from the owner's task grant.</summary>
public sealed record CalendarTaskTiming(DateOnly StartDate, TimeOnly StartTime, DateOnly EndDate, TimeOnly EndTime)
{
    private static readonly Regex NamedFields = new(
        @"\bstart date\s+(?<sd>[A-Za-z]+\s+\d{1,2},\s*\d{4}),\s*start time\s+(?<st>\d{1,2}:\d{2}\s*[AP]M),\s*end date\s+(?<ed>[A-Za-z]+\s+\d{1,2},\s*\d{4}),\s*end time\s+(?<et>\d{1,2}:\d{2}\s*[AP]M)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static CalendarTaskTiming? FromOwnerInstruction(string instruction)
    {
        var match = NamedFields.Match(instruction);
        if (!match.Success ||
            !TryDate(match.Groups["sd"].Value, out var startDate) ||
            !TryTime(match.Groups["st"].Value, out var startTime) ||
            !TryDate(match.Groups["ed"].Value, out var endDate) ||
            !TryTime(match.Groups["et"].Value, out var endTime))
            return null;
        var start = startDate.ToDateTime(startTime);
        var end = endDate.ToDateTime(endTime);
        if (end <= start || end - start > TimeSpan.FromDays(1))
            return null;
        return new CalendarTaskTiming(startDate, startTime, endDate, endTime);
    }

    public string TextFor(string field) => field switch
    {
        "Start date" => StartDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
        "Start time" => StartTime.ToString("h:mm tt", CultureInfo.InvariantCulture),
        "End date" => EndDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
        "End time" => EndTime.ToString("h:mm tt", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    public bool Matches(string field, string actual) => field switch
    {
        "Start date" => TryDate(actual, out var date) && date == StartDate,
        "Start time" => TryTime(actual, out var time) && time == StartTime,
        "End date" => TryDate(actual, out var date) && date == EndDate,
        "End time" => TryTime(actual, out var time) && time == EndTime,
        _ => false
    };

    public static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);

    public static bool TryTime(string text, out TimeOnly time) =>
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time);
}
