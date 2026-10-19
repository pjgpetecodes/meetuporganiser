using MeetupOrganiser.Core.DTOs;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.AspNetCore.Components;

namespace MeetupOrganiser.Web.Components;

public abstract class BankHolidayAwareComponent : ComponentBase
{
    [Inject] public IBankHolidayService BankHolidayService { get; set; } = default!;
    protected BankHolidayCalendar? HolidayCalendar { get; private set; }

    protected override async Task OnInitializedAsync() =>
        HolidayCalendar = await BankHolidayService.GetCalendarAsync();
}
