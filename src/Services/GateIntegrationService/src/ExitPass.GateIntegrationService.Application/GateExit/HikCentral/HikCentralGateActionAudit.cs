using System.Security.Cryptography;
using System.Text;

namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Safe metadata persisted for one HikCentral gate action vendor exchange.
/// </summary>
public sealed record HikCentralGateActionAuditRecord(
    Guid AuditId,
    Guid GateCommandId,
    Guid SourceProcessingId,
    Guid SourceEventId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    Guid? GateDeviceId,
    string? GateDeviceIdentifier,
    string DoorIndexCode,
    Guid? LaneId,
    Guid? SiteId,
    Guid? VendorSystemId,
    string VendorCode,
    string VendorName,
    string Operation,
    string RequestMethod,
    string RequestPath,
    string RequestBodySha256,
    string SignedHeadersList,
    Guid RequestCorrelationId,
    string? VendorRequestId,
    string? VendorCorrelationId,
    int? HttpStatusCode,
    string? VendorResponseCode,
    string? VendorResponseMessage,
    string OutcomeCategory,
    bool Retryable,
    bool TerminalFailure,
    int DurationMs,
    bool TimeoutOccurred,
    bool VendorUnavailable,
    string? TransportErrorCode,
    string? TransportErrorMessage,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset RespondedAtUtc,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Persists safe HikCentral gate action request and response audit metadata.
/// </summary>
public interface IHikCentralGateActionAuditRecorder
{
    /// <summary>
    /// Records one HikCentral gate action attempt.
    /// </summary>
    Task RecordAsync(
        HikCentralGateActionAuditRecord record,
        CancellationToken cancellationToken);
}

/// <summary>
/// Test recorder that captures audit records without durable storage.
/// </summary>
public sealed class InMemoryHikCentralGateActionAuditRecorder : IHikCentralGateActionAuditRecorder
{
    private readonly List<HikCentralGateActionAuditRecord> _records = new();

    /// <summary>
    /// Captured HikCentral audit records.
    /// </summary>
    public IReadOnlyList<HikCentralGateActionAuditRecord> Records => _records;

    /// <inheritdoc />
    public Task RecordAsync(
        HikCentralGateActionAuditRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        _records.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Creates safe HikCentral audit records from signed requests and classified responses.
/// </summary>
public static class HikCentralGateActionAuditRecordFactory
{
    /// <summary>
    /// Creates one safe audit record for a completed HikCentral transport attempt.
    /// </summary>
    public static HikCentralGateActionAuditRecord Create(
        HikCentralGateActionRequest vendorRequest,
        HikCentralSignedRequest signedRequest,
        HikCentralGateActionTransportResult transportResult,
        HikCentralGateActionResponse response,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset respondedAtUtc,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(vendorRequest);
        ArgumentNullException.ThrowIfNull(signedRequest);
        ArgumentNullException.ThrowIfNull(transportResult);
        ArgumentNullException.ThrowIfNull(response);

        return new HikCentralGateActionAuditRecord(
            Guid.NewGuid(),
            vendorRequest.CommandId,
            vendorRequest.SourceProcessingId,
            vendorRequest.SourceEventId,
            vendorRequest.ExitAuthorizationId,
            vendorRequest.GateAuthorizationConsumptionId,
            vendorRequest.ParkingSessionId,
            vendorRequest.PaymentAttemptId,
            vendorRequest.TariffSnapshotId,
            vendorRequest.GateDeviceId,
            vendorRequest.GateDeviceIdentifier,
            vendorRequest.DoorIndexCode,
            vendorRequest.LaneId,
            vendorRequest.SiteId,
            vendorRequest.VendorSystemId,
            VendorCode: "HikCentral",
            VendorName: "HikCentral",
            Operation: "doorControl",
            signedRequest.Method,
            signedRequest.PathAndQuery,
            Sha256Hex(signedRequest.Body),
            ResolveSignedHeadersList(signedRequest),
            vendorRequest.CorrelationId,
            response.VendorRequestId,
            response.VendorCorrelationId,
            transportResult.HttpStatusCode,
            response.VendorResponseCode,
            Truncate(response.VendorResponseMessage, 256),
            response.Outcome.ToString(),
            response.Retryable,
            response.TerminalFailure,
            Math.Max(0, (int)Math.Ceiling(duration.TotalMilliseconds)),
            transportResult.TimedOut,
            transportResult.VendorUnavailable,
            ResolveTransportErrorCode(response, transportResult),
            SanitizeTransportError(transportResult.TransportError),
            requestedAtUtc,
            respondedAtUtc,
            DateTimeOffset.UtcNow);
    }

    private static string ResolveSignedHeadersList(HikCentralSignedRequest signedRequest) =>
        signedRequest.Headers.TryGetValue("X-Ca-Signature-Headers", out var value)
            ? value
            : string.Join(
                ",",
                signedRequest.Headers.Keys
                    .Where(key => !string.Equals(key, "X-Ca-Signature", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase));

    private static string? ResolveTransportErrorCode(
        HikCentralGateActionResponse response,
        HikCentralGateActionTransportResult transportResult)
    {
        if (transportResult.TimedOut)
        {
            return "TIMEOUT";
        }

        if (transportResult.VendorUnavailable)
        {
            return "VENDOR_UNAVAILABLE";
        }

        return response.Outcome is HikCentralGateActionOutcome.Succeeded
            ? null
            : response.Outcome.ToString();
    }

    private static string? SanitizeTransportError(string? value)
    {
        var sanitized = Truncate(value, 512);
        return string.IsNullOrWhiteSpace(sanitized)
            ? null
            : sanitized;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
