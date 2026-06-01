namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Gate action adapter modes supported by the Gate Integration Service.
/// </summary>
public enum GateActionAdapterMode
{
    /// <summary>
    /// Use the existing no-op adapter.
    /// </summary>
    NoOp,

    /// <summary>
    /// Use the non-live HikCentral fake adapter and fixture transport.
    /// </summary>
    HikCentralFake,

    /// <summary>
    /// Reserved for a future live HikCentral adapter. Not implemented in this slice.
    /// </summary>
    HikCentralLive
}

/// <summary>
/// Gate action adapter selection options.
/// </summary>
public sealed class GateActionAdapterOptions
{
    /// <summary>
    /// Configuration section name for adapter selection.
    /// </summary>
    public const string SectionName = "GateActionAdapter";

    /// <summary>
    /// Adapter mode. Missing configuration defaults safely to no-op.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Parses the configured adapter mode.
    /// </summary>
    public GateActionAdapterMode ResolveMode()
    {
        if (string.IsNullOrWhiteSpace(Mode))
        {
            return GateActionAdapterMode.NoOp;
        }

        return Enum.TryParse<GateActionAdapterMode>(Mode, ignoreCase: true, out var mode)
            ? mode
            : throw new InvalidOperationException($"Unsupported gate action adapter mode '{Mode}'.");
    }
}
