using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.CentralPms.Infrastructure.VendorParking;

/// <summary>
/// Deterministic mock Vendor PMS Adapter client used by local development and automated tests.
/// </summary>
/// <remarks>
/// BRD v1.2: validates the parking lookup and tariff calculation flow without live vendor connectivity.
/// SDD v1.2: preserves a test adapter behind the same provider-neutral Vendor PMS boundary as HikCentral.
/// Invariant: mock resolution may create only Central PMS session/tariff records through the approved handler.
/// </remarks>
public sealed class MockVendorPmsParkingResolutionClient : IVendorPmsParkingResolutionClient
{
    private static readonly DateTimeOffset FixedCalculatedAt = new(2030, 4, 1, 1, 30, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken)
    {
        var lookupKey = Normalize(request.PlateNumber) ?? Normalize(request.TicketReference) ?? string.Empty;

        var response = lookupKey.ToUpperInvariant() switch
        {
            "NOTFOUND" => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.NotFound,
                null,
                "SESSION_NOT_FOUND",
                false,
                request.CorrelationId),

            "AMBIGUOUS" => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.Ambiguous,
                null,
                "VENDOR_SESSION_AMBIGUOUS",
                false,
                request.CorrelationId),

            "UNAVAILABLE" => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.UnavailableRetryable,
                null,
                "VENDOR_UNAVAILABLE",
                true,
                request.CorrelationId),

            "MALFORMED" => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.Found,
                new VendorParkingSessionDto(
                    string.Empty,
                    "FAKE-SESSION-MALFORMED",
                    "MALFORMED",
                    FixedCalculatedAt.AddHours(-2),
                    7200,
                    "PAYMENT_REQUIRED",
                    CreateQuote(10000, "FAKE-TARIFF-MALFORMED")),
                null,
                false,
                request.CorrelationId),

            "REJECTED" => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.VendorRejected,
                null,
                "VENDOR_REJECTED_LOOKUP",
                false,
                request.CorrelationId),

            "TARIFFFAIL" => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.Found,
                new VendorParkingSessionDto(
                    "FAKE-PMS",
                    "FAKE-SESSION-TARIFFFAIL",
                    Normalize(request.PlateNumber) ?? "PLATE-FROM-TICKET",
                    FixedCalculatedAt.AddHours(-2),
                    7200,
                    "PAYMENT_REQUIRED",
                    null),
                null,
                false,
                request.CorrelationId),

            _ => new VendorParkingSessionLookupResponse(
                VendorParkingLookupStatus.Found,
                new VendorParkingSessionDto(
                    "FAKE-PMS",
                    $"FAKE-SESSION-{lookupKey.ToUpperInvariant()}",
                    Normalize(request.PlateNumber) ?? "PLATE-FROM-TICKET",
                    FixedCalculatedAt.AddHours(-2),
                    7200,
                    "PAYMENT_REQUIRED",
                    CreateQuote(10000, "FAKE-TARIFF-001")),
                null,
                false,
                request.CorrelationId)
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var lookupKey = Normalize(request.PlateNumber) ?? Normalize(request.TicketReference) ?? string.Empty;
        var response = lookupKey.Equals("TARIFFFAIL", StringComparison.OrdinalIgnoreCase)
            ? new VendorTariffQuoteResponse(
                VendorParkingLookupStatus.VendorRejected,
                null,
                "VENDOR_TARIFF_REJECTED",
                false,
                request.CorrelationId)
            : new VendorTariffQuoteResponse(
                VendorParkingLookupStatus.Found,
                CreateQuote(10000, "FAKE-TARIFF-001"),
                null,
                false,
                request.CorrelationId);

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
        VendorParkingFeeConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AmountMinor is null)
        {
            return Task.FromResult(new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.ValidationError,
                null,
                "VENDOR_CONFIRMATION_AMOUNT_REQUIRED",
                false,
                request.CorrelationId));
        }

        var lookupKey = Normalize(request.PlateNumber) ?? Normalize(request.TicketReference) ?? string.Empty;
        var response = lookupKey.Equals("UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
            ? new VendorParkingFeeConfirmationResponse(
                VendorParkingLookupStatus.UnavailableRetryable,
                null,
                "VENDOR_UNAVAILABLE",
                true,
                request.CorrelationId)
            : lookupKey.Equals("REJECTED", StringComparison.OrdinalIgnoreCase)
                ? new VendorParkingFeeConfirmationResponse(
                    VendorParkingLookupStatus.VendorRejected,
                    null,
                    "VENDOR_CONFIRMATION_REJECTED",
                    false,
                    request.CorrelationId)
                : new VendorParkingFeeConfirmationResponse(
                    VendorParkingLookupStatus.Confirmed,
                    new VendorParkingFeeConfirmationDto(
                        request.AmountMinor.Value,
                        string.IsNullOrWhiteSpace(request.Currency) ? "PHP" : request.Currency.Trim().ToUpperInvariant(),
                        FixedCalculatedAt),
                    "0",
                    false,
                    request.CorrelationId);

        return Task.FromResult(response);
    }

    private static VendorTariffQuoteDto CreateQuote(long amountMinor, string tariffVersionReference)
    {
        return new VendorTariffQuoteDto(
            amountMinor,
            "PHP",
            tariffVersionReference,
            "Fake tariff",
            FixedCalculatedAt);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
