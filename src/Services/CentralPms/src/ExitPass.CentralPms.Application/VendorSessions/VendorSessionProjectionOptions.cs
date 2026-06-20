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
    public int DefaultPollIntervalSeconds { get; set; } = 300;

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
    public int MaxProjectionAgeMinutes { get; set; } = 60;

    /// <summary>
    /// Consecutive failure count at which target health becomes FAILING.
    /// </summary>
    public int FailingFailureCountThreshold { get; set; } = 3;

    /// <summary>
    /// Returns a bounded poll interval.
    /// </summary>
    public int EffectivePollIntervalSeconds(int value) => value > 0 ? value : Math.Max(1, DefaultPollIntervalSeconds);

    /// <summary>
    /// Returns a bounded lookback window.
    /// </summary>
    public int EffectiveLookbackWindowMinutes(int value) => value > 0 ? value : Math.Max(1, DefaultLookbackWindowMinutes);

    /// <summary>
    /// Returns a bounded page size accepted by the HikCentral client.
    /// </summary>
    public int EffectivePageSize(int value) => Math.Clamp(value > 0 ? value : DefaultPageSize, 1, 500);

    /// <summary>
    /// Returns a bounded parallelism value.
    /// </summary>
    public int EffectiveMaxParallelSiteJobs() => Math.Max(1, MaxParallelSiteJobs);

    /// <summary>
    /// Returns a bounded page limit for one sync run.
    /// </summary>
    public int EffectiveMaxPagesPerRun() => Math.Max(1, MaxPagesPerRun);

    /// <summary>
    /// Returns a bounded fallback freshness threshold.
    /// </summary>
    public TimeSpan EffectiveMaxProjectionAge() => TimeSpan.FromMinutes(Math.Max(1, MaxProjectionAgeMinutes));
}
