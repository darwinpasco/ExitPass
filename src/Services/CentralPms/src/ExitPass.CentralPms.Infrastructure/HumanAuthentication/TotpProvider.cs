using System.Globalization;
using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using Microsoft.Extensions.Options;
using OtpNet;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class TotpProvider : ITotpProvider
{
    private readonly HumanAuthenticationOptions _options;

    public TotpProvider(IOptions<HumanAuthenticationOptions> options)
    {
        _options = options.Value;
        if (_options.TotpStepSeconds is < 15 or > 120 || _options.TotpDigits is < 6 or > 8 ||
            _options.TotpAllowedPreviousSteps is < 0 or > 2 || _options.TotpAllowedFutureSteps is < 0 or > 2)
        {
            throw new InvalidOperationException("TOTP configuration is outside the bounded interoperable range.");
        }
    }

    public byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(20);
    public string EncodeSecret(byte[] secret) => Base32Encoding.ToString(secret);

    public string BuildProvisioningUri(string accountName, byte[] secret)
    {
        var issuer = Uri.EscapeDataString(_options.TotpIssuer);
        var account = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{issuer}:{account}?secret={EncodeSecret(secret)}&issuer={issuer}&algorithm=SHA1&digits={_options.TotpDigits.ToString(CultureInfo.InvariantCulture)}&period={_options.TotpStepSeconds.ToString(CultureInfo.InvariantCulture)}";
    }

    public TotpVerificationResult Verify(byte[] secret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != _options.TotpDigits || !code.All(char.IsAsciiDigit))
        {
            return new TotpVerificationResult(false, null);
        }

        var totp = new Totp(secret, _options.TotpStepSeconds, OtpHashMode.Sha1, _options.TotpDigits);
        var valid = totp.VerifyTotp(now.UtcDateTime, code, out var matchedStep, new VerificationWindow(_options.TotpAllowedPreviousSteps, _options.TotpAllowedFutureSteps));
        return new TotpVerificationResult(valid, valid ? matchedStep : null);
    }
}
