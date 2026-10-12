using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetupOrganiser.Tests;

public class InfrastructureDependencyInjectionTests
{
    [Fact]
    public void AddInfrastructureServices_ResolvesHttpServicesWithSettingsAwareConstructors()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISessionizeService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IZoomService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMeetupService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISocialMediaService>());
    }
}
