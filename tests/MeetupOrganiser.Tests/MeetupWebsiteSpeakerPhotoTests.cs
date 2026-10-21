using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Services;
using SkiaSharp;

namespace MeetupOrganiser.Tests;

public class MeetupWebsiteSpeakerPhotoTests
{
    [Fact]
    public async Task PreparedPhoto_UsesFirstSpeakerAndPreservesAspectRatioAsBoundedPng()
    {
        var fileName = $"handoff-{Guid.NewGuid():N}.jpg";
        var folder = Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "speakers");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        using var bitmap = new SKBitmap(2000, 1000);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        await File.WriteAllBytesAsync(path, jpeg.ToArray());
        try
        {
            var meetup = Event("/images/speakers/" + fileName);
            meetup.Sessions.Insert(0, new() { OrderIndex = 2, Speaker = new() { Name = "Second", PhotoBlobUrl = "/not-allowed" } });
            var payload = MeetupWebsite.Prepare(meetup, "Title", "Description", Png);
            var result = await MeetupWebsiteSpeakerPhoto.AttachAsync(payload, meetup);
            Assert.Equal("First", result.Speaker!.Name);
            Assert.Null(result.Speaker.PhotoError);
            using var photo = SKBitmap.Decode(Convert.FromBase64String(result.Speaker.PhotoBase64!));
            Assert.Equal(1600, photo.Width);
            Assert.Equal(800, photo.Height);
            Assert.Equal(Png, result.ImageBase64);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/images/speakers/../../appsettings.json")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task MissingOrUnsafePhoto_ReturnsExplicitPhotoErrorWithoutBlockingEventContent(string? url)
    {
        var meetup = Event(url);
        var result = await MeetupWebsiteSpeakerPhoto.AttachAsync(MeetupWebsite.Prepare(meetup, "Title", "Description", Png), meetup);
        Assert.Null(result.Speaker!.PhotoBase64);
        Assert.False(string.IsNullOrWhiteSpace(result.Speaker.PhotoError));
        Assert.Equal("Description", result.Description);
    }

    [Fact]
    public void InvalidAndOversizeImages_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => MeetupWebsiteSpeakerPhoto.ToPng([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => MeetupWebsiteSpeakerPhoto.ToPng(new byte[MeetupWebsite.MaximumImageBytes + 1]));
    }

    [Fact]
    public void DetailedPhoto_IsDownscaledToFitAlongsideEventImageInExtensionStorage()
    {
        using var bitmap = new SKBitmap(1600, 1000);
        var pixels = new byte[bitmap.ByteCount];
        new Random(42).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var prepared = MeetupWebsiteSpeakerPhoto.ToPng(jpeg.ToArray());
        Assert.True(prepared.Length <= 1024 * 1024);
        using var decoded = SKBitmap.Decode(prepared);
        Assert.True(decoded.Width <= 800);
        Assert.Equal(decoded.Width * 5 / 8, decoded.Height);
    }

    private static readonly string Png = Convert.ToBase64String([137, 80, 78, 71, 13, 10, 26, 10, 0]);
    private static MeetupEvent Event(string? photo) => new()
    {
        MeetupGroup = new() { MeetupUrlName = "test-group" },
        Sessions = [new() { OrderIndex = 1, Speaker = new() { Name = "First", PhotoBlobUrl = photo } }]
    };
}
