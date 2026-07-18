using System.Security.Cryptography;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Supplies explicit runtime material for one HikCentral gate-action attempt.
/// </summary>
public interface IHikCentralGateRuntimeMaterialProvider
{
    /// <summary>
    /// Gets one runtime-material snapshot for the supplied gate-action request.
    /// </summary>
    ValueTask<HikCentralGateRuntimeMaterial> GetAsync(
        HikCentralGateActionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Owns one HikCentral runtime-material snapshot, including clearable in-memory secret bytes.
/// </summary>
public sealed class HikCentralGateRuntimeMaterial : IDisposable
{
    private byte[] _appSecretBytes;
    private bool _disposed;

    /// <summary>
    /// Creates a runtime-material snapshot with explicit non-secret request settings and owned secret bytes.
    /// </summary>
    public HikCentralGateRuntimeMaterial(
        Uri baseAddress,
        HikCentralGateControlProfile controlProfile,
        string clientKeyIdentifier,
        ReadOnlySpan<byte> secretBytes,
        string timestampMilliseconds,
        string nonce,
        string signatureMethod)
    {
        BaseAddress = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress));
        ControlProfile = controlProfile ?? throw new ArgumentNullException(nameof(controlProfile));
        ClientKeyIdentifier = !string.IsNullOrWhiteSpace(clientKeyIdentifier)
            ? clientKeyIdentifier
            : throw new ArgumentException("HikCentral client key identifier is required.", nameof(clientKeyIdentifier));
        TimestampMilliseconds = !string.IsNullOrWhiteSpace(timestampMilliseconds)
            ? timestampMilliseconds
            : throw new ArgumentException("HikCentral timestamp is required.", nameof(timestampMilliseconds));
        Nonce = !string.IsNullOrWhiteSpace(nonce)
            ? nonce
            : throw new ArgumentException("HikCentral nonce is required.", nameof(nonce));
        SignatureMethod = !string.IsNullOrWhiteSpace(signatureMethod)
            ? signatureMethod
            : throw new ArgumentException("HikCentral signature method is required.", nameof(signatureMethod));
        if (secretBytes.IsEmpty)
        {
            throw new ArgumentException("HikCentral secret bytes are required.", nameof(secretBytes));
        }

        _appSecretBytes = secretBytes.ToArray();
    }

    /// <summary>
    /// Explicit HTTPS base address for this attempt.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// Explicit guide-confirmed control profile for this attempt.
    /// </summary>
    public HikCentralGateControlProfile ControlProfile { get; }

    /// <summary>
    /// Runtime client key identifier used as a signing header value.
    /// </summary>
    public string ClientKeyIdentifier { get; }

    /// <summary>
    /// Deterministic runtime timestamp supplied by the provider.
    /// </summary>
    public string TimestampMilliseconds { get; }

    /// <summary>
    /// Deterministic runtime nonce supplied by the provider.
    /// </summary>
    public string Nonce { get; }

    /// <summary>
    /// Explicit guide-confirmed signature-method identifier supplied by the provider.
    /// </summary>
    public string SignatureMethod { get; }

    /// <summary>
    /// Indicates whether this runtime material has been cleared.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Owned secret bytes for immediate signature calculation. Do not log, persist, or retain.
    /// </summary>
    public ReadOnlySpan<byte> SecretBytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _appSecretBytes;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_appSecretBytes);
        _disposed = true;
    }

    public override string ToString() =>
        $"{nameof(HikCentralGateRuntimeMaterial)} {{ {nameof(BaseAddress)} = [REDACTED], {nameof(ControlProfile)} = {ControlProfile.ProfileCode}, {nameof(ClientKeyIdentifier)} = [REDACTED], {nameof(TimestampMilliseconds)} = [REDACTED], {nameof(Nonce)} = [REDACTED], {nameof(SignatureMethod)} = {SignatureMethod}, SecretBytes = [REDACTED] }}";
}
