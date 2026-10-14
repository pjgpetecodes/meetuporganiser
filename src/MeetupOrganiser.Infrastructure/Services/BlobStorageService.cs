using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    // Storage:AccountUrl uses the app's managed identity (Azure); Storage:ConnectionString is for Azurite in development.
    public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
    {
        var accountUrl = configuration["Storage:AccountUrl"];
        var connectionString = configuration.GetConnectionString("Storage")
            ?? configuration["Storage:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(accountUrl))
        {
            _blobServiceClient = new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
            logger.LogInformation("Using Azure Blob Storage at {AccountUrl} with managed identity.", accountUrl);
        }
        else if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            logger.LogInformation("Using Blob Storage from connection string (Azurite/development).");
        }
        else
        {
            throw new InvalidOperationException("Blob storage is not configured. Set Storage:AccountUrl, or Storage:ConnectionString (e.g. UseDevelopmentStorage=true for Azurite).");
        }

        RemotePhotoLoader.TrustEndpoint(_blobServiceClient.Uri);
    }
    public async Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var publicAccess = containerName == "templates" ? PublicAccessType.None : PublicAccessType.Blob;
        await containerClient.CreateIfNotExistsAsync(publicAccess, cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(safeFileName);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(content, options, cancellationToken);
        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadAsync(string containerName, string fileName, CancellationToken cancellationToken = default)
    {
        var blobClient = _blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(Path.GetFileName(fileName));
        var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
        return downloadResult.Value.Content.ToStream();
    }

    public async Task<bool> DeleteAsync(string containerName, string fileName, CancellationToken cancellationToken = default)
    {
        var blobClient = _blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(Path.GetFileName(fileName));
        var result = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return result.Value;
    }

    public string GetBlobUrl(string containerName, string fileName) =>
        _blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(Path.GetFileName(fileName)).Uri.ToString();
}
