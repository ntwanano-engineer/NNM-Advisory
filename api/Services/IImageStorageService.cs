using Microsoft.AspNetCore.Http;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Abstracts image storage operations so that the business logic layer
/// remains decoupled from the underlying provider (AWS S3 in production,
/// local filesystem in development).
/// </summary>
/// <remarks>
/// Requirements 4.5, 9.3:
///   - The implementation MUST reject files that are not JPEG or PNG format
///     or that exceed 5 MB in size, returning an error indicating the reason.
///   - On a valid upload the implementation stores the image and returns the
///     storage key used to retrieve it later.
///   - Pre-signed GET URLs are generated per request so that the stored objects
///     are never publicly accessible by default.
/// </remarks>
public interface IImageStorageService
{
    /// <summary>
    /// Validates and uploads an image file to the configured storage backend.
    /// </summary>
    /// <param name="file">
    /// The uploaded file received from the HTTP request.
    /// Must be JPEG or PNG and must not exceed 5 MB.
    /// </param>
    /// <param name="key">
    /// The storage key (object name / relative file path) under which the
    /// image will be saved.  The caller is responsible for generating a
    /// unique, collision-resistant key (e.g. a GUID-based path).
    /// </param>
    /// <returns>
    /// The storage key of the saved image, which can be passed to
    /// <see cref="GetPresignedUrlAsync"/> to generate a retrieval URL.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the file's MIME type is not <c>image/jpeg</c> or
    /// <c>image/png</c>, or when the file size exceeds 5 MB.
    /// </exception>
    Task<string> UploadImageAsync(IFormFile file, string key);

    /// <summary>
    /// Generates a time-limited, pre-signed URL that allows the holder to
    /// retrieve the stored image without any additional credentials.
    /// </summary>
    /// <param name="key">
    /// The storage key previously returned by <see cref="UploadImageAsync"/>.
    /// </param>
    /// <param name="expiry">
    /// How long the URL remains valid.  The design specifies 1 hour for
    /// leadership profile photos served to the frontend.
    /// </param>
    /// <returns>
    /// An absolute URL string that the frontend can use directly in an
    /// <c>&lt;img&gt;</c> tag or similar element.
    /// </returns>
    Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry);
}
