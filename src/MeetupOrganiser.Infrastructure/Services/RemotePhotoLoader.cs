using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MeetupOrganiser.Infrastructure.Services;

/// <summary>Downloads remote speaker photos (e.g. from Sessionize) for the image and deck generators.</summary>
public static class RemotePhotoLoader
{
    private const int MaxBytes = 5 * 1024 * 1024;

    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public static bool IsRemote(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static async Task<byte[]?> LoadAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (Cache.TryGetValue(uri.AbsoluteUri, out var cached))
            return cached;

        try
        {
            if (!await IsPublicHostAsync(uri.Host, cancellationToken))
                return null;

            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) return null;
            }

            var bytes = buffer.ToArray();
            Cache[uri.AbsoluteUri] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    // Photo URLs come from imported data, so refuse hosts that resolve to private or loopback addresses.
    private static async Task<bool> IsPublicHostAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.Length > 0 && addresses.All(a => !IsPrivate(a));
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;

        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || b[0] == 0;
    }
}
