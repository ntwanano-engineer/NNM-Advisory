using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Production <see cref="IImageStorageService"/> implementation that stores
/// leadership profile photographs in an AWS S3 bucket using the
/// <c>AWSSDK.S3</c> package.
///
/// <para>
/// Credentials are resolved via the AWS SDK default credential provider chain,
/// which in production uses the ECS task role (no access keys are hard-coded or
/// read from configuration) — see design "Image / File Storage (S3)" and
/// Requirement 13.6. The bucket name comes from <see cref="S3Settings.ImagesBucket"/>
/// (<c>S3_IMAGES_BUCKET</c> locally, <c>/nnm/s3/images-bucket</c> in AWS).
/// </para>
///
/// <para>
/// Uploaded files are validated for MIME type (JPEG/PNG only) and size (≤ 5 MB)
/// <em>before</em> any object is written to S3 — satisfying Requirements 4.5,
/// 4.6, 9.3, 9.4, 9.5 and correctness Property 5. Stored objects are never
/// publicly accessible; the frontend retrieves them via pre-signed GET URLs that
/// are valid for 1 hour.
/// </para>
/// </summary>
public sealed class S3ImageStorageService : IImageStorageService
{
    /// <summary>Maximum permitted upload size: 5 MB (Requirements 4.6, 9.4).</summary>
    public const long MaxFileSizeBytes = 5L * 1024 * 1024;

    /// <summary>Accepted MIME types (Requirements 4.5, 9.3).</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    private readonly IAmazonS3 _s3Client;
    private readonly S3Settings _settings;
    private readonly ILogger<S3ImageStorageService> _logger;

    public S3ImageStorageService(
        IAmazonS3 s3Client,
        IOptions<S3Settings> settings,
        ILogger<S3ImageStorageService> logger)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_settings.ImagesBucket))
        {
            throw new InvalidOperationException(
                "S3:ImagesBucket (S3_IMAGES_BUCKET) must be configured when the " +
                "image storage provider is 's3'.");
        }
    }

    /// <inheritdoc />
    public async Task<string> UploadImageAsync(IFormFile file, string key)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Storage key must be provided.", nameof(key));
        }

        // ------------------------------------------------------------------
        // Validate BEFORE touching S3 so invalid files never result in an
        // object being written (Property 5 — the bucket object count must be
        // unchanged on rejection). An InvalidOperationException here lets the
        // controller translate the failure into HTTP 400.
        // ------------------------------------------------------------------
        if (!AllowedContentTypes.Contains(file.ContentType ?? string.Empty))
        {
            throw new InvalidOperationException(
                $"Unsupported file type '{file.ContentType}'. " +
                "Only JPEG (image/jpeg) and PNG (image/png) images are accepted.");
        }

        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size {file.Length} bytes exceeds the maximum permitted " +
                $"size of {MaxFileSizeBytes} bytes (5 MB).");
        }

        // ------------------------------------------------------------------
        // Upload to S3. The object is private by default (no public ACL is
        // applied); retrieval is only possible via a pre-signed URL.
        // ------------------------------------------------------------------
        await using var stream = file.OpenReadStream();

        var request = new PutObjectRequest
        {
            BucketName = _settings.ImagesBucket,
            Key = key,
            InputStream = stream,
            ContentType = file.ContentType,
            AutoCloseStream = false,
        };

        try
        {
            await _s3Client.PutObjectAsync(request);
            _logger.LogInformation(
                "Uploaded image to S3 bucket {Bucket} with key {Key}",
                _settings.ImagesBucket,
                key);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to upload image to S3 bucket {Bucket} with key {Key}: {ErrorMessage}",
                _settings.ImagesBucket,
                key,
                ex.Message);
            throw;
        }

        return key;
    }

    /// <inheritdoc />
    public async Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Storage key must be provided.", nameof(key));
        }

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentException("Expiry must be a positive duration.", nameof(expiry));
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.ImagesBucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry),
        };

        return await _s3Client.GetPreSignedURLAsync(request);
    }
}
