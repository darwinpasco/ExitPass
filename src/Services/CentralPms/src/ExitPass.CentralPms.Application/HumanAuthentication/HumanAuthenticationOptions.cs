namespace ExitPass.CentralPms.Application.HumanAuthentication;

public sealed record HumanAuthenticationOptions
{
    public const string SectionName = "HumanAuthentication";

    public bool Enabled { get; init; } = true;
    public Guid CentralPmsServiceIdentityId { get; init; } = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");
    public int PasswordMinimumLength { get; init; } = 15;
    public int PasswordMaximumUtf8Bytes { get; init; } = 1024;
    public int Argon2Iterations { get; init; } = 3;
    public int Argon2MemoryKiB { get; init; } = 65536;
    public short Argon2Parallelism { get; init; } = 1;
    public int Argon2HashBytes { get; init; } = 32;
    public int FailureWindowMinutes { get; init; } = 15;
    public int MaximumFailures { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
    public int WebIdleMinutes { get; init; } = 30;
    public int WebAbsoluteHours { get; init; } = 8;
    public int AptIdleMinutes { get; init; } = 15;
    public int AptAbsoluteHours { get; init; } = 12;
    public int FreshAuthenticationMinutes { get; init; } = 5;
    public int CredentialChallengeMinutes { get; init; } = 30;
    public string CookieName { get; init; } = "__Host-ExitPass-HumanSession";
    public string AptSessionAuthorizationScheme { get; init; } = "ExitPass-HumanSession";
    public string TotpIssuer { get; init; } = "ExitPass";
    public int TotpStepSeconds { get; init; } = 30;
    public int TotpDigits { get; init; } = 6;
    public int TotpAllowedPreviousSteps { get; init; } = 1;
    public int TotpAllowedFutureSteps { get; init; } = 1;
    public string? TotpProtectionKeyBase64 { get; init; }
    public string TotpProtectionKeyReference { get; init; } = string.Empty;
    public string TotpProtectionKeyVersion { get; init; } = string.Empty;
    public bool OidcEnabled { get; init; }
    public IReadOnlyList<string> AllowedWebOrigins { get; init; } = [];
}
