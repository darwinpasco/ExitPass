namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Top-level non-secret enablement and composition options for the live HikCentral gate integration.
/// </summary>
public sealed class HikCentralGateIntegrationOptions
{
    public const string SectionName = "CentralPms:HikCentralGateIntegration";
    public const int DefaultHttpTimeoutSeconds = 10;
    public const int MaximumHttpTimeoutSeconds = 60;

    public bool Enabled { get; set; }

    public string? BaseAddress { get; set; }

    public string? ClientKeyIdentifier { get; set; }

    public string? ProfileCode { get; set; }

    public HikCentralGateControlMechanism ControlMechanism { get; set; } =
        HikCentralGateControlMechanism.AccessControlDoorControl;

    public string? SecretFilePath { get; set; }

    public int MaxSecretBytes { get; set; } = HikCentralGateSecretFileOptions.DefaultMaxSecretBytes;

    public int HttpTimeoutSeconds { get; set; } = DefaultHttpTimeoutSeconds;

    public int MaxResponseBodyBytes { get; set; } = HikCentralHttpTransportOptions.DefaultMaxResponseBodyBytes;

    /// <summary>
    /// Validates deployment settings only when the live integration is explicitly enabled.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        if (!Enabled)
        {
            return [];
        }

        var errors = new List<string>();
        errors.AddRange(ToRuntimeOptions().Validate());
        errors.AddRange(ToSecretFileOptions().Validate());

        if (HttpTimeoutSeconds <= 0 || HttpTimeoutSeconds > MaximumHttpTimeoutSeconds)
        {
            errors.Add("HIKCENTRAL_HTTP_TIMEOUT_SECONDS_INVALID");
        }

        if (MaxResponseBodyBytes <= 0 ||
            MaxResponseBodyBytes > HikCentralHttpTransportOptions.MaximumAllowedResponseBodyBytes)
        {
            errors.Add("HIKCENTRAL_MAX_RESPONSE_BODY_BYTES_INVALID");
        }

        return errors;
    }

    public HikCentralGateRuntimeOptions ToRuntimeOptions() =>
        new()
        {
            BaseAddress = BaseAddress,
            ClientKeyIdentifier = ClientKeyIdentifier,
            ProfileCode = ProfileCode,
            ControlMechanism = ControlMechanism
        };

    public HikCentralGateSecretFileOptions ToSecretFileOptions() =>
        new()
        {
            SecretFilePath = SecretFilePath,
            MaxSecretBytes = MaxSecretBytes
        };

    public HikCentralHttpTransportOptions ToHttpTransportOptions() =>
        new(MaxResponseBodyBytes);

    public TimeSpan EffectiveHttpTimeout() =>
        TimeSpan.FromSeconds(HttpTimeoutSeconds);
}
