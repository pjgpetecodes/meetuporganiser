using System.Reflection;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Web.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class CfpSettingsTests
{
    [Fact]
    public async Task EnableDisableAndRotate_SaveSeparatelyAndInvalidatePreviousPublicKeys()
    {
        var factory = new TestFactory();
        var group = new MeetupGroup { Name = "Test group" };
        await using (var db = factory.CreateDbContext())
        {
            db.Add(group);
            await db.SaveChangesAsync();
        }
        var settings = new CfpSettings();
        SetProperty(settings, "GroupId", group.Id);
        SetProperty(settings, "DbContextFactory", factory);
        SetProperty(settings, "Logger", NullLogger<CfpSettings>.Instance);
        await InvokeAsync(settings, "OnParametersSetAsync");
        SetField(settings, "enabled", true);

        await InvokeAsync(settings, "PersistAsync", false);
        var first = await ReadGroupAsync(factory);
        Assert.True(first.CfpEnabled);
        Assert.False(string.IsNullOrWhiteSpace(first.CfpKey));

        await InvokeAsync(settings, "PersistAsync", false);
        Assert.Equal(first.CfpKey, (await ReadGroupAsync(factory)).CfpKey);

        await InvokeAsync(settings, "PersistAsync", true);
        var rotated = await ReadGroupAsync(factory);
        Assert.True(rotated.CfpEnabled);
        Assert.NotEqual(first.CfpKey, rotated.CfpKey);

        SetField(settings, "enabled", false);
        await InvokeAsync(settings, "PersistAsync", false);
        var disabled = await ReadGroupAsync(factory);
        Assert.False(disabled.CfpEnabled);
        Assert.NotEqual(rotated.CfpKey, disabled.CfpKey);
    }

    private static async Task<MeetupGroup> ReadGroupAsync(TestFactory factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.MeetupGroups.AsNoTracking().SingleAsync();
    }

    private static async Task InvokeAsync(object component, string method, params object[] arguments) =>
        await (Task)component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments)!;

    private static void SetProperty(object component, string name, object value) =>
        component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);

    private static void SetField(object component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class TestFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        public AppDbContext CreateDbContext() => new(options);
    }
}
