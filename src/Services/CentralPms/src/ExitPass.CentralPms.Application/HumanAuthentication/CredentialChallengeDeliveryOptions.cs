namespace ExitPass.CentralPms.Application.HumanAuthentication;

public sealed record CredentialChallengeDeliveryOptions
{
    public const string SectionName = "CredentialChallengeDelivery";

    public string PublicAccountLifecycleBaseUrl { get; init; } = string.Empty;
    public SmtpCredentialChallengeDeliveryOptions Smtp { get; init; } = new();
}

public sealed record SmtpCredentialChallengeDeliveryOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableTls { get; init; } = true;
    public string SenderAddress { get; init; } = string.Empty;
    public string? SenderDisplayName { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}
