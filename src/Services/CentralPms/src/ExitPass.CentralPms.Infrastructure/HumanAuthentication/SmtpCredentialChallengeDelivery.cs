using System.Net;
using System.Net.Mail;
using System.Text;
using ExitPass.CentralPms.Application.HumanAuthentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class CredentialChallengeLinkBuilder : ICredentialChallengeLinkBuilder
{
    private readonly CredentialChallengeDeliveryOptions _options;

    public CredentialChallengeLinkBuilder(IOptions<CredentialChallengeDeliveryOptions> options) =>
        _options = options.Value;

    public bool Enabled => TryGetBaseUri(out _);

    public string BuildUrl(string purpose, Guid challengeReference, string challengeSecret)
    {
        if (!TryGetBaseUri(out var baseUri))
        {
            throw new InvalidOperationException("The public account lifecycle URL is not configured.");
        }

        var route = purpose == "PASSWORD_RESET" ? "/account/reset-password" : "/account/activate";
        var endpoint = new Uri(baseUri, route);
        var builder = new UriBuilder(endpoint)
        {
            Query = $"challengeReference={Uri.EscapeDataString(challengeReference.ToString("D"))}&challengeSecret={Uri.EscapeDataString(challengeSecret)}"
        };
        return builder.Uri.AbsoluteUri;
    }

    public static bool IsUsablePublicBaseUrl(string? value) => TryParseBaseUri(value, out _);

    private bool TryGetBaseUri(out Uri baseUri) => TryParseBaseUri(_options.PublicAccountLifecycleBaseUrl, out baseUri);

    private static bool TryParseBaseUri(string? configuredValue, out Uri baseUri)
    {
        var value = configuredValue?.Trim() ?? string.Empty;
        if (Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(parsed.Query) && string.IsNullOrEmpty(parsed.Fragment))
        {
            baseUri = parsed;
            return true;
        }

        baseUri = null!;
        return false;
    }
}

public sealed class SmtpCredentialChallengeDelivery : ICredentialChallengeDelivery
{
    private readonly CredentialChallengeDeliveryOptions _options;
    private readonly ICredentialChallengeLinkBuilder _links;

    public SmtpCredentialChallengeDelivery(
        IOptions<CredentialChallengeDeliveryOptions> options,
        ICredentialChallengeLinkBuilder links)
    {
        _options = options.Value;
        _links = links;
    }

    public bool Enabled =>
        _options.Smtp.Enabled && _links.Enabled &&
        !string.IsNullOrWhiteSpace(_options.Smtp.Host) && _options.Smtp.Port is > 0 and <= 65535 &&
        IsUsableEmail(_options.Smtp.SenderAddress);

    public async Task DeliverAsync(CredentialChallengeDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("SMTP credential challenge delivery is not configured.");
        }

        var recipient = new MailAddress(request.RecipientEmail);
        var sender = string.IsNullOrWhiteSpace(_options.Smtp.SenderDisplayName)
            ? new MailAddress(_options.Smtp.SenderAddress)
            : new MailAddress(_options.Smtp.SenderAddress, _options.Smtp.SenderDisplayName.Trim(), Encoding.UTF8);
        var url = _links.BuildUrl(request.Purpose, request.ChallengeReference, request.ChallengeSecret);
        var purposeLabel = request.Purpose == "PASSWORD_RESET" ? "password reset" : "account activation";
        var subject = request.Purpose == "PASSWORD_RESET" ? "Reset your ExitPass password" : "Activate your ExitPass account";
        var expires = request.ExpiresAt.ToUniversalTime().ToString("u");

        using var message = new MailMessage(sender, recipient)
        {
            Subject = subject,
            Body = $"ExitPass {purposeLabel}\n\nUse this secure link before {expires}:\n{url}\n\nIf you did not expect this message, you can ignore it.",
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            $"<p>ExitPass {WebUtility.HtmlEncode(purposeLabel)}</p><p><a href=\"{WebUtility.HtmlEncode(url)}\">Continue securely</a></p><p>This link expires at {WebUtility.HtmlEncode(expires)}.</p><p>If you did not expect this message, you can ignore it.</p>",
            Encoding.UTF8,
            "text/html"));

        using var client = new SmtpClient(_options.Smtp.Host.Trim(), _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
        {
            client.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }

    public static bool IsUsableEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try
        {
            var parsed = new MailAddress(value.Trim());
            return string.Equals(parsed.Address, value.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   parsed.Host.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
