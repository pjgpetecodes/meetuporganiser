using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MeetupOrganiser.Web;

public static class LocalOrganizerCookie
{
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier))) return;

        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(LocalOrganizerCookie))
            .LogInformation("Renewing a legacy local sign-in that has no stable organiser identity.");
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
