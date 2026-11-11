using System.ComponentModel.DataAnnotations;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Strongly-typed settings for AWS S3 image storage.
/// Maps to the "S3" configuration section.
/// </summary>
public sealed class S3Settings
{
    public const string SectionName = "S3";

    /// <summary>
    /// Selects the image storage provider implementation.
    /// Accepted values: "local" (filesystem, for development), "s3" (AWS S3).
    /// Sourced from IMAGE_STORAGE_PROVIDER.
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "S3:ImageStorageProvider is required. " +
                       "Set IMAGE_STORAGE_PROVIDER to 'local' or 's3'.")]
    [RegularExpression("^(local|s3)$",
        ErrorMessage = "S3:ImageStorageProvider must be either 'local' or 's3'.")]
    public string ImageStorageProvider { get; set; } = string.Empty;

    /// <summary>
    /// Name of the S3 bucket used for leadership profile photographs.
    /// Required when S3:ImageStorageProvider is "s3".
    /// Sourced from S3_IMAGES_BUCKET (local) or /nnm/s3/images-bucket (AWS).
    /// </summary>
    public string? ImagesBucket { get; set; }

    /// <summary>
    /// AWS region for the S3 bucket (e.g., "af-south-1", "us-east-1").
    /// Required when S3:ImageStorageProvider is "s3".
    /// </summary>
    public string? AwsRegion { get; set; }

    /// <summary>
    /// Local filesystem directory used when ImageStorageProvider is "local".
    /// Defaults to "uploads" relative to the application working directory.
    /// </summary>
    public string LocalStoragePath { get; set; } = "uploads";
}
