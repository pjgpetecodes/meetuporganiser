using System.Globalization;

namespace MeetupOrganiser.Core.Services;

public static class ShortUrl
{
    public const string DefaultHost = "https://bit.ly/";

    // "https://bit.ly/dotnetnotts" + Oct 2026 => "https://bit.ly/dotnetnottsoctober2026"
    public static string? ForEvent(string? stem, DateTime eventUtc)
    {
        if (string.IsNullOrWhiteSpace(stem)) return null;

        var trimmed = stem.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = DefaultHost + trimmed.TrimStart('/');
        }

        var uk = EventHeader.ToUkTime(eventUtc);
        var month = uk.ToString("MMMM", CultureInfo.InvariantCulture).ToLowerInvariant();
        return $"{trimmed}{month}{uk.Year}";
    }
}
