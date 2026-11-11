using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Development <see cref="IImageStorageService"/> implementation that stores
/// leadership profile photographs on the local filesystem. Selected at startup
/// when <c>IMAGE_STORAGE_PROVIDER=local</c> (<see cref="S3Settings.ImageStorageProvider"/>).
///
/// <para>
/// Files are written beneath <see cref="S3Settings.LocalStoragePath"/> (defaults
/// to an <c>uploads</c> directory relative to the application working directory).
/// This lets developers run the full upload/retrieve flow without any AWS
/// dependency (Requirement 12.2).
/// </para>
///
/// <para>
/// Validation mirrors <see cref="S3ImageStorageService"/> exactly: the MIME type
/// (JPEG/PNG only) and size (≤ 5 MB) are checked <em>before</em> any file is
/// written to disk, so a rejected upload leaves the storage directory unchanged
/// (Requirements 4.5, 4.6, 9.3, 9.4, 9.5 and correctness Property 5). Invalid
/// input throws <see cref="InvalidOperationException"/> and writes nothing.
/// </para>
///
/// <para>
/// <see cref="GetPresignedUrlAsync"/> returns a relative URL path
/// (<c>/uploads/{key}</c>) that the application can serve as a static file in
/// development. The <paramref name="expiry"/> is accepted for interface
/// compatibility but has no effect locally.
/// </para>
/// </summary>
public sealed class LocalFileImageStorageService : IImageStorageService
{
    /// <summary>Maximum permitted upload size: 5 MB (Requirements 4.6, 9.4).</summary>
    public const long MaxFileSizeBytes = 5L * 1024 * 1024;

    /// <summary>Accepted MIME types (Requirements 4.5, 9.3).</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    /// <summary>URL prefix under which stored images are served locally.</summary>
    private const string UrlPrefix = "/uploads";

    private readonly string _storageRoot;
    private readonly ILogger<LocalFileImageStorageService> _logger;

    public LocalFileImageStorageService(
        IOptions<S3Settings> settings,
        ILogger<LocalFileImageStorageService> logger)
    {
        var options = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var configuredPath = string.IsNullOrWhiteSpace(options.LocalStoragePath)
            ? "uploads"
            : options.LocalStoragePath;

        // Resolve to an absolute path rooted at the working directory when a
        // relative path is configured, so the location is deterministic.
        _storageRoot = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
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
        // Validate BEFORE touching the filesystem so invalid files never
        // result in a file being written (Property 5 — the stored file count
        // must be unchanged on rejection). An InvalidOperationException here
        // lets the controller translate the failure into HTTP 400.
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
        // Resolve the destination path, guarding against path traversal in
        // the supplied key (e.g. "../../etc/passwd"). The final path must
        // remain inside the storage root.
        // ------------------------------------------------------------------
        var destinationPath = ResolveSafePath(key);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(destination);
        }

        _logger.LogInformation(
            "Stored image on local filesystem at {Path} with key {Key}",
            destinationPath,
            key);

        return key;
    }

    /// <inheritdoc />
    public Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Storage key must be provided.", nameof(key));
        }

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentException("Expiry must be a positive duration.", nameof(expiry));
        }

        // Local provider serves files statically; the expiry has no effect but
        // is validated for parity with the S3 implementation. The returned path
        // is relative to the application origin, e.g. "/uploads/<key>".
        var relativeKey = key.Replace('\\', '/').TrimStart('/');
        var url = $"{UrlPrefix}/{relativeKey}";

        return Task.FromResult(url);
    }

    /// <summary>
    /// Combines <paramref name="key"/> with the storage root and verifies the
    /// resulting path does not escape the root (path-traversal defence).
    /// </summary>
    private string ResolveSafePath(string key)
    {
        var normalizedKey = key.Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(_storageRoot, normalizedKey));
        var rootFull = Path.GetFullPath(_storageRoot);

        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Storage key '{key}' resolves outside the permitted storage directory.");
        }

        return combined;
    }
}
