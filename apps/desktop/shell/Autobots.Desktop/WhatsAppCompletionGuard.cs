namespace Autobots.Desktop;

/// <summary>Current desktop pilot has no independent recipient or message readback for WhatsApp.</summary>
public static class WhatsAppCompletionGuard
{
    public static bool RequiresReview(string ownerInstruction)
    {
        foreach (var clause in ownerInstruction.Split(['.', ';', '\n', '\r']))
        {
            var trimmed = clause.TrimStart();
            if (!trimmed.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("Do not ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Don't ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Never ", StringComparison.OrdinalIgnoreCase))
                continue;
            return true;
        }
        return false;
    }

    public const string ReviewMessage =
        "The AI reported that it finished in WhatsApp, but Autobots cannot independently verify the chat recipient, draft text, or delivery. Review the visible chat before treating this task as complete.";
}
