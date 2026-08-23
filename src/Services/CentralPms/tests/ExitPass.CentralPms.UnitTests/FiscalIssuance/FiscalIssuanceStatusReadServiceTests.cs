using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceStatusReadServiceTests
{
    [Fact]
    public async Task GetByReferenceIdAsync_WhenRecorded_ReturnsFiscalDocumentEvidence()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            PosServerFiscalDocumentId = Guid.Parse("deac11e4-fc31-4c40-9a44-da690b9730ef"),
            FiscalDocumentNumber = "SI-00000001-UAT",
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 1,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            ResultClassification = FiscalIssuanceResultClassification.NewlyCreated
        };
        var repository = RepositoryReturning(reference);
        var sut = new FiscalIssuanceStatusReadService(repository);

        var status = await sut.GetByReferenceIdAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        status.Should().NotBeNull();
        status!.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_RECORDED");
        status.ResultClassification.Should().Be("NEWLY_CREATED");
        status.PosServerFiscalDocumentId.Should().Be(reference.PosServerFiscalDocumentId);
        status.FiscalDocumentNumber.Should().Be("SI-00000001-UAT");
    }

    [Fact]
    public async Task GetByReferenceIdAsync_WhenReplayed_ReturnsSafeReplayPosture()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed) with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalDocumentNumber = "SI-00000001-UAT",
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 1,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            ResultClassification = FiscalIssuanceResultClassification.IdempotentReplay
        };
        var sut = new FiscalIssuanceStatusReadService(RepositoryReturning(reference));

        var status = await sut.GetByReferenceIdAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        status.Should().NotBeNull();
        status!.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_REPLAYED");
        status.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        status.FiscalDocumentNumber.Should().Be("SI-00000001-UAT");
    }

    [Fact]
    public async Task GetByReferenceIdAsync_WhenConflict_ReturnsConflictPostureWithoutFiscalDocumentNumber()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceConflict) with
        {
            PosServerFiscalDocumentId = null,
            FiscalDocumentNumber = null,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.NotAssigned,
            LatestExceptionReason = FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict,
            LatestErrorCode = "fiscal_document_idempotency_conflict",
            LatestErrorPosture = FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange
        };
        var sut = new FiscalIssuanceStatusReadService(RepositoryReturning(reference));

        var status = await sut.GetByReferenceIdAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        status.Should().NotBeNull();
        status!.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_CONFLICT");
        status.LatestExceptionReason.Should().Be("FISCAL_DOCUMENT_IDEMPOTENCY_CONFLICT");
        status.LatestErrorCode.Should().Be("fiscal_document_idempotency_conflict");
        status.LatestErrorPosture.Should().Be("DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE");
        status.PosServerFiscalDocumentId.Should().BeNull();
        status.FiscalDocumentNumber.Should().BeNull();
    }

    [Fact]
    public async Task GetByReferenceIdAsync_WhenFailedService_ReturnsSafeErrorPosture()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService) with
        {
            LatestExceptionReason = FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            LatestErrorCode = "get_readback_service_failed",
            LatestErrorPosture = FiscalIssuanceErrorPosture.RetryAfterServiceRecovery
        };
        var sut = new FiscalIssuanceStatusReadService(RepositoryReturning(reference));

        var status = await sut.GetByReferenceIdAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        status.Should().NotBeNull();
        status!.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_FAILED_SERVICE");
        status.LatestExceptionReason.Should().Be("GET_READBACK_SERVICE_FAILED");
        status.LatestErrorCode.Should().Be("get_readback_service_failed");
        status.LatestErrorPosture.Should().Be("RETRY_AFTER_SERVICE_RECOVERY");
    }

    [Fact]
    public async Task GetByReferenceIdAsync_WhenMissing_ReturnsNull()
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalIssuanceReferenceIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((FiscalIssuanceReferenceRecord?)null);
        var sut = new FiscalIssuanceStatusReadService(repository);

        var status = await sut.GetByReferenceIdAsync(Guid.NewGuid(), CancellationToken.None);

        status.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_WhenGuid_UsesReferenceLookup()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            FiscalIssuanceReferenceId = Guid.Parse("64000000-0000-0000-0000-000000000001"),
            FiscalDocumentNumber = "SI-00000001-UAT"
        };
        var sut = new FiscalIssuanceStatusReadService(RepositoryReturning(reference));

        var result = await sut.LookupAsync(reference.FiscalIssuanceReferenceId.ToString("D"), CancellationToken.None);

        result.Outcome.Should().Be(FiscalIssuanceStatusLookupOutcome.Found);
        result.Status.Should().NotBeNull();
        result.Status!.FiscalIssuanceReferenceId.Should().Be(reference.FiscalIssuanceReferenceId);
    }

    [Fact]
    public async Task LookupAsync_WhenFiscalDocumentNumber_ResolvesExactNumber()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            FiscalIssuanceReferenceId = Guid.Parse("64000000-0000-0000-0000-000000000002"),
            FiscalDocumentNumber = "SI-OCVOID-0001-UAT"
        };
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalDocumentNumberAsync("SI-OCVOID-0001-UAT", Arg.Any<CancellationToken>())
            .Returns([reference]);
        var sut = new FiscalIssuanceStatusReadService(repository);

        var result = await sut.LookupAsync(" SI-OCVOID-0001-UAT ", CancellationToken.None);

        result.Outcome.Should().Be(FiscalIssuanceStatusLookupOutcome.Found);
        result.Status.Should().NotBeNull();
        result.Status!.FiscalIssuanceReferenceId.Should().Be(reference.FiscalIssuanceReferenceId);
        result.Status.FiscalDocumentNumber.Should().Be("SI-OCVOID-0001-UAT");
    }

    [Fact]
    public async Task LookupAsync_WhenFiscalDocumentNumberMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalDocumentNumberAsync("SI-MISSING-UAT", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<FiscalIssuanceReferenceRecord>());
        var sut = new FiscalIssuanceStatusReadService(repository);

        var result = await sut.LookupAsync("SI-MISSING-UAT", CancellationToken.None);

        result.Outcome.Should().Be(FiscalIssuanceStatusLookupOutcome.NotFound);
        result.SafeReasonCode.Should().Be("fiscal_document_number_not_found");
        result.Status.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_WhenFiscalDocumentNumberAmbiguous_ReturnsAmbiguous()
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalDocumentNumberAsync("SI-DUP-UAT", Arg.Any<CancellationToken>())
            .Returns([
                Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
                {
                    FiscalIssuanceReferenceId = Guid.Parse("64000000-0000-0000-0000-000000000003")
                },
                Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
                {
                    FiscalIssuanceReferenceId = Guid.Parse("64000000-0000-0000-0000-000000000004")
                }
            ]);
        var sut = new FiscalIssuanceStatusReadService(repository);

        var result = await sut.LookupAsync("SI-DUP-UAT", CancellationToken.None);

        result.Outcome.Should().Be(FiscalIssuanceStatusLookupOutcome.Ambiguous);
        result.SafeReasonCode.Should().Be("fiscal_document_number_ambiguous");
        result.Status.Should().BeNull();
    }

    [Fact]
    public async Task GetByReferenceIdAsync_DoesNotMutateFiscalOrPaymentExitGateState()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalDocumentNumber = "SI-00000001-UAT",
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 1,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned
        };
        var repository = RepositoryReturning(reference);
        var sut = new FiscalIssuanceStatusReadService(repository);

        await sut.GetByReferenceIdAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        await repository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await repository.DidNotReceiveWithAnyArgs().UpdateStateAsync(default, default!, default);
        await repository.DidNotReceiveWithAnyArgs().RecordSemanticRequestHashAsync(default, default!, default, default);
    }

    [Fact]
    public async Task GetByReferenceIdAsync_WhenPosReadReturnsVoidedDocument_ExposesSafeVoidPosture()
    {
        var posDocumentId = Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395");
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            PosServerFiscalDocumentId = posDocumentId,
            FiscalDocumentNumber = "SI-00000002-UAT",
            FiscalSequenceValue = 2,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned
        };
        var posRead = Substitute.For<IPosServerFiscalDocumentClient>();
        posRead.GetFiscalDocumentAsync(
                posDocumentId,
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new PosServerFiscalDocumentReadResult(
                Outcome: PosServerFiscalDocumentOutcome.Accepted,
                Succeeded: true,
                HttpStatusCode: 200,
                Code: "found",
                Message: "Fiscal document found.",
                FiscalDocumentId: posDocumentId,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                FiscalDocumentStatusCodeId: reference.FiscalDocumentStatusCodeId,
                FiscalDocumentStatusCodeKey: "voided",
                FiscalSequenceValue: 2,
                FiscalDocumentNumber: "SI-00000002-UAT",
                VoidStatus: "recorded",
                VoidReasonCode: "operator_error",
                VoidedAt: DateTimeOffset.Parse("2026-07-09T16:45:00Z")));
        var sut = new FiscalIssuanceStatusReadService(RepositoryReturning(reference), posRead);

        var status = await sut.GetByReferenceIdAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        status.Should().NotBeNull();
        status!.PosServerFiscalDocumentReadStatus.Should().Be("AVAILABLE");
        status.PosServerFiscalDocumentStatusCodeKey.Should().Be("voided");
        status.PosServerVoidStatus.Should().Be("recorded");
        status.PosServerVoidReasonCode.Should().Be("operator_error");
        status.PosServerVoidedAt.Should().Be(DateTimeOffset.Parse("2026-07-09T16:45:00Z"));
    }

    [Fact]
    public void StatusReadService_UsesOnlyReadOnlyPosServerStatusDependency()
    {
        var constructorParameters = typeof(FiscalIssuanceStatusReadService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().Contain(parameter => parameter == nameof(IFiscalIssuanceReferenceRepository));
        constructorParameters.Should().Contain(parameter => parameter == nameof(IPosServerFiscalDocumentClient));
        constructorParameters.Should().NotContain(parameter =>
            parameter.Contains("Retry", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Gate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FiscalIssuanceStatusEndpoint_IsReadOnlyAndDoesNotWirePosServerOrRetryExecution()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Api",
            "Endpoints",
            "FiscalIssuanceStatusEndpoints.cs");

        source.Should().Contain("MapGet(\"/references/{fiscalIssuanceReferenceId:guid}\"");
        source.Should().NotContain("MapPost");
        source.Should().NotContain("IPosServer");
        source.Should().NotContain("RetryExecution");
        source.Should().NotContain("UpdateStateAsync");
        source.Should().NotContain("CreateAsync");
    }

    private static IFiscalIssuanceReferenceRepository RepositoryReturning(FiscalIssuanceReferenceRecord reference)
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalIssuanceReferenceIdAsync(reference.FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns(reference);

        return repository;
    }

    private static FiscalIssuanceReferenceRecord Reference(FiscalIssuanceIntegrationState state)
    {
        var now = DateTimeOffset.Parse("2026-07-08T10:00:00+08:00");
        return new FiscalIssuanceReferenceRecord(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
            PosServerFiscalDocumentId: null,
            FiscalIdentityId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: "central-pms-uat-si-sequence-policy",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid(),
            FiscalDocumentTypeCodeId: Guid.NewGuid(),
            FiscalDocumentTypeCodeKey: "sales_invoice",
            SemanticRequestHashStatus: FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue: "ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d",
            SemanticRequestHashAlgorithm: "SHA-256",
            SemanticRequestHashSourceVersion: "sha256:v1",
            SemanticRequestHashSourceFactCount: 24,
            SemanticRequestHashSafeSummary: "safe fiscal request facts",
            SemanticRequestHashRecordedAt: now);
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }
}
