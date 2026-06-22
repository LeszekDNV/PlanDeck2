using System.Text.RegularExpressions;

namespace PlanDeck.Core.Shared.Validation;

/// <summary>
/// Shared, lightweight email-format validation used by both the server (authoritative,
/// before persisting a member) and the client (fast pre-submit feedback). Keeping the
/// rule in one place avoids the two layers drifting apart.
/// </summary>
public static partial class EmailValidator
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        return trimmed.Length <= 254 && EmailRegex().IsMatch(trimmed);
    }
}
