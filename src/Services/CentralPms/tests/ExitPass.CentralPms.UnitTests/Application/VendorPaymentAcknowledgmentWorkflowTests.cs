using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for post-finality Vendor PMS payment acknowledgments.
/// </summary>
public sealed class VendorPaymentAcknowledgmentWorkflowTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("27900000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentConfirmationId = Guid.Parse("27900000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("27900000-0000-0000-0000-000000000003");
    private static readonly Guid AcknowledgmentId = Guid.Parse("27900000-0000-0000-0000-000000000004");
    private static readonly Guid CorrelationId = Guid.Parse("27900000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-18T01:30:00Z");
    private static readonly DateTimeOffset FeeTime = DateTimeOffset.Parse("2026-06-18T01:31:00Z");

    [Fact]
    public async Task ProcessAsync_WhenConfirmDisabled_MarksSkippedAndDoesNotCallVendor()
    {
        var repository = new FakeAcknowledgmentRepository { Basis = Basis() };
        var vendorClient = new FakeVendorClient();
        var sut = CreateSut(repository, vendorClient, enabled: false);

        await sut.ProcessAsync(Command(), CancellationToken.None);

        repository.Record.Should().NotBeNull();
        repository.Record!.AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.SkippedDisabled);
        repository.Record.VendorCode.Should().Be("CONFIRM_DISABLED");
        repository.Record.VendorMessage.Should().Be("HIKCENTRAL_CONFIRM_PAYMENT_ENABLED is false.");
        repository.CreatePendingCalls.Should().Be(1);
        vendorClient.ConfirmParkingFeeCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_WhenConfirmSucceeds_MarksConfirmedWithVendorEvidence()
    {
        var repository = new FakeAcknowledgmentRepository { Basis = Basis(cardNum: "CARD-279") };
        var vendorClient = new FakeVendorClient
        {
            ConfirmationResponse = new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.Confirmed,
                new VendorParkingFeeConfirmationDto(5000, "PHP", FeeTime),
                "0",
                false,
                CorrelationId)
        };
        var sut = CreateSut(repository, vendorClient, enabled: true);

        await sut.ProcessAsync(Command(), CancellationToken.None);

        vendorClient.ConfirmParkingFeeCalls.Should().Be(1);
        vendorClient.LastConfirmationRequest.Should().NotBeNull();
        vendorClient.LastConfirmationRequest!.TicketReference.Should().Be("CARD-279");
        vendorClient.LastConfirmationRequest.PlateNumber.Should().BeNull();
        vendorClient.LastConfirmationRequest.ImmediatelyLeave.Should().Be(0);
        vendorClient.LastConfirmationRequest.AmountMinor.Should().Be(5000);
        repository.Record.Should().NotBeNull();
        repository.Record!.AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.Confirmed);
        repository.Record.VendorCode.Should().Be("0");
        repository.Record.VendorMessage.Should().Be("Vendor PMS confirmed paid-state acknowledgment.");
        repository.Record.ConfirmedFeeMinorUnits.Should().Be(5000);
        repository.Record.VendorConfirmedAt.Should().Be(FeeTime);
        repository.Record.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_WhenVendorFailureIsRetryable_MarksRetryPending()
    {
        var repository = new FakeAcknowledgmentRepository { Basis = Basis() };
        var vendorClient = new FakeVendorClient
        {
            ConfirmationResponse = new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.UnavailableRetryable,
                null,
                "VENDOR_UNAVAILABLE",
                true,
                CorrelationId)
        };
        var sut = CreateSut(repository, vendorClient, enabled: true);

        await sut.ProcessAsync(Command(), CancellationToken.None);

        repository.Record.Should().NotBeNull();
        repository.Record!.AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.RetryPending);
        repository.Record.VendorCode.Should().Be("VENDOR_UNAVAILABLE");
        repository.Record.VendorMessage.Should().Be("Vendor PMS confirmation returned UnavailableRetryable.");
        repository.Record.NextRetryAt.Should().Be(Now.AddMinutes(5));
        repository.Record.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_WhenAcknowledgmentAlreadyConfirmed_DoesNotCallVendorAgain()
    {
        var repository = new FakeAcknowledgmentRepository
        {
            Basis = Basis(),
            Record = Record(VendorPaymentAcknowledgmentStatuses.Confirmed)
        };
        var vendorClient = new FakeVendorClient();
        var sut = CreateSut(repository, vendorClient, enabled: true);

        await sut.ProcessAsync(Command(), CancellationToken.None);

        repository.CreatePendingCalls.Should().Be(0);
        vendorClient.ConfirmParkingFeeCalls.Should().Be(0);
        repository.Record!.AcknowledgmentStatus.Should().Be(VendorPaymentAcknowledgmentStatuses.Confirmed);
    }

    private static VendorPaymentAcknowledgmentWorkflow CreateSut(
        FakeAcknowledgmentRepository repository,
        FakeVendorClient vendorClient,
        bool enabled)
    {
        var guard = new FakeConfirmationGuard(enabled);
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        return new VendorPaymentAcknowledgmentWorkflow(
            repository,
            vendorClient,
            guard,
            clock,
            NullLogger<VendorPaymentAcknowledgmentWorkflow>.Instance);
    }

    private static VendorPaymentAcknowledgmentWorkflowCommand Command() =>
        new(PaymentAttemptId, PaymentConfirmationId, ParkingSessionId, CorrelationId);

    private static VendorPaymentAcknowledgmentBasis Basis(string? cardNum = "CARD-279") =>
        new(
            PaymentAttemptId,
            PaymentConfirmationId,
            ParkingSessionId,
            "HIKCENTRAL",
            "HIK:CARD-279",
            "TICKET-279",
            cardNum,
            5000,
            "PHP");

    private static VendorPaymentAcknowledgmentRecord Record(string status) =>
        new(
            AcknowledgmentId,
            PaymentAttemptId,
            PaymentConfirmationId,
            ParkingSessionId,
            "HIKCENTRAL",
            "HIK:CARD-279",
            "TICKET-279",
            "CARD-279",
            status,
            null,
            null,
            5000,
            "PHP",
            null,
            null,
            0,
            null,
            null,
            "vendor-ack-279",
            CorrelationId,
            Now,
            Now);

    private sealed class FakeAcknowledgmentRepository : IVendorPaymentAcknowledgmentRepository
    {
        public VendorPaymentAcknowledgmentBasis? Basis { get; init; }

        public VendorPaymentAcknowledgmentRecord? Record { get; set; }

        public int CreatePendingCalls { get; private set; }

        public Task<VendorPaymentAcknowledgmentBasis?> LoadBasisAsync(
            Guid paymentAttemptId,
            Guid paymentConfirmationId,
            Guid parkingSessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Basis);

        public Task<VendorPaymentAcknowledgmentRecord> CreatePendingAsync(
            CreateVendorPaymentAcknowledgmentCommand command,
            CancellationToken cancellationToken)
        {
            CreatePendingCalls++;
            if (Record is not null)
            {
                throw new VendorPaymentAcknowledgmentConflictException(
                    "VENDOR_PAYMENT_ACKNOWLEDGMENT_ALREADY_EXISTS",
                    "Duplicate acknowledgment.");
            }

            Record = new VendorPaymentAcknowledgmentRecord(
                AcknowledgmentId,
                command.PaymentAttemptId,
                command.PaymentConfirmationId,
                command.ParkingSessionId,
                command.VendorSystemCode,
                command.VendorSessionRef,
                command.TicketNumber,
                command.CardNum,
                VendorPaymentAcknowledgmentStatuses.Pending,
                null,
                null,
                command.RequestFeeMinorUnits,
                command.RequestCurrencyCode,
                null,
                null,
                0,
                null,
                null,
                command.IdempotencyKey,
                command.CorrelationId,
                command.CreatedAt,
                command.CreatedAt);

            return Task.FromResult(Record);
        }

        public Task<VendorPaymentAcknowledgmentRecord> MarkConfirmedAsync(
            MarkVendorPaymentAcknowledgmentConfirmedCommand command,
            CancellationToken cancellationToken)
        {
            Record = Record! with
            {
                AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.Confirmed,
                VendorCode = command.VendorCode,
                VendorMessage = command.VendorMessage,
                ConfirmedFeeMinorUnits = command.ConfirmedFeeMinorUnits,
                VendorConfirmedAt = command.VendorConfirmedAt,
                LastAttemptedAt = command.UpdatedAt,
                NextRetryAt = null,
                AttemptCount = Record.AttemptCount + 1,
                UpdatedAt = command.UpdatedAt
            };

            return Task.FromResult(Record);
        }

        public Task<VendorPaymentAcknowledgmentRecord> MarkFailedAsync(
            MarkVendorPaymentAcknowledgmentFailedCommand command,
            CancellationToken cancellationToken)
        {
            Record = Record! with
            {
                AcknowledgmentStatus = command.NextRetryAt is null
                    ? VendorPaymentAcknowledgmentStatuses.Failed
                    : VendorPaymentAcknowledgmentStatuses.RetryPending,
                VendorCode = command.VendorCode,
                VendorMessage = command.VendorMessage,
                LastAttemptedAt = command.LastAttemptedAt,
                NextRetryAt = command.NextRetryAt,
                AttemptCount = Record.AttemptCount + 1,
                UpdatedAt = command.UpdatedAt
            };

            return Task.FromResult(Record);
        }

        public Task<VendorPaymentAcknowledgmentRecord> MarkSkippedDisabledAsync(
            MarkVendorPaymentAcknowledgmentSkippedDisabledCommand command,
            CancellationToken cancellationToken)
        {
            Record = Record! with
            {
                AcknowledgmentStatus = VendorPaymentAcknowledgmentStatuses.SkippedDisabled,
                VendorCode = "CONFIRM_DISABLED",
                VendorMessage = command.VendorMessage,
                UpdatedAt = command.UpdatedAt
            };

            return Task.FromResult(Record);
        }

        public Task<IReadOnlyList<VendorPaymentAcknowledgmentRecord>> FindDueRetryPendingAsync(
            DateTimeOffset utcNow,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VendorPaymentAcknowledgmentRecord>>(Array.Empty<VendorPaymentAcknowledgmentRecord>());

        public Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
            Guid vendorPaymentAcknowledgmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Record);

        public Task<VendorPaymentAcknowledgmentRecord?> ReadByPaymentConfirmationAsync(
            Guid paymentConfirmationId,
            string vendorSystemCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Record is not null &&
                Record.PaymentConfirmationId == paymentConfirmationId &&
                string.Equals(Record.VendorSystemCode, vendorSystemCode, StringComparison.Ordinal)
                    ? Record
                    : null);

        public Task<VendorPaymentAcknowledgmentRecord?> ReadLatestByPaymentAttemptAsync(
            Guid paymentAttemptId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Record is not null && Record.PaymentAttemptId == paymentAttemptId
                    ? Record
                    : null);
    }

    private sealed class FakeVendorClient : IVendorPmsParkingResolutionClient
    {
        public VendorParkingFeeConfirmationResponse ConfirmationResponse { get; init; } =
            new(
                VendorParkingLookupStatus.Confirmed,
                new VendorParkingFeeConfirmationDto(5000, "PHP", FeeTime),
                "0",
                false,
                CorrelationId);

        public int ConfirmParkingFeeCalls { get; private set; }

        public VendorParkingFeeConfirmationRequest? LastConfirmationRequest { get; private set; }

        public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
            VendorParkingSessionLookupRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Payment acknowledgment workflow must not resolve sessions.");

        public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
            VendorTariffQuoteRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Payment acknowledgment workflow must not recalculate tariff.");

        public Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
            VendorParkingFeeConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            ConfirmParkingFeeCalls++;
            LastConfirmationRequest = request;
            return Task.FromResult(ConfirmationResponse);
        }
    }

    private sealed class FakeConfirmationGuard : IVendorPaymentConfirmationGuard
    {
        private readonly bool _enabled;

        public FakeConfirmationGuard(bool enabled)
        {
            _enabled = enabled;
        }

        public bool IsConfirmPaymentEnabled(string vendorSystemCode) => _enabled;

        public string DisabledMessage(string vendorSystemCode) =>
            "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED is false.";
    }
}
