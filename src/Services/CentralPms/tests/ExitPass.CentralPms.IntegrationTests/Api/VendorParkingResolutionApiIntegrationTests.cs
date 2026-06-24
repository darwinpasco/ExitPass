using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public.PaymentAttempts;
using ExitPass.CentralPms.Contracts.Public.VendorParking;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// API integration tests for provider-neutral Central PMS vendor parking resolution.
/// </summary>
public sealed class VendorParkingResolutionApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="VendorParkingResolutionApiIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">In-memory Central PMS API factory.</param>
    public VendorParkingResolutionApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies plate-based vendor parking resolution returns Central PMS session and tariff identifiers.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenPlateProvided_ReturnsResolvedSessionAndTariff()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "ABC1234", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        payload.Should().NotBeNull();
        payload!.ParkingSessionId.Should().NotBe(Guid.Empty);
        payload.TariffSnapshotId.Should().NotBe(Guid.Empty);
        payload.LookupOutcome.Should().Be("resolved");
        payload.PlateNumber.Should().Be("ABC1234");
        payload.EntryTime.Should().NotBeNull();
        payload.CurrentFeeCalculationTime.Should().NotBeNull();
        payload.NetPayableMinorUnits.Should().Be(10000);
        payload.Currency.Should().Be("PHP");
        payload.ParkingStatus.Should().Be("PaymentRequired");
        payload.PaymentStatus.Should().Be("Not Started");
        payload.StatutoryDiscountApplied.Should().BeFalse();
        payload.EffectiveTariffSnapshotId.Should().Be(payload.TariffSnapshotId);
        payload.AppliedTariffSnapshotId.Should().BeNull();
        Guid.TryParse(payload.VendorSystemId, out var vendorSystemId).Should().BeTrue();
        vendorSystemId.Should().NotBe(Guid.Empty);
        payload.CorrelationId.Should().Be(correlationId);
    }

    /// <summary>
    /// Verifies ticket-based vendor parking resolution returns Central PMS session and tariff identifiers.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenTicketProvided_ReturnsResolvedSessionAndTariff()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000002");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: null, ticketReference: "TICKET-001", correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        payload.Should().NotBeNull();
        payload!.ParkingSessionId.Should().NotBe(Guid.Empty);
        payload.TariffSnapshotId.Should().NotBe(Guid.Empty);
        payload.TicketReference.Should().Be("TICKET-001");
        payload.NetPayableMinorUnits.Should().Be(10000);
        payload.StatutoryDiscountApplied.Should().BeFalse();
        payload.EffectiveTariffSnapshotId.Should().Be(payload.TariffSnapshotId);
    }

    /// <summary>
    /// Verifies missing lookup identifiers are rejected at the API boundary.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenPlateAndTicketMissing_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000003");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: null, ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("INVALID_REQUEST");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies deterministic not-found envelope mapping.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorReturnsNotFound_ReturnsNotFoundEnvelope()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000004");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "NOTFOUND", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("SESSION_NOT_FOUND");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies ambiguous vendor matches map to a deterministic conflict envelope.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorReturnsAmbiguous_ReturnsConflictEnvelope()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000013");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "AMBIGUOUS", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("VENDOR_SESSION_AMBIGUOUS");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies retryable vendor unavailability maps to HTTP 503.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorUnavailable_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000005");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "UNAVAILABLE", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("VENDOR_UNAVAILABLE");
        payload.Retryable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies malformed vendor data maps to HTTP 502.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorMalformed_ReturnsBadGateway()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000006");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "MALFORMED", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("MALFORMED_VENDOR_SESSION");
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies vendor business rejection maps to HTTP 409 with the standard envelope.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenVendorRejected_ReturnsConflictEnvelope()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000007");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "REJECTED", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("VENDOR_REJECTED_LOOKUP");
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies tariff calculation failure maps to a deterministic canonical error.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenTariffCalculationFails_ReturnsConflictEnvelope()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000014");

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: "TARIFFFAIL", ticketReference: null, correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("VENDOR_TARIFF_REJECTED");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies repeated fake-adapter resolution keeps provider-neutral tariff data stable.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenRepeated_ReturnsIdempotentOrStableSessionTariffResult()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000008");
        var request = Request(plateNumber: "ABC1234", ticketReference: null, correlationId);

        using var first = await client.PostAsJsonAsync("/v1/vendor-parking/resolve", request);
        using var second = await client.PostAsJsonAsync("/v1/vendor-parking/resolve", request);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstPayload = await first.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        var secondPayload = await second.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();

        firstPayload.Should().NotBeNull();
        secondPayload.Should().NotBeNull();
        secondPayload!.LookupOutcome.Should().Be(firstPayload!.LookupOutcome);
        secondPayload.NetPayableMinorUnits.Should().Be(firstPayload.NetPayableMinorUnits);
        secondPayload.Currency.Should().Be(firstPayload.Currency);
        secondPayload.VendorSystemId.Should().Be(firstPayload.VendorSystemId);
        secondPayload.ParkingSessionId.Should().NotBe(Guid.Empty);
        secondPayload.TariffSnapshotId.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Verifies vendor parking resolution does not mutate payment, confirmation, or exit authorization truth.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenResolved_DoesNotCreatePaymentConfirmationOrExitTruth()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000015");
        var plateNumber = UniqueLookup("FLOW-NO-PAYMENT");

        var before = await CountPaymentExitTruthRowsAsync(correlationId);

        using var response = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: plateNumber, ticketReference: null, correlationId));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var after = await CountPaymentExitTruthRowsAsync(correlationId);

        after.Should().Be(before);
    }

    /// <summary>
    /// Verifies WebPay resolves the APPLIED statutory discount tariff snapshot as the effective payable basis.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WebPaySessionSummary_WhenStatutoryDiscountApplied_ReturnsAppliedPayableBasis()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000016");
        var ticketReference = UniqueLookup("WEBPAY-APPLIED");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        var applied = await CreateAppliedPayableBasisFixtureAsync(initial, correlationId);
        var beforePaymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);

        var resolved = await ResolveAsync(client, request);
        var afterPaymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);

        resolved.ParkingSessionId.Should().Be(initial.ParkingSessionId);
        resolved.TariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        resolved.EffectiveTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        resolved.OriginalTariffSnapshotId.Should().Be(initial.TariffSnapshotId);
        resolved.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        resolved.StatutoryDiscountApplied.Should().BeTrue();
        resolved.StatutoryDiscountValidationId.Should().Be(applied.ValidationId);
        resolved.StatutoryDiscountApplicationId.Should().Be(applied.ApplicationId);
        resolved.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        resolved.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        resolved.NetPayableMinorUnits.Should().Be(7143);
        resolved.TariffSnapshotId.Should().NotBe(initial.TariffSnapshotId);
        afterPaymentAttempts.Should().Be(beforePaymentAttempts);

        var tariffState = await ReadTariffStateAsync(initial.TariffSnapshotId, applied.AppliedTariffSnapshotId);
        tariffState.OriginalStatus.Should().Be("SUPERSEDED");
        tariffState.OriginalGrossAmount.Should().Be(100m);
        tariffState.OriginalNetAmount.Should().Be(100m);
        tariffState.AppliedStatus.Should().Be("ACTIVE");
        tariffState.AppliedNetAmount.Should().Be(71.43m);
    }

    /// <summary>
    /// Verifies payment creation consumes the same applied tariff snapshot returned by WebPay session summary.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_WhenStatutoryDiscountApplied_UsesEffectiveAppliedTariffSnapshot()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000017");
        var ticketReference = UniqueLookup("PAY-APPLIED");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        var applied = await CreateAppliedPayableBasisFixtureAsync(initial, correlationId);
        var resolved = await ResolveAsync(client, request);

        using var paymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey: $"idem-pay-applied-{Guid.NewGuid():N}",
            correlationId);

        var paymentRaw = await paymentResponse.Content.ReadAsStringAsync();
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, paymentRaw);

        var payment = await paymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        payment.Should().NotBeNull();
        payment!.PaymentProvider.Should().Be("GCASH");
        payment.WasReused.Should().BeFalse();

        var persisted = await ReadPaymentAttemptAsync(payment.PaymentAttemptId);
        persisted.Should().NotBeNull();
        persisted!.ParkingSessionId.Should().Be(initial.ParkingSessionId);
        persisted.TariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        persisted.Amount.Should().Be(71.43m);

        var tariffState = await ReadTariffStateAsync(initial.TariffSnapshotId, applied.AppliedTariffSnapshotId);
        tariffState.OriginalStatus.Should().Be("SUPERSEDED");
        tariffState.AppliedStatus.Should().Be("CONSUMED");
    }

    /// <summary>
    /// Verifies idempotent replay for an APPLIED tariff snapshot still reuses after the first attempt consumes it.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptHandler_WhenAppliedSnapshotConsumedAndIdempotencyMatches_ReusesAttempt()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000020");
        var ticketReference = UniqueLookup("PAY-APPLIED-REPLAY");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);
        var idempotencyKey = $"idem-pay-applied-replay-{Guid.NewGuid():N}";

        var initial = await ResolveAsync(client, request);
        var applied = await CreateAppliedPayableBasisFixtureAsync(initial, correlationId);
        var resolved = await ResolveAsync(client, request);

        using var firstPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey,
            correlationId);

        using var replayPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey,
            correlationId);

        var firstRaw = await firstPaymentResponse.Content.ReadAsStringAsync();
        var replayRaw = await replayPaymentResponse.Content.ReadAsStringAsync();
        firstPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, firstRaw);
        replayPaymentResponse.StatusCode.Should().Be(HttpStatusCode.OK, replayRaw);

        var firstPayment = await firstPaymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        var replayPayment = await replayPaymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        firstPayment.Should().NotBeNull();
        replayPayment.Should().NotBeNull();
        replayPayment!.PaymentAttemptId.Should().Be(firstPayment!.PaymentAttemptId);
        replayPayment.WasReused.Should().BeTrue();

        var paymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);
        paymentAttempts.Should().Be(1);

        var persisted = await ReadPaymentAttemptAsync(firstPayment.PaymentAttemptId);
        persisted.Should().NotBeNull();
        persisted!.TariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        persisted.Amount.Should().Be(71.43m);

        var tariffState = await ReadTariffStateAsync(initial.TariffSnapshotId, applied.AppliedTariffSnapshotId);
        tariffState.AppliedStatus.Should().Be("CONSUMED");
    }

    /// <summary>
    /// Verifies same idempotency key cannot be replayed with a different submitted tariff snapshot.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptHandler_WhenAppliedReplayUsesDifferentTariffSnapshot_RejectsIdempotencyConflict()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000021");
        var ticketReference = UniqueLookup("PAY-APPLIED-IDEM-TARIFF");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);
        var idempotencyKey = $"idem-pay-applied-conflict-tariff-{Guid.NewGuid():N}";

        var initial = await ResolveAsync(client, request);
        await CreateAppliedPayableBasisFixtureAsync(initial, correlationId);
        var resolved = await ResolveAsync(client, request);

        using var firstPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey,
            correlationId);

        using var conflictResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey,
            correlationId);

        var firstRaw = await firstPaymentResponse.Content.ReadAsStringAsync();
        var conflictRaw = await conflictResponse.Content.ReadAsStringAsync();
        firstPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, firstRaw);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, conflictRaw);

        var error = await conflictResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");

        var paymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);
        paymentAttempts.Should().Be(1);
    }

    /// <summary>
    /// Verifies a consumed applied snapshot remains protected from a second non-idempotent payment attempt.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptHandler_WhenAppliedSnapshotConsumedWithoutMatchingReplay_RejectsActiveAttempt()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000023");
        var ticketReference = UniqueLookup("PAY-APPLIED-CONSUMED-NEW");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        await CreateAppliedPayableBasisFixtureAsync(initial, correlationId);
        var resolved = await ResolveAsync(client, request);

        using var firstPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey: $"idem-pay-applied-first-{Guid.NewGuid():N}",
            correlationId);

        using var newPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey: $"idem-pay-applied-new-{Guid.NewGuid():N}",
            correlationId);

        var firstRaw = await firstPaymentResponse.Content.ReadAsStringAsync();
        var newRaw = await newPaymentResponse.Content.ReadAsStringAsync();
        firstPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, firstRaw);
        newPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, newRaw);

        var error = await newPaymentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("ACTIVE_PAYMENT_ATTEMPT_EXISTS");

        var paymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);
        paymentAttempts.Should().Be(1);
    }

    /// <summary>
    /// Verifies a failed provider handoff does not trap WebPay on a consumed payable basis.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenLatestConsumedSnapshotBelongsOnlyToFailedAttempt_ReturnsFreshPayableBasis()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000024");
        var ticketReference = UniqueLookup("PAY-FAILED-RETRY");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        using var firstPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey: $"idem-pay-failed-first-{Guid.NewGuid():N}",
            correlationId);

        var firstRaw = await firstPaymentResponse.Content.ReadAsStringAsync();
        firstPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, firstRaw);
        var firstPayment = await firstPaymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        firstPayment.Should().NotBeNull();

        var failed = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString(),
            firstPayment!.PaymentAttemptId,
            "FAILED",
            "CENTRAL_PMS_API",
            correlationId);

        failed.Should().NotBeNull();
        failed!.AttemptStatus.Should().Be("FAILED");

        using var stalePaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey: $"idem-pay-failed-stale-{Guid.NewGuid():N}",
            correlationId);

        var staleRaw = await stalePaymentResponse.Content.ReadAsStringAsync();
        stalePaymentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, staleRaw);

        var staleError = await stalePaymentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        staleError.Should().NotBeNull();
        staleError!.ErrorCode.Should().Be("PAYABLE_BASIS_REFRESH_REQUIRED");
        staleError.ErrorCode.Should().NotBe("TARIFF_SNAPSHOT_INVALID");

        var refreshed = await ResolveAsync(client, request);
        refreshed.ParkingSessionId.Should().Be(initial.ParkingSessionId);
        refreshed.TariffSnapshotId.Should().NotBe(initial.TariffSnapshotId);
        refreshed.NetPayableMinorUnits.Should().Be(initial.NetPayableMinorUnits);

        using var retryPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            refreshed,
            idempotencyKey: $"idem-pay-failed-retry-{Guid.NewGuid():N}",
            correlationId);

        var retryRaw = await retryPaymentResponse.Content.ReadAsStringAsync();
        retryPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, retryRaw);
        var retryPayment = await retryPaymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        retryPayment.Should().NotBeNull();
        retryPayment!.PaymentAttemptId.Should().NotBe(firstPayment.PaymentAttemptId);

        var persistedRetry = await ReadPaymentAttemptAsync(retryPayment.PaymentAttemptId);
        persistedRetry.Should().NotBeNull();
        persistedRetry!.TariffSnapshotId.Should().Be(refreshed.TariffSnapshotId);

        (await CountPaymentAttemptsAsync(initial.ParkingSessionId)).Should().Be(2);
        (await CountPaymentConfirmationsAsync(initial.ParkingSessionId)).Should().Be(0);
        (await CountExitAuthorizationsAsync(initial.ParkingSessionId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies an expired ACTIVE snapshot is not reused as the WebPay payable basis.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenLatestActiveSnapshotIsExpired_ReturnsFreshPayableBasis()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000026");
        var ticketReference = UniqueLookup("PAY-EXPIRED-RETRY");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        await ExpireTariffSnapshotAsync(initial.TariffSnapshotId);

        using var expiredPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey: $"idem-pay-expired-stale-{Guid.NewGuid():N}",
            correlationId);

        var expiredRaw = await expiredPaymentResponse.Content.ReadAsStringAsync();
        expiredPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, expiredRaw);

        var expiredError = await expiredPaymentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        expiredError.Should().NotBeNull();
        expiredError!.ErrorCode.Should().Be("PAYABLE_BASIS_REFRESH_REQUIRED");
        expiredError.Retryable.Should().BeTrue();
        expiredError.ErrorCode.Should().NotBe("TARIFF_SNAPSHOT_INVALID");

        var refreshed = await ResolveAsync(client, request);
        refreshed.ParkingSessionId.Should().Be(initial.ParkingSessionId);
        refreshed.TariffSnapshotId.Should().NotBe(initial.TariffSnapshotId);
        refreshed.NetPayableMinorUnits.Should().Be(initial.NetPayableMinorUnits);

        using var retryPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            refreshed,
            idempotencyKey: $"idem-pay-expired-retry-{Guid.NewGuid():N}",
            correlationId);

        var retryRaw = await retryPaymentResponse.Content.ReadAsStringAsync();
        retryPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, retryRaw);

        (await CountPaymentConfirmationsAsync(initial.ParkingSessionId)).Should().Be(0);
        (await CountExitAuthorizationsAsync(initial.ParkingSessionId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies an EXPIRED unconsumed browser-held snapshot returns refresh-required instead of generic tariff invalid.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_WhenBrowserHeldSnapshotStatusIsExpiredWithoutAttempt_ReturnsRefreshRequired()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000027");
        var ticketReference = UniqueLookup("PAY-EXPIRED-STATUS");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        await ExpireTariffSnapshotStatusAsync(initial.TariffSnapshotId);

        using var expiredPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey: $"idem-pay-expired-status-{Guid.NewGuid():N}",
            correlationId);

        var expiredRaw = await expiredPaymentResponse.Content.ReadAsStringAsync();
        expiredPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, expiredRaw);

        var expiredError = await expiredPaymentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        expiredError.Should().NotBeNull();
        expiredError!.ErrorCode.Should().Be("PAYABLE_BASIS_REFRESH_REQUIRED");
        expiredError.Retryable.Should().BeTrue();
        expiredError.ErrorCode.Should().NotBe("TARIFF_SNAPSHOT_INVALID");

        (await CountPaymentAttemptsAsync(initial.ParkingSessionId)).Should().Be(0);
        (await CountPaymentConfirmationsAsync(initial.ParkingSessionId)).Should().Be(0);
        (await CountExitAuthorizationsAsync(initial.ParkingSessionId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies a consumed snapshot tied to confirmed payment finality remains protected from a new payment attempt.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenLatestConsumedSnapshotBelongsToConfirmedAttempt_DoesNotRefreshPayableBasis()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000025");
        var ticketReference = UniqueLookup("PAY-CONFIRMED-PROTECT");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        using var paymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey: $"idem-pay-confirmed-first-{Guid.NewGuid():N}",
            correlationId);

        var paymentRaw = await paymentResponse.Content.ReadAsStringAsync();
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, paymentRaw);
        var payment = await paymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        payment.Should().NotBeNull();

        var confirmed = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString(),
            payment!.PaymentAttemptId,
            "CONFIRMED",
            "CENTRAL_PMS_API",
            correlationId);

        confirmed.Should().NotBeNull();
        confirmed!.AttemptStatus.Should().Be("CONFIRMED");

        var resolvedAfterConfirmation = await ResolveAsync(client, request);
        resolvedAfterConfirmation.ParkingSessionId.Should().Be(initial.ParkingSessionId);
        resolvedAfterConfirmation.TariffSnapshotId.Should().Be(initial.TariffSnapshotId);

        using var secondPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolvedAfterConfirmation,
            idempotencyKey: $"idem-pay-confirmed-second-{Guid.NewGuid():N}",
            correlationId);

        var secondRaw = await secondPaymentResponse.Content.ReadAsStringAsync();
        secondPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, secondRaw);

        var error = await secondPaymentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("TARIFF_SNAPSHOT_INVALID");

        (await CountPaymentAttemptsAsync(initial.ParkingSessionId)).Should().Be(1);
    }

    /// <summary>
    /// Verifies payment creation rejects stale original tariff snapshots after a statutory discount is APPLIED.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_WhenStatutoryDiscountAppliedAndOriginalSnapshotSubmitted_RejectsStaleSnapshot()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000018");
        var ticketReference = UniqueLookup("PAY-STALE");
        var request = Request(plateNumber: null, ticketReference: ticketReference, correlationId);

        var initial = await ResolveAsync(client, request);
        await CreateAppliedPayableBasisFixtureAsync(initial, correlationId);
        var beforePaymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);

        using var paymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            initial,
            idempotencyKey: $"idem-pay-stale-{Guid.NewGuid():N}",
            correlationId);

        var paymentRaw = await paymentResponse.Content.ReadAsStringAsync();
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, paymentRaw);

        var error = await paymentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("STALE_TARIFF_SNAPSHOT");

        var afterPaymentAttempts = await CountPaymentAttemptsAsync(initial.ParkingSessionId);
        afterPaymentAttempts.Should().Be(beforePaymentAttempts);
    }

    /// <summary>
    /// Verifies a plate-resolved vendor session persists IDs that the normal CreatePaymentAttempt API can read.
    /// </summary>
    [Fact]
    public async Task VendorResolveThenCreatePaymentAttempt_WhenPlateResolved_CreatesPaymentAttempt()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000009");
        var plateNumber = UniqueLookup("FLOW-PLATE");

        var resolved = await ResolveAsync(
            client,
            Request(plateNumber: plateNumber, ticketReference: null, correlationId));

        using var paymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey: $"idem-flow-plate-{Guid.NewGuid():N}",
            correlationId);

        var paymentRaw = await paymentResponse.Content.ReadAsStringAsync();
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, paymentRaw);
        var payment = await paymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        payment.Should().NotBeNull();
        payment!.PaymentAttemptId.Should().NotBe(Guid.Empty);
        payment.WasReused.Should().BeFalse();
        payment.PaymentProvider.Should().Be("GCASH");
    }

    /// <summary>
    /// Verifies a ticket-resolved vendor session persists IDs that the normal CreatePaymentAttempt API can read.
    /// </summary>
    [Fact]
    public async Task VendorResolveThenCreatePaymentAttempt_WhenTicketResolved_CreatesPaymentAttempt()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000010");
        var ticketReference = UniqueLookup("FLOW-TICKET");

        var resolved = await ResolveAsync(
            client,
            Request(plateNumber: null, ticketReference: ticketReference, correlationId));

        using var paymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey: $"idem-flow-ticket-{Guid.NewGuid():N}",
            correlationId);

        var paymentRaw = await paymentResponse.Content.ReadAsStringAsync();
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, paymentRaw);
        var payment = await paymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        payment.Should().NotBeNull();
        payment!.PaymentAttemptId.Should().NotBe(Guid.Empty);
        payment.WasReused.Should().BeFalse();
    }

    /// <summary>
    /// Verifies repeated resolve/payment calls reuse the authoritative payment attempt by idempotency key.
    /// </summary>
    [Fact]
    public async Task VendorResolveThenCreatePaymentAttempt_WhenRepeated_ReusesAccordingToExistingInvariant()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000011");
        var idempotencyKey = $"idem-flow-repeat-{Guid.NewGuid():N}";
        var plateNumber = UniqueLookup("FLOW-REPEAT");

        var firstResolved = await ResolveAsync(
            client,
            Request(plateNumber: plateNumber, ticketReference: null, correlationId));

        using var firstPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            firstResolved,
            idempotencyKey,
            correlationId);

        var secondResolved = await ResolveAsync(
            client,
            Request(plateNumber: plateNumber, ticketReference: null, correlationId));

        secondResolved.TariffSnapshotId.Should().Be(firstResolved.TariffSnapshotId);
        secondResolved.FeeValidUntil.Should().Be(firstResolved.FeeValidUntil);

        using var secondPaymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            secondResolved,
            idempotencyKey,
            correlationId);

        var firstRaw = await firstPaymentResponse.Content.ReadAsStringAsync();
        var secondRaw = await secondPaymentResponse.Content.ReadAsStringAsync();
        firstPaymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, firstRaw);
        secondPaymentResponse.StatusCode.Should().Be(HttpStatusCode.OK, secondRaw);

        var firstPayment = await firstPaymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
        var secondPayment = await secondPaymentResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();

        firstPayment.Should().NotBeNull();
        secondPayment.Should().NotBeNull();
        secondPayment!.PaymentAttemptId.Should().Be(firstPayment!.PaymentAttemptId);
        secondPayment.WasReused.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the vendor-to-payment flow preserves the caller correlation ID at the API boundary.
    /// </summary>
    [Fact]
    public async Task VendorResolveThenCreatePaymentAttempt_PreservesCorrelationIdAcrossFlow()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000012");
        var plateNumber = UniqueLookup("FLOW-CORRELATION");

        using var resolveResponse = await client.PostAsJsonAsync(
            "/v1/vendor-parking/resolve",
            Request(plateNumber: plateNumber, ticketReference: null, correlationId));

        resolveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        resolveResponse.Headers.TryGetValues("X-Correlation-Id", out var resolveHeaders).Should().BeTrue();
        resolveHeaders.Should().Contain(correlationId.ToString());

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        resolved.Should().NotBeNull();
        resolved!.CorrelationId.Should().Be(correlationId);

        using var paymentResponse = await PostCreatePaymentAttemptAsync(
            client,
            resolved,
            idempotencyKey: $"idem-flow-correlation-{Guid.NewGuid():N}",
            correlationId);

        var paymentRaw = await paymentResponse.Content.ReadAsStringAsync();
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Created, paymentRaw);
        paymentResponse.Headers.TryGetValues("X-Correlation-Id", out var paymentHeaders).Should().BeTrue();
        paymentHeaders.Should().Contain(correlationId.ToString());
    }

    private static async Task<ResolveVendorParkingResponse> ResolveAsync(
        HttpClient client,
        ResolveVendorParkingRequest request)
    {
        using var response = await client.PostAsJsonAsync("/v1/vendor-parking/resolve", request);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);

        var payload = await response.Content.ReadFromJsonAsync<ResolveVendorParkingResponse>();
        payload.Should().NotBeNull();
        payload!.ParkingSessionId.Should().NotBe(Guid.Empty);
        payload.TariffSnapshotId.Should().NotBe(Guid.Empty);
        return payload;
    }

    private static async Task<HttpResponseMessage> PostCreatePaymentAttemptAsync(
        HttpClient client,
        ResolveVendorParkingResponse resolved,
        string idempotencyKey,
        Guid correlationId,
        string paymentProvider = "GCASH")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/public/payment-attempts")
        {
            Content = JsonContent.Create(new CreatePaymentAttemptRequest
            {
                ParkingSessionId = resolved.ParkingSessionId,
                TariffSnapshotId = resolved.TariffSnapshotId,
                PaymentProvider = paymentProvider
            })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        return await client.SendAsync(request);
    }

    private static ResolveVendorParkingRequest Request(
        string? plateNumber,
        string? ticketReference,
        Guid correlationId)
    {
        return new ResolveVendorParkingRequest
        {
            SiteGroupId = CorrelatedGuid("21000000", correlationId),
            SiteId = CorrelatedGuid("22000000", correlationId),
            VendorSystemId = "FAKE-PMS",
            PlateNumber = plateNumber,
            TicketReference = ticketReference,
            CorrelationId = correlationId
        };
    }

    private static string CorrelatedGuid(string prefix, Guid correlationId)
    {
        var suffix = correlationId.ToString("N")[^12..];
        return $"{prefix}-0000-0000-0000-{suffix}";
    }

    private static string UniqueLookup(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}"[..32];
    }

    private static async Task<long> CountPaymentExitTruthRowsAsync(Guid correlationId)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts WHERE correlation_id = @correlation_id)
              + (SELECT COUNT(*) FROM core.payment_confirmations WHERE correlation_id = @correlation_id)
              + (SELECT COUNT(*) FROM core.exit_authorizations WHERE correlation_id = @correlation_id);
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<AppliedPayableBasisFixture> CreateAppliedPayableBasisFixtureAsync(
        ResolveVendorParkingResponse resolved,
        Guid correlationId)
    {
        var validationId = Guid.NewGuid();
        var appliedTariffSnapshotId = Guid.NewGuid();

        const string sql = """
            UPDATE core.tariff_snapshots
            SET snapshot_status = 'SUPERSEDED'::core.tariff_snapshot_status_enum,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @original_tariff_snapshot_id;

            INSERT INTO discounts.statutory_discount_validations (
                statutory_discount_validation_id,
                parking_session_id,
                tariff_snapshot_id,
                entitlement_type,
                policy_resolution_basis,
                local_ordinance_applied,
                national_law_fallback_applied,
                validation_channel,
                validation_status,
                currency_code,
                gross_amount_at_validation,
                statutory_discount_amount,
                net_amount_after_discount,
                evidence_required,
                evidence_captured,
                requested_at,
                validated_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @validation_id,
                @parking_session_id,
                @original_tariff_snapshot_id,
                'SENIOR_CITIZEN',
                'NATIONAL_LAW_FALLBACK',
                FALSE,
                TRUE,
                'OPERATOR_ASSISTED',
                'APPROVED',
                'PHP',
                100,
                17.86,
                71.43,
                FALSE,
                TRUE,
                NOW(),
                NOW(),
                @correlation_id,
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            );

            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
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
                row_version
            )
            SELECT
                @applied_tariff_snapshot_id,
                ts.parking_session_id,
                ts.vendor_system_id,
                ts.vendor_tariff_ref,
                COALESCE(ts.tariff_version_reference, 'TEST') || '|STATUTORY_DISCOUNT_APPLIED',
                ts.currency_code,
                100,
                17.86,
                0,
                71.43,
                @validation_id,
                NULL,
                'ACTIVE',
                NOW(),
                ts.expires_at,
                NULL,
                @correlation_id,
                NOW(),
                ts.created_by_service_identity_id,
                NOW(),
                ts.updated_by_service_identity_id,
                1
            FROM core.tariff_snapshots AS ts
            WHERE ts.tariff_snapshot_id = @original_tariff_snapshot_id;

            UPDATE core.tariff_snapshots
            SET superseded_by_tariff_snapshot_id = @applied_tariff_snapshot_id,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @original_tariff_snapshot_id;

            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("validation_id", validationId);
        command.Parameters.AddWithValue("applied_tariff_snapshot_id", appliedTariffSnapshotId);
        command.Parameters.AddWithValue("parking_session_id", resolved.ParkingSessionId);
        command.Parameters.AddWithValue("original_tariff_snapshot_id", resolved.TariffSnapshotId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("service_identity_id", Guid.Parse("12000000-0000-0000-0000-000000000001"));

        await command.ExecuteNonQueryAsync();

        return new AppliedPayableBasisFixture(validationId, null, appliedTariffSnapshotId);
    }

    private static async Task<long> CountPaymentAttemptsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task ExpireTariffSnapshotAsync(Guid tariffSnapshotId)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET
                expires_at = NOW() - INTERVAL '1 minute',
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);

        var affected = await command.ExecuteNonQueryAsync();
        affected.Should().Be(1);
    }

    private static async Task ExpireTariffSnapshotStatusAsync(Guid tariffSnapshotId)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET
                snapshot_status = 'EXPIRED'::core.tariff_snapshot_status_enum,
                expires_at = NOW() - INTERVAL '1 minute',
                consumed_at = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id
              AND consumed_at IS NULL;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);

        var affected = await command.ExecuteNonQueryAsync();
        affected.Should().Be(1);
    }

    private static async Task<long> CountPaymentConfirmationsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM core.payment_confirmations AS pc
            INNER JOIN core.payment_attempts AS pa
                ON pa.payment_attempt_id = pc.payment_attempt_id
            WHERE pa.parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> CountExitAuthorizationsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM core.exit_authorizations
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<TariffState> ReadTariffStateAsync(
        Guid originalTariffSnapshotId,
        Guid appliedTariffSnapshotId)
    {
        const string sql = """
            SELECT
                original.snapshot_status::text AS original_status,
                original.gross_amount AS original_gross_amount,
                original.net_amount AS original_net_amount,
                applied.snapshot_status::text AS applied_status,
                applied.net_amount AS applied_net_amount
            FROM core.tariff_snapshots AS original
            CROSS JOIN core.tariff_snapshots AS applied
            WHERE original.tariff_snapshot_id = @original_tariff_snapshot_id
              AND applied.tariff_snapshot_id = @applied_tariff_snapshot_id;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("original_tariff_snapshot_id", originalTariffSnapshotId);
        command.Parameters.AddWithValue("applied_tariff_snapshot_id", appliedTariffSnapshotId);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        return new TariffState(
            reader.GetString(reader.GetOrdinal("original_status")),
            reader.GetDecimal(reader.GetOrdinal("original_gross_amount")),
            reader.GetDecimal(reader.GetOrdinal("original_net_amount")),
            reader.GetString(reader.GetOrdinal("applied_status")),
            reader.GetDecimal(reader.GetOrdinal("applied_net_amount")));
    }

    private static async Task<PaymentAttemptState?> ReadPaymentAttemptAsync(Guid paymentAttemptId)
    {
        const string sql = """
            SELECT
                parking_session_id,
                tariff_snapshot_id,
                amount
            FROM core.payment_attempts
            WHERE payment_attempt_id = @payment_attempt_id
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentAttemptState(
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetDecimal(reader.GetOrdinal("amount")));
    }

    private sealed record AppliedPayableBasisFixture(
        Guid ValidationId,
        Guid? ApplicationId,
        Guid AppliedTariffSnapshotId);

    private sealed record TariffState(
        string OriginalStatus,
        decimal OriginalGrossAmount,
        decimal OriginalNetAmount,
        string AppliedStatus,
        decimal AppliedNetAmount);

    private sealed record PaymentAttemptState(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        decimal Amount);
}
