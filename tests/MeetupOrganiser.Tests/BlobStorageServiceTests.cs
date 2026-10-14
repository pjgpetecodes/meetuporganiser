using MeetupOrganiser.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetupOrganiser.Tests;

public class BlobStorageServiceTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Constructor_WithNoConfiguration_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            new BlobStorageService(Config(), NullLogger<BlobStorageService>.Instance));

    [Fact]
    public void GetBlobUrl_WithAccountUrl_UsesAccountEndpoint()
    {
        var service = new BlobStorageService(
            Config(("Storage:AccountUrl", "https://stexample.blob.core.windows.net")),
            NullLogger<BlobStorageService>.Instance);

        Assert.Equal("https://stexample.blob.core.windows.net/logos/a.png", service.GetBlobUrl("logos", "a.png"));
    }

    [Fact]
    public void GetBlobUrl_WithAzurite_UsesDevelopmentStorageEndpoint()
    {
        var service = new BlobStorageService(
            Config(("Storage:ConnectionString", "UseDevelopmentStorage=true")),
            NullLogger<BlobStorageService>.Instance);

        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1/templates/a.pptx", service.GetBlobUrl("templates", "a.pptx"));
    }

    [Theory]
    [InlineData("https://stexample.blob.core.windows.net/templates/abc_My%20Deck.pptx", "abc_My Deck.pptx")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/templates/abc_deck.pptx", "abc_deck.pptx")]
    public void TryParseFileName_AcceptsBlobUrls(string url, string expected)
    {
        Assert.True(PptxTemplateStore.TryParseFileName(url, out var fileName));
        Assert.Equal(expected, fileName);
    }
}
