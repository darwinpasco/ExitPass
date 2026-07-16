namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Configuration for the hosted Central PMS stale gate command recovery worker.
/// </summary>
public sealed class GateCommandRecoveryWorkerOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "CentralPms:GateCommandRecoveryWorker";

    /// <summary>
    /// Minimum accepted startup delay.
    /// </summary>
    public const int MinInitialDelaySeconds = 0;

    /// <summary>
    /// Maximum accepted startup delay.
    /// </summary>
    public const int MaxInitialDelaySeconds = 3_600;

    /// <summary>
    /// Minimum accepted worker interval.
    /// </summary>
    public const int MinIntervalSeconds = 1;

    /// <summary>
    /// Maximum accepted worker interval.
    /// </summary>
    public const int MaxIntervalSeconds = 3_600;

    /// <summary>
    /// Minimum accepted stale age before a command is eligible for recovery.
    /// </summary>
    public const int MinStaleAfterSeconds = 1;

    /// <summary>
    /// Maximum accepted stale age before a command is eligible for recovery.
    /// </summary>
    public const int MaxStaleAfterSeconds = 86_400;

    /// <summary>
    /// Minimum accepted retry delay for recovered retryable commands.
    /// </summary>
    public const int MinRetryDelaySeconds = 1;

    /// <summary>
    /// Maximum accepted retry delay for recovered retryable commands.
    /// </summary>
    public const int MaxRetryDelaySeconds = 86_400;

    /// <summary>
    /// Enables the hosted worker. The default is intentionally disabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Delay before the first recovery cycle.
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Delay between completed recovery cycles.
    /// </summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Age in seconds after which an IN_PROGRESS command may be considered stale.
    /// </summary>
    public int StaleAfterSeconds { get; set; } = 300;

    /// <summary>
    /// Delay in seconds before a retryable recovered command becomes due.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 300;

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
    /// Returns the configured stale age.
    /// </summary>
    public TimeSpan EffectiveStaleAfter() =>
        TimeSpan.FromSeconds(StaleAfterSeconds);

    /// <summary>
    /// Returns the configured retry delay.
    /// </summary>
    public TimeSpan EffectiveRetryDelay() =>
        TimeSpan.FromSeconds(RetryDelaySeconds);

    /// <summary>
    /// Validates configured values.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        AddRangeError(errors, nameof(InitialDelaySeconds), InitialDelaySeconds, MinInitialDelaySeconds, MaxInitialDelaySeconds);
        AddRangeError(errors, nameof(IntervalSeconds), IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds);
        AddRangeError(errors, nameof(StaleAfterSeconds), StaleAfterSeconds, MinStaleAfterSeconds, MaxStaleAfterSeconds);
        AddRangeError(errors, nameof(RetryDelaySeconds), RetryDelaySeconds, MinRetryDelaySeconds, MaxRetryDelaySeconds);
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
