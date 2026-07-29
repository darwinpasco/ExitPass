using Microsoft.Extensions.Logging.Abstractions;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using NSubstitute;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.InitiateProviderPayment;

/// <summary>
/// Unit tests for <see cref="InitiateProviderPaymentHandler"/>.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider session creation must remain traceable to a single PaymentAttempt.
/// - POA may initiate provider flows but may not finalize PaymentAttempt state.
/// </summary>
public sealed class InitiateProviderPaymentHandlerTests
{
    private static readonly Guid StablePaymentAttemptId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>
    /// Verifies that the handler returns a redirect handoff for a PayMongo Checkout Session flow.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ReturnsRedirectHandoff_ForPayMongoCheckout()
    {
        var registry = Substitute.For<IPaymentProviderRegistry>();
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = Substitute.For<IPaymentProviderAdapter>();

        var paymentAttemptId = Guid.NewGuid();

        registry.GetRequired("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION").Returns(adapter);
        repository.TryReserveInitiationAsync(
                Arg.Any<ProviderSessionInitiationReservation>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var reservation = call.Arg<ProviderSessionInitiationReservation>();
                return new ProviderSessionInitiationReservationResult(
                    ProviderSessionInitiationReservationOutcome.Acquired,
                    ReservedProviderSession(reservation));
            });
        repository.CompleteInitiationAsync(
                Arg.Any<Guid>(),
                Arg.Any<ProviderSessionRecord>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        adapter.CreatePaymentSessionAsync(
                Arg.Any<CreateProviderPaymentSessionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateProviderPaymentSessionResult(
                "cs_test_123",
                "cs_test_123",
                "PENDING_PROVIDER",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://checkout.paymongo.test/session",
                    "GET",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(30)),
                DateTimeOffset.UtcNow.AddMinutes(30),
                "{\"data\":{}}"));

        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var request = new InitiateProviderPaymentRequest(
            paymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            15000,
            "PHP",
            "ExitPass parking payment",
            Guid.NewGuid().ToString("N"),
            "https://example.test/success",
            "https://example.test/failure",
            "https://example.test/cancel",
            "https://example.test/webhook",
            new Dictionary<string, string> { ["payment_attempt_id"] = paymentAttemptId.ToString() });

        var response = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(paymentAttemptId, response.PaymentAttemptId);
        Assert.Equal("PAYMONGO", response.ProviderCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", response.ProviderProduct);
        Assert.Equal("cs_test_123", response.ProviderSessionId);
        Assert.Equal(ProviderHandoffType.Redirect, response.ProviderHandoff.Type);
        Assert.Equal("https://checkout.paymongo.test/session", response.ProviderHandoff.RedirectUrl);
    }

    /// <summary>
    /// Verifies that the handler completes the durable provider-session reservation after successful provider creation.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CompletesProviderSessionReservation()
    {
        var registry = Substitute.For<IPaymentProviderRegistry>();
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = Substitute.For<IPaymentProviderAdapter>();

        var paymentAttemptId = Guid.NewGuid();

        registry.GetRequired("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION").Returns(adapter);
        repository.TryReserveInitiationAsync(
                Arg.Any<ProviderSessionInitiationReservation>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var reservation = call.Arg<ProviderSessionInitiationReservation>();
                return new ProviderSessionInitiationReservationResult(
                    ProviderSessionInitiationReservationOutcome.Acquired,
                    ReservedProviderSession(reservation));
            });
        repository.CompleteInitiationAsync(
                Arg.Any<Guid>(),
                Arg.Any<ProviderSessionRecord>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        adapter.CreatePaymentSessionAsync(
                Arg.Any<CreateProviderPaymentSessionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateProviderPaymentSessionResult(
                "cs_test_456",
                "cs_test_456",
                "PENDING_PROVIDER",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://checkout.paymongo.test/another-session",
                    "GET",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(30)),
                DateTimeOffset.UtcNow.AddMinutes(30),
                "{\"data\":{\"id\":\"cs_test_456\"}}"));

        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var request = new InitiateProviderPaymentRequest(
            paymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            25000,
            "PHP",
            "ExitPass parking payment",
            Guid.NewGuid().ToString("N"),
            "https://example.test/success",
            "https://example.test/failure",
            "https://example.test/cancel",
            "https://example.test/webhook",
            new Dictionary<string, string> { ["payment_attempt_id"] = paymentAttemptId.ToString() });

        await handler.HandleAsync(request, CancellationToken.None);

        await repository.Received(1).CompleteInitiationAsync(
            Arg.Any<Guid>(),
            Arg.Is<ProviderSessionRecord>(x =>
                x.PaymentAttemptId == paymentAttemptId &&
                x.ProviderCode == "PAYMONGO" &&
                x.ProviderProduct == "PAYMONGO_CHECKOUT_SESSION" &&
                x.ProviderSessionId == "cs_test_456" &&
                x.SessionStatus == "PENDING_PROVIDER"),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().AddAsync(
            Arg.Any<ProviderSessionRecord>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies an existing durable provider session is replayed without provider initiation or duplicate persistence.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenProviderSessionAlreadyExists_ReconstructsResponseWithoutProviderCall()
    {
        var registry = new FakePaymentProviderRegistry(new CapturingPaymentProviderAdapter());
        var repository = new InMemoryProviderSessionRepository();
        repository.Seed(ExistingProviderSession("cs_test_existing", "https://checkout.paymongo.test/existing", "PENDING"));
        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var response = await handler.HandleAsync(DefaultRequest(), CancellationToken.None);

        Assert.Equal(StablePaymentAttemptId, response.PaymentAttemptId);
        Assert.Equal("cs_test_existing", response.ProviderSessionId);
        Assert.Equal("https://checkout.paymongo.test/existing", response.ProviderHandoff.RedirectUrl);
        Assert.Equal(0, registry.Adapter.CreateCallCount);
        Assert.Equal(0, repository.AddCallCount);
        Assert.Equal(0, repository.CompleteCallCount);
    }

    /// <summary>
    /// Verifies concurrent identical initiations converge on one provider call and one durable provider session.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenConcurrentRequestsTargetSameAttempt_CreatesOneProviderSession()
    {
        var adapter = new CapturingPaymentProviderAdapter();
        var registry = new FakePaymentProviderRegistry(adapter);
        var repository = new InMemoryProviderSessionRepository();
        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var first = handler.HandleAsync(DefaultRequest(), CancellationToken.None);
        var second = handler.HandleAsync(DefaultRequest(), CancellationToken.None);

        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, response =>
        {
            Assert.Equal(StablePaymentAttemptId, response.PaymentAttemptId);
            Assert.Equal("cs_test_created_001", response.ProviderSessionId);
            Assert.Equal("https://checkout.paymongo.test/created", response.ProviderHandoff.RedirectUrl);
        });
        Assert.Equal(1, adapter.CreateCallCount);
        Assert.Equal(1, repository.CompleteCallCount);
        Assert.Single(repository.Records);
    }

    /// <summary>
    /// Verifies independently constructed handlers converge through durable repository reservation.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenIndependentHandlersRace_UsesOneDurableProviderInitiation()
    {
        var adapter = new CapturingPaymentProviderAdapter();
        var registry = new FakePaymentProviderRegistry(adapter);
        var repository = new InMemoryProviderSessionRepository();
        var firstHandler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);
        var secondHandler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var first = firstHandler.HandleAsync(DefaultRequest(), CancellationToken.None);
        var second = secondHandler.HandleAsync(DefaultRequest(), CancellationToken.None);

        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, response =>
        {
            Assert.Equal(StablePaymentAttemptId, response.PaymentAttemptId);
            Assert.Equal("cs_test_created_001", response.ProviderSessionId);
            Assert.Equal("https://checkout.paymongo.test/created", response.ProviderHandoff.RedirectUrl);
        });
        Assert.Equal(1, adapter.CreateCallCount);
        Assert.Equal(1, repository.AcquiredReservationCount);
        Assert.Equal(1, repository.ExistingReservationCount);
        Assert.Equal(1, repository.CompleteCallCount);
        Assert.Single(repository.Records);
    }

    /// <summary>
    /// Verifies a crash-window reservation without provider handoff fails closed without calling the provider.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenIncompleteReservationExists_ReturnsPendingWithoutProviderCall()
    {
        var adapter = new CapturingPaymentProviderAdapter();
        var registry = new FakePaymentProviderRegistry(adapter);
        var repository = new InMemoryProviderSessionRepository();
        repository.Seed(new ProviderSessionRecord(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            StablePaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            string.Empty,
            null,
            "CREATED",
            null,
            null,
            null,
            "webpay-test-idempotency",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "{}",
            "{}",
            DateTimeOffset.UtcNow,
            15000,
            "PHP"));
        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var exception = await Assert.ThrowsAsync<ProviderSessionInitiationPendingException>(
            () => handler.HandleAsync(DefaultRequest(), CancellationToken.None));

        Assert.Equal("PAYMENT_PROVIDER_HANDOFF_IN_PROGRESS", exception.ErrorCode);
        Assert.Equal(StablePaymentAttemptId, exception.PaymentAttemptId);
        Assert.Equal(0, adapter.CreateCallCount);
        Assert.Equal(0, repository.CompleteCallCount);
    }

    /// <summary>
    /// Verifies stale incomplete reservations require reconciliation instead of a blind provider retry.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenStaleIncompleteReservationExists_ReturnsReconciliationWithoutProviderCall()
    {
        var adapter = new CapturingPaymentProviderAdapter();
        var registry = new FakePaymentProviderRegistry(adapter);
        var repository = new InMemoryProviderSessionRepository();
        repository.Seed(new ProviderSessionRecord(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            StablePaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            string.Empty,
            null,
            "CREATED",
            null,
            null,
            null,
            "webpay-test-idempotency",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "{}",
            "{}",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            15000,
            "PHP"));
        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var exception = await Assert.ThrowsAsync<ProviderSessionInitiationPendingException>(
            () => handler.HandleAsync(DefaultRequest(), CancellationToken.None));

        Assert.Equal("PAYMENT_PROVIDER_HANDOFF_RECONCILIATION_REQUIRED", exception.ErrorCode);
        Assert.Equal(StablePaymentAttemptId, exception.PaymentAttemptId);
        Assert.Equal(0, adapter.CreateCallCount);
        Assert.Equal(0, repository.CompleteCallCount);
    }

    private static InitiateProviderPaymentRequest DefaultRequest()
    {
        return new InitiateProviderPaymentRequest(
            StablePaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            15000,
            "PHP",
            "ExitPass parking payment",
            "webpay-test-idempotency",
            "https://example.test/success",
            "https://example.test/failure",
            "https://example.test/cancel",
            "https://example.test/webhook",
            new Dictionary<string, string>
            {
                ["payment_attempt_id"] = StablePaymentAttemptId.ToString(),
                ["correlation_id"] = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
            });
    }

    private static ProviderSessionRecord ExistingProviderSession(
        string providerSessionId,
        string checkoutUrl,
        string sessionStatus)
    {
        return new ProviderSessionRecord(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            StablePaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            providerSessionId,
            "pi_test_existing",
            sessionStatus,
            checkoutUrl,
            null,
            DateTimeOffset.UtcNow.AddMinutes(30),
            "webpay-test-idempotency",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "{}",
            "{}",
            DateTimeOffset.UtcNow,
            15000,
            "PHP");
    }

    private static ProviderSessionRecord ReservedProviderSession(ProviderSessionInitiationReservation reservation)
    {
        return new ProviderSessionRecord(
            reservation.ProviderSessionRecordId,
            reservation.PaymentAttemptId,
            "PAYMONGO",
            reservation.ProviderProduct,
            string.Empty,
            null,
            "CREATED",
            null,
            null,
            null,
            reservation.IdempotencyKey,
            reservation.CorrelationId,
            reservation.RequestPayloadJson,
            "{}",
            reservation.CreatedAtUtc,
            reservation.AmountMinorUnits,
            reservation.CurrencyCode);
    }

    private sealed class FakePaymentProviderRegistry : IPaymentProviderRegistry
    {
        public FakePaymentProviderRegistry(CapturingPaymentProviderAdapter adapter)
        {
            Adapter = adapter;
        }

        public CapturingPaymentProviderAdapter Adapter { get; }

        public IPaymentProviderAdapter GetRequired(string providerCode, string providerProduct)
        {
            Assert.Equal("PAYMONGO", providerCode);
            Assert.Equal("PAYMONGO_CHECKOUT_SESSION", providerProduct);
            return Adapter;
        }
    }

    private sealed class CapturingPaymentProviderAdapter : IPaymentProviderAdapter
    {
        private int _createCallCount;

        public string ProviderCode => "PAYMONGO";

        public string ProviderProduct => "PAYMONGO_CHECKOUT_SESSION";

        public int CreateCallCount => _createCallCount;

        public async Task<CreateProviderPaymentSessionResult> CreatePaymentSessionAsync(
            CreateProviderPaymentSessionCommand command,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCallCount);
            await Task.Delay(50, cancellationToken);
            return new CreateProviderPaymentSessionResult(
                "cs_test_created_001",
                "pi_test_created_001",
                "PENDING",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://checkout.paymongo.test/created",
                    "GET",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(30)),
                DateTimeOffset.UtcNow.AddMinutes(30),
                "{\"data\":{\"id\":\"cs_test_created_001\"}}");
        }

        public Task<ProviderWebhookVerificationResult> VerifyWebhookAsync(
            ProviderWebhookRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class InMemoryProviderSessionRepository : IProviderSessionRepository
    {
        private readonly object _gate = new();
        private readonly List<ProviderSessionRecord> _records = new();

        public int AddCallCount { get; private set; }

        public int CompleteCallCount { get; private set; }

        public int AcquiredReservationCount { get; private set; }

        public int ExistingReservationCount { get; private set; }

        public IReadOnlyList<ProviderSessionRecord> Records
        {
            get
            {
                lock (_gate)
                {
                    return _records.ToArray();
                }
            }
        }

        public void Seed(ProviderSessionRecord record)
        {
            lock (_gate)
            {
                _records.Add(record);
            }
        }

        public Task<ProviderSessionInitiationReservationResult> TryReserveInitiationAsync(
            ProviderSessionInitiationReservation reservation,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var existing = _records.LastOrDefault(record => record.PaymentAttemptId == reservation.PaymentAttemptId);
                if (existing is not null)
                {
                    ExistingReservationCount++;
                    return Task.FromResult(new ProviderSessionInitiationReservationResult(
                        ProviderSessionInitiationReservationOutcome.Existing,
                        existing));
                }

                var reserved = ReservedProviderSession(reservation);
                _records.Add(reserved);
                AcquiredReservationCount++;
                return Task.FromResult(new ProviderSessionInitiationReservationResult(
                    ProviderSessionInitiationReservationOutcome.Acquired,
                    reserved));
            }
        }

        public Task CompleteInitiationAsync(
            Guid providerSessionRecordId,
            ProviderSessionRecord record,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var index = _records.FindIndex(candidate => candidate.ProviderSessionRecordId == providerSessionRecordId);
                if (index < 0)
                {
                    throw new InvalidOperationException("Reservation not found.");
                }

                CompleteCallCount++;
                _records[index] = record with { ProviderSessionRecordId = providerSessionRecordId };
            }

            return Task.CompletedTask;
        }

        public Task AddAsync(ProviderSessionRecord record, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                AddCallCount++;
                _records.Add(record);
            }

            return Task.CompletedTask;
        }

        public Task<ProviderSessionRecord?> FindByProviderSessionIdAsync(
            string providerCode,
            string providerSessionId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_records.FirstOrDefault(record =>
                    string.Equals(record.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.ProviderSessionId, providerSessionId, StringComparison.Ordinal)));
            }
        }

        public Task<ProviderSessionRecord?> FindLatestActiveByParkingSessionIdAsync(
            Guid parkingSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ProviderSessionRecord?>(null);
        }

        public Task<ProviderSessionRecord?> FindLatestByPaymentAttemptIdAsync(
            Guid paymentAttemptId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_records.LastOrDefault(record => record.PaymentAttemptId == paymentAttemptId));
            }
        }

        public Task MarkWebhookOutcomeAsync(
            string providerCode,
            string providerSessionId,
            string? providerReference,
            string sessionStatus,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
