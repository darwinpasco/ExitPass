using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Payments;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

/// <summary>
/// Verifies the v1.2 internal PaymentConfirmation recording contract.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 7.3 Provider Callback / Confirmation Handling
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - PaymentConfirmation is recorded only through Central PMS internal contract boundaries.
/// - Unknown payment attempts cannot receive confirmation evidence.
/// - Provider evidence replay is deterministic and does not create duplicate confirmations.
/// - Accepted confirmation evidence finalizes PaymentAttempt state through Central PMS.
/// - Confirmation recording does not issue ExitAuthorization.
/// </summary>
public sealed class RecordPaymentConfirmationContractTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string RequestedByActor = "payment-orchestrator";
    private readonly CustomWebApplicationFactory _factory;

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    public RecordPaymentConfirmationContractTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies BRD 9.10 and SDD 7.3 successful confirmation recording shape and side-effect boundary.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_201_created_for_valid_verified_provider_evidence_and_does_not_authorize_exit()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_returns_201_created_for_valid_verified_provider_evidence_and_does_not_authorize_exit));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment confirmation contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");

            var providerReference = $"prov-{Guid.NewGuid():N}";

            using var client = CreateClient();
            using var response = await PostConfirmationAsync(
                client,
                BuildRequest(created.PaymentAttemptId, providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var payload = await response.Content.ReadFromJsonAsync<RecordPaymentConfirmationResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            payload.PaymentConfirmationId.Should().NotBe(Guid.Empty);
            payload.ProviderReference.Should().Be(providerReference);
            payload.ProviderStatus.Should().Be("SUCCESS");
            payload.ConfirmationStatus.Should().Be("RECORDED");
            payload.VerifiedTimestamp.Should().BeAfter(DateTimeOffset.MinValue);

            var persistedAttempt = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var persistedConfirmation = await PaymentRoutineTestHelper.GetPaymentConfirmationByIdAsync(
                ConnectionString,
                payload.PaymentConfirmationId);
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                created.ParkingSessionId,
                issuedOnly: true);

            persistedAttempt.Should().NotBeNull();
            persistedAttempt!.AttemptStatus.Should().Be("CONFIRMED");
            persistedAttempt.FinalizedAt.Should().NotBeNull();
            persistedConfirmation.Should().NotBeNull();
            persistedConfirmation!.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            persistedConfirmation.ProviderReference.Should().Be(providerReference);
            persistedConfirmation.ConfirmationStatus.Should().Be("RECORDED");
            persistedConfirmation.AmountConfirmed.Should().Be(100.00m);
            persistedConfirmation.CurrencyCode.Trim().Should().Be("PHP");
            confirmationCount.Should().Be(1);
            issuedAuthorizationCount.Should().Be(0, "confirmation recording is not exit authorization issuance");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.10 and SDD 7.3 unknown-payment-attempt behavior.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_404_not_found_for_unknown_payment_attempt()
    {
        var correlationId = Guid.NewGuid();

        using var client = CreateClient();
        using var response = await PostConfirmationAsync(
            client,
            BuildRequest(Guid.NewGuid(), $"prov-{Guid.NewGuid():N}"),
            includeCorrelationId: true,
            correlationId: correlationId,
            includeIdempotencyKey: true,
            idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_NOT_FOUND");
        payload.CorrelationId.Should().Be(correlationId);
    }

    /// <summary>
    /// Verifies BRD 9.13 same-attempt provider evidence replay behavior.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_existing_confirmation_for_same_provider_reference_replay()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_returns_existing_confirmation_for_same_provider_reference_replay));
        var providerReference = $"prov-{Guid.NewGuid():N}";
        var idempotencyKey = $"ctest-confirmation-{Guid.NewGuid():N}";
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment confirmation replay contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");

            using var client = CreateClient();
            using var first = await PostConfirmationAsync(
                client,
                BuildRequest(created.PaymentAttemptId, providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: idempotencyKey);

            using var replay = await PostConfirmationAsync(
                client,
                BuildRequest(created.PaymentAttemptId, providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: idempotencyKey);

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            replay.StatusCode.Should().Be(HttpStatusCode.Created);

            var firstPayload = await first.Content.ReadFromJsonAsync<RecordPaymentConfirmationResponse>();
            var replayPayload = await replay.Content.ReadFromJsonAsync<RecordPaymentConfirmationResponse>();
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);

            replayPayload.Should().NotBeNull();
            replayPayload!.PaymentConfirmationId.Should().Be(firstPayload!.PaymentConfirmationId);
            replayPayload.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            replayPayload.ProviderReference.Should().Be(providerReference);
            confirmationCount.Should().Be(1, "same provider evidence replay must not create duplicate confirmations");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 deterministic duplicate behavior for one attempt with conflicting provider references.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_409_conflict_when_confirmation_already_exists_for_attempt()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_returns_409_conflict_when_confirmation_already_exists_for_attempt));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment confirmation duplicate contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");

            using var client = CreateClient();
            using var first = await PostConfirmationAsync(
                client,
                BuildRequest(created.PaymentAttemptId, $"prov-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            using var conflict = await PostConfirmationAsync(
                client,
                BuildRequest(created.PaymentAttemptId, $"prov-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_CONFIRMATION_ALREADY_EXISTS");
            payload.CorrelationId.Should().Be(context.CorrelationId);
            confirmationCount.Should().Be(1);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 deterministic duplicate behavior for cross-attempt provider reference reuse.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_409_conflict_for_provider_reference_reused_on_different_attempt()
    {
        var firstContext = PaymentTestContext.Create(
            $"{nameof(RecordPaymentConfirmation_returns_409_conflict_for_provider_reference_reused_on_different_attempt)}First");
        var secondContext = PaymentTestContext.Create(
            $"{nameof(RecordPaymentConfirmation_returns_409_conflict_for_provider_reference_reused_on_different_attempt)}Second");
        var providerReference = $"prov-{Guid.NewGuid():N}";

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            firstContext,
            "Seed first data set for duplicate provider-reference confirmation tests");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            secondContext,
            "Seed second data set for duplicate provider-reference confirmation tests");

        try
        {
            var firstCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                firstContext,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");
            var secondCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                secondContext,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");

            using var client = CreateClient();
            using var first = await PostConfirmationAsync(
                client,
                BuildRequest(firstCreated.PaymentAttemptId, providerReference),
                includeCorrelationId: true,
                correlationId: firstContext.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            using var conflict = await PostConfirmationAsync(
                client,
                BuildRequest(secondCreated.PaymentAttemptId, providerReference),
                includeCorrelationId: true,
                correlationId: secondContext.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
            var firstConfirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                firstCreated.PaymentAttemptId);
            var secondConfirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                secondCreated.PaymentAttemptId);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PROVIDER_REFERENCE_ALREADY_RECORDED");
            payload.CorrelationId.Should().Be(secondContext.CorrelationId);
            firstConfirmationCount.Should().Be(1);
            secondConfirmationCount.Should().Be(0);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
        }
    }

    /// <summary>
    /// Verifies BRD 9.10 payable-basis validation for provider amount mismatch.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_409_conflict_for_provider_amount_mismatch()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_returns_409_conflict_for_provider_amount_mismatch));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment confirmation amount validation tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");

            using var client = CreateClient();
            using var response = await PostConfirmationAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    $"prov-{Guid.NewGuid():N}",
                    amountConfirmed: 99.99m,
                    currencyCode: "PHP"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_AMOUNT_MISMATCH");
            payload.CorrelationId.Should().Be(context.CorrelationId);
            confirmationCount.Should().Be(0);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.10 payable-basis validation for provider currency mismatch.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_409_conflict_for_provider_currency_mismatch()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_returns_409_conflict_for_provider_currency_mismatch));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment confirmation currency validation tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "payment-confirmation-contract-test");

            using var client = CreateClient();
            using var response = await PostConfirmationAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    $"prov-{Guid.NewGuid():N}",
                    amountConfirmed: 100.00m,
                    currencyCode: "USD"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_CURRENCY_MISMATCH");
            payload.CorrelationId.Should().Be(context.CorrelationId);
            confirmationCount.Should().Be(0);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing idempotency metadata.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_400_bad_request_without_idempotency_key()
    {
        using var client = CreateClient();
        using var response = await PostConfirmationAsync(
            client,
            BuildRequest(Guid.NewGuid(), $"prov-{Guid.NewGuid():N}"),
            includeCorrelationId: true,
            correlationId: Guid.NewGuid(),
            includeIdempotencyKey: false,
            idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing correlation metadata.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_returns_400_bad_request_without_correlation_id()
    {
        using var client = CreateClient();
        using var response = await PostConfirmationAsync(
            client,
            BuildRequest(Guid.NewGuid(), $"prov-{Guid.NewGuid():N}"),
            includeCorrelationId: false,
            correlationId: Guid.Empty,
            includeIdempotencyKey: true,
            idempotencyKey: $"ctest-confirmation-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static RecordPaymentConfirmationRequest BuildRequest(
        Guid paymentAttemptId,
        string providerReference,
        string providerStatus = "SUCCESS",
        decimal? amountConfirmed = 100.00m,
        string? currencyCode = "PHP")
    {
        return new RecordPaymentConfirmationRequest(
            PaymentAttemptId: paymentAttemptId,
            ProviderReference: providerReference,
            ProviderStatus: providerStatus,
            RequestedBy: RequestedByActor,
            RawCallbackReference: $"callback-{Guid.NewGuid():N}",
            ProviderSignatureValid: true,
            ProviderPayloadHash: $"sha256:{Guid.NewGuid():N}",
            AmountConfirmed: amountConfirmed,
            CurrencyCode: currencyCode);
    }

    private static async Task<HttpResponseMessage> PostConfirmationAsync(
        HttpClient client,
        RecordPaymentConfirmationRequest request,
        bool includeCorrelationId,
        Guid correlationId,
        bool includeIdempotencyKey,
        string? idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/internal/payments/confirmation")
        {
            Content = JsonContent.Create(request)
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        if (includeIdempotencyKey)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(message);
    }
}
