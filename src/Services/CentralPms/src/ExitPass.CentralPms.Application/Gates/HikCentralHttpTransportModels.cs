using System.Net.Http;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Sends one already-built HikCentral HTTP request and returns safe transport metadata.
/// </summary>
public interface IHikCentralHttpTransport
{
    /// <summary>
    /// Sends the caller-owned signed request exactly once. The caller remains responsible for disposing the request.
    /// </summary>
    Task<HikCentralHttpTransportResult> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Controlled HikCentral HTTP and transport outcome classification.
/// </summary>
public enum HikCentralHttpTransportOutcome
{
    Succeeded,
    ClientError,
    Unauthorized,
    Forbidden,
    RequestTimeout,
    Throttled,
    VendorFailure,
    MalformedResponse,
    ResponseBodyTooLarge,
    TimedOut,
    TransportFailure
}

/// <summary>
/// Non-secret bounded transport options.
/// </summary>
public sealed record HikCentralHttpTransportOptions(int MaxResponseBodyBytes = 16 * 1024)
{
    public const int DefaultMaxResponseBodyBytes = 16 * 1024;
    public const int MaximumAllowedResponseBodyBytes = 1024 * 1024;
}

/// <summary>
/// Secret-free HikCentral transport result. This result never proves that a physical gate opened.
/// </summary>
public sealed record HikCentralHttpTransportResult(
    int? HttpStatusCode,
    bool IsSuccessStatusCode,
    HikCentralHttpTransportOutcome Outcome,
    bool TimedOut,
    bool TransportFailure,
    bool VendorUnavailable,
    bool ResponseBodyTooLarge,
    int ResponseBodyByteCount,
    string? ResponseBodySha256,
    string? VendorResultCode,
    string? VendorResultMessage,
    string? VendorCorrelationId,
    long DurationMs,
    DateTimeOffset RespondedAt);
