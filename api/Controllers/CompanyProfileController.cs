using Microsoft.AspNetCore.Mvc;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;

namespace NnmAdvisory.Api.Controllers;

/// <summary>
/// Public, read-only endpoints that expose company profile content to the
/// frontend: general profile content, service blocks, and leadership profiles.
///
/// All endpoints are anonymous (no <c>[Authorize]</c>) because the marketing
/// site must render this content for unauthenticated visitors.
///
/// Requirements 10.1, 10.2: the Backend API SHALL expose public endpoints that
/// return company profile content, service blocks, and leadership profiles as
/// JSON with an HTTP 200 status on success. Unhandled exceptions propagate to
/// the global exception handler (Task 2.3), which returns HTTP 500 with a
/// generic message.
/// </summary>
[ApiController]
[Route("api/profile")]
public class CompanyProfileController : ControllerBase
{
    /// <summary>
    /// Pre-signed leadership photo URLs are valid for one hour, matching the
    /// design document's S3 GET URL expiry.
    /// </summary>
    private static readonly TimeSpan PhotoUrlExpiry = TimeSpan.FromHours(1);

    private readonly ICompanyProfileService _profileService;
    private readonly IImageStorageService _imageStorageService;

    public CompanyProfileController(
        ICompanyProfileService profileService,
        IImageStorageService imageStorageService)
    {
        _profileService = profileService;
        _imageStorageService = imageStorageService;
    }

    /// <summary>
    /// Returns the configurable company profile content (e.g. tagline, hero CTA
    /// label) as a JSON array of key/value content items.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CompanyContent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile()
    {
        var content = await _profileService.GetProfileAsync();
        return Ok(content);
    }

    /// <summary>
    /// Returns every service block as a JSON array, ordered by the service's
    /// configured display order (ordering is applied by the data layer).
    /// </summary>
    [HttpGet("services")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceBlock>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetServices()
    {
        var services = await _profileService.GetServicesAsync();
        return Ok(services);
    }

    /// <summary>
    /// Returns every leadership profile as a JSON array. The stored S3 object
    /// key is translated into a time-limited pre-signed photo URL so the
    /// frontend can render the image without the objects being publicly
    /// accessible. Profiles without a stored photo return a null URL.
    /// </summary>
    [HttpGet("leadership")]
    [ProducesResponseType(typeof(IReadOnlyList<LeadershipProfileResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeadership()
    {
        var profiles = await _profileService.GetLeadershipAsync();

        var response = new List<LeadershipProfileResponse>(profiles.Count);
        foreach (var profile in profiles)
        {
            string? photoUrl = null;
            if (!string.IsNullOrWhiteSpace(profile.PhotoS3Key))
            {
                photoUrl = await _imageStorageService.GetPresignedUrlAsync(
                    profile.PhotoS3Key,
                    PhotoUrlExpiry);
            }

            response.Add(new LeadershipProfileResponse
            {
                Id = profile.Id,
                FullName = profile.FullName,
                Title = profile.Title,
                Biography = profile.Biography,
                PhotoUrl = photoUrl,
                DisplayOrder = profile.DisplayOrder,
                UpdatedAt = profile.UpdatedAt,
            });
        }

        return Ok(response);
    }
}

/// <summary>
/// Public-facing shape of a leadership profile. Mirrors
/// <see cref="LeadershipProfile"/> but replaces the internal S3 object key with
/// a ready-to-use pre-signed <see cref="PhotoUrl"/>.
/// </summary>
public sealed class LeadershipProfileResponse
{
    public Guid Id { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Biography { get; init; } = string.Empty;

    /// <summary>
    /// Time-limited pre-signed URL for the profile photo, or <c>null</c> when
    /// no photo has been uploaded for this profile.
    /// </summary>
    public string? PhotoUrl { get; init; }

    public int DisplayOrder { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
