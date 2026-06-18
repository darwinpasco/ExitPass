using ExitPass.CentralPms.Application.Operations;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for read-only ticket session summary composition.
/// </summary>
public sealed class TicketSessionSummaryServiceTests
{
    private static readonly Guid CorrelationId = Guid.Parse("27500000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("27500000-0000-0000-0000-000000000002");
    private static readonly Guid PaymentAttemptId = Guid.Parse("27500000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset EntryTime = new(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 6, 18, 4, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the summary composes vendor session, tariff, and local payment status.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenTicketFound_ReturnsSummaryWithoutRequiringPlate()
    {
        var vendor = FakeVendorClient.FoundWithInlineQuote(plateNumber: string.Empty);
        var repository = FakeRepository.Found();
        var sut = CreateSut(vendor, repository);

        var result = await sut.GetAsync(Command(), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.Resolved);
        result.Summary.Should().NotBeNull();
        result.Summary!.TicketNumber.Should().Be("TICKET-275");
        result.Summary.PlateLicense.Should().Be("Unknown");
        result.Summary.ParkingInTime.Should().Be(EntryTime);
        result.Summary.ParkingDurationSeconds.Should().Be(10800);
        result.Summary.FeeMinorUnits.Should().Be(12550);
        result.Summary.CurrencyCode.Should().Be("PHP");
        result.Summary.FeeRuleIndexCode.Should().Be("RULE-001");
        result.Summary.FeeRuleName.Should().Be("Standard parking");
        result.Summary.VendorSystemCode.Should().Be("FAKE_PMS");
        result.Summary.VendorConfirmationCode.Should().Be("VENDOR_CONFIRMATION_STATUS_UNAVAILABLE");
        result.Summary.VendorMessage.Should().Be("Vendor session and tariff summary resolved.");
        result.Summary.PaymentAttemptId.Should().Be(PaymentAttemptId);
        result.Summary.PaymentStatus.Should().Be("Paid");
        result.Summary.PaymentConfirmationStatus.Should().Be("RECORDED");
        result.Summary.VendorConfirmationStatus.Should().BeNull();
        result.Summary.VendorConfirmationTimestamp.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE" &&
            diagnostic.VendorSystemCode == "FAKE_PMS" &&
            diagnostic.VendorConfirmationCode == "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE" &&
            diagnostic.CorrelationId == CorrelationId);
        vendor.ResolveSessionCalls.Should().Be(1);
        vendor.ResolveTariffCalls.Should().Be(0);
        vendor.ConfirmParkingFeeCalls.Should().Be(0);
    }

    /// <summary>
    /// Verifies the summary reads durable Vendor PMS acknowledgment status when it exists locally.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenDurableVendorAcknowledgmentExists_ReturnsVendorConfirmationStatus()
    {
        var vendor = FakeVendorClient.FoundWithInlineQuote("Unknown");
        var repository = FakeRepository.FoundWithDurableVendorConfirmation();
        var sut = CreateSut(vendor, repository);

        var result = await sut.GetAsync(Command(), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.Resolved);
        result.Summary.Should().NotBeNull();
        result.Summary!.VendorSystemCode.Should().Be("HIKCENTRAL");
        result.Summary.VendorConfirmationStatus.Should().Be("CONFIRMED");
        result.Summary.VendorConfirmationCode.Should().Be("0");
        result.Summary.VendorConfirmationTimestamp.Should().Be(CalculatedAt);
        result.Summary.VendorMessage.Should().Be("Success");
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "0" &&
            diagnostic.Message == "Vendor payment confirmation status was read from durable Central PMS acknowledgment state." &&
            diagnostic.VendorSystemCode == "HIKCENTRAL");
        vendor.ConfirmParkingFeeCalls.Should().Be(0);
    }

    /// <summary>
    /// Verifies tariff calculation is read only and uses the adapter quote path when not inlined.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenSessionHasNoInlineQuote_CallsTariffLookupOnly()
    {
        var vendor = FakeVendorClient.FoundWithSeparateQuote();
        var sut = CreateSut(vendor, FakeRepository.NotFound());

        var result = await sut.GetAsync(Command(cardNum: "TICKET-275"), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.Resolved);
        result.Summary!.FeeMinorUnits.Should().Be(15000);
        result.Summary.CardNum.Should().Be("TICKET-275");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "LOCAL_PAYMENT_STATUS_UNAVAILABLE");
        vendor.ResolveSessionCalls.Should().Be(1);
        vendor.ResolveTariffCalls.Should().Be(1);
        vendor.ConfirmParkingFeeCalls.Should().Be(0);
    }

    /// <summary>
    /// Verifies invalid ticket input is rejected before adapter calls.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenTicketMissing_ReturnsInvalidRequest()
    {
        var vendor = FakeVendorClient.FoundWithInlineQuote("ABC1234");
        var sut = CreateSut(vendor, FakeRepository.NotFound());

        var result = await sut.GetAsync(Command(ticketNumber: " ", cardNum: null), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.InvalidRequest);
        result.ErrorCode.Should().Be("INVALID_TICKET_SESSION_SUMMARY_REQUEST");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "TICKET_IDENTIFIER_REQUIRED");
        vendor.ResolveSessionCalls.Should().Be(0);
        vendor.ResolveTariffCalls.Should().Be(0);
    }

    /// <summary>
    /// Verifies deterministic not-found mapping.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenVendorNotFound_ReturnsNotFound()
    {
        var sut = CreateSut(FakeVendorClient.NotFound(), FakeRepository.NotFound());

        var result = await sut.GetAsync(Command(), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.NotFound);
        result.ErrorCode.Should().Be("TICKET_SESSION_NOT_FOUND");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Source == "vendor-session-lookup");
    }

    /// <summary>
    /// Verifies deterministic tariff calculation failure diagnostics.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenTariffCalculationFails_ReturnsVendorError()
    {
        var sut = CreateSut(FakeVendorClient.TariffRejected(), FakeRepository.NotFound());

        var result = await sut.GetAsync(Command(), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.VendorError);
        result.ErrorCode.Should().Be("VENDOR_TARIFF_CALCULATION_FAILED");
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "VENDOR_TARIFF_REJECTED" &&
            diagnostic.Source == "vendor-tariff-calculation" &&
            diagnostic.VendorConfirmationCode == "VENDOR_TARIFF_REJECTED" &&
            diagnostic.CorrelationId == CorrelationId);
    }

    /// <summary>
    /// Verifies deterministic local ambiguity mapping.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenLocalTicketIsAmbiguous_ReturnsAmbiguous()
    {
        var sut = CreateSut(FakeVendorClient.FoundWithInlineQuote("ABC1234"), FakeRepository.Ambiguous());

        var result = await sut.GetAsync(Command(), CancellationToken.None);

        result.Outcome.Should().Be(TicketSessionSummaryOutcome.Ambiguous);
        result.ErrorCode.Should().Be("AMBIGUOUS_TICKET_SESSION");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "LOCAL_TICKET_AMBIGUOUS");
    }

    private static TicketSessionSummaryService CreateSut(
        IVendorPmsParkingResolutionClient vendorClient,
        ITicketSessionSummaryReadRepository repository) =>
        new(vendorClient, repository, NullLogger<TicketSessionSummaryService>.Instance);

    private static TicketSessionSummaryCommand Command(
        string? ticketNumber = "TICKET-275",
        string? cardNum = null) =>
        new(
            ticketNumber,
            cardNum,
            SiteId: Guid.Parse("27500000-0000-0000-0000-000000000004"),
            SiteGroupId: Guid.Parse("27500000-0000-0000-0000-000000000005"),
            CorrelationId);

    private sealed class FakeVendorClient : IVendorPmsParkingResolutionClient
    {
        private readonly VendorParkingSessionLookupResponse _sessionResponse;
        private readonly VendorTariffQuoteResponse _tariffResponse;

        private FakeVendorClient(
            VendorParkingSessionLookupResponse sessionResponse,
            VendorTariffQuoteResponse tariffResponse)
        {
            _sessionResponse = sessionResponse;
            _tariffResponse = tariffResponse;
        }

        public int ResolveSessionCalls { get; private set; }

        public int ResolveTariffCalls { get; private set; }

        public int ConfirmParkingFeeCalls { get; private set; }

        public static FakeVendorClient FoundWithInlineQuote(string plateNumber)
        {
            var quote = new VendorTariffQuoteDto(12550, "php", "RULE-001", "Standard parking", CalculatedAt);
            var session = new VendorParkingSessionDto(
                "FAKE-PMS",
                "TICKET-275",
                plateNumber,
                EntryTime,
                10800,
                "PAYMENT_REQUIRED",
                quote);

            return new FakeVendorClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.Found, session, null, false, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.Found, quote, null, false, CorrelationId));
        }

        public static FakeVendorClient FoundWithSeparateQuote()
        {
            var session = new VendorParkingSessionDto(
                "FAKE-PMS",
                "TICKET-275",
                "ABC1234",
                EntryTime,
                10800,
                "PAYMENT_REQUIRED",
                TariffQuote: null);

            return new FakeVendorClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.Found, session, null, false, CorrelationId),
                new VendorTariffQuoteResponse(
                    VendorParkingLookupStatus.Found,
                    new VendorTariffQuoteDto(15000, "PHP", "RULE-002", "Separate tariff", CalculatedAt),
                    null,
                    false,
                    CorrelationId));
        }

        public static FakeVendorClient NotFound() =>
            new(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.NotFound, null, "SESSION_NOT_FOUND", false, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.NotFound, null, "SESSION_NOT_FOUND", false, CorrelationId));

        public static FakeVendorClient TariffRejected()
        {
            var session = new VendorParkingSessionDto(
                "FAKE-PMS",
                "TICKET-275",
                "ABC1234",
                EntryTime,
                10800,
                "PAYMENT_REQUIRED",
                TariffQuote: null);

            return new FakeVendorClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.Found, session, null, false, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.VendorRejected, null, "VENDOR_TARIFF_REJECTED", false, CorrelationId));
        }

        public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
            VendorParkingSessionLookupRequest request,
            CancellationToken cancellationToken)
        {
            ResolveSessionCalls++;
            return Task.FromResult(_sessionResponse);
        }

        public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
            VendorTariffQuoteRequest request,
            CancellationToken cancellationToken)
        {
            ResolveTariffCalls++;
            return Task.FromResult(_tariffResponse);
        }

        public Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
            VendorParkingFeeConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            ConfirmParkingFeeCalls++;
            return Task.FromResult(new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.Confirmed,
                new VendorParkingFeeConfirmationDto(request.AmountMinor ?? 0, request.Currency, CalculatedAt),
                "0",
                false,
                request.CorrelationId));
        }
    }

    private sealed class FakeRepository : ITicketSessionSummaryReadRepository
    {
        private readonly TicketSessionLocalStatusResult _result;

        private FakeRepository(TicketSessionLocalStatusResult result)
        {
            _result = result;
        }

        public static FakeRepository Found() =>
            new(new TicketSessionLocalStatusResult(
                TicketSessionLocalStatusOutcome.Found,
                new TicketSessionLocalStatusReadModel(
                    ParkingSessionId,
                    PaymentAttemptId,
                    "FINALIZED",
                    "Paid",
                    "RECORDED",
                    VendorSystemCode: null,
                    VendorConfirmationCode: null,
                    VendorMessage: null,
                    VendorConfirmationStatus: null,
                    VendorConfirmationTimestamp: null)));

        public static FakeRepository FoundWithDurableVendorConfirmation() =>
            new(new TicketSessionLocalStatusResult(
                TicketSessionLocalStatusOutcome.Found,
                new TicketSessionLocalStatusReadModel(
                    ParkingSessionId,
                    PaymentAttemptId,
                    "CONFIRMED",
                    "Paid",
                    "RECORDED",
                    VendorSystemCode: "HIKCENTRAL",
                    VendorConfirmationCode: "0",
                    VendorMessage: "Success",
                    VendorConfirmationStatus: "CONFIRMED",
                    VendorConfirmationTimestamp: CalculatedAt)));

        public static FakeRepository NotFound() =>
            new(new TicketSessionLocalStatusResult(TicketSessionLocalStatusOutcome.NotFound, Status: null));

        public static FakeRepository Ambiguous() =>
            new(new TicketSessionLocalStatusResult(TicketSessionLocalStatusOutcome.Ambiguous, Status: null));

        public Task<TicketSessionLocalStatusResult> FindLocalStatusAsync(
            string ticketNumber,
            Guid? siteId,
            Guid? siteGroupId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}
