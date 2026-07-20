namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Disabled-by-default options for the internal controlled HikCentral gate execution endpoint.
/// </summary>
public sealed class HikCentralControlledGateExecutionOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "CentralPms:HikCentralControlledGateExecution";

    /// <summary>
    /// Enables the internal one-command controlled execution endpoint. The default is intentionally disabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Validates endpoint options.
    /// </summary>
    public IReadOnlyList<string> Validate() => [];
}
