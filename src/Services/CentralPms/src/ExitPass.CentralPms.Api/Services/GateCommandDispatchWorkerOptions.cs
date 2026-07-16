namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Configuration for the hosted Central PMS gate command dispatch worker.
/// </summary>
public sealed class GateCommandDispatchWorkerOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "CentralPms:GateCommandDispatchWorker";

    /// <summary>
    /// Minimum accepted worker interval.
    /// </summary>
    public const int MinIntervalSeconds = 1;

    /// <summary>
    /// Maximum accepted worker interval.
    /// </summary>
    public const int MaxIntervalSeconds = 3_600;

    /// <summary>
    /// Minimum accepted startup delay.
    /// </summary>
    public const int MinInitialDelaySeconds = 0;

    /// <summary>
    /// Maximum accepted startup delay.
    /// </summary>
    public const int MaxInitialDelaySeconds = 3_600;

    /// <summary>
    /// Enables the hosted worker. The default is intentionally disabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Delay before the first dispatch cycle.
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Delay between completed dispatch cycles.
    /// </summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Returns the configured startup delay.
    /// </summary>
    public TimeSpan EffectiveInitialDelay() =>
        TimeSpan.FromSeconds(InitialDelaySeconds);

    /// <summary>
    /// Returns the configured interval.
    /// </summary>
    public TimeSpan EffectiveInterval() =>
        TimeSpan.FromSeconds(IntervalSeconds);

    /// <summary>
    /// Validates configured values.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        AddRangeError(errors, nameof(InitialDelaySeconds), InitialDelaySeconds, MinInitialDelaySeconds, MaxInitialDelaySeconds);
        AddRangeError(errors, nameof(IntervalSeconds), IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds);
        return errors;
    }

    /// <summary>
    /// Throws a clear configuration exception when validation fails.
    /// </summary>
    public void ThrowIfInvalid()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid {SectionName} configuration: {string.Join(", ", errors)}.");
        }
    }

    private static void AddRangeError(
        List<string> errors,
        string propertyName,
        int value,
        int minimum,
        int maximum)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"{propertyName} must be between {minimum} and {maximum}; actual value is {value}");
        }
    }
}
