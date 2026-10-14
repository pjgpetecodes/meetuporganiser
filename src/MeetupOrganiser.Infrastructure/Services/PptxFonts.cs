using SkiaSharp;

namespace MeetupOrganiser.Infrastructure.Services;

/// <summary>Resolves template fonts: installed fonts first, then any .ttf/.otf shipped in Resources\fonts.</summary>
internal static class PptxFonts
{
    private static readonly Lazy<Dictionary<string, SKTypeface>> Bundled = new(LoadBundled);

    public static (SKTypeface Typeface, bool NeedsEmbolden) Resolve(string? family, bool bold, bool italic)
    {
        var style = new SKFontStyle(bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        if (!string.IsNullOrWhiteSpace(family))
        {
            var installed = SKTypeface.FromFamilyName(family, style);
            if (installed != null && string.Equals(installed.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return (installed, false);
            }

            if (Bundled.Value.TryGetValue(family.Trim(), out var bundled))
            {
                return (bundled, bold && !bundled.IsBold);
            }
        }

        var fallback = SKTypeface.FromFamilyName("Segoe UI", style) ?? SKTypeface.Default;
        return (fallback, false);
    }

    private static Dictionary<string, SKTypeface> LoadBundled()
    {
        var fonts = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);
        var directories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "fonts"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "MeetupOrganiser.Infrastructure", "Resources", "fonts"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "fonts")
        };

        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var typeface = SKTypeface.FromFile(file);
                    if (typeface != null) fonts.TryAdd(typeface.FamilyName, typeface);
                }
                catch
                {
                    // A broken font file should not stop rendering.
                }
            }
        }

        return fonts;
    }
}
