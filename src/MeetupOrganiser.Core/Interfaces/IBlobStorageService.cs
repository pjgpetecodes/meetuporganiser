using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string containerName, string fileName, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string containerName, string fileName, CancellationToken cancellationToken = default);
    string GetBlobUrl(string containerName, string fileName);
}
