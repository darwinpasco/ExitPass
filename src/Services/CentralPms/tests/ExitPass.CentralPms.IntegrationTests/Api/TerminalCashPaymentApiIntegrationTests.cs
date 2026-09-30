using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Infrastructure.TerminalCashPayments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Focused API tests for terminal cash-payment command acceptance and durable readback.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class TerminalCashPaymentApiIntegrationTests
{
    private const string Route = "/v1/terminal-cash-payments";
    private const string SemanticHashSourceVersion = "terminal-cash-payment:sha256:v1";
    private static readonly SemaphoreSlim PatchSemaphore = new(1, 1);
    private static bool patchApplied;

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task TerminalCashPayment_WithValidRequest_CreatesCashConfirmationWithoutFiscalExitOrProviderSideEffects()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithValidRequest_CreatesCashConfirmationWithoutFiscalExitOrProviderSideEffects));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment success.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context, amountDueMinorUnits: 10_000, amountTenderedMinorUnits: 12_000, changeDueMinorUnits: 2_000);

            using var response = await SendCreateAsync(client, request, idempotencyKey: $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await ReadJsonAsync<TerminalCashPaymentResponse>(response);
            Assert.Equal(request.TerminalCashTenderId, body.TerminalCashTenderId);
            Assert.Equal("CONFIRMED", body.CanonicalPaymentStatus);
            Assert.Equal("CREATED", body.ResultClassification);
            Assert.Equal(SemanticHashSourceVersion, body.SemanticHashSourceVersion);
            Assert.Equal("NOT_STARTED_IN_THIS_SLICE", body.FiscalStatus);

            var counts = await ReadCountsAsync(context);
            Assert.Equal(1, counts.TerminalCashCommandCount);
            Assert.Equal(1, counts.PaymentAttemptCount);
            Assert.Equal(1, counts.PaymentConfirmationCount);
            Assert.Equal(0, counts.ProviderSessionCount);
            Assert.Equal(0, counts.ProviderOutcomeCount);
            Assert.Equal(0, counts.FiscalReferenceCount);
            Assert.Equal(0, counts.ExitAuthorizationCount);

            var railCode = await ReadScalarAsync<string>(
                """
                SELECT pr.rail_code
                FROM core.payment_attempts pa
                INNER JOIN payments.payment_rails pr ON pr.payment_rail_id = pa.payment_rail_id
                WHERE pa.payment_attempt_id = @payment_attempt_id;
                """,
                ("payment_attempt_id", NpgsqlDbType.Uuid, body.PaymentAttemptId));
            Assert.Equal("CASH", railCode);

            using var readback = await client.GetAsync($"{Route}/references/{request.TerminalCashTenderId}");
            Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
            var readbackBody = await ReadJsonAsync<TerminalCashPaymentReadbackResponse>(readback);
            Assert.Equal(body.PaymentConfirmationId, readbackBody.PaymentConfirmationId);
            Assert.Equal("CONFIRMED", readbackBody.CanonicalPaymentStatus);
            Assert.Equal("NOT_STARTED_IN_THIS_SLICE", readbackBody.FiscalStatus);

            var authoritativeReadback = await new TerminalCashPaymentRepository(ConnectionString)
                .GetByTerminalCashTenderIdAsync(request.TerminalCashTenderId, CancellationToken.None);
            Assert.NotNull(authoritativeReadback);
            Assert.Equal(
                request.CashReceivedAt.ToUnixTimeMilliseconds(),
                authoritativeReadback!.CashReceivedAt!.Value.ToUnixTimeMilliseconds());
            Assert.Equal($"TEST-SITE-{context.SiteId:N}", authoritativeReadback.SiteName);
            Assert.Equal($"TICKET-{context.ParkingSessionId:N}", authoritativeReadback.TicketNumber);
            Assert.Equal("ABC1234", authoritativeReadback.PlateNumber);
            Assert.NotNull(authoritativeReadback.EntryTime);
            Assert.False(authoritativeReadback.ExitAuthorizationIssued);

            var authorization = await PaymentRoutineTestHelper.IssueExitAuthorizationAsync(
                ConnectionString,
                context.ParkingSessionId,
                body.PaymentAttemptId,
                context.RequestedByUserId,
                context.CorrelationId);
            Assert.NotNull(authorization);
            Assert.Equal("ISSUED", authorization!.AuthorizationStatus);

            var issuedReadback = await new TerminalCashPaymentRepository(ConnectionString)
                .GetByTerminalCashTenderIdAsync(request.TerminalCashTenderId, CancellationToken.None);
            Assert.NotNull(issuedReadback);
            Assert.True(issuedReadback!.ExitAuthorizationIssued);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSameIdempotencyKeyAndPayload_ReturnsReplayAndDoesNotDuplicateConfirmation()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSameIdempotencyKeyAndPayload_ReturnsReplayAndDoesNotDuplicateConfirmation));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment replay.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context);
            var key = $"cash-{Guid.NewGuid():N}";

            using var first = await SendCreateAsync(client, request, key, context.CorrelationId);
            var firstBody = await ReadJsonAsync<TerminalCashPaymentResponse>(first);

            using var second = await SendCreateAsync(client, request, key, Guid.NewGuid());
            var secondBody = await ReadJsonAsync<TerminalCashPaymentResponse>(second);

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(firstBody.PaymentConfirmationId, secondBody.PaymentConfirmationId);
            Assert.Equal(firstBody.PaymentAttemptId, secondBody.PaymentAttemptId);
            Assert.Equal("IDEMPOTENT_REPLAY", secondBody.ResultClassification);

            var counts = await ReadCountsAsync(context);
            Assert.Equal(1, counts.TerminalCashCommandCount);
            Assert.Equal(1, counts.PaymentAttemptCount);
            Assert.Equal(1, counts.PaymentConfirmationCount);
            Assert.Equal(1, await CountAuditsAsync(request.TerminalCashTenderId, "IDEMPOTENT_REPLAY"));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSameIdempotencyKeyAndDifferentPayload_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSameIdempotencyKeyAndDifferentPayload_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment idempotency conflict.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context);
            var key = $"cash-{Guid.NewGuid():N}";
            using var first = await SendCreateAsync(client, request, key, context.CorrelationId);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            var changed = request with
            {
                AmountTenderedMinorUnits = request.AmountTenderedMinorUnits + 100,
                ChangeDueMinorUnits = request.ChangeDueMinorUnits + 100
            };

            using var second = await SendCreateAsync(client, changed, key, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var error = await ReadJsonAsync<ErrorResponse>(second);
            Assert.Equal("IDEMPOTENCY_SEMANTIC_CONFLICT", error.ErrorCode);
            Assert.Equal(1, await CountAuditsAsync(request.TerminalCashTenderId, "SEMANTIC_CONFLICT"));
            Assert.Equal(1, (await ReadCountsAsync(context)).PaymentConfirmationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSameTenderAndSamePayload_ReturnsExistingResultForDifferentIdempotencyKey()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSameTenderAndSamePayload_ReturnsExistingResultForDifferentIdempotencyKey));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment tender replay.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context);

            using var first = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);
            var firstBody = await ReadJsonAsync<TerminalCashPaymentResponse>(first);

            using var second = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", Guid.NewGuid());
            var secondBody = await ReadJsonAsync<TerminalCashPaymentResponse>(second);

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(firstBody.PaymentConfirmationId, secondBody.PaymentConfirmationId);
            Assert.Equal("IDEMPOTENT_REPLAY", secondBody.ResultClassification);
            Assert.Equal(1, (await ReadCountsAsync(context)).TerminalCashCommandCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSameTenderAndDifferentPayload_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSameTenderAndDifferentPayload_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment tender conflict.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context);
            using var first = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            var changed = request with { LocalEventReference = $"local-{Guid.NewGuid():N}" };
            using var second = await SendCreateAsync(client, changed, $"cash-{Guid.NewGuid():N}", Guid.NewGuid());

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var error = await ReadJsonAsync<ErrorResponse>(second);
            Assert.Equal("DUPLICATE_CASH_TENDER", error.ErrorCode);
            Assert.Equal(1, (await ReadCountsAsync(context)).PaymentConfirmationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithInvalidAmounts_ReturnsBadRequest()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithInvalidAmounts_ReturnsBadRequest));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment invalid amount.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var belowDue = BuildRequest(context) with { AmountTenderedMinorUnits = 9_000, ChangeDueMinorUnits = 0 };
            using var response = await SendCreateAsync(client, belowDue, $"cash-{Guid.NewGuid():N}", context.CorrelationId);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var wrongChange = BuildRequest(context) with { ChangeDueMinorUnits = 1 };
            using var second = await SendCreateAsync(client, wrongChange, $"cash-{Guid.NewGuid():N}", context.CorrelationId);
            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
            Assert.Equal(0, (await ReadCountsAsync(context)).PaymentConfirmationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSnapshotValidAtCashReceivedButExpiredBeforeSubmission_Succeeds()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSnapshotValidAtCashReceivedButExpiredBeforeSubmission_Succeeds));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment historical tariff acceptance.");

        try
        {
            var cashReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await SetSnapshotLifecycleAsync(
                context.TariffSnapshotId,
                "EXPIRED",
                cashReceivedAt.AddMinutes(-10),
                cashReceivedAt.AddMinutes(1));

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { CashReceivedAt = cashReceivedAt };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(1, (await ReadCountsAsync(context)).PaymentConfirmationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithExpiredTariff_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithExpiredTariff_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment expired tariff.");

        try
        {
            var cashReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await SetSnapshotLifecycleAsync(
                context.TariffSnapshotId,
                "EXPIRED",
                cashReceivedAt.AddMinutes(-10),
                cashReceivedAt.AddMilliseconds(-1));

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { CashReceivedAt = cashReceivedAt };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await ReadJsonAsync<ErrorResponse>(response);
            Assert.Equal("STALE_TARIFF", error.ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithInvalidatedTariff_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithInvalidatedTariff_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment invalidated tariff.");

        try
        {
            var cashReceivedAt = DateTimeOffset.UtcNow;
            await SetSnapshotLifecycleAsync(
                context.TariffSnapshotId,
                "INVALIDATED",
                cashReceivedAt.AddMinutes(-10),
                cashReceivedAt.AddMinutes(10));

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { CashReceivedAt = cashReceivedAt };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("STALE_TARIFF", (await ReadJsonAsync<ErrorResponse>(response)).ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithConsumedTariff_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithConsumedTariff_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment consumed tariff.");

        try
        {
            var cashReceivedAt = DateTimeOffset.UtcNow;
            await SetSnapshotLifecycleAsync(
                context.TariffSnapshotId,
                "CONSUMED",
                cashReceivedAt.AddMinutes(-10),
                cashReceivedAt.AddMinutes(10),
                consumedAt: cashReceivedAt.AddMinutes(-1));

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { CashReceivedAt = cashReceivedAt };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("STALE_TARIFF", (await ReadJsonAsync<ErrorResponse>(response)).ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSnapshotSupersededBeforeCashReceived_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSnapshotSupersededBeforeCashReceived_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment unprovable superseded tariff.");

        try
        {
            var cashReceivedAt = DateTimeOffset.UtcNow;
            await SupersedeSnapshotAsync(
                context.TariffSnapshotId,
                Guid.NewGuid(),
                cashReceivedAt.AddMinutes(-10),
                cashReceivedAt.AddMinutes(10),
                cashReceivedAt.AddMinutes(-1));

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { CashReceivedAt = cashReceivedAt };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("STALE_TARIFF", (await ReadJsonAsync<ErrorResponse>(response)).ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSnapshotSupersededAfterCashReceived_Succeeds()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSnapshotSupersededAfterCashReceived_Succeeds));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment historically authoritative superseded tariff.");
        var successorTariffSnapshotId = Guid.NewGuid();

        try
        {
            var cashReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await SupersedeSnapshotAsync(
                context.TariffSnapshotId,
                successorTariffSnapshotId,
                cashReceivedAt.AddMinutes(-10),
                cashReceivedAt.AddMinutes(10),
                cashReceivedAt.AddMinutes(1));

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { CashReceivedAt = cashReceivedAt };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(1, (await ReadCountsAsync(context)).PaymentConfirmationCount);
        }
        finally
        {
            await RemoveSuccessorSnapshotAsync(context.TariffSnapshotId, successorTariffSnapshotId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithPayableBasisMismatch_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithPayableBasisMismatch_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment payable mismatch.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with
            {
                AmountDueMinorUnits = 9_999,
                AmountTenderedMinorUnits = 10_000,
                ChangeDueMinorUnits = 1
            };

            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await ReadJsonAsync<ErrorResponse>(response);
            Assert.Equal("PAYABLE_BASIS_MISMATCH", error.ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithCurrencyMismatch_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithCurrencyMismatch_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment currency mismatch.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { Currency = "USD" };
            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("UNSUPPORTED_CURRENCY", (await ReadJsonAsync<ErrorResponse>(response)).ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSiteOrSiteGroupMismatch_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSiteOrSiteGroupMismatch_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment scope mismatch.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var wrongSite = await SendCreateAsync(
                client,
                BuildRequest(context) with { SiteId = Guid.NewGuid() },
                $"cash-{Guid.NewGuid():N}",
                context.CorrelationId);
            Assert.Equal(HttpStatusCode.Conflict, wrongSite.StatusCode);
            Assert.Equal("INVALID_SESSION_TARIFF_RELATIONSHIP", (await ReadJsonAsync<ErrorResponse>(wrongSite)).ErrorCode);

            using var wrongSiteGroup = await SendCreateAsync(
                client,
                BuildRequest(context) with { SiteGroupId = Guid.NewGuid() },
                $"cash-{Guid.NewGuid():N}",
                Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Conflict, wrongSiteGroup.StatusCode);
            Assert.Equal("INVALID_SESSION_TARIFF_RELATIONSHIP", (await ReadJsonAsync<ErrorResponse>(wrongSiteGroup)).ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WithSessionTariffMismatch_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSessionTariffMismatch_ReturnsConflict));
        var otherContext = PaymentTestContext.Create(nameof(TerminalCashPayment_WithSessionTariffMismatch_ReturnsConflict) + "Other");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment mismatch.");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, otherContext, "Seed terminal cash-payment mismatch other.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var request = BuildRequest(context) with { TariffSnapshotId = otherContext.TariffSnapshotId };

            using var response = await SendCreateAsync(client, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await ReadJsonAsync<ErrorResponse>(response);
            Assert.Equal("INVALID_SESSION_TARIFF_RELATIONSHIP", error.ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, otherContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_WhenPaymentAlreadyFinal_ReturnsConflict()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_WhenPaymentAlreadyFinal_ReturnsConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment already final.");

        try
        {
            var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"attempt-{Guid.NewGuid():N}",
                "terminal-cash-payment-test");
            _ = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                $"provider-{Guid.NewGuid():N}",
                "terminal-cash-payment-test",
                context.CorrelationId);

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            using var response = await SendCreateAsync(client, BuildRequest(context), $"cash-{Guid.NewGuid():N}", context.CorrelationId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await ReadJsonAsync<ErrorResponse>(response);
            Assert.Equal("PAYMENT_ALREADY_FINAL", error.ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_ReadbackSurvivesNewApplicationFactoryAndUnknownTenderReturnsNotFound()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_ReadbackSurvivesNewApplicationFactoryAndUnknownTenderReturnsNotFound));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment readback restart.");

        try
        {
            var request = BuildRequest(context);
            TerminalCashPaymentResponse created;
            using (var firstFactory = new CustomWebApplicationFactory())
            using (var firstClient = firstFactory.CreateClient())
            using (var create = await SendCreateAsync(firstClient, request, $"cash-{Guid.NewGuid():N}", context.CorrelationId))
            {
                created = await ReadJsonAsync<TerminalCashPaymentResponse>(create);
            }

            using var secondFactory = new CustomWebApplicationFactory();
            using var secondClient = secondFactory.CreateClient();
            using var readback = await secondClient.GetAsync($"{Route}/references/{request.TerminalCashTenderId}");
            Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
            var readbackBody = await ReadJsonAsync<TerminalCashPaymentReadbackResponse>(readback);
            Assert.Equal(created.PaymentConfirmationId, readbackBody.PaymentConfirmationId);

            using var missing = await secondClient.GetAsync($"{Route}/references/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task TerminalCashPayment_ConcurrentDuplicateHandling_CreatesSingleConfirmation()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var context = PaymentTestContext.Create(nameof(TerminalCashPayment_ConcurrentDuplicateHandling_CreatesSingleConfirmation));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed terminal cash-payment concurrency.");

        try
        {
            using var factory = new CustomWebApplicationFactory();
            var request = BuildRequest(context);
            var key = $"cash-{Guid.NewGuid():N}";

            var tasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(async () =>
                {
                    using var client = factory.CreateClient();
                    using var response = await SendCreateAsync(client, request, key, context.CorrelationId);
                    return await ReadJsonAsync<TerminalCashPaymentResponse>(response);
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.Single(results.Select(result => result.PaymentConfirmationId).Distinct());
            Assert.Contains(results, result => string.Equals(result.ResultClassification, "CREATED", StringComparison.Ordinal));
            Assert.Contains(results, result => string.Equals(result.ResultClassification, "IDEMPOTENT_REPLAY", StringComparison.Ordinal));
            var counts = await ReadCountsAsync(context);
            Assert.Equal(1, counts.TerminalCashCommandCount);
            Assert.Equal(1, counts.PaymentConfirmationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static TerminalCashPaymentRequest BuildRequest(
        PaymentTestContext context,
        long amountDueMinorUnits = 10_000,
        long amountTenderedMinorUnits = 10_000,
        long changeDueMinorUnits = 0)
    {
        return new TerminalCashPaymentRequest(
            TerminalCashTenderId: Guid.NewGuid(),
            CashCustodySessionId: Guid.NewGuid(),
            ParkingSessionId: context.ParkingSessionId,
            TariffSnapshotId: context.TariffSnapshotId,
            CashierId: "cashier-001",
            CashierSessionReference: $"cashier-session-{Guid.NewGuid():N}",
            CashierShiftId: "shift-001",
            TerminalId: "terminal-001",
            SiteId: context.SiteId,
            SiteGroupId: context.SiteGroupId,
            PosServerId: "pos-server-001",
            Currency: "PHP",
            AmountDueMinorUnits: amountDueMinorUnits,
            AmountTenderedMinorUnits: amountTenderedMinorUnits,
            ChangeDueMinorUnits: changeDueMinorUnits,
            CashReceivedAt: DateTimeOffset.UtcNow,
            DenominationEntries:
            [
                new TerminalCashDenominationEntryDto("PHP100", 10_000, 1)
            ],
            LocalEventReference: $"apt-local-{Guid.NewGuid():N}");
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        TerminalCashPaymentRequest request,
        string idempotencyKey,
        Guid correlationId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(message);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(body);
        return body!;
    }

    private static async Task EnsureTerminalCashPatchAppliedAsync()
    {
        if (patchApplied)
        {
            return;
        }

        await PatchSemaphore.WaitAsync();
        try
        {
            if (patchApplied)
            {
                return;
            }

            var patchPath = ResolveRepositoryPath(
                "infra",
                "db",
                "patches",
                "ExitPass_TerminalCashPaymentCommandReadback_v1.3.sql");
            await ExecuteAsync(await File.ReadAllTextAsync(patchPath));
            patchApplied = true;
        }
        finally
        {
            PatchSemaphore.Release();
        }
    }

    private static string ResolveRepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static async Task ExecuteAsync(string sql, params (string Name, NpgsqlDbType Type, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter.Name, parameter.Type).Value = parameter.Value;
        }

        await command.ExecuteNonQueryAsync();
    }

    private static Task SetSnapshotLifecycleAsync(
        Guid tariffSnapshotId,
        string status,
        DateTimeOffset calculatedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt = null) =>
        ExecuteAsync(
            """
            UPDATE core.tariff_snapshots
            SET snapshot_status = @snapshot_status::core.tariff_snapshot_status_enum,
                calculated_at = @calculated_at,
                expires_at = @expires_at,
                consumed_at = @consumed_at,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """,
            ("snapshot_status", NpgsqlDbType.Varchar, status),
            ("calculated_at", NpgsqlDbType.TimestampTz, calculatedAt),
            ("expires_at", NpgsqlDbType.TimestampTz, expiresAt),
            ("consumed_at", NpgsqlDbType.TimestampTz, (object?)consumedAt ?? DBNull.Value),
            ("tariff_snapshot_id", NpgsqlDbType.Uuid, tariffSnapshotId));

    private static Task SupersedeSnapshotAsync(
        Guid tariffSnapshotId,
        Guid successorTariffSnapshotId,
        DateTimeOffset calculatedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset successorCalculatedAt) =>
        ExecuteAsync(
            """
            UPDATE core.tariff_snapshots
            SET snapshot_status = 'SUPERSEDED'::core.tariff_snapshot_status_enum,
                calculated_at = @calculated_at,
                expires_at = @expires_at,
                superseded_by_tariff_snapshot_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;

            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                superseded_by_tariff_snapshot_id,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                statutory_discount_validation_id,
                coupon_application_id,
                snapshot_status,
                calculated_at,
                expires_at,
                consumed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version)
            SELECT
                @successor_tariff_snapshot_id,
                parking_session_id,
                NULL,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                statutory_discount_validation_id,
                coupon_application_id,
                'ACTIVE'::core.tariff_snapshot_status_enum,
                @successor_calculated_at,
                @expires_at,
                NULL,
                correlation_id,
                NOW(),
                created_by_service_identity_id,
                NOW(),
                updated_by_service_identity_id,
                1
            FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @tariff_snapshot_id;

            UPDATE core.tariff_snapshots
            SET superseded_by_tariff_snapshot_id = @successor_tariff_snapshot_id,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """,
            ("tariff_snapshot_id", NpgsqlDbType.Uuid, tariffSnapshotId),
            ("successor_tariff_snapshot_id", NpgsqlDbType.Uuid, successorTariffSnapshotId),
            ("calculated_at", NpgsqlDbType.TimestampTz, calculatedAt),
            ("expires_at", NpgsqlDbType.TimestampTz, expiresAt),
            ("successor_calculated_at", NpgsqlDbType.TimestampTz, successorCalculatedAt));

    private static Task RemoveSuccessorSnapshotAsync(
        Guid tariffSnapshotId,
        Guid successorTariffSnapshotId) =>
        ExecuteAsync(
            """
            UPDATE core.tariff_snapshots
            SET superseded_by_tariff_snapshot_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;

            DELETE FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @successor_tariff_snapshot_id;
            """,
            ("tariff_snapshot_id", NpgsqlDbType.Uuid, tariffSnapshotId),
            ("successor_tariff_snapshot_id", NpgsqlDbType.Uuid, successorTariffSnapshotId));

    private static async Task<T> ReadScalarAsync<T>(
        string sql,
        params (string Name, NpgsqlDbType Type, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter.Name, parameter.Type).Value = parameter.Value;
        }

        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return (T)value!;
    }

    private static async Task<int> CountAuditsAsync(Guid terminalCashTenderId, string auditEventType)
    {
        var value = await ReadScalarAsync<long>(
            """
            SELECT count(*)
            FROM core.terminal_cash_payment_command_audits
            WHERE terminal_cash_tender_id = @terminal_cash_tender_id
              AND audit_event_type = @audit_event_type;
            """,
            ("terminal_cash_tender_id", NpgsqlDbType.Uuid, terminalCashTenderId),
            ("audit_event_type", NpgsqlDbType.Varchar, auditEventType));
        return checked((int)value);
    }

    private static async Task<PaymentSideEffectCounts> ReadCountsAsync(PaymentTestContext context)
    {
        const string sql = """
            SELECT
                (SELECT count(*) FROM core.terminal_cash_payment_commands WHERE parking_session_id = @parking_session_id) AS terminal_cash_command_count,
                (SELECT count(*) FROM core.payment_attempts WHERE parking_session_id = @parking_session_id) AS payment_attempt_count,
                (SELECT count(*) FROM core.payment_confirmations pc
                    INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                    WHERE pa.parking_session_id = @parking_session_id) AS payment_confirmation_count,
                (SELECT count(*) FROM payments.provider_sessions ps
                    INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id
                    WHERE pa.parking_session_id = @parking_session_id) AS provider_session_count,
                (SELECT count(*) FROM payments.provider_outcomes po
                    INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
                    WHERE pa.parking_session_id = @parking_session_id) AS provider_outcome_count,
                (SELECT count(*) FROM core.fiscal_issuance_references WHERE parking_session_id = @parking_session_id) AS fiscal_reference_count,
                (SELECT count(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id) AS exit_authorization_count;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = context.ParkingSessionId;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaymentSideEffectCounts(
            TerminalCashCommandCount: reader.GetInt64(reader.GetOrdinal("terminal_cash_command_count")),
            PaymentAttemptCount: reader.GetInt64(reader.GetOrdinal("payment_attempt_count")),
            PaymentConfirmationCount: reader.GetInt64(reader.GetOrdinal("payment_confirmation_count")),
            ProviderSessionCount: reader.GetInt64(reader.GetOrdinal("provider_session_count")),
            ProviderOutcomeCount: reader.GetInt64(reader.GetOrdinal("provider_outcome_count")),
            FiscalReferenceCount: reader.GetInt64(reader.GetOrdinal("fiscal_reference_count")),
            ExitAuthorizationCount: reader.GetInt64(reader.GetOrdinal("exit_authorization_count")));
    }

    private sealed record PaymentSideEffectCounts(
        long TerminalCashCommandCount,
        long PaymentAttemptCount,
        long PaymentConfirmationCount,
        long ProviderSessionCount,
        long ProviderOutcomeCount,
        long FiscalReferenceCount,
        long ExitAuthorizationCount);
}
