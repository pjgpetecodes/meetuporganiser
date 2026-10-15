using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataProtection();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=MeetupOrganiserDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
        });

        services.AddHttpClient<ISessionizeService, SessionizeService>();
        services.AddHttpClient<ISessionizeProfileService, SessionizeProfileService>();
        services.AddHttpClient("ZoomService");
        services.AddScoped<IZoomService>(serviceProvider => new ZoomService(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ZoomService"),
            serviceProvider.GetRequiredService<ISettingsService>(),
            configuration,
            serviceProvider.GetRequiredService<ILogger<ZoomService>>()));
        services.AddHttpClient<MeetupOAuthService>();
        services.AddScoped<IMeetupOAuthService>(serviceProvider =>
            serviceProvider.GetRequiredService<MeetupOAuthService>());
        services.AddHttpClient("MeetupService");
        services.AddScoped<IMeetupService>(serviceProvider => new MeetupService(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("MeetupService"),
            serviceProvider.GetRequiredService<ISettingsService>(),
            configuration,
            serviceProvider.GetRequiredService<ILogger<MeetupService>>(),
            serviceProvider.GetRequiredService<IMeetupOAuthService>()));
        services.AddHttpClient("SocialMediaService");
        services.AddScoped<ISocialMediaService>(serviceProvider => new SocialMediaService(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("SocialMediaService"),
            serviceProvider.GetRequiredService<ISettingsService>(),
            configuration,
            serviceProvider.GetRequiredService<ILogger<SocialMediaService>>()));

        services.AddScoped<IBlobStorageService, BlobStorageService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IPptxGeneratorService, PptxGeneratorService>();
        services.AddMemoryCache();
        services.AddScoped<IPptxPreviewService, PptxPreviewService>();
        services.AddScoped<ISocialImageGeneratorService, SocialImageGeneratorService>();
        services.AddScoped<IMagicLinkService, MagicLinkService>();
        services.AddScoped<AcsEmailService>();
        services.AddScoped<AcsSmtpEmailService>();
        services.AddScoped<IEmailService, EmailService>();

        return services;
    }
}
