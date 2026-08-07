using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.HumanAuthentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class AesGcmTotpSecretProtector : ITotpSecretProtector
{
    private const short CurrentFormatVersion = 1;
    private readonly byte[]? _key;

    public AesGcmTotpSecretProtector(IOptions<HumanAuthenticationOptions> options)
    {
        var value = options.Value;
        KeyReference = value.TotpProtectionKeyReference.Trim();
        KeyVersion = value.TotpProtectionKeyVersion.Trim();
        if (!string.IsNullOrWhiteSpace(value.TotpProtectionKeyBase64))
        {
            try { _key = Convert.FromBase64String(value.TotpProtectionKeyBase64); }
            catch (FormatException ex) { throw new InvalidOperationException("The configured TOTP protection key is not valid Base64.", ex); }
        }

        if (_key is not null && _key.Length != 32)
        {
            throw new InvalidOperationException("The configured TOTP protection key must contain exactly 256 bits.");
        }
    }

    public bool IsConfigured => _key is not null && KeyReference.Length > 0 && KeyVersion.Length > 0;
    public string KeyReference { get; }
    public string KeyVersion { get; }
    public short EnvelopeFormatVersion => CurrentFormatVersion;

    public byte[] Protect(Guid userId, Guid authenticatorId, byte[] secret)
    {
        EnsureConfigured();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[secret.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key!, 16);
        aes.Encrypt(nonce, secret, ciphertext, tag, BuildAssociatedData(userId, authenticatorId, KeyReference, KeyVersion));
        var envelope = new byte[2 + nonce.Length + tag.Length + ciphertext.Length];
        BinaryPrimitives.WriteInt16BigEndian(envelope.AsSpan(0, 2), CurrentFormatVersion);
        nonce.CopyTo(envelope, 2);
        tag.CopyTo(envelope, 14);
        ciphertext.CopyTo(envelope, 30);
        CryptographicOperations.ZeroMemory(ciphertext);
        return envelope;
    }

    public byte[] Unprotect(Guid userId, Guid authenticatorId, TotpAuthenticatorRecord authenticator)
    {
        EnsureConfigured();
        var envelope = authenticator.ProtectedSecretEnvelope;
        if (authenticator.EnvelopeFormatVersion != CurrentFormatVersion || envelope.Length < 31 ||
            !string.Equals(authenticator.ProtectionKeyReference, KeyReference, StringComparison.Ordinal) ||
            !string.Equals(authenticator.ProtectionKeyVersion, KeyVersion, StringComparison.Ordinal) ||
            BinaryPrimitives.ReadInt16BigEndian(envelope.AsSpan(0, 2)) != CurrentFormatVersion)
        {
            throw new CryptographicException("The protected authenticator envelope cannot be opened by the active key version.");
        }

        var plaintext = new byte[envelope.Length - 30];
        using var aes = new AesGcm(_key!, 16);
        aes.Decrypt(envelope.AsSpan(2, 12), envelope.AsSpan(30), envelope.AsSpan(14, 16), plaintext, BuildAssociatedData(userId, authenticatorId, KeyReference, KeyVersion));
        return plaintext;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("TOTP key protection is not configured. The requested MFA operation is unavailable.");
    }

    private static byte[] BuildAssociatedData(Guid userId, Guid authenticatorId, string keyReference, string keyVersion) =>
        Encoding.UTF8.GetBytes($"exitpass:totp:v1:{userId:D}:{authenticatorId:D}:{keyReference}:{keyVersion}");
}
