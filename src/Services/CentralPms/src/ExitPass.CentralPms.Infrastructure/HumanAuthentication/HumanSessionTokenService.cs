using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.HumanAuthentication;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class HumanSessionTokenService : IHumanSessionTokenService
{
    public SessionCredential Create()
    {
        var reference = Guid.NewGuid();
        var secret = EncodeBase64Url(RandomNumberGenerator.GetBytes(32));
        return new SessionCredential(reference, secret, $"{reference:D}.{secret}");
    }

    public bool TryParse(string? token, out SessionCredential credential)
    {
        credential = new SessionCredential(Guid.Empty, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 160) return false;
        var separator = token.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == token.Length - 1 || !Guid.TryParse(token.AsSpan(0, separator), out var reference) || reference == Guid.Empty) return false;
        var secret = token[(separator + 1)..];
        try { if (DecodeBase64Url(secret).Length != 32) return false; }
        catch (FormatException) { return false; }
        credential = new SessionCredential(reference, secret, token);
        return true;
    }

    public string HashSecret(string secret) => HashPrivacyValue(secret);
    public string HashPrivacyValue(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

public sealed class DisabledExternalHumanAuthenticationAdapter : IExternalHumanAuthenticationAdapter
{
    public bool Enabled => false;
}

public sealed class DisabledCredentialChallengeDelivery : ICredentialChallengeDelivery
{
    public bool Enabled => false;

    public Task DeliverAsync(CredentialChallengeDeliveryRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Credential challenge delivery is not configured.");
}
