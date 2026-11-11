using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalReporting;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class PostgresFiscalBusinessDateAdmissionGateIntegrationTests(
    StatutoryDiscountCanonicalDatabaseFixture database)
{
    [Theory]
    [InlineData("REQUESTED")]
    [InlineData("PENDING_PROVIDER")]
    [InlineData("PENDING_FINALIZATION")]
    public async Task EnsureSafeToClose_UnverifiedAttempt_DoesNotBlock(string attemptStatus)
    {
        var scenario = $"fiscal-close-{attemptStatus.ToLowerInvariant()}";
        var (context, _) = await SeedAttemptAsync(scenario, attemptStatus);

        try
        {
            await AssertSafeToCloseAsync(context.SiteId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(database.ConnectionString, context);
        }
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReconciled)]
    public async Task EnsureSafeToClose_ConfirmedAttemptWithCompletedFiscalIssuance_DoesNotBlock(
        FiscalIssuanceIntegrationState fiscalState)
    {
        var (context, attempt) = await SeedAttemptAsync($"fiscal-close-{fiscalState}", "REQUESTED");

        try
        {
            var confirmation = await ConfirmAttemptAsync(context, attempt);
            await CreateFiscalReferenceAsync(context, attempt, confirmation, fiscalState);

            await AssertSafeToCloseAsync(context.SiteId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(database.ConnectionString, context);
        }
    }

    [Fact]
    public async Task EnsureSafeToClose_ConfirmedAttemptWithoutFiscalIssuance_Blocks()
    {
        var (context, attempt) = await SeedAttemptAsync("fiscal-close-confirmed-without-fiscal", "REQUESTED");

        try
        {
            await ConfirmAttemptAsync(context, attempt);

            await AssertUnsafeToCloseAsync(context.SiteId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(database.ConnectionString, context);
        }
    }

    [Fact]
    public async Task EnsureSafeToClose_NonterminalFiscalIssuanceReference_Blocks()
    {
        var (context, attempt) = await SeedAttemptAsync("fiscal-close-pending-fiscal", "REQUESTED");

        try
        {
            var confirmation = await ConfirmAttemptAsync(context, attempt);
            await CreateFiscalReferenceAsync(
                context,
                attempt,
                confirmation,
                FiscalIssuanceIntegrationState.PendingFiscalIssuance);

            await AssertUnsafeToCloseAsync(context.SiteId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(database.ConnectionString, context);
        }
    }

    [Fact]
    public async Task EnsureSafeToClose_AllPaymentsFiscallyRecorded_DoesNotBlock()
    {
        var (context, attempt) = await SeedAttemptAsync("fiscal-close-all-recorded", "REQUESTED");

        try
        {
            var confirmation = await ConfirmAttemptAsync(context, attempt);
            await CreateFiscalReferenceAsync(
                context,
                attempt,
                confirmation,
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);

            await AssertSafeToCloseAsync(context.SiteId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(database.ConnectionString, context);
        }
    }

    private async Task<(PaymentTestContext Context, CreateAttemptResult Attempt)> SeedAttemptAsync(
        string scenario,
        string attemptStatus)
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(database.ConnectionString);
        var context = PaymentTestContext.Create(scenario);
        await PaymentTestDataHelper.ResetAndSeedAsync(
            database.ConnectionString,
            context,
            $"Fiscal business-date close guard integration: {scenario}");
        var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
            database.ConnectionString,
            context,
            $"close-guard-{Guid.NewGuid():N}",
            "fiscal-close-guard-test");

        if (!string.Equals(attemptStatus, "REQUESTED", StringComparison.Ordinal))
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                UPDATE core.payment_attempts
                SET attempt_status=CAST(@attempt_status AS core.payment_attempt_status_enum),
                    updated_at=current_timestamp,
                    row_version=row_version + 1
                WHERE payment_attempt_id=@payment_attempt_id;
                """,
                connection);
            command.Parameters.AddWithValue("attempt_status", attemptStatus);
            command.Parameters.AddWithValue("payment_attempt_id", attempt.PaymentAttemptId);
            await command.ExecuteNonQueryAsync();
        }

        return (context, attempt);
    }

    private async Task<RecordPaymentConfirmationResult> ConfirmAttemptAsync(
        PaymentTestContext context,
        CreateAttemptResult attempt)
    {
        var confirmation = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
            database.ConnectionString,
            attempt.PaymentAttemptId,
            $"CLOSE-GUARD-{Guid.NewGuid():N}",
            "fiscal-close-guard-test",
            context.CorrelationId);
        Assert.NotNull(confirmation);
        return confirmation!;
    }

    private async Task CreateFiscalReferenceAsync(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation,
        FiscalIssuanceIntegrationState fiscalState)
    {
        var completed = fiscalState is
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded or
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed or
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled;
        var sequence = Random.Shared.Next(100000, 999999);
        var request = new CreateFiscalIssuanceReferenceRequest(
            PaymentConfirmationId: confirmation.PaymentConfirmationId,
            PaymentAttemptId: attempt.PaymentAttemptId,
            ParkingSessionId: context.ParkingSessionId,
            TariffSnapshotId: context.TariffSnapshotId,
            SiteId: context.SiteId,
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: $"site-pos-{context.SiteCode}",
            FiscalDocumentTypeCodeId: Guid.NewGuid(),
            FiscalDocumentTypeCodeKey: "SALES_INVOICE",
            PayableBasisRef: context.TariffSnapshotId.ToString("N"),
            UpstreamFinalityReference: $"close-guard-{confirmation.PaymentConfirmationId:N}",
            PosServerFiscalDocumentId: completed ? Guid.NewGuid() : null,
            FiscalIdentityId: completed ? Guid.NewGuid() : null,
            FiscalSequencePolicyId: completed ? Guid.NewGuid() : null,
            FiscalSequenceValue: completed ? sequence : null,
            FiscalDocumentNumber: completed ? $"SI-{sequence}" : null,
            FiscalSeries: completed ? "SI" : null,
            FiscalNumberPrefixText: completed ? "SI-" : null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: completed ? DateTimeOffset.UtcNow : null,
            FiscalNumberAssignedByRef: completed ? "pos-server-close-guard-test" : null,
            FiscalDocumentStatusCodeId: completed ? Guid.NewGuid() : null,
            ResultClassification: completed ? FiscalIssuanceResultClassification.NewlyCreated : null,
            FiscalIssuanceEvidenceStatus: completed ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned : null,
            FiscalNumberAssignmentState: completed ? FiscalNumberAssignmentState.Assigned : FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: fiscalState,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: context.CorrelationId,
            PosServerResponseTimestamp: completed ? DateTimeOffset.UtcNow : null,
            RecordedByServiceIdentityId: context.RequestedByUserId);

        var repository = new PostgresFiscalIssuanceReferenceRepository(database.ConnectionString);
        await repository.CreateAsync(request, CancellationToken.None);
    }

    private async Task AssertSafeToCloseAsync(Guid siteId)
    {
        var gate = CreateGate();
        await using var lease = await gate.AcquireTransitionAsync(siteId, CancellationToken.None);
        await lease.EnsureSafeToCloseAsync(CancellationToken.None);
    }

    private async Task AssertUnsafeToCloseAsync(Guid siteId)
    {
        var gate = CreateGate();
        await using var lease = await gate.AcquireTransitionAsync(siteId, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<FiscalBusinessDateAdmissionException>(
            () => lease.EnsureSafeToCloseAsync(CancellationToken.None));
        Assert.Equal(FiscalBusinessDateAdmissionCodes.UnsafeToClose, exception.Code);
    }

    private PostgresFiscalBusinessDateAdmissionGate CreateGate()
    {
        var openedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        return new PostgresFiscalBusinessDateAdmissionGate(
            database.ConnectionString,
            new OpenLifecycleGateway(openedAt));
    }

    private sealed class OpenLifecycleGateway(DateTimeOffset openedAt) : IFiscalReportingPosServerGateway
    {
        public Task<FiscalReportingGatewayResponse> SendAsync(
            FiscalReportingGatewayRequest request,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(FiscalReportingGatewayAction.BusinessDateLifecycleRead, request.Action);
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                lifecycle = new
                {
                    state = "OPEN",
                    activePeriod = new { openedAt }
                }
            });
            return Task.FromResult(new FiscalReportingGatewayResponse(200, "application/json", body, "OK"));
        }
    }
}
