namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Configuration for vendor session projection scheduling and degraded resolve fallback.
/// </summary>
public sealed class VendorSessionProjectionOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "CentralPms:VendorSessionProjections";

    /// <summary>
    /// Activation mode used by the guarded, operator-driven local launch profile.
    /// </summary>
    public const string LocalProfileActivationMode = "LOCAL_PROFILE";

    /// <summary>
    /// Activation mode used by an approved ordinary deployment.
    /// </summary>
    public const string ManagedDeploymentActivationMode = "MANAGED_DEPLOYMENT";

    /// <summary>
    /// Minimum accepted poll interval for projection sync targets.
    /// </summary>
    public const int MinPollIntervalSeconds = 30;

    /// <summary>
    /// Maximum accepted poll interval for projection sync targets.
    /// </summary>
    public const int MaxPollIntervalSeconds = 86_400;

    /// <summary>
    /// Minimum accepted passageway lookback window.
    /// </summary>
    public const int MinLookbackWindowMinutes = 1;

    /// <summary>
    /// Maximum accepted passageway lookback window.
    /// </summary>
    public const int MaxLookbackWindowMinutes = 10_080;

    /// <summary>
    /// Minimum accepted HikCentral passageway page size.
    /// </summary>
    public const int MinPageSize = 1;

    /// <summary>
    /// Maximum accepted HikCentral passageway page size.
    /// </summary>
    public const int MaxPageSize = 500;

    /// <summary>
    /// Minimum accepted scheduler parallelism.
    /// </summary>
    public const int MinParallelSiteJobs = 1;

    /// <summary>
    /// Maximum accepted scheduler parallelism.
    /// </summary>
    public const int MaxParallelSiteJobsLimit = 16;

    /// <summary>
    /// Minimum accepted scheduler startup delay.
    /// </summary>
    public const int MinStartupDelaySeconds = 0;

    /// <summary>
    /// Maximum accepted scheduler startup delay.
    /// </summary>
    public const int MaxStartupDelaySeconds = 3_600;

    /// <summary>
    /// Minimum accepted scheduler scan interval.
    /// </summary>
    public const int MinSchedulerScanIntervalSeconds = 15;

    /// <summary>
    /// Maximum accepted scheduler scan interval.
    /// </summary>
    public const int MaxSchedulerScanIntervalSeconds = 3_600;

    /// <summary>
    /// Minimum accepted page limit for one target run.
    /// </summary>
    public const int MinPagesPerRun = 1;

    /// <summary>
    /// Maximum accepted page limit for one target run.
    /// </summary>
    public const int MaxPagesPerRunLimit = 100;

    /// <summary>
    /// Minimum accepted projection age threshold for fallback.
    /// </summary>
    public const int MinProjectionAgeMinutes = 1;

    /// <summary>
    /// Maximum accepted projection age threshold for fallback.
    /// </summary>
    public const int MaxProjectionAgeMinutesLimit = 10_080;

    /// <summary>
    /// Minimum consecutive failure count before target health can become FAILING.
    /// </summary>
    public const int MinFailingFailureCountThreshold = 1;

    /// <summary>
    /// Maximum consecutive failure count before target health can become FAILING.
    /// </summary>
    public const int MaxFailingFailureCountThreshold = 100;

    /// <summary>
    /// Enables the centralized background scheduler.
    /// </summary>
    public bool SchedulerEnabled { get; set; }

    /// <summary>
    /// Enables projection fallback when live vendor lookup is unavailable.
    /// Disabled by default because projection data is not tariff/payment/session authority.
    /// </summary>
    public bool DegradedResolveFallbackEnabled { get; set; }

    /// <summary>
    /// Default poll interval for targets missing an explicit interval.
    /// </summary>
    public int DefaultPollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Normal operating target for age since the last completed successful cycle.
    /// </summary>
    public int NormalFreshnessTargetSeconds { get; set; } = 60;

    /// <summary>
    /// Default lookback window for passageway pulls.
    /// </summary>
    public int DefaultLookbackWindowMinutes { get; set; } = 180;

    /// <summary>
    /// Default page size for passageway pulls.
    /// </summary>
    public int DefaultPageSize { get; set; } = 100;

    /// <summary>
    /// Maximum concurrent site-scoped sync jobs in one scheduler pass.
    /// </summary>
    public int MaxParallelSiteJobs { get; set; } = 2;

    /// <summary>
    /// Delay before the hosted scheduler starts polling.
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Delay between scheduler scans for due targets.
    /// </summary>
    public int SchedulerScanIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of pages pulled for one target in one run.
    /// </summary>
    public int MaxPagesPerRun { get; set; } = 20;

    /// <summary>
    /// Projection freshness threshold used by degraded resolve fallback.
    /// </summary>
    public int MaxProjectionAgeMinutes { get; set; } = 1;

    /// <summary>
    /// Declares that projection is required for this runtime environment.
    /// </summary>
    public bool RequiredForEnvironment { get; set; }

    /// <summary>
    /// Selects either the guarded local launch profile or normal managed deployment controls.
    /// </summary>
    public string ActivationMode { get; set; } = LocalProfileActivationMode;

    /// <summary>
    /// Explicit deployment-owned approval for ordinary managed runtime activation.
    /// </summary>
    public bool ManagedDeploymentApproved { get; set; }

    /// <summary>
    /// Permits an approved managed deployment to use a non-loopback database host.
    /// </summary>
    public bool AllowNonLoopbackDatabase { get; set; }

    /// <summary>
    /// Permits an approved managed deployment to use an endpoint whose host is production-marked.
    /// </summary>
    public bool AllowProductionEndpoint { get; set; }

    /// <summary>
    /// Expected HikCentral host for this deployment, without scheme, path, or credentials.
    /// </summary>
    public string? ExpectedEndpointHost { get; set; }

    /// <summary>
    /// Expected HikCentral scheme for this deployment.
    /// </summary>
    public string? ExpectedEndpointScheme { get; set; }

    /// <summary>
    /// Expected HikCentral port for this deployment.
    /// </summary>
    public int ExpectedEndpointPort { get; set; }

    /// <summary>
    /// Environment name that is explicitly authorized to run this scheduler configuration.
    /// </summary>
    public string? ActivationEnvironment { get; set; }

    /// <summary>
    /// Explicit acknowledgement that a local endpoint is non-Production.
    /// </summary>
    public bool LocalNonProductionEndpointAcknowledged { get; set; }

    /// <summary>
    /// Expected local development database name used to prevent ambiguous activation.
    /// </summary>
    public string? ExpectedDatabaseName { get; set; }

    /// <summary>
    /// Expected site for the single local development target.
    /// </summary>
    public Guid? ExpectedTargetSiteId { get; set; }

    /// <summary>
    /// Expected site group for the single local development target.
    /// </summary>
    public Guid? ExpectedTargetSiteGroupId { get; set; }

    /// <summary>
    /// Expected vendor system for the single local development target.
    /// </summary>
    public Guid? ExpectedTargetVendorSystemId { get; set; }

    /// <summary>
    /// Expected HikCentral parking-lot mapping for the single local development target.
    /// </summary>
    public string? ExpectedTargetParkingLotIndexCode { get; set; }

    /// <summary>
    /// Consecutive failure count at which target health becomes FAILING.
    /// </summary>
    public int FailingFailureCountThreshold { get; set; } = 3;

    /// <summary>
    /// Returns a bounded poll interval.
    /// </summary>
    public int EffectivePollIntervalSeconds(int value) =>
        Math.Clamp(value > 0 ? value : DefaultPollIntervalSeconds, MinPollIntervalSeconds, MaxPollIntervalSeconds);

    /// <summary>
    /// Returns a bounded lookback window.
    /// </summary>
    public int EffectiveLookbackWindowMinutes(int value) =>
        Math.Clamp(value > 0 ? value : DefaultLookbackWindowMinutes, MinLookbackWindowMinutes, MaxLookbackWindowMinutes);

    /// <summary>
    /// Returns a bounded page size accepted by the HikCentral client.
    /// </summary>
    public int EffectivePageSize(int value) =>
        Math.Clamp(value > 0 ? value : DefaultPageSize, MinPageSize, MaxPageSize);

    /// <summary>
    /// Returns a bounded parallelism value.
    /// </summary>
    public int EffectiveMaxParallelSiteJobs() =>
        Math.Clamp(MaxParallelSiteJobs, MinParallelSiteJobs, MaxParallelSiteJobsLimit);

    /// <summary>
    /// Returns a bounded scheduler startup delay.
    /// </summary>
    public TimeSpan EffectiveStartupDelay() =>
        TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, MinStartupDelaySeconds, MaxStartupDelaySeconds));

    /// <summary>
    /// Returns a bounded scheduler scan interval.
    /// </summary>
    public TimeSpan EffectiveSchedulerScanInterval() =>
        TimeSpan.FromSeconds(Math.Clamp(
            SchedulerScanIntervalSeconds,
            MinSchedulerScanIntervalSeconds,
            MaxSchedulerScanIntervalSeconds));

    /// <summary>
    /// Returns a bounded page limit for one sync run.
    /// </summary>
    public int EffectiveMaxPagesPerRun() =>
        Math.Clamp(MaxPagesPerRun, MinPagesPerRun, MaxPagesPerRunLimit);

    /// <summary>
    /// Returns a bounded fallback freshness threshold.
    /// </summary>
    public TimeSpan EffectiveMaxProjectionAge() =>
        TimeSpan.FromMinutes(Math.Clamp(
            MaxProjectionAgeMinutes,
            MinProjectionAgeMinutes,
            MaxProjectionAgeMinutesLimit));

    /// <summary>
    /// Returns the normal successful-cycle freshness target.
    /// </summary>
    public TimeSpan EffectiveNormalFreshnessTarget() =>
        TimeSpan.FromSeconds(Math.Clamp(
            NormalFreshnessTargetSeconds,
            MinPollIntervalSeconds,
            MaxPollIntervalSeconds));

    /// <summary>
    /// Returns a bounded consecutive failure threshold.
    /// </summary>
    public int EffectiveFailingFailureCountThreshold() =>
        Math.Clamp(
            FailingFailureCountThreshold,
            MinFailingFailureCountThreshold,
            MaxFailingFailureCountThreshold);

    /// <summary>
    /// Validates configured values before the scheduler or fallback path uses them.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        AddRangeError(errors, nameof(DefaultPollIntervalSeconds), DefaultPollIntervalSeconds, MinPollIntervalSeconds, MaxPollIntervalSeconds);
        AddRangeError(errors, nameof(NormalFreshnessTargetSeconds), NormalFreshnessTargetSeconds, MinPollIntervalSeconds, MaxPollIntervalSeconds);
        AddRangeError(errors, nameof(DefaultLookbackWindowMinutes), DefaultLookbackWindowMinutes, MinLookbackWindowMinutes, MaxLookbackWindowMinutes);
        AddRangeError(errors, nameof(DefaultPageSize), DefaultPageSize, MinPageSize, MaxPageSize);
        AddRangeError(errors, nameof(MaxParallelSiteJobs), MaxParallelSiteJobs, MinParallelSiteJobs, MaxParallelSiteJobsLimit);
        AddRangeError(errors, nameof(StartupDelaySeconds), StartupDelaySeconds, MinStartupDelaySeconds, MaxStartupDelaySeconds);
        AddRangeError(errors, nameof(SchedulerScanIntervalSeconds), SchedulerScanIntervalSeconds, MinSchedulerScanIntervalSeconds, MaxSchedulerScanIntervalSeconds);
        AddRangeError(errors, nameof(MaxPagesPerRun), MaxPagesPerRun, MinPagesPerRun, MaxPagesPerRunLimit);
        AddRangeError(errors, nameof(MaxProjectionAgeMinutes), MaxProjectionAgeMinutes, MinProjectionAgeMinutes, MaxProjectionAgeMinutesLimit);
        AddRangeError(errors, nameof(FailingFailureCountThreshold), FailingFailureCountThreshold, MinFailingFailureCountThreshold, MaxFailingFailureCountThreshold);
        if (EffectiveMaxProjectionAge() < EffectiveNormalFreshnessTarget())
        {
            errors.Add("MaxProjectionAgeMinutes must not be shorter than NormalFreshnessTargetSeconds");
        }

        if (RequiredForEnvironment && !SchedulerEnabled)
        {
            errors.Add("RequiredForEnvironment requires SchedulerEnabled");
        }

        if (!string.Equals(ActivationMode, LocalProfileActivationMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ActivationMode, ManagedDeploymentActivationMode, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"ActivationMode must be {LocalProfileActivationMode} or {ManagedDeploymentActivationMode}");
        }
        return errors;
    }

    /// <summary>
    /// Returns whether this process uses the guarded interactive local activation posture.
    /// </summary>
    public bool UsesLocalProfileActivation() =>
        string.Equals(ActivationMode, LocalProfileActivationMode, StringComparison.OrdinalIgnoreCase);

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
