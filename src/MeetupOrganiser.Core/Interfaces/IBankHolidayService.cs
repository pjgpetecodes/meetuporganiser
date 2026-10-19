using MeetupOrganiser.Core.DTOs;

namespace MeetupOrganiser.Core.Interfaces;

public interface IBankHolidayService
{
    Task<BankHolidayCalendar> GetCalendarAsync(CancellationToken cancellationToken = default);
}
