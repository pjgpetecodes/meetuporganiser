using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Data;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetupOrganiser.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=MeetupOrganiserDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        services.AddDbContext<AppDbContext>(options =>
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
        services.AddHttpClient<IZoomService, ZoomService>();
        services.AddHttpClient<IMeetupService, MeetupService>();
        services.AddHttpClient<ISocialMediaService, SocialMediaService>();

        services.AddScoped<IBlobStorageService, BlobStorageService>();
        services.AddScoped<IPptxGeneratorService, PptxGeneratorService>();
        services.AddScoped<ISocialImageGeneratorService, SocialImageGeneratorService>();
        services.AddScoped<IMagicLinkService, MagicLinkService>();

        return services;
    }
}
