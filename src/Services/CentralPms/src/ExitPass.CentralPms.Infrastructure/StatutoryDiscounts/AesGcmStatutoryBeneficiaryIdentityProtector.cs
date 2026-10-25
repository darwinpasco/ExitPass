using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

public sealed class AesGcmStatutoryBeneficiaryIdentityProtector : IStatutoryBeneficiaryIdentityProtector
{
    private const short CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[]? _key;

    public AesGcmStatutoryBeneficiaryIdentityProtector(IOptions<StatutoryBeneficiaryProtectionOptions> options)
    {
        var value = options.Value;
        KeyReference = value.ProtectionKeyReference.Trim();
        KeyVersion = value.ProtectionKeyVersion.Trim();
        if (!string.IsNullOrWhiteSpace(value.ProtectionKeyBase64))
        {
            try { _key = Convert.FromBase64String(value.ProtectionKeyBase64); }
            catch (FormatException ex) { throw new InvalidOperationException("The statutory beneficiary protection key is not valid Base64.", ex); }
        }

        if (_key is not null && _key.Length != 32)
            throw new InvalidOperationException("The statutory beneficiary protection key must contain exactly 256 bits.");
    }

    public bool IsConfigured => _key is not null && KeyReference.Length > 0 && KeyVersion.Length > 0;
    public string KeyReference { get; }
    public string KeyVersion { get; }

    public ProtectedStatutoryBeneficiaryIdentity Protect(
        Guid statutoryDiscountDecisionCommandId,
        Guid siteId,
        StatutoryBeneficiaryIdentityInput identity)
    {
        EnsureConfigured();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(_key!, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(statutoryDiscountDecisionCommandId, siteId));
            var envelope = new byte[2 + nonce.Length + tag.Length + ciphertext.Length];
            BinaryPrimitives.WriteInt16BigEndian(envelope.AsSpan(0, 2), CurrentFormatVersion);
            nonce.CopyTo(envelope, 2);
            tag.CopyTo(envelope, 14);
            ciphertext.CopyTo(envelope, 30);
            CryptographicOperations.ZeroMemory(ciphertext);
            return new(envelope, KeyReference, KeyVersion, CurrentFormatVersion);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public StatutoryBeneficiaryIdentityInput Unprotect(
        Guid statutoryDiscountDecisionCommandId,
        Guid siteId,
        ProtectedStatutoryBeneficiaryIdentity protectedIdentity)
    {
        EnsureConfigured();
        var envelope = protectedIdentity.Envelope;
        if (protectedIdentity.EnvelopeFormatVersion != CurrentFormatVersion || envelope.Length < 31 ||
            !string.Equals(protectedIdentity.KeyReference, KeyReference, StringComparison.Ordinal) ||
            !string.Equals(protectedIdentity.KeyVersion, KeyVersion, StringComparison.Ordinal) ||
            BinaryPrimitives.ReadInt16BigEndian(envelope.AsSpan(0, 2)) != CurrentFormatVersion)
            throw new CryptographicException("The statutory beneficiary envelope cannot be opened by the active key version.");

        var plaintext = new byte[envelope.Length - 30];
        try
        {
            using var aes = new AesGcm(_key!, 16);
            aes.Decrypt(envelope.AsSpan(2, 12), envelope.AsSpan(30), envelope.AsSpan(14, 16), plaintext,
                AssociatedData(statutoryDiscountDecisionCommandId, siteId));
            return JsonSerializer.Deserialize<StatutoryBeneficiaryIdentityInput>(plaintext, JsonOptions)
                ?? throw new CryptographicException("The statutory beneficiary envelope payload is invalid.");
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private byte[] AssociatedData(Guid decisionId, Guid siteId) =>
        Encoding.UTF8.GetBytes($"exitpass:central-pms:statutory-beneficiary:v1:{decisionId:D}:{siteId:D}:{KeyReference}:{KeyVersion}");

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Statutory beneficiary identity protection is not configured.");
    }
}
