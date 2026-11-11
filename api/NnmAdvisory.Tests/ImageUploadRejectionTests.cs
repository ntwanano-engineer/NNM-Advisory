using CsCheck;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based tests for profile-photo upload rejection.
///
/// <para><b>Property 5: Profile Photo Upload Type and Size Rejection</b></para>
/// <para>
/// For any file upload where the MIME type is not <c>image/jpeg</c> or
/// <c>image/png</c>, or the file size exceeds 5 MB, the service SHALL reject the
/// upload (surfaced by the controller as HTTP 400) and no new object SHALL be
/// written to storage — i.e. the stored object/file count is unchanged.
/// </para>
///
/// <para><b>Validates: Requirements 4.5, 4.6, 9.3, 9.4, 9.5</b></para>
///
/// <para>
/// The rejection logic lives in <see cref="IImageStorageService"/> implementations
/// (<see cref="LocalFileImageStorageService"/> and <see cref="S3ImageStorageService"/>),
/// which validate MIME type and size <em>before</em> writing anything. These tests
/// exercise both implementations:
/// <list type="bullet">
///   <item>The local provider is run against a temporary directory and the file
///         count is asserted to be unchanged after a rejected upload.</item>
///   <item>The S3 provider is run against a fake <see cref="Amazon.S3.IAmazonS3"/>
///         that counts <c>PutObject</c> calls, asserting the count stays at zero.</item>
/// </list>
/// A rejection is signalled by an <see cref="InvalidOperationException"/>, which the
/// admin controller maps to an HTTP 400 response.
/// </para>
/// </summary>
public sealed class ImageUploadRejectionTests
{
    /// <summary>Maximum permitted upload size: 5 MB.</summary>
    private const long MaxFileSizeBytes = 5L * 1024 * 1024;

    // ------------------------------------------------------------------
    // Generators for invalid uploads.
    // ------------------------------------------------------------------

    /// <summary>
    /// A MIME type that is NOT one of the two accepted image types. Covers
    /// empty/blank, common wrong image types, and arbitrary free-form strings.
    /// </summary>
    private static readonly Gen<string> GenDisallowedContentType =
        Gen.OneOf(
            Gen.OneOfConst(
                "application/octet-stream",
                "image/gif",
                "image/bmp",
                "image/webp",
                "image/svg+xml",
                "image/tiff",
                "text/plain",
                "application/pdf",
                "application/json",
                "video/mp4",
                "",
                " "),
            // Arbitrary free-form content types, filtered so we never
            // accidentally generate an allowed type.
            Gen.Char['a', 'z'].Array[1, 15].Select(c => $"x-{new string(c)}/{new string(c)}"))
        .Where(ct => !IsAllowed(ct));

    /// <summary>
    /// A small-but-non-empty payload size used for the wrong-type case: the
    /// type must be rejected regardless of size, so we keep this well under
    /// the 5 MB ceiling (1 byte .. 1 KB).
    /// </summary>
    private static readonly Gen<int> GenSmallValidSize = Gen.Int[1, 1024];

    /// <summary>
    /// A size that exceeds the 5 MB ceiling. Bounded to keep the test fast —
    /// we never allocate these bytes; we only report the length via the stub.
    /// </summary>
    private static readonly Gen<long> GenOversizedLength =
        Gen.Long[MaxFileSizeBytes + 1, MaxFileSizeBytes * 3];

    private static bool IsAllowed(string contentType) =>
        string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase);

    // ==================================================================
    //  LocalFileImageStorageService — assert file count unchanged.
    // ==================================================================

    [Fact]
    public void LocalProvider_RejectsDisallowedContentType_WithoutWritingAFile()
    {
        Gen.Select(GenDisallowedContentType, GenSmallValidSize)
           .Sample((contentType, size) =>
           {
               using var temp = new TempStorageDirectory();
               var service = CreateLocalService(temp.Path);
               var file = new StubFormFile(contentType, size);

               var before = temp.FileCount();

               // A disallowed MIME type must be rejected before any write.
               Assert.ThrowsAny<InvalidOperationException>(
                   () => service.UploadImageAsync(file, "leadership/photo.bin").GetAwaiter().GetResult());

               // No object written: the file count is unchanged.
               Assert.Equal(before, temp.FileCount());
               Assert.False(file.StreamWasOpened,
                   "A rejected upload must not open the file stream for writing.");
           });
    }

    [Fact]
    public void LocalProvider_RejectsOversizedFile_WithoutWritingAFile()
    {
        GenOversizedLength.Sample(length =>
        {
            using var temp = new TempStorageDirectory();
            var service = CreateLocalService(temp.Path);

            // A validly-typed (JPEG) file whose declared length exceeds 5 MB.
            var file = new StubFormFile("image/jpeg", length);

            var before = temp.FileCount();

            Assert.ThrowsAny<InvalidOperationException>(
                () => service.UploadImageAsync(file, "leadership/photo.jpg").GetAwaiter().GetResult());

            Assert.Equal(before, temp.FileCount());
            Assert.False(file.StreamWasOpened,
                "A rejected upload must not open the file stream for writing.");
        });
    }

    // ==================================================================
    //  S3ImageStorageService — assert PutObject call count stays at zero.
    // ==================================================================

    [Fact]
    public void S3Provider_RejectsDisallowedContentType_WithoutPuttingAnObject()
    {
        Gen.Select(GenDisallowedContentType, GenSmallValidSize)
           .Sample((contentType, size) =>
           {
               var s3 = new PutCountingS3();
               var service = CreateS3Service(s3);
               var file = new StubFormFile(contentType, size);

               Assert.ThrowsAny<InvalidOperationException>(
                   () => service.UploadImageAsync(file, "leadership/photo.bin").GetAwaiter().GetResult());

               Assert.Equal(0, s3.PutObjectCallCount);
               Assert.False(file.StreamWasOpened);
           });
    }

    [Fact]
    public void S3Provider_RejectsOversizedFile_WithoutPuttingAnObject()
    {
        GenOversizedLength.Sample(length =>
        {
            var s3 = new PutCountingS3();
            var service = CreateS3Service(s3);
            var file = new StubFormFile("image/png", length);

            Assert.ThrowsAny<InvalidOperationException>(
                () => service.UploadImageAsync(file, "leadership/photo.png").GetAwaiter().GetResult());

            Assert.Equal(0, s3.PutObjectCallCount);
            Assert.False(file.StreamWasOpened);
        });
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static LocalFileImageStorageService CreateLocalService(string storageRoot)
    {
        var settings = Options.Create(new S3Settings
        {
            ImageStorageProvider = "local",
            LocalStoragePath = storageRoot,
        });

        return new LocalFileImageStorageService(
            settings,
            NullLogger<LocalFileImageStorageService>.Instance);
    }

    private static S3ImageStorageService CreateS3Service(Amazon.S3.IAmazonS3 s3)
    {
        var settings = Options.Create(new S3Settings
        {
            ImageStorageProvider = "s3",
            ImagesBucket = "nnm-test-images",
            AwsRegion = "af-south-1",
        });

        return new S3ImageStorageService(
            s3,
            settings,
            NullLogger<S3ImageStorageService>.Instance);
    }

    /// <summary>
    /// A disposable temp directory used as the local storage root. Tracks the
    /// number of files written beneath it (recursively).
    /// </summary>
    private sealed class TempStorageDirectory : IDisposable
    {
        public string Path { get; }

        public TempStorageDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"nnm-img-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public int FileCount() =>
            Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories).Count();

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; ignore failures during teardown.
            }
        }
    }

    /// <summary>
    /// An <see cref="IFormFile"/> stub wrapping a declared content type and
    /// length. For oversized cases the byte payload is never materialised; the
    /// length is reported directly so the test stays fast. Tracks whether the
    /// read stream was ever opened so we can assert a rejected upload performs
    /// no I/O.
    /// </summary>
    private sealed class StubFormFile : IFormFile
    {
        private readonly byte[] _content;

        public bool StreamWasOpened { get; private set; }

        public StubFormFile(string contentType, long length)
        {
            ContentType = contentType;
            Length = length;
            // Only allocate a backing buffer for small payloads; oversized
            // declarations report their length without allocating 5 MB+.
            _content = length > 0 && length <= 4096 ? new byte[length] : Array.Empty<byte>();
        }

        public string ContentType { get; set; }
        public string ContentDisposition { get; set; } = string.Empty;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length { get; }
        public string Name { get; set; } = "file";
        public string FileName { get; set; } = "upload.bin";

        public void CopyTo(Stream target)
        {
            StreamWasOpened = true;
            target.Write(_content, 0, _content.Length);
        }

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            StreamWasOpened = true;
            return target.WriteAsync(_content, 0, _content.Length, cancellationToken);
        }

        public Stream OpenReadStream()
        {
            StreamWasOpened = true;
            return new MemoryStream(_content);
        }
    }

    /// <summary>
    /// A fake <see cref="Amazon.S3.IAmazonS3"/> that counts calls to
    /// <c>PutObjectAsync</c> and throws for every other member. The upload
    /// service validates before calling PutObject, so on a rejected upload the
    /// count must remain zero — proving no object was written to the bucket.
    /// </summary>
    private sealed class PutCountingS3 : Amazon.S3.AmazonS3Client
    {
        public int PutObjectCallCount { get; private set; }

        public PutCountingS3()
            // Supply dummy anonymous-style credentials/region so construction
            // never reaches out to the network or the credential chain.
            : base(
                new Amazon.Runtime.BasicAWSCredentials("test-access-key", "test-secret-key"),
                Amazon.RegionEndpoint.AFSouth1)
        {
        }

        public override Task<Amazon.S3.Model.PutObjectResponse> PutObjectAsync(
            Amazon.S3.Model.PutObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            PutObjectCallCount++;
            return Task.FromResult(new Amazon.S3.Model.PutObjectResponse());
        }
    }
}
