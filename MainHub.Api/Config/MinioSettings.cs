namespace MainHub.Api.Config;

/// <summary>
/// Configuration settings for the Minio (S3-compatible) object storage client.
/// </summary>
public class MinioSettings
{
    /// <summary>
    /// Internal endpoint host[:port] of the Minio server, e.g. "minio:9000" or "localhost:9000".
    /// Used for server-to-server operations (uploads, downloads, bucket ops).
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Public endpoint host[:port] that external clients can reach, e.g. "cdn.example.com"
    /// or "localhost:9000". Used exclusively when generating presigned URLs so the signature
    /// binds to a host the client can actually resolve. Falls back to <see cref="Endpoint"/>
    /// when not set (useful when Minio is directly reachable and no split is needed).
    /// </summary>
    public string? PublicEndpoint { get; set; }

    /// <summary>
    /// Whether presigned URLs should be issued over TLS. Defaults to <see cref="UseSsl"/>
    /// when not set. In production this is typically true even if the internal endpoint is plain HTTP.
    /// </summary>
    public bool? PublicUseSsl { get; set; }

    /// <summary>
    /// Access key (Minio "root user" or a service account).
    /// </summary>
    public required string AccessKey { get; set; }

    /// <summary>
    /// Secret key paired with <see cref="AccessKey"/>.
    /// </summary>
    public required string SecretKey { get; set; }

    /// <summary>
    /// Whether the client should connect over TLS. False for local docker setups.
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Default bucket used when the caller doesn't specify one.
    /// Auto-created on startup if it doesn't exist.
    /// </summary>
    public required string DefaultBucket { get; set; }

    /// <summary>
    /// Optional region hint for S3-style requests.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Default TTL for presigned GET URLs, in seconds. Minio caps at 7 days (604800).
    /// </summary>
    public int PresignedUrlExpirySeconds { get; set; } = 3600;
}
