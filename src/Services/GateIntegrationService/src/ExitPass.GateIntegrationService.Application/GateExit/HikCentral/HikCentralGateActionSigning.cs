using System.Collections.ObjectModel;

namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Provides the current timestamp for deterministic HikCentral signing.
/// </summary>
public interface IHikCentralClock
{
    /// <summary>
    /// Returns the current UTC instant.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Default system clock for HikCentral signing.
/// </summary>
public sealed class SystemHikCentralClock : IHikCentralClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Provides request nonces for deterministic HikCentral signing tests.
/// </summary>
public interface IHikCentralNonceProvider
{
    /// <summary>
    /// Creates a request nonce.
    /// </summary>
    string CreateNonce();
}

/// <summary>
/// Default UUID nonce provider for HikCentral signing.
/// </summary>
public sealed class GuidHikCentralNonceProvider : IHikCentralNonceProvider
{
    /// <inheritdoc />
    public string CreateNonce() => Guid.NewGuid().ToString("D");
}

/// <summary>
/// Canonical HikCentral request data used as HMAC input.
/// </summary>
public sealed record HikCentralCanonicalRequest(
    string Method,
    string PathAndQuery,
    string Accept,
    string ContentMd5,
    string ContentType,
    IReadOnlyDictionary<string, string> SignedHeaders,
    string StringToSign);

/// <summary>
/// Signed, non-live HikCentral request prepared for a transport boundary.
/// </summary>
public sealed record HikCentralSignedRequest(
    string Method,
    string PathAndQuery,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    HikCentralCanonicalRequest CanonicalRequest,
    string Signature)
{
    /// <summary>
    /// Creates a stable read-only header dictionary.
    /// </summary>
    public static IReadOnlyDictionary<string, string> HeadersOf(IDictionary<string, string> headers) =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));
}
