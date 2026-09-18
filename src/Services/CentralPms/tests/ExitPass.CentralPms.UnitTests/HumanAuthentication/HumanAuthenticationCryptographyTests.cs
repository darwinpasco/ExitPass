using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.HumanAuthentication;

public sealed class HumanAuthenticationCryptographyTests
{
    [Fact]
    public async Task Argon2id_hash_verifies_and_wrong_password_fails()
    {
        var hasher = new Argon2idHumanPasswordHasher(Options.Create(TestOptions()));
        var material = await hasher.HashAsync("correct horse battery staple", CancellationToken.None);
        var credential = Credential(material);
        (await hasher.VerifyAsync("correct horse battery staple", credential, CancellationToken.None)).Should().BeTrue();
        (await hasher.VerifyAsync("incorrect horse battery staple", credential, CancellationToken.None)).Should().BeFalse();
        hasher.NeedsUpgrade(credential).Should().BeFalse();
        material.Verifier.Should().NotEqual(Encoding.UTF8.GetBytes("correct horse battery staple"));
    }

    [Fact]
    public async Task Argon2id_marks_weaker_work_parameters_for_upgrade()
    {
        var hasher = new Argon2idHumanPasswordHasher(Options.Create(TestOptions() with { Argon2Iterations = 2 }));
        var oldHasher = new Argon2idHumanPasswordHasher(Options.Create(TestOptions()));
        var old = await oldHasher.HashAsync("correct horse battery staple", CancellationToken.None);
        hasher.NeedsUpgrade(Credential(old)).Should().BeTrue();
    }

    [Fact]
    public void Totp_matches_the_Rfc6238_sha1_vector()
    {
        var provider = new TotpProvider(Options.Create(TestOptions() with { TotpDigits = 8, TotpAllowedPreviousSteps = 0, TotpAllowedFutureSteps = 0 }));
        var result = provider.Verify(Encoding.ASCII.GetBytes("12345678901234567890"), "94287082", DateTimeOffset.FromUnixTimeSeconds(59));
        result.Succeeded.Should().BeTrue();
        result.MatchedTimeStep.Should().Be(1);
    }

    [Fact]
    public void Totp_envelope_round_trips_and_does_not_contain_plaintext()
    {
        var protector = new AesGcmTotpSecretProtector(Options.Create(TestOptions() with
        {
            TotpProtectionKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            TotpProtectionKeyReference = "test-key",
            TotpProtectionKeyVersion = "1"
        }));
        var userId = Guid.NewGuid();
        var authenticatorId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(20);
        var envelope = protector.Protect(userId, authenticatorId, secret);
        envelope.Should().NotContainInOrder(secret);
        var record = new TotpAuthenticatorRecord(authenticatorId, "ACTIVE", envelope, protector.KeyReference, protector.KeyVersion, protector.EnvelopeFormatVersion, null, 1);
        protector.Unprotect(userId, authenticatorId, record).Should().Equal(secret);
    }

    [Fact]
    public void Totp_operations_fail_closed_without_key_configuration()
    {
        var protector = new AesGcmTotpSecretProtector(Options.Create(TestOptions()));
        protector.IsConfigured.Should().BeFalse();
        var action = () => protector.Protect(Guid.NewGuid(), Guid.NewGuid(), new byte[20]);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Session_tokens_are_random_parseable_and_hash_only()
    {
        var service = new HumanSessionTokenService();
        var first = service.Create();
        var second = service.Create();
        first.SerializedToken.Should().NotBe(second.SerializedToken);
        service.TryParse(first.SerializedToken, out var parsed).Should().BeTrue();
        parsed.SessionReference.Should().Be(first.SessionReference);
        service.HashSecret(first.Secret).Should().MatchRegex("^[0-9a-f]{64}$");
        service.HashSecret(first.Secret).Should().NotContain(first.Secret);
    }

    private static HumanAuthenticationOptions TestOptions() => new()
    {
        Argon2Iterations = 1,
        Argon2MemoryKiB = 19456,
        Argon2Parallelism = 1,
        Argon2HashBytes = 32,
        PasswordMinimumLength = 15,
        TotpIssuer = "ExitPass Test"
    };

    private static LocalCredentialRecord Credential(PasswordHashMaterial material) =>
        new(Guid.NewGuid(), "ACTIVE", material.Verifier, material.Salt, material.AlgorithmCode, material.AlgorithmVersion,
            material.Iterations, material.MemoryKiB, material.Parallelism, 1, 1);
}
