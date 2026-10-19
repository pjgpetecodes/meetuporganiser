using MeetupOrganiser.Core.Services;

namespace MeetupOrganiser.Web;

public static class MeetupDateTime
{
    private static TimeZoneInfo UkZone => TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static DateTime ToUtc(DateTime local)
    {
        if (!TryToUtc(local, out var utc))
            throw new ArgumentException("This UK time falls within a clock change. Choose an unambiguous start time.");
        return utc;
    }

    public static bool TryToUtc(DateTime local, out DateTime utc)
    {
        utc = default;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (UkZone.IsInvalidTime(local) || UkZone.IsAmbiguousTime(local))
            return false;

        utc = TimeZoneInfo.ConvertTimeToUtc(local, UkZone);
        return true;
    }

    public static string Display(DateTime utc)
    {
        var local = EventHeader.ToUkTime(utc);
        var zone = UkZone.IsDaylightSavingTime(local) ? "BST" : "GMT";
        return $"{local:dddd d MMMM yyyy, HH:mm} {zone}";
    }
}
