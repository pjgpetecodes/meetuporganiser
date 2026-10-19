using System.Collections.Frozen;

namespace MeetupOrganiser.Core.DTOs;

public sealed record BankHoliday(DateOnly Date, string Title, string? Notes = null)
{
    public string Description => string.IsNullOrWhiteSpace(Notes) ? Title : $"{Title} ({Notes})";
}

public sealed class BankHolidayCalendar(IEnumerable<BankHoliday> holidays)
{
    private readonly FrozenDictionary<DateOnly, BankHoliday> byDate = holidays.ToFrozenDictionary(h => h.Date);

    public static BankHolidayCalendar Unavailable { get; } = new([]);
    public bool IsAvailable => byDate.Count > 0;
    public bool CoversYear(int year) => byDate.Keys.Any(date => date.Year == year);
    public BankHoliday? Find(DateOnly date) => byDate.GetValueOrDefault(date);
}
