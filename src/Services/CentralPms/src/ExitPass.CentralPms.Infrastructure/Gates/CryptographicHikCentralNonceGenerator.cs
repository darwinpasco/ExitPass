using System.Security.Cryptography;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Generates a 32-character lowercase hexadecimal nonce from 128 bits of cryptographic randomness.
/// </summary>
public sealed class CryptographicHikCentralNonceGenerator : IHikCentralNonceGenerator
{
    public const int NonceByteLength = 16;
    public const int NonceTextLength = NonceByteLength * 2;

    /// <inheritdoc />
    public string Generate()
    {
        Span<byte> nonceBytes = stackalloc byte[NonceByteLength];
        RandomNumberGenerator.Fill(nonceBytes);
        return Convert.ToHexString(nonceBytes).ToLowerInvariant();
    }
}
