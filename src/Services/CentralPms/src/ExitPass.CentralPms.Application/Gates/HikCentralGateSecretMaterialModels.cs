using System.Security.Cryptography;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Supplies owned, clearable HikCentral secret bytes for one adapter attempt.
/// </summary>
public interface IHikCentralGateSecretSource
{
    /// <summary>
    /// Gets one disposable secret-material snapshot. Implementations decide where secrets come from in a later slice.
    /// </summary>
    ValueTask<HikCentralGateSecretMaterial> GetSecretAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns one clearable in-memory HikCentral secret byte buffer.
/// </summary>
public sealed class HikCentralGateSecretMaterial : IDisposable
{
    private byte[] _secretBytes;
    private bool _disposed;

    /// <summary>
    /// Copies the supplied secret bytes so disposal clears only this owned buffer.
    /// </summary>
    public HikCentralGateSecretMaterial(ReadOnlySpan<byte> secretBytes)
    {
        if (secretBytes.IsEmpty)
        {
            throw new ArgumentException("HikCentral secret material is required.", nameof(secretBytes));
        }

        _secretBytes = secretBytes.ToArray();
    }

    /// <summary>
    /// Indicates whether the owned secret buffer has been cleared.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Owned secret bytes for immediate runtime-material construction. Do not log, persist, or retain.
    /// </summary>
    public ReadOnlySpan<byte> SecretBytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _secretBytes;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_secretBytes);
        _disposed = true;
    }

    public override string ToString() =>
        $"{nameof(HikCentralGateSecretMaterial)} {{ SecretBytes = [REDACTED] }}";
}
