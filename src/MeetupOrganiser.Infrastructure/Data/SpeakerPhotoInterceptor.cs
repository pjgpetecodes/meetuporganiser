using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Data;

/// <summary>
/// Copies remote speaker photos (e.g. Sessionize CDN) into blob storage when a speaker is saved,
/// so the app never depends on third-party image hosts. Failures keep the original URL.
/// </summary>
public sealed class SpeakerPhotoInterceptor(IServiceScopeFactory scopeFactory, ILogger<SpeakerPhotoInterceptor> logger)
    : SaveChangesInterceptor
{
    private const string Container = "speaker-photos";

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && context is not AppDbContext { SkipSpeakerPhotoLocalization: true })
        {
            await LocalizeAsync(context, cancellationToken);
        }

        return result;
    }

    public async Task LocalizeAsync(DbContext context, CancellationToken cancellationToken)
    {
        var speakers = context.ChangeTracker.Entries<Speaker>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .Where(s => IsRemote(s.PhotoBlobUrl))
            .ToList();
        if (speakers.Count == 0) return;

        using var scope = scopeFactory.CreateScope();
        var blobs = scope.ServiceProvider.GetService<IBlobStorageService>();
        if (blobs is null) return;
        var ownAuthority = new Uri(blobs.GetBlobUrl(Container, "x")).GetLeftPart(UriPartial.Authority);

        foreach (var speaker in speakers)
        {
            var url = speaker.PhotoBlobUrl!;
            if (url.StartsWith(ownAuthority, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var bytes = await RemotePhotoLoader.LoadAsync(url, cancellationToken);
                var type = bytes is null ? null : SniffImageType(bytes);
                if (bytes is null || type is null) continue;

                using var stream = new MemoryStream(bytes);
                speaker.PhotoBlobUrl = await blobs.UploadAsync(Container, $"{Slug(speaker.Name)}{type.Value.Extension}", stream, type.Value.ContentType, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not copy photo for speaker {Speaker} to blob storage; keeping remote URL.", speaker.Name);
            }
        }
    }

    private static bool IsRemote(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    internal static (string ContentType, string Extension)? SniffImageType(byte[] b) => b switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => ("image/png", ".png"),
        [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", ".jpg"),
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ("image/webp", ".webp"),
        [0x47, 0x49, 0x46, 0x38, ..] => ("image/gif", ".gif"),
        _ => null
    };

    private static string Slug(string? name)
    {
        var chars = (name ?? "speaker").Where(char.IsLetterOrDigit).Take(40).ToArray();
        return chars.Length == 0 ? "speaker" : new string(chars);
    }
}
