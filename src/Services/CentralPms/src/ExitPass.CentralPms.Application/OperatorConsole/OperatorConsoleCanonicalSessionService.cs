using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Materializes a canonical parking session only after an explicit Operator Console mutation.
/// This boundary never resolves a live tariff or creates financial state.
/// </summary>
public sealed class OperatorConsoleCanonicalSessionService(
    IVendorSessionProjectionLookupService projectionLookup,
    IOperatorConsoleCanonicalSessionRepository repository,
    IOptions<VendorSessionProjectionOptions> projectionOptions,
    ISystemClock clock) : IOperatorConsoleCanonicalSessionService
{
    private const string TicketLookup = "TICKET_REFERENCE";
    private const string PlateLookup = "PLATE_LICENSE";

    public async Task<OperatorConsoleCanonicalSessionResult> EnsureAsync(
        OperatorConsoleCanonicalSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.SiteId, nameof(command.SiteId));
        ValidateGuid(command.SiteGroupId, nameof(command.SiteGroupId));
        ValidateGuid(command.VendorSystemId, nameof(command.VendorSystemId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        var lookupMode = Normalize(command.LookupMode);
        if (lookupMode is not TicketLookup and not PlateLookup)
        {
            throw new ArgumentException("LookupMode must be TICKET_REFERENCE or PLATE_LICENSE.", nameof(command.LookupMode));
        }

        var ticketReference = lookupMode == TicketLookup ? NormalizeOptional(command.TicketReference) : null;
        var plateNumber = lookupMode == PlateLookup ? NormalizeOptional(command.PlateNumber)?.ToUpperInvariant() : null;
        if (ticketReference is null && plateNumber is null)
        {
            throw new ArgumentException("The selected lookup identifier is required.", nameof(command));
        }

        var requestedAt = clock.UtcNow;
        VendorSessionProjectionLookupResult lookup;
        try
        {
            lookup = await projectionLookup.LookupAsync(
                new VendorSessionProjectionLookupQuery(
                    ticketReference,
                    plateNumber,
                    command.SiteId,
                    command.SiteGroupId,
                    ParkingLotIndexCode: null,
                    requestedAt,
                    command.CorrelationId,
                    command.VendorSystemId),
                cancellationToken);
        }
        catch (VendorSessionProjectionLookupException exception)
        {
            throw new OperatorConsoleCanonicalSessionException(exception.ErrorCode);
        }

        var projection = lookup.Projection;
        if (!lookup.Found || projection is null)
        {
            throw new OperatorConsoleCanonicalSessionException("OPERATOR_SESSION_PROJECTION_NOT_FOUND");
        }

        if (lookup.FreshnessAge is null ||
            lookup.FreshnessAge > projectionOptions.Value.EffectiveMaxProjectionAge())
        {
            throw new OperatorConsoleCanonicalSessionException("OPERATOR_SESSION_PROJECTION_STALE");
        }

        if (projection.ProjectionStatus != VendorSessionProjectionStatus.Active ||
            projection.SiteId != command.SiteId ||
            projection.SiteGroupId != command.SiteGroupId ||
            projection.VendorSystemId != command.VendorSystemId ||
            projection.SourceAdapterIdentityId is null ||
            projection.EnterTime is null ||
            (string.IsNullOrWhiteSpace(projection.VendorRecordGuid) &&
             string.IsNullOrWhiteSpace(projection.StableIdentityKey)))
        {
            throw new OperatorConsoleCanonicalSessionException("OPERATOR_SESSION_PROJECTION_INVALID");
        }

        var persisted = await repository.EnsureAsync(
            new OperatorConsoleCanonicalSessionPersistenceCommand(projection, command.CorrelationId),
            cancellationToken);

        return new OperatorConsoleCanonicalSessionResult(
            persisted.ParkingSessionId,
            persisted.ReusedExistingSession,
            projection.VendorSessionProjectionId,
            command.CorrelationId);
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
