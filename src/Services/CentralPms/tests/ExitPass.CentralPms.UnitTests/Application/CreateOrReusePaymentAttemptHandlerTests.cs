using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Sessions.Exceptions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.CentralPms.Domain.Tariffs.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="CreateOrReusePaymentAttemptHandler"/>.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.21 Audit and Traceability
/// - 10.7.4 One Active Payment Attempt Per Session
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 8.3 PaymentAttempt State Machine
///
/// Invariants Enforced:
/// - Only eligible parking sessions may enter the payment-attempt creation path
/// - Tariff snapshot existence is required before DB-backed create-or-reuse execution
/// - DB outcome codes must be translated into deterministic domain behavior
/// - Observability dependencies must not alter business outcomes under test
/// </summary>
public sealed class CreateOrReusePaymentAttemptHandlerTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentAttemptId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CorrelationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 3, 31, 5, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that a CREATED DB outcome maps into a created result with provider handoff details.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_returns_created_result_when_db_routine_creates_attempt()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.PaymentRequired));

        fixture.TariffSnapshotReadRepository
            .GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns(CreateTariffSnapshot());

        fixture.PaymentAttemptDbRoutineGateway
            .CreateOrReusePaymentAttemptAsync(
                Arg.Is<CreateOrReusePaymentAttemptDbRequest>(r =>
                    r.ParkingSessionId == ParkingSessionId &&
                    r.TariffSnapshotId == TariffSnapshotId &&
                    r.PaymentProviderCode == "GCASH" &&
                    r.IdempotencyKey == "idem-001" &&
                    r.CorrelationId == CorrelationId &&
                    r.RequestedBy == "unit-test" &&
                    r.RequestedAt == Now),
                Arg.Any<CancellationToken>())
            .Returns(new CreateOrReusePaymentAttemptDbResult
            {
                PaymentAttemptId = PaymentAttemptId,
                ParkingSessionId = ParkingSessionId,
                TariffSnapshotId = TariffSnapshotId,
                AttemptStatus = "INITIATED",
                PaymentProviderCode = "GCASH",
                WasReused = false,
                OutcomeCode = "CREATED",
                GrossAmountSnapshot = 100m,
                StatutoryDiscountSnapshot = 0m,
                CouponDiscountSnapshot = 0m,
                NetAmountDueSnapshot = 100m,
                CurrencyCode = "PHP",
                TariffVersionReference = "TVR-001",
                IdempotencyKey = "idem-001"
            });

        fixture.ProviderHandoffFactory
            .CreatePlaceholder(Arg.Is<PaymentProvider>(p => p.Code == "GCASH"), PaymentAttemptId)
            .Returns(new ProviderHandoffResult
            {
                Type = "REDIRECT",
                Url = "/payments/gcash/33333333-3333-3333-3333-333333333333",
                ExpiresAt = Now.AddMinutes(15)
            });

        var sut = fixture.CreateSut();

        var result = await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        result.PaymentAttemptId.Should().Be(PaymentAttemptId);
        result.ParkingSessionId.Should().Be(ParkingSessionId);
        result.TariffSnapshotId.Should().Be(TariffSnapshotId);
        result.AttemptStatus.Should().Be("INITIATED");
        result.PaymentProviderCode.Should().Be("GCASH");
        result.WasReused.Should().BeFalse();
        result.ProviderHandoff.Type.Should().Be("REDIRECT");
        result.ProviderHandoff.Url.Should().Be("/payments/gcash/33333333-3333-3333-3333-333333333333");
    }

    /// <summary>
    /// Verifies that a REUSED DB outcome maps into a reused result.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_returns_reused_result_when_db_routine_reuses_attempt()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.PaymentRequired));

        fixture.TariffSnapshotReadRepository
            .GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns(CreateTariffSnapshot());

        fixture.PaymentAttemptDbRoutineGateway
            .CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateOrReusePaymentAttemptDbResult
            {
                PaymentAttemptId = PaymentAttemptId,
                ParkingSessionId = ParkingSessionId,
                TariffSnapshotId = TariffSnapshotId,
                AttemptStatus = "INITIATED",
                PaymentProviderCode = "GCASH",
                WasReused = true,
                OutcomeCode = "REUSED",
                GrossAmountSnapshot = 100m,
                StatutoryDiscountSnapshot = 0m,
                CouponDiscountSnapshot = 0m,
                NetAmountDueSnapshot = 100m,
                CurrencyCode = "PHP",
                TariffVersionReference = "TVR-001",
                IdempotencyKey = "idem-001"
            });

        fixture.ProviderHandoffFactory
            .CreatePlaceholder(Arg.Any<PaymentProvider>(), Arg.Any<Guid>())
            .Returns(new ProviderHandoffResult
            {
                Type = "REDIRECT",
                Url = "/payments/gcash/33333333-3333-3333-3333-333333333333",
                ExpiresAt = Now.AddMinutes(15)
            });

        var sut = fixture.CreateSut();

        var result = await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        result.PaymentAttemptId.Should().Be(PaymentAttemptId);
        result.WasReused.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an active-attempt DB rejection becomes a deterministic conflict exception.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_throws_active_payment_attempt_already_exists_when_db_routine_rejects_conflict()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.PaymentRequired));

        fixture.TariffSnapshotReadRepository
            .GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns(CreateTariffSnapshot());

        fixture.PaymentAttemptDbRoutineGateway
            .CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateOrReusePaymentAttemptDbResult
            {
                PaymentAttemptId = PaymentAttemptId,
                ParkingSessionId = ParkingSessionId,
                TariffSnapshotId = TariffSnapshotId,
                AttemptStatus = "INITIATED",
                PaymentProviderCode = "GCASH",
                WasReused = false,
                OutcomeCode = "REJECTED_ACTIVE_ATTEMPT_EXISTS",
                FailureCode = "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                GrossAmountSnapshot = 100m,
                StatutoryDiscountSnapshot = 0m,
                CouponDiscountSnapshot = 0m,
                NetAmountDueSnapshot = 100m,
                CurrencyCode = "PHP",
                TariffVersionReference = "TVR-001",
                IdempotencyKey = "idem-002"
            });

        var sut = fixture.CreateSut();

        var act = async () => await sut.ExecuteAsync(CreateCommand("idem-002"), CancellationToken.None);

        await act.Should().ThrowAsync<ActivePaymentAttemptAlreadyExistsException>();
    }

    /// <summary>
    /// Verifies that a missing parking session is rejected before DB-backed create-or-reuse execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_throws_parking_session_not_found_when_session_does_not_exist()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns((ParkingSession?)null);

        var sut = fixture.CreateSut();

        var act = async () => await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        await act.Should().ThrowAsync<ParkingSessionNotFoundException>();
    }

    /// <summary>
    /// Verifies that a missing tariff snapshot is rejected before DB-backed create-or-reuse execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_throws_tariff_snapshot_not_found_when_snapshot_does_not_exist()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.PaymentRequired));

        fixture.TariffSnapshotReadRepository
            .GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns((TariffSnapshot?)null);

        var sut = fixture.CreateSut();

        var act = async () => await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        await act.Should().ThrowAsync<TariffSnapshotNotFoundException>();
    }

    /// <summary>
    /// Verifies that a non-eligible parking session is rejected before DB-backed create-or-reuse execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_throws_when_session_is_not_eligible_for_payment_attempt_creation()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.ExitAuthorized));

        var sut = fixture.CreateSut();

        var act = async () => await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not eligible for payment attempt creation*");
    }

    /// <summary>
    /// Verifies that an idempotency-conflict DB rejection becomes a deterministic exception.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_throws_idempotency_conflict_when_db_routine_rejects_semantic_conflict()
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.PaymentRequired));

        fixture.TariffSnapshotReadRepository
            .GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns(CreateTariffSnapshot());

        fixture.PaymentAttemptDbRoutineGateway
            .CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateOrReusePaymentAttemptDbResult
            {
                PaymentAttemptId = PaymentAttemptId,
                ParkingSessionId = ParkingSessionId,
                TariffSnapshotId = TariffSnapshotId,
                AttemptStatus = "INITIATED",
                PaymentProviderCode = "GCASH",
                WasReused = false,
                OutcomeCode = "REJECTED_IDEMPOTENCY_CONFLICT",
                FailureCode = "IDEMPOTENCY_CONFLICT",
                IdempotencyKey = "idem-001"
            });

        var sut = fixture.CreateSut();

        var act = async () => await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    /// <summary>
    /// Verifies that DB snapshot-rejection outcomes become deterministic tariff snapshot eligibility exceptions.
    /// </summary>
    /// <param name="outcomeCode">The DB outcome code to translate.</param>
    [Theory]
    [InlineData("REJECTED_SNAPSHOT_INVALID")]
    [InlineData("REJECTED_SNAPSHOT_EXPIRED")]
    [InlineData("REJECTED_SNAPSHOT_ALREADY_BOUND")]
    [InlineData("REJECTED_SNAPSHOT_SESSION_MISMATCH")]
    public async Task ExecuteAsync_throws_tariff_snapshot_not_eligible_when_db_routine_rejects_snapshot(string outcomeCode)
    {
        var fixture = CreateFixture();

        fixture.ParkingSessionReadRepository
            .GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateParkingSession(ParkingSessionStatus.PaymentRequired));

        fixture.TariffSnapshotReadRepository
            .GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns(CreateTariffSnapshot());

        fixture.PaymentAttemptDbRoutineGateway
            .CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateOrReusePaymentAttemptDbResult
            {
                OutcomeCode = outcomeCode,
                FailureCode = "TARIFF_SNAPSHOT_INVALID",
                ParkingSessionId = ParkingSessionId,
                TariffSnapshotId = TariffSnapshotId,
                IdempotencyKey = "idem-001"
            });

        var sut = fixture.CreateSut();

        var act = async () => await sut.ExecuteAsync(CreateCommand("idem-001"), CancellationToken.None);

        await act.Should().ThrowAsync<TariffSnapshotNotEligibleException>();
    }

    /// <summary>
    /// Creates a canonical command for create-or-reuse payment attempt tests.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to use in the test command.</param>
    /// <returns>A populated <see cref="CreateOrReusePaymentAttemptCommand"/>.</returns>
    private static CreateOrReusePaymentAttemptCommand CreateCommand(string idempotencyKey)
    {
        return new CreateOrReusePaymentAttemptCommand
        {
            ParkingSessionId = ParkingSessionId,
            TariffSnapshotId = TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = idempotencyKey,
            CorrelationId = CorrelationId,
            RequestedBy = "unit-test"
        };
    }

    /// <summary>
    /// Creates a canonical rehydrated parking session for tests.
    /// </summary>
    /// <param name="status">The parking session status to assign.</param>
    /// <returns>A rehydrated <see cref="ParkingSession"/> instance.</returns>
    private static ParkingSession CreateParkingSession(ParkingSessionStatus status)
    {
        return ParkingSession.Rehydrate(
            parkingSessionId: ParkingSessionId,
            siteGroupId: "SG-TEST-001",
            siteId: "SITE-TEST-001",
            vendorSystemCode: "VENDOR-TEST-001",
            vendorSessionRef: "VSESSION-001",
            identifierType: "PLATE",
            plateNumber: "ABC1234",
            ticketNumber: null,
            entryTimestamp: Now.AddHours(-2),
            sessionStatus: status);
    }

    /// <summary>
    /// Creates a canonical rehydrated tariff snapshot for tests.
    /// </summary>
    /// <returns>A rehydrated <see cref="TariffSnapshot"/> instance.</returns>
    private static TariffSnapshot CreateTariffSnapshot()
    {
        return TariffSnapshot.Rehydrate(
            tariffSnapshotId: TariffSnapshotId,
            parkingSessionId: ParkingSessionId,
            sourceType: TariffSnapshotSourceType.Base,
            grossAmount: 100m,
            statutoryDiscountAmount: 0m,
            couponDiscountAmount: 0m,
            netPayable: 100m,
            currencyCode: "PHP",
            baseFeeAmount: 100m,
            tariffVersionReference: "TVR-001",
            policyVersionReference: null,
            calculatedAt: Now,
            expiresAt: Now.AddMinutes(30),
            snapshotStatus: TariffSnapshotStatus.Active,
            supersedesTariffSnapshotId: null,
            consumedByPaymentAttemptId: null);
    }

    /// <summary>
    /// Creates a fully substituted fixture for create-or-reuse payment attempt handler tests.
    /// </summary>
    /// <returns>A populated <see cref="Fixture"/> record.</returns>
    private static Fixture CreateFixture()
    {
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        return new Fixture(
            Substitute.For<IParkingSessionReadRepository>(),
            Substitute.For<ITariffSnapshotReadRepository>(),
            Substitute.For<IPaymentAttemptDbRoutineGateway>(),
            Substitute.For<IPaymentAttemptCreationPolicy>(),
            Substitute.For<IProviderHandoffFactory>(),
            clock,
            new CentralPmsMetrics());
    }

    /// <summary>
    /// Encapsulates substituted dependencies and the system-under-test factory for these tests.
    /// </summary>
    /// <param name="ParkingSessionReadRepository">Parking session repository substitute.</param>
    /// <param name="TariffSnapshotReadRepository">Tariff snapshot repository substitute.</param>
    /// <param name="PaymentAttemptDbRoutineGateway">Payment attempt DB routine gateway substitute.</param>
    /// <param name="PaymentAttemptCreationPolicy">Payment attempt creation policy substitute.</param>
    /// <param name="ProviderHandoffFactory">Provider handoff factory substitute.</param>
    /// <param name="SystemClock">System clock substitute.</param>
    /// <param name="Metrics">Central PMS business metrics instance.</param>
    private sealed record Fixture(
        IParkingSessionReadRepository ParkingSessionReadRepository,
        ITariffSnapshotReadRepository TariffSnapshotReadRepository,
        IPaymentAttemptDbRoutineGateway PaymentAttemptDbRoutineGateway,
        IPaymentAttemptCreationPolicy PaymentAttemptCreationPolicy,
        IProviderHandoffFactory ProviderHandoffFactory,
        ISystemClock SystemClock,
        CentralPmsMetrics Metrics)
    {
        /// <summary>
        /// Creates the system under test using substituted dependencies, shared metrics, and no-op logging.
        /// </summary>
        /// <returns>A configured <see cref="CreateOrReusePaymentAttemptHandler"/> instance.</returns>
        public CreateOrReusePaymentAttemptHandler CreateSut()
        {
            return new CreateOrReusePaymentAttemptHandler(
                ParkingSessionReadRepository,
                TariffSnapshotReadRepository,
                PaymentAttemptDbRoutineGateway,
                PaymentAttemptCreationPolicy,
                ProviderHandoffFactory,
                SystemClock,
                Metrics,
                NullLogger<CreateOrReusePaymentAttemptHandler>.Instance);
        }
    }
}
