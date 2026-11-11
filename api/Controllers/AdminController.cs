using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;

namespace NnmAdvisory.Api.Controllers;

/// <summary>
/// Protected admin endpoints for editing configurable company content:
/// the tagline, service blocks, leadership biographies, and leadership photos.
///
/// Every endpoint requires a valid JWT bearer token. The controller-level
/// <c>[Authorize]</c> attribute means the authentication middleware (Task 9.4)
/// rejects any request lacking a valid token before the action runs — a missing
/// token yields HTTP 401 and a token that fails authorisation yields HTTP 403,
/// so unauthenticated callers can never reach the content-mutation logic
/// (Requirements 8.1, 8.2).
///
/// Successful writes return HTTP 200 with the updated entity (Requirement 9.2).
/// Field-level validation failures return HTTP 422 with a per-field error map.
/// When no entity matches the supplied identifier the controller returns
/// HTTP 404. Database/infrastructure failures propagate to the global exception
/// handler, which rolls back the transaction and returns HTTP 500 while leaving
/// the stored content unchanged (Requirement 9.8).
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    /// <summary>
    /// Pre-signed leadership photo URLs are valid for one hour, matching the
    /// design document's S3 GET URL expiry and the public read endpoint.
    /// </summary>
    private static readonly TimeSpan PhotoUrlExpiry = TimeSpan.FromHours(1);

    private readonly ICompanyProfileService _profileService;
    private readonly IImageStorageService _imageStorageService;

    public AdminController(
        ICompanyProfileService profileService,
        IImageStorageService imageStorageService)
    {
        _profileService = profileService;
        _imageStorageService = imageStorageService;
    }

    /// <summary>
    /// Updates the company tagline.
    ///
    /// Maps the service outcome to HTTP status:
    /// Updated → 200 with the updated content,
    /// ValidationFailed → 422 with a per-field error map,
    /// NotFound → 404.
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(CompanyContent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateTaglineRequest request)
    {
        var result = await _profileService.UpdateTaglineAsync(request.Tagline);
        return MapResult(result);
    }

    /// <summary>
    /// Updates a service block's title, description, and offerings.
    /// </summary>
    [HttpPut("services/{id:guid}")]
    [ProducesResponseType(typeof(ServiceBlock), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateServiceBlock(
        Guid id,
        [FromBody] UpdateServiceBlockRequest request)
    {
        var result = await _profileService.UpdateServiceBlockAsync(
            id,
            request.Title,
            request.Description,
            request.Offerings);
        return MapResult(result);
    }

    /// <summary>
    /// Updates a leadership profile's biography and, optionally, its photo key.
    /// </summary>
    [HttpPut("leadership/{id:guid}")]
    [ProducesResponseType(typeof(LeadershipProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLeadership(
        Guid id,
        [FromBody] UpdateLeadershipRequest request)
    {
        var result = await _profileService.UpdateLeadershipBiographyAsync(
            id,
            request.Biography,
            request.PhotoS3Key);
        return MapResult(result);
    }

    /// <summary>
    /// Uploads a new profile photograph for a leadership profile.
    ///
    /// The multipart file is validated and stored by
    /// <see cref="IImageStorageService.UploadImageAsync"/>, which throws
    /// <see cref="InvalidOperationException"/> for a non-JPEG/PNG file or a file
    /// exceeding 5 MB (Requirements 9.3–9.5). On a valid upload the returned
    /// storage key is persisted against the leadership profile and the response
    /// carries the updated profile plus a ready-to-use pre-signed photo URL.
    ///
    /// Outcome mapping:
    /// invalid file → 400 with the reason,
    /// no file supplied → 400,
    /// profile not found → 404,
    /// success → 200 with the updated profile and pre-signed URL.
    /// </summary>
    [HttpPost("leadership/{id:guid}/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadLeadershipPhoto(Guid id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "A non-empty image file is required." });
        }

        // Preserve the original extension (defaulting to .jpg) so the stored key
        // reflects the image type; the storage service independently validates
        // the MIME type and size.
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var key = $"leadership/{id}/{Guid.NewGuid():N}{extension}";

        string storedKey;
        try
        {
            storedKey = await _imageStorageService.UploadImageAsync(file, key);
        }
        catch (InvalidOperationException ex)
        {
            // Invalid MIME type or oversized file (Requirements 9.4, 9.5).
            return BadRequest(new { error = ex.Message });
        }

        // Persist the new photo key against the profile. The biography is left
        // unchanged by re-supplying the profile's current biography.
        var profiles = await _profileService.GetLeadershipAsync();
        var existing = profiles.FirstOrDefault(p => p.Id == id);
        if (existing is null)
        {
            return NotFound();
        }

        var result = await _profileService.UpdateLeadershipBiographyAsync(
            id,
            existing.Biography,
            storedKey);

        if (result.Outcome == ProfileUpdateOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Outcome == ProfileUpdateOutcome.ValidationFailed)
        {
            // The biography was previously valid; a validation failure here is
            // unexpected, so surface it the same way as other admin edits.
            return UnprocessableEntity(new { errors = result.Errors });
        }

        var updated = result.Value!;
        var photoUrl = await _imageStorageService.GetPresignedUrlAsync(storedKey, PhotoUrlExpiry);

        return Ok(new
        {
            id = updated.Id,
            fullName = updated.FullName,
            title = updated.Title,
            biography = updated.Biography,
            photoS3Key = updated.PhotoS3Key,
            photoUrl,
            displayOrder = updated.DisplayOrder,
            updatedAt = updated.UpdatedAt,
        });
    }

    /// <summary>
    /// Translates a <see cref="ProfileUpdateResult{T}"/> into the HTTP response
    /// expected by the admin content endpoints.
    /// </summary>
    private IActionResult MapResult<T>(ProfileUpdateResult<T> result) where T : class =>
        result.Outcome switch
        {
            ProfileUpdateOutcome.Updated => Ok(result.Value),
            ProfileUpdateOutcome.ValidationFailed =>
                UnprocessableEntity(new { errors = result.Errors }),
            ProfileUpdateOutcome.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
}
