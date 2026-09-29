using System.Security.Cryptography;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleStatutoryIdPhotoServiceTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("11000000-0000-4000-8000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("22000000-0000-4000-8000-000000000002");
    private static readonly Guid SiteGroupId = Guid.Parse("33000000-0000-4000-8000-000000000003");
    private static readonly Guid ParkingSessionId = Guid.Parse("44000000-0000-4000-8000-000000000004");
    private static readonly Guid CorrelationId = Guid.Parse("55000000-0000-4000-8000-000000000005");
    private readonly DirectoryInfo _keyDirectory = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"exitpass-statutory-id-photo-{Guid.NewGuid():N}"));

    [Theory]
    [InlineData("SENIOR_CITIZEN")]
    [InlineData("PWD")]
    public async Task Upload_StoresRestrictedImageAndReturnsBoundProtectedReceipt(string entitlementType)
    {
        var fixture = CreateFixture(Session());
        var bytes = "restricted-photo-bytes"u8.ToArray();

        var outcome = await fixture.Service.UploadAsync(
            Command(entitlementType, bytes.Length),
            new MemoryStream(bytes),
            CancellationToken.None);

        outcome.Accepted.Should().BeTrue();
        outcome.UploadReceipt.Should().NotBeNullOrWhiteSpace();
        outcome.UploadReceipt.Should().NotContain(ParkingSessionId.ToString("D"));
        outcome.UploadReceipt.Should().NotContain(SiteId.ToString("D"));
        fixture.Upload.Should().NotBeNull();
        fixture.Upload!.BucketName.Should().Be("statutory-evidence");
        fixture.Upload.InternalObjectKey.Should().StartWith($"test/operator-console/statutory-id/{SiteId:N}/{ParkingSessionId:N}/");
        fixture.Upload.ContentType.Should().Be("image/jpeg");
        fixture.Upload.ContentLength.Should().Be(bytes.Length);
        fixture.Upload.ChecksumSha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)));

        var resolved = fixture.Service.ResolveReceipt(
            outcome.UploadReceipt,
            UserId,
            SiteId,
            ParkingSessionId,
            entitlementType);
        resolved.Should().NotBeNull();
        resolved!.StorageReference.Should().Be(fixture.Upload.InternalObjectKey);
        resolved.ChecksumSha256.Should().Be(fixture.Upload.ChecksumSha256);

        fixture.Service.ResolveReceipt(outcome.UploadReceipt, Guid.NewGuid(), SiteId, ParkingSessionId, entitlementType).Should().BeNull();
        fixture.Service.ResolveReceipt(outcome.UploadReceipt, UserId, Guid.NewGuid(), ParkingSessionId, entitlementType).Should().BeNull();
        fixture.Service.ResolveReceipt(outcome.UploadReceipt, UserId, SiteId, Guid.NewGuid(), entitlementType).Should().BeNull();
        fixture.Service.ResolveReceipt(outcome.UploadReceipt, UserId, SiteId, ParkingSessionId, entitlementType == "PWD" ? "SENIOR_CITIZEN" : "PWD").Should().BeNull();
        fixture.Service.ResolveReceipt(outcome.UploadReceipt + "tampered", UserId, SiteId, ParkingSessionId, entitlementType).Should().BeNull();
        await fixture.AccessEvaluation.Received(1).EvaluateAsync(
            Arg.Is<OperatorConsoleAccessEvaluationCommand>(command =>
                command.ControlledActionCode == OperatorConsoleActionCodes.CreateStatutoryDiscountDraft &&
                command.ParkingSessionId == ParkingSessionId &&
                command.SiteId == SiteId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_WhenPaymentAndExitAreFinal_RejectsBeforeSensitiveImageStorage()
    {
        var fixture = CreateFixture(Session(paymentConfirmationStatus: "RECORDED", exitAuthorizationStatus: "ISSUED"));
        var bytes = "must-not-be-uploaded"u8.ToArray();

        var outcome = await fixture.Service.UploadAsync(
            Command("SENIOR_CITIZEN", bytes.Length),
            new MemoryStream(bytes),
            CancellationToken.None);

        outcome.Accepted.Should().BeFalse();
        outcome.ErrorCode.Should().Be("STATUTORY_DISCOUNT_REQUEST_COMPLETED_TRANSACTION");
        fixture.Upload.Should().BeNull();
        await fixture.AccessEvaluation.Received(1).EvaluateAsync(
            Arg.Any<OperatorConsoleAccessEvaluationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_WhenDirectSiteDraftAccessIsDenied_DoesNotStoreSensitiveImage()
    {
        var fixture = CreateFixture(Session(), accessAllowed: false);
        var bytes = "must-not-be-uploaded"u8.ToArray();

        var outcome = await fixture.Service.UploadAsync(
            Command("SENIOR_CITIZEN", bytes.Length),
            new MemoryStream(bytes),
            CancellationToken.None);

        outcome.Accepted.Should().BeFalse();
        outcome.ErrorCode.Should().Be("ACCESS_DENIED");
        fixture.Upload.Should().BeNull();
    }

    public void Dispose() => _keyDirectory.Delete(recursive: true);

    private Fixture CreateFixture(OperatorConsoleSessionReadModel session, bool accessAllowed = true)
    {
        var provider = DataProtectionProvider.Create(_keyDirectory, configuration =>
            configuration.SetApplicationName("ExitPass.CentralPms.Tests"));
        var sessions = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        sessions.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var accessEvaluation = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessEvaluation.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessAllowed
                ? AllowedAccess()
                : AllowedAccess() with
                {
                    Allowed = false,
                    Decision = "DENIED",
                    DenialReasons = ["DIRECT_SITE_SCOPE_REQUIRED"],
                    SiteContext = new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: false)
                });
        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with { Persisted = true });
        var storage = Substitute.For<IStatutoryEvidenceProtectedObjectStorageAdapter>();
        StatutoryEvidenceObjectUploadRequest? upload = null;
        storage.UploadObjectAsync(Arg.Do<StatutoryEvidenceObjectUploadRequest>(request => upload = request), Arg.Any<CancellationToken>())
            .Returns(new StatutoryEvidenceObjectUploadResult("ACCEPTED", Retryable: false));

        var service = new OperatorConsoleStatutoryIdPhotoService(
            provider,
            sessions,
            accessEvaluation,
            accessWriter,
            storage,
            Options.Create(new StatutoryEvidenceUploadOptions
            {
                BucketName = "statutory-evidence",
                EnvironmentPartition = "test",
                MaxContentLengthBytes = 5 * 1024 * 1024
            }),
            Options.Create(new OperatorConsoleStatutoryIdPhotoOptions { ReceiptLifetimeMinutes = 15 }),
            TimeProvider.System);
        return new Fixture(service, accessEvaluation, () => upload);
    }

    private static OperatorConsoleStatutoryIdPhotoUploadCommand Command(string entitlementType, long contentLength) => new(
        UserId,
        OperatorDeviceBindingId: null,
        SiteId,
        SiteGroupId,
        OperatorShiftId: null,
        ParkingSessionId,
        entitlementType,
        "image/jpeg",
        contentLength,
        CorrelationId);

    private static OperatorConsoleSessionReadModel Session(
        string? paymentConfirmationStatus = null,
        string? exitAuthorizationStatus = null) => new(
        ParkingSessionId,
        "PHOTO-TEST-001",
        "PHOTO 001",
        SiteId,
        SiteGroupId,
        "ACTIVE",
        DateTimeOffset.Parse("2026-09-26T08:00:00Z"),
        CurrentPayableAmountMinorUnits: 10000,
        CurrencyCode: "PHP",
        PaymentStatus: "UNPAID",
        DiscountStatus: "NOT_APPLIED",
        exitAuthorizationStatus,
        PaymentConfirmationStatus: paymentConfirmationStatus);

    private static OperatorConsoleAccessEvaluationResult AllowedAccess() => new(
        Guid.NewGuid(),
        Allowed: true,
        "ALLOWED",
        Array.Empty<string>(),
        "SITE_OPERATOR",
        new OperatorConsoleDeviceTrustResult(null, "NOT_REQUIRED", "HUMAN_SESSION", Trusted: true),
        new OperatorConsoleShiftContextResult(null, "NOT_REQUIRED", Active: true),
        new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: true),
        DateTimeOffset.Parse("2026-09-26T08:00:00Z"),
        Persisted: false,
        CorrelationId,
        new OperatorConsoleAccessEvaluationPersistenceContext(
            UserId,
            HrIdentityMappingId: null,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            ShiftTakeoverId: null,
            SiteGroupId,
            SiteId,
            OperatorConsoleActionCodes.CaptureEvidence,
            OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow,
            "PARKING_SESSION",
            ParkingSessionId));

    private sealed class Fixture(
        OperatorConsoleStatutoryIdPhotoService service,
        IOperatorConsoleAccessEvaluationService accessEvaluation,
        Func<StatutoryEvidenceObjectUploadRequest?> getUpload)
    {
        public OperatorConsoleStatutoryIdPhotoService Service { get; } = service;
        public IOperatorConsoleAccessEvaluationService AccessEvaluation { get; } = accessEvaluation;
        public StatutoryEvidenceObjectUploadRequest? Upload => getUpload();
    }
}
