using MainHub.Api.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace MainHub.Api.Services;

// Minio = S3-compatible object store.
//   - Bucket: top-level container (must exist before writing).
//   - Object: a file identified by `objectName` (the "key", e.g.
//     "avatars/123.png"). Slashes are just part of the name - no real folders.
//     Re-uploading the same key overwrites.
//   - Presigned URL: temporary signed link that lets a client GET or PUT one
//     object directly, without going through the API.
public interface IMinioService
{
    // Creates the bucket if missing; no-op if it already exists.
    // `bucket == null` falls back to MinioSettings.DefaultBucket (same for all
    // methods below).
    Task EnsureBucketAsync(string? bucket = null, CancellationToken ct = default);

    // Uploads `size` bytes from `data` under key `objectName`.
    // `contentType` is stored as metadata (e.g. "image/png"). Returns
    // `objectName` for convenience (handy to persist in the DB).
    Task<string> UploadAsync(
        Stream data,
        long size,
        string objectName,
        string contentType,
        string? bucket = null,
        CancellationToken ct = default);

    // Buffers the object into a MemoryStream. Fine for small/medium files;
    // stream directly to the response for very large ones.
    Task<Stream> DownloadAsync(string objectName, string? bucket = null, CancellationToken ct = default);

    // Deletes the object. Missing keys don't throw (S3 semantics).
    Task DeleteAsync(string objectName, string? bucket = null, CancellationToken ct = default);

    // HEAD-style existence check - returns false instead of throwing on 404.
    Task<bool> ExistsAsync(string objectName, string? bucket = null, CancellationToken ct = default);

    // Signed download URL - hand to the client so it fetches from Minio
    // directly. Expiry capped at 7 days (604800s).
    Task<string> GetPresignedGetUrlAsync(
        string objectName,
        int? expirySeconds = null,
        string? bucket = null,
        CancellationToken ct = default);

    // Signed upload URL - client PUTs bytes straight to Minio. Your API
    // decides the key and returns the URL.
    Task<string> GetPresignedPutUrlAsync(
        string objectName,
        int? expirySeconds = null,
        string? bucket = null,
        CancellationToken ct = default);
}

public class MinioService(
    IMinioClient client,
    [FromKeyedServices(MinioClientKeys.Presign)] IMinioClient presignClient,
    IOptions<MinioSettings> settings,
    ILogger<MinioService> logger
) : IMinioService
{
    private readonly IMinioClient _client = client;
    private readonly IMinioClient _presignClient = presignClient;
    private readonly MinioSettings _settings = settings.Value;
    private readonly ILogger<MinioService> _logger = logger;

    private string Resolve(string? bucket) =>
        string.IsNullOrWhiteSpace(bucket) ? _settings.DefaultBucket : bucket;

    public async Task EnsureBucketAsync(string? bucket = null, CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(name), ct);

        if (!exists)
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(name), ct);
            _logger.LogInformation("Created Minio bucket {Bucket}", name);
        }
    }

    public async Task<string> UploadAsync(
        Stream data,
        long size,
        string objectName,
        string contentType,
        string? bucket = null,
        CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        await _client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(name)
                .WithObject(objectName)
                .WithStreamData(data)
                .WithObjectSize(size)
                .WithContentType(contentType),
            ct);
        return objectName;
    }

    public async Task<Stream> DownloadAsync(string objectName, string? bucket = null, CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        var ms = new MemoryStream();
        await _client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(name)
                .WithObject(objectName)
                .WithCallbackStream(async (stream, innerCt) =>
                {
                    await stream.CopyToAsync(ms, innerCt);
                }),
            ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string objectName, string? bucket = null, CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        await _client.RemoveObjectAsync(
            new RemoveObjectArgs().WithBucket(name).WithObject(objectName), ct);
    }

    public async Task<bool> ExistsAsync(string objectName, string? bucket = null, CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        try
        {
            await _client.StatObjectAsync(
                new StatObjectArgs().WithBucket(name).WithObject(objectName), ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public Task<string> GetPresignedGetUrlAsync(
        string objectName,
        int? expirySeconds = null,
        string? bucket = null,
        CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        return _presignClient.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(name)
                .WithObject(objectName)
                .WithExpiry(expirySeconds ?? _settings.PresignedUrlExpirySeconds));
    }

    public Task<string> GetPresignedPutUrlAsync(
        string objectName,
        int? expirySeconds = null,
        string? bucket = null,
        CancellationToken ct = default)
    {
        var name = Resolve(bucket);
        return _presignClient.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(name)
                .WithObject(objectName)
                .WithExpiry(expirySeconds ?? _settings.PresignedUrlExpirySeconds));
    }
}
