using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MeetupOrganiser.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeetupOrganiser.Infrastructure.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly string _localStoragePath;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly bool _useLocalStorage;

    public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("Storage")
            ?? configuration["Storage:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("UseDevelopmentStorage=true"))
        {
            _useLocalStorage = true;
            _localStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "storage");
            Directory.CreateDirectory(_localStoragePath);
            _logger.LogInformation("Using local file storage at {Path}", _localStoragePath);
        }
        else
        {
            try
            {
                _blobServiceClient = new BlobServiceClient(connectionString);
                _useLocalStorage = false;
                _localStoragePath = string.Empty;
                _logger.LogInformation("Using Azure Blob Storage.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Azure Blob Storage; falling back to local file storage.");
                _useLocalStorage = true;
                _localStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "storage");
                Directory.CreateDirectory(_localStoragePath);
            }
        }
    }

    public async Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

        if (_useLocalStorage || _blobServiceClient == null)
        {
            var containerDir = Path.Combine(_localStoragePath, containerName);
            Directory.CreateDirectory(containerDir);
            var filePath = Path.Combine(containerDir, safeFileName);

            using var fileStream = File.Create(filePath);
            await content.CopyToAsync(fileStream, cancellationToken);
            return $"/storage/{containerName}/{safeFileName}";
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

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
        if (_useLocalStorage || _blobServiceClient == null)
        {
            var filePath = Path.Combine(_localStoragePath, containerName, Path.GetFileName(fileName));
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found in local storage: {filePath}");
            }
            var memoryStream = new MemoryStream();
            using (var fileStream = File.OpenRead(filePath))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }
            memoryStream.Position = 0;
            return memoryStream;
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(Path.GetFileName(fileName));
        var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
        return downloadResult.Value.Content.ToStream();
    }

    public async Task<bool> DeleteAsync(string containerName, string fileName, CancellationToken cancellationToken = default)
    {
        if (_useLocalStorage || _blobServiceClient == null)
        {
            var filePath = Path.Combine(_localStoragePath, containerName, Path.GetFileName(fileName));
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(Path.GetFileName(fileName));
        var result = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return result.Value;
    }

    public string GetBlobUrl(string containerName, string fileName)
    {
        if (_useLocalStorage || _blobServiceClient == null)
        {
            return $"/storage/{containerName}/{Path.GetFileName(fileName)}";
        }
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(Path.GetFileName(fileName));
        return blobClient.Uri.ToString();
    }
}
