using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MeetupOrganiser.Web;

// Local-development store only. Cloud persistence requires a separate private Blob/Key Vault implementation.
public sealed class HostedBrowserLoginStore(IDataProtectionProvider protection, IConfiguration configuration, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private string DirectoryPath => configuration["HostedBrowser:LoginDirectory"] ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetupOrganiser", "BrowserLogin");

    private string PathFor(string owner) => Path.Combine(DirectoryPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))) + ".protected");

    private IDataProtector Protector(string owner) => protection.CreateProtector("MeetupOrganiser.LocalBrowserLogin.v1", owner);

    public async Task SaveAsync(string owner, JsonElement state, CancellationToken cancellationToken = default)
    {
        var json = state.GetRawText();
        if (json.Length > 1024 * 1024) throw new InvalidOperationException("The login state exceeds the local storage limit.");
        Directory.CreateDirectory(DirectoryPath);
        var file = PathFor(owner);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var value = JsonSerializer.Serialize(new SavedLogin(clock.GetUtcNow().AddDays(7), state));
            await File.WriteAllTextAsync(temporary, Protector(owner).Protect(value), cancellationToken);
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<JsonElement?> LoadAsync(string owner, CancellationToken cancellationToken = default)
    {
        var file = PathFor(owner);
        if (!File.Exists(file)) return null;
        var protectedState = await File.ReadAllTextAsync(file, cancellationToken);
        SavedLogin? saved;
        try { saved = JsonSerializer.Deserialize<SavedLogin>(Protector(owner).Unprotect(protectedState)); }
        catch (CryptographicException ex)
        { throw new InvalidOperationException("Saved browser login cannot be decrypted. Forget it and sign in again.", ex); }
        if (saved is null || saved.ExpiresAt <= clock.GetUtcNow())
        {
            File.Delete(file);
            return null;
        }
        return saved.State;
    }

    public void Forget(string owner)
    {
        var file = PathFor(owner);
        if (File.Exists(file)) File.Delete(file);
    }

    private sealed record SavedLogin(DateTimeOffset ExpiresAt, JsonElement State);
}
