using Microsoft.Extensions.Logging.Abstractions;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Unit tests for <see cref="ContactService"/> (Task 5.2).
///
/// Covers the field-level validation rules — full name ≤ 100, email
/// <c>local-part@domain.tld</c> ≤ 254, subject ≤ 150, message 10–2000 — the
/// persist-then-email success path, and the best-effort email behaviour where a
/// send failure does not fail a request whose submission is already persisted.
///
/// <para><b>Validates: Requirements 5.2, 5.3, 5.6, 5.7</b></para>
/// </summary>
public sealed class ContactServiceTests
{
    // ------------------------------------------------------------------
    // Hand-written test doubles — no mocking framework dependency.
    // ------------------------------------------------------------------

    /// <summary>Records what was persisted and can be made to throw.</summary>
    private sealed class FakeContactRepository : IContactRepository
    {
        public int AddCallCount { get; private set; }
        public ContactSubmission? LastAdded { get; private set; }
        public bool ThrowOnAdd { get; init; }

        public Task<ContactSubmission> AddAsync(
            ContactSubmission submission, CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException("simulated database failure");
            }

            submission.Id = Guid.NewGuid();
            submission.SubmittedAt = DateTimeOffset.UtcNow;
            LastAdded = submission;
            return Task.FromResult(submission);
        }
    }

    /// <summary>Records email sends and can be made to throw on either send.</summary>
    private sealed class FakeEmailService : IEmailService
    {
        public int AcknowledgementCount { get; private set; }
        public int AdminNotificationCount { get; private set; }
        public string? AcknowledgementTo { get; private set; }
        public string? AcknowledgementName { get; private set; }
        public bool ThrowOnAcknowledgement { get; init; }
        public bool ThrowOnAdminNotification { get; init; }

        public Task SendAcknowledgementEmailAsync(string toEmail, string clientName)
        {
            AcknowledgementCount++;
            AcknowledgementTo = toEmail;
            AcknowledgementName = clientName;
            if (ThrowOnAcknowledgement)
            {
                throw new InvalidOperationException("simulated acknowledgement failure");
            }

            return Task.CompletedTask;
        }

        public Task SendAdminNotificationEmailAsync(ContactSubmission submission)
        {
            AdminNotificationCount++;
            if (ThrowOnAdminNotification)
            {
                throw new InvalidOperationException("simulated admin notification failure");
            }

            return Task.CompletedTask;
        }
    }

    private static ContactService CreateService(
        FakeContactRepository repository, FakeEmailService emailService) =>
        new(repository, emailService, NullLogger<ContactService>.Instance);

    private static ContactSubmission ValidSubmission() => new()
    {
        FullName = "Jane Smith",
        Email = "jane@example.com",
        Subject = "Service Enquiry",
        Message = "I would like to know more about your accounting services.",
    };

    // ------------------------------------------------------------------
    // Success path — persist then email (Requirements 5.2, 5.3).
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubmitAsync_ValidSubmission_PersistsAndReturnsAccepted()
    {
        var repository = new FakeContactRepository();
        var emailService = new FakeEmailService();
        var service = CreateService(repository, emailService);

        var result = await service.SubmitAsync(ValidSubmission());

        Assert.Equal(ContactOutcome.Accepted, result.Outcome);
        Assert.Empty(result.Errors);
        Assert.Equal(1, repository.AddCallCount);
        Assert.NotNull(repository.LastAdded);
    }

    [Fact]
    public async Task SubmitAsync_ValidSubmission_SendsBothEmailsAfterPersist()
    {
        var repository = new FakeContactRepository();
        var emailService = new FakeEmailService();
        var service = CreateService(repository, emailService);
        var submission = ValidSubmission();

        await service.SubmitAsync(submission);

        Assert.Equal(1, emailService.AcknowledgementCount);
        Assert.Equal(1, emailService.AdminNotificationCount);
        // Acknowledgement is addressed to the client, personalised by full name.
        Assert.Equal(submission.Email, emailService.AcknowledgementTo);
        Assert.Equal(submission.FullName, emailService.AcknowledgementName);
    }

    // ------------------------------------------------------------------
    // Best-effort email — a send failure does not fail the request
    // because the submission is already persisted (Requirement 5.3).
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubmitAsync_AcknowledgementEmailThrows_StillReturnsAccepted()
    {
        var repository = new FakeContactRepository();
        var emailService = new FakeEmailService { ThrowOnAcknowledgement = true };
        var service = CreateService(repository, emailService);

        var result = await service.SubmitAsync(ValidSubmission());

        Assert.Equal(ContactOutcome.Accepted, result.Outcome);
        Assert.Equal(1, repository.AddCallCount);
        // Admin notification is still attempted even though acknowledgement threw.
        Assert.Equal(1, emailService.AdminNotificationCount);
    }

    [Fact]
    public async Task SubmitAsync_AdminNotificationEmailThrows_StillReturnsAccepted()
    {
        var repository = new FakeContactRepository();
        var emailService = new FakeEmailService { ThrowOnAdminNotification = true };
        var service = CreateService(repository, emailService);

        var result = await service.SubmitAsync(ValidSubmission());

        Assert.Equal(ContactOutcome.Accepted, result.Outcome);
        Assert.Equal(1, repository.AddCallCount);
    }

    // ------------------------------------------------------------------
    // Validation failures — nothing persisted, no email, per-field errors
    // (Requirements 5.6, 5.7).
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubmitAsync_AllFieldsEmpty_ReturnsValidationFailedForEachField()
    {
        var repository = new FakeContactRepository();
        var emailService = new FakeEmailService();
        var service = CreateService(repository, emailService);

        var result = await service.SubmitAsync(new ContactSubmission());

        Assert.Equal(ContactOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("fullName", result.Errors.Keys);
        Assert.Contains("email", result.Errors.Keys);
        Assert.Contains("subject", result.Errors.Keys);
        Assert.Contains("message", result.Errors.Keys);
        // No persistence and no email on validation failure.
        Assert.Equal(0, repository.AddCallCount);
        Assert.Equal(0, emailService.AcknowledgementCount);
        Assert.Equal(0, emailService.AdminNotificationCount);
    }

    [Theory]
    [InlineData("plainaddress")]      // no @ or domain
    [InlineData("missing@domain")]    // no TLD
    [InlineData("@no-local.com")]     // empty local part
    [InlineData("has space@a.com")]   // whitespace in address
    [InlineData("two@@at.com")]       // malformed
    public async Task SubmitAsync_MalformedEmail_ReturnsValidationFailedOnEmailField(string email)
    {
        var repository = new FakeContactRepository();
        var service = CreateService(repository, new FakeEmailService());
        var submission = ValidSubmission();
        submission.Email = email;

        var result = await service.SubmitAsync(submission);

        Assert.Equal(ContactOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("email", result.Errors.Keys);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task SubmitAsync_FullNameTooLong_ReturnsValidationFailedOnFullNameField()
    {
        var repository = new FakeContactRepository();
        var service = CreateService(repository, new FakeEmailService());
        var submission = ValidSubmission();
        submission.FullName = new string('a', ContactService.MaxFullNameLength + 1);

        var result = await service.SubmitAsync(submission);

        Assert.Equal(ContactOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("fullName", result.Errors.Keys);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task SubmitAsync_SubjectTooLong_ReturnsValidationFailedOnSubjectField()
    {
        var repository = new FakeContactRepository();
        var service = CreateService(repository, new FakeEmailService());
        var submission = ValidSubmission();
        submission.Subject = new string('a', ContactService.MaxSubjectLength + 1);

        var result = await service.SubmitAsync(submission);

        Assert.Equal(ContactOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("subject", result.Errors.Keys);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Theory]
    [InlineData(ContactService.MinMessageLength - 1)]   // too short
    [InlineData(ContactService.MaxMessageLength + 1)]   // too long
    public async Task SubmitAsync_MessageOutOfBounds_ReturnsValidationFailedOnMessageField(int length)
    {
        var repository = new FakeContactRepository();
        var service = CreateService(repository, new FakeEmailService());
        var submission = ValidSubmission();
        submission.Message = new string('a', length);

        var result = await service.SubmitAsync(submission);

        Assert.Equal(ContactOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("message", result.Errors.Keys);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task SubmitAsync_BoundaryValidValues_AreAccepted()
    {
        var repository = new FakeContactRepository();
        var service = CreateService(repository, new FakeEmailService());
        var submission = new ContactSubmission
        {
            FullName = new string('a', ContactService.MaxFullNameLength),
            Email = "a@b.co",
            Subject = new string('s', ContactService.MaxSubjectLength),
            Message = new string('m', ContactService.MinMessageLength),
        };

        var result = await service.SubmitAsync(submission);

        Assert.Equal(ContactOutcome.Accepted, result.Outcome);
        Assert.Equal(1, repository.AddCallCount);
    }
}
