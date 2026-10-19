using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;

namespace MeetupOrganiser.Tests;

internal sealed class BankHolidayStub(BankHolidayCalendar? calendar = null) : IBankHolidayService
{
    public int Calls { get; private set; }

    public Task<BankHolidayCalendar> GetCalendarAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(calendar ?? BankHolidayCalendar.Unavailable);
    }
}
