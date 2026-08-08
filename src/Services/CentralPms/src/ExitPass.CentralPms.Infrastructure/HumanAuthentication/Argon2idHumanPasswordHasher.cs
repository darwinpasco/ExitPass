using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.HumanAuthentication;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class Argon2idHumanPasswordHasher : IHumanPasswordHasher
{
    private const string AlgorithmCode = "ARGON2ID";
    private const short AlgorithmVersion = 19;
    private readonly HumanAuthenticationOptions _options;

    public Argon2idHumanPasswordHasher(IOptions<HumanAuthenticationOptions> options)
    {
        _options = options.Value;
        ValidateOptions(_options);
    }

    public async Task<bool> VerifyAsync(string password, LocalCredentialRecord? credential, CancellationToken cancellationToken)
    {
        var passwordBytes = GetBoundedPasswordBytes(password);
        var metadataValid = IsSupportedCredential(credential);
        var salt = metadataValid ? credential!.Salt : new byte[16];
        var expected = metadataValid ? credential!.PasswordVerifier : new byte[_options.Argon2HashBytes];
        var iterations = metadataValid ? credential!.Iterations : _options.Argon2Iterations;
        var memory = metadataValid ? credential!.MemoryKiB!.Value : _options.Argon2MemoryKiB;
        var parallelism = metadataValid ? credential!.Parallelism!.Value : _options.Argon2Parallelism;

        try
        {
            var derived = await DeriveAsync(passwordBytes, salt, iterations, memory, parallelism, expected.Length, cancellationToken);
            try
            {
                return metadataValid && CryptographicOperations.FixedTimeEquals(derived, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public async Task<PasswordHashMaterial> HashAsync(string password, CancellationToken cancellationToken)
    {
        ValidateNewPassword(password);
        var passwordBytes = GetBoundedPasswordBytes(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        try
        {
            var verifier = await DeriveAsync(passwordBytes, salt, _options.Argon2Iterations, _options.Argon2MemoryKiB, _options.Argon2Parallelism, _options.Argon2HashBytes, cancellationToken);
            return new PasswordHashMaterial(verifier, salt, AlgorithmCode, AlgorithmVersion, _options.Argon2Iterations, _options.Argon2MemoryKiB, _options.Argon2Parallelism);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public bool NeedsUpgrade(LocalCredentialRecord credential) =>
        !string.Equals(credential.AlgorithmCode, AlgorithmCode, StringComparison.OrdinalIgnoreCase) ||
        credential.AlgorithmVersion != AlgorithmVersion || credential.Iterations < _options.Argon2Iterations ||
        credential.MemoryKiB.GetValueOrDefault() < _options.Argon2MemoryKiB ||
        credential.Parallelism.GetValueOrDefault() < _options.Argon2Parallelism ||
        credential.PasswordVerifier.Length < _options.Argon2HashBytes;

    public void ValidateNewPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < _options.PasswordMinimumLength)
        {
            throw new ArgumentException("The new password does not meet the configured minimum length.", nameof(password));
        }

        var bytes = GetBoundedPasswordBytes(password);
        CryptographicOperations.ZeroMemory(bytes);
    }

    private byte[] GetBoundedPasswordBytes(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        if (bytes.Length > _options.PasswordMaximumUtf8Bytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentException("The password exceeds the configured maximum encoded length.", nameof(password));
        }

        return bytes;
    }

    private static async Task<byte[]> DeriveAsync(byte[] password, byte[] salt, int iterations, int memoryKiB, int parallelism, int outputBytes, CancellationToken cancellationToken)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            Iterations = iterations,
            MemorySize = memoryKiB,
            DegreeOfParallelism = parallelism
        };
        cancellationToken.ThrowIfCancellationRequested();
        return await argon2.GetBytesAsync(outputBytes);
    }

    private static void ValidateOptions(HumanAuthenticationOptions options)
    {
        if (options.Argon2Iterations is < 1 or > 20 || options.Argon2MemoryKiB is < 19456 or > 1048576 ||
            options.Argon2Parallelism is < 1 or > 16 || options.Argon2HashBytes is < 32 or > 128)
        {
            throw new InvalidOperationException("Human authentication Argon2id configuration is below the supported security floor.");
        }
    }

    private static bool IsSupportedCredential(LocalCredentialRecord? credential) =>
        credential is not null &&
        string.Equals(credential.AlgorithmCode, AlgorithmCode, StringComparison.OrdinalIgnoreCase) &&
        credential.AlgorithmVersion == AlgorithmVersion &&
        credential.Iterations is >= 1 and <= 20 &&
        credential.MemoryKiB is >= 19456 and <= 1048576 &&
        credential.Parallelism is >= 1 and <= 16 &&
        credential.Salt.Length is >= 16 and <= 128 &&
        credential.PasswordVerifier.Length is >= 32 and <= 128;
}
