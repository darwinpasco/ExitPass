using System.Security.Cryptography;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryDiscounts;

public sealed class StatutoryBeneficiaryIdentityProtectorTests
{
    [Fact]
    public void ProtectUsesRandomizedAes256GcmAndRoundTripsRestrictedValues()
    {
        var protector = Protector(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var decision = Guid.NewGuid();
        var site = Guid.NewGuid();
        var identity = Identity();

        var first = protector.Protect(decision, site, identity);
        var second = protector.Protect(decision, site, identity);

        Assert.NotEqual(first.Envelope, second.Envelope);
        Assert.Equal(identity, protector.Unprotect(decision, site, first));
        Assert.DoesNotContain(identity.StatutoryIdNumber, Convert.ToBase64String(first.Envelope), StringComparison.Ordinal);
    }

    [Fact]
    public void WrongKeyTamperingAndWrongFiscalScopeFailClosed()
    {
        var decision = Guid.NewGuid();
        var site = Guid.NewGuid();
        var protectedValue = Protector(new byte[32]).Protect(decision, site, Identity());

        Assert.ThrowsAny<CryptographicException>(() => Protector(Enumerable.Repeat((byte)1, 32).ToArray()).Unprotect(decision, site, protectedValue));
        Assert.ThrowsAny<CryptographicException>(() => Protector(new byte[32]).Unprotect(Guid.NewGuid(), site, protectedValue));
        protectedValue.Envelope[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => Protector(new byte[32]).Unprotect(decision, site, protectedValue));
    }

    private static AesGcmStatutoryBeneficiaryIdentityProtector Protector(byte[] key) => new(Options.Create(new StatutoryBeneficiaryProtectionOptions
    {
        ProtectionKeyBase64 = Convert.ToBase64String(key),
        ProtectionKeyReference = "central-statutory-beneficiary",
        ProtectionKeyVersion = "v1"
    }));

    private static StatutoryBeneficiaryIdentityInput Identity() =>
        new("Maria Dela Cruz", "PWD_ID", "PWD-12345", "123-456-789-000");
}
