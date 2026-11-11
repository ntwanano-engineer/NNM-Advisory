using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Unit tests for <see cref="AwsSesEmailService"/> (Task 7.4).
///
/// These tests exercise the construction of the SES v2
/// <see cref="SendEmailRequest"/> that the service builds for each email type,
/// without performing any real network call. A capturing stub SES client
/// (<see cref="CapturingSesClient"/>) records the request so the body, subject,
/// and destination can be asserted.
///
/// <list type="bullet">
///   <item>Acknowledgement email body contains the client's full name (Req 5.4).</item>
///   <item>Admin notification email body contains all four required fields —
///         full name, email, subject, message body — and is addressed to the
///         configured admin inbox (Req 5.5).</item>
/// </list>
///
/// <para><b>Validates: Requirements 5.4, 5.5</b></para>
/// </summary>
public sealed class AwsSesEmailServiceTests
{
    private const string SenderEmail = "no-reply@nnmadvisory.com";
    private const string AdminEmail = "admin@nnmadvisory.com";

    // ------------------------------------------------------------------
    // Capturing SES client — subclasses the concrete SES v2 client and
    // overrides SendEmailAsync so no real AWS call is made. No mocking
    // framework dependency is required.
    // ------------------------------------------------------------------

    /// <summary>
    /// A stub <see cref="AmazonSimpleEmailServiceV2Client"/> that captures the
    /// <see cref="SendEmailRequest"/> passed to <c>SendEmailAsync</c> and returns
    /// a canned response instead of contacting AWS.
    /// </summary>
    private sealed class CapturingSesClient : AmazonSimpleEmailServiceV2Client
    {
        public CapturingSesClient()
            : base(
                new BasicAWSCredentials("test-access-key", "test-secret-key"),
                new AmazonSimpleEmailServiceV2Config { RegionEndpoint = RegionEndpoint.EUWest1 })
        {
        }

        public List<SendEmailRequest> Requests { get; } = new();

        public SendEmailRequest LastRequest =>
            Requests.Count > 0
                ? Requests[^1]
                : throw new InvalidOperationException("No SendEmailRequest was captured.");

        public override Task<SendEmailResponse> SendEmailAsync(
            SendEmailRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new SendEmailResponse { MessageId = "stub-message-id" });
        }
    }

    private static AwsSesEmailService CreateService(CapturingSesClient client)
    {
        var settings = Options.Create(new EmailSettings
        {
            Provider = "ses",
            SenderEmail = SenderEmail,
            AdminEmail = AdminEmail,
        });

        return new AwsSesEmailService(
            client, settings, NullLogger<AwsSesEmailService>.Instance);
    }

    private static ContactSubmission SampleSubmission() => new()
    {
        FullName = "Jane Smith",
        Email = "jane.smith@example.com",
        Subject = "Service Enquiry",
        Message = "I would like to know more about your accounting services.",
    };

    /// <summary>Returns the combined text + HTML body of a request for content assertions.</summary>
    private static string BodyText(SendEmailRequest request)
    {
        var body = request.Content.Simple.Body;
        return (body.Text?.Data ?? string.Empty) + "\n" + (body.Html?.Data ?? string.Empty);
    }

    // ------------------------------------------------------------------
    // Acknowledgement email — Requirement 5.4.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SendAcknowledgementEmailAsync_BodyContainsClientFullName()
    {
        var client = new CapturingSesClient();
        var service = CreateService(client);

        await service.SendAcknowledgementEmailAsync("jane.smith@example.com", "Jane Smith");

        var request = client.LastRequest;
        Assert.Contains("Jane Smith", BodyText(request));
    }

    [Fact]
    public async Task SendAcknowledgementEmailAsync_SendsFromConfiguredSenderToClient()
    {
        var client = new CapturingSesClient();
        var service = CreateService(client);

        await service.SendAcknowledgementEmailAsync("jane.smith@example.com", "Jane Smith");

        var request = client.LastRequest;
        Assert.Equal(SenderEmail, request.FromEmailAddress);
        Assert.Contains("jane.smith@example.com", request.Destination.ToAddresses);
    }

    // ------------------------------------------------------------------
    // Admin notification email — Requirement 5.5.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SendAdminNotificationEmailAsync_BodyContainsAllFourRequiredFields()
    {
        var client = new CapturingSesClient();
        var service = CreateService(client);
        var submission = SampleSubmission();

        await service.SendAdminNotificationEmailAsync(submission);

        var body = BodyText(client.LastRequest);
        Assert.Contains(submission.FullName, body);  // name
        Assert.Contains(submission.Email, body);      // email
        Assert.Contains(submission.Subject, body);    // subject
        Assert.Contains(submission.Message, body);    // message
    }

    [Fact]
    public async Task SendAdminNotificationEmailAsync_IsAddressedToConfiguredAdminInbox()
    {
        var client = new CapturingSesClient();
        var service = CreateService(client);

        await service.SendAdminNotificationEmailAsync(SampleSubmission());

        var request = client.LastRequest;
        Assert.Contains(AdminEmail, request.Destination.ToAddresses);
        Assert.Equal(SenderEmail, request.FromEmailAddress);
    }

    [Fact]
    public async Task SendAdminNotificationEmailAsync_NullSubmission_Throws()
    {
        var client = new CapturingSesClient();
        var service = CreateService(client);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SendAdminNotificationEmailAsync(null!));
    }
}
