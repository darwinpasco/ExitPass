using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceControlledUatVoidSmokeServiceTests
{
    [Fact]
    public async Task RunAsync_WhenApprovedDocumentIsRecorded_RecordsVoidSmokePosture()
    {
        var store = Substitute.For<IControlledUatFiscalVoidSmokeStore>();
        store.RecordApprovedVoidPostureAsync(
                Arg.Any<ControlledUatFiscalVoidSmokeStoreRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(RecordedStoreResult());

        var response = await CreateSut(store: store)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be("controlled_uat_void_smoke_recorded");
        response.FiscalDocumentStatusPosture.Should().Be("CONTROLLED_UAT_VOID_SMOKE_RECORDED");
        response.FiscalDocumentNumber.Should().Be("SI-00000002-UAT");
        response.FiscalSequenceValue.Should().Be(2);
        response.NewFiscalNumberAllocated.Should().BeFalse();
        response.PaymentFinalityChanged.Should().BeFalse();
        response.ExitAuthorizationIssued.Should().BeFalse();
        response.GateBehaviorTriggered.Should().BeFalse();
        response.RefundOrReversalCreated.Should().BeFalse();
        response.HikCentralCalled.Should().BeFalse();
        response.PaymentProviderCalled.Should().BeFalse();
        response.RenderingGenerated.Should().BeFalse();

        await store.Received(1).RecordApprovedVoidPostureAsync(
            Arg.Is<ControlledUatFiscalVoidSmokeStoreRequest>(request =>
                request.ProfileId == "CPS-POS-UAT-20260709-DEV-ATC-001" &&
                request.FiscalIssuanceReferenceId == Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf") &&
                request.PosServerFiscalDocumentId == Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395") &&
                request.FiscalDocumentNumber == "SI-00000002-UAT" &&
                request.PaymentFinalityRef == "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001" &&
                request.ReasonCode == "CONTROLLED_UAT_VOID_SMOKE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenFiscalDocumentIsUnknown_RejectsBeforeStore()
    {
        var store = Substitute.For<IControlledUatFiscalVoidSmokeStore>();

        var response = await CreateSut(store: store)
            .RunAsync(ApprovedRequest() with { PosServerFiscalDocumentId = Guid.NewGuid() }, CancellationToken.None);

        response.HttpStatusCode.Should().Be(400);
        response.Errors.Should().Contain("pos_server_fiscal_document_id_not_approved");
        response.NewFiscalNumberAllocated.Should().BeFalse();
        await store.DidNotReceiveWithAnyArgs().RecordApprovedVoidPostureAsync(default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenReferenceDoesNotMatchApprovedDocument_FailsClosed()
    {
        var store = Substitute.For<IControlledUatFiscalVoidSmokeStore>();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalIssuanceReferenceIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(RecordedReference() with { PosServerFiscalDocumentId = Guid.NewGuid() });

        var response = await CreateSut(repository: repository, store: store)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("pos_server_fiscal_document_id_mismatch");
        response.NewFiscalNumberAllocated.Should().BeFalse();
        await store.DidNotReceiveWithAnyArgs().RecordApprovedVoidPostureAsync(default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenPostureAlreadyRecorded_ReturnsIdempotentSuccess()
    {
        var store = Substitute.For<IControlledUatFiscalVoidSmokeStore>();
        store.RecordApprovedVoidPostureAsync(
                Arg.Any<ControlledUatFiscalVoidSmokeStoreRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(RecordedStoreResult() with { AlreadyRecorded = true, StatusHistoryRecorded = false });

        var response = await CreateSut(store: store)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be("controlled_uat_void_smoke_already_recorded");
        response.IdempotentReplay.Should().BeTrue();
        response.StatusHistoryRecorded.Should().BeFalse();
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenPersistenceIsNotConfigured_FailsClosed()
    {
        var response = await CreateSut(store: new PersistenceNotConfiguredControlledUatFiscalVoidSmokeStore())
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Status.Should().Be("controlled_uat_void_smoke_pos_persistence_not_configured");
        response.Errors.Should().Contain("pos_server_connection_string_missing");
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenStoreThrows_FailsSafelyWithoutLeakingExceptionDetails()
    {
        var store = Substitute.For<IControlledUatFiscalVoidSmokeStore>();
        store.RecordApprovedVoidPostureAsync(
                Arg.Any<ControlledUatFiscalVoidSmokeStoreRequest>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ControlledUatFiscalVoidSmokeStoreResult>>(_ =>
                throw new InvalidOperationException("database exception detail"));

        var response = await CreateSut(store: store)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Status.Should().Be("controlled_uat_void_smoke_failed_safely");
        response.Errors.Should().Equal("controlled_uat_void_smoke_failed_safely");
        response.Errors.Should().NotContain(error => error.Contains("database", StringComparison.OrdinalIgnoreCase));
        response.NewFiscalNumberAllocated.Should().BeFalse();
        response.PaymentFinalityChanged.Should().BeFalse();
        response.ExitAuthorizationIssued.Should().BeFalse();
        response.GateBehaviorTriggered.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DoesNotWireForbiddenOperationalDependencies()
    {
        var constructorTypes = typeof(FiscalIssuanceControlledUatVoidSmokeService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorTypes.Should().NotContain(typeof(IPosServerFiscalDocumentClient));
        constructorTypes.Should().NotContain(type => type.Name.Contains("PaymentProvider", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Gate", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Render", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalIssuanceControlledUatVoidSmokeService CreateSut(
        IFiscalIssuanceReferenceRepository? repository = null,
        IControlledUatFiscalVoidSmokeStore? store = null,
        FiscalIssuancePosServerIntegrationOptions? options = null,
        FiscalIssuanceExitAuthorizationGatingOptions? gatingOptions = null)
    {
        var resolvedRepository = repository ?? Substitute.For<IFiscalIssuanceReferenceRepository>();
        if (repository is null)
        {
            resolvedRepository.FindByFiscalIssuanceReferenceIdAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(RecordedReference());
        }

        return new FiscalIssuanceControlledUatVoidSmokeService(
            resolvedRepository,
            store ?? Substitute.For<IControlledUatFiscalVoidSmokeStore>(),
            Options.Create(options ?? EnabledOptions()),
            Options.Create(gatingOptions ?? new FiscalIssuanceExitAuthorizationGatingOptions()),
            Substitute.For<ILogger<FiscalIssuanceControlledUatVoidSmokeService>>());
    }

    private static FiscalIssuancePosServerIntegrationOptions EnabledOptions() =>
        new()
        {
            EnableControlledUatDiagnosticPath = true,
            EnablePosServerFiscalIssuanceLiveCall = true,
            PosServerBaseUrl = "http://localhost:5000",
            EnableLiveFiscalIssuanceFromPaymentFlow = false,
            EnableLiveFiscalIssuanceFromExitFlow = false
        };

    private static ControlledUatFiscalVoidSmokeRequest ApprovedRequest() =>
        new(
            ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            FiscalIssuanceReferenceId: Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf"),
            PosServerFiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalDocumentNumber: "SI-00000002-UAT",
            ReasonCode: "CONTROLLED_UAT_VOID_SMOKE",
            CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
            ApprovedBy: "Darwin Pasco",
            ExplicitExecutionApproval: true);

    private static ControlledUatFiscalVoidSmokeStoreResult RecordedStoreResult() =>
        new(
            Succeeded: true,
            Status: "controlled_uat_void_smoke_recorded",
            Errors: Array.Empty<string>(),
            FiscalDocumentNumber: "SI-00000002-UAT",
            FiscalSequenceValue: 2,
            FiscalDocumentStatusPosture: "CONTROLLED_UAT_VOID_SMOKE_RECORDED",
            StatusHistoryRecorded: true,
            AlreadyRecorded: false);

    private static FiscalIssuanceReferenceRecord RecordedReference() =>
        new(
            FiscalIssuanceReferenceId: Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf"),
            PaymentConfirmationId: Guid.Parse("00000000-0000-4000-8000-000000000301"),
            PaymentAttemptId: Guid.Parse("00000000-0000-4000-8000-000000000302"),
            ParkingSessionId: Guid.Parse("00000000-0000-4000-8000-000000000303"),
            TariffSnapshotId: null,
            SiteId: Guid.Parse("00000000-0000-4000-8000-000000000402"),
            SitePosServerId: Guid.Parse("10000000-0000-4000-8000-000000000201"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            PosServerFiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalIdentityId: Guid.Parse("10000000-0000-4000-8000-000000000301"),
            FiscalSequencePolicyId: Guid.Parse("10000000-0000-4000-8000-000000000401"),
            FiscalSequenceValue: 2,
            FiscalDocumentNumber: "SI-00000002-UAT",
            FiscalSeries: "central-pms-uat-si-sequence-policy",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-09T09:33:57.142508+00:00"),
            FiscalNumberAssignedByRef: "pos-server:system",
            FiscalDocumentStatusCodeId: Guid.Parse("10000000-0000-4000-8000-000000000107"),
            ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.Parse("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df"),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.UtcNow,
            LastUpdatedAt: DateTimeOffset.UtcNow,
            RecordedByServiceIdentityId: null);
}
