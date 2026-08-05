namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public static class StatutoryEvidenceScanConstants
{
    public const string ExecutePermission = "statutory-discounts.evidence.scan.execute";
    public const string ExecutePolicy = "StatutoryEvidenceScanExecute";
    public const string ScannerProviderClamAvCompatible = "CLAMAV_COMPATIBLE";
    public const string ScannerProviderNoopTestOnly = "NOOP_TEST_ONLY";
    public const string WorkerDisabled = "DISABLED";
    public const string WorkerReady = "READY";
    public const string WorkerNotConfigured = "NOT_CONFIGURED";
    public const string WorkerUnavailable = "UNAVAILABLE";
    public const string WorkerDegraded = "DEGRADED";
    public const string WorkerStale = "STALE";
    public const string WorkerUnknown = "UNKNOWN";
}

public sealed class StatutoryEvidenceScanWorkerOptions
{
    public const string SectionName = "CentralPms:StatutoryEvidence:ScanWorker";

    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 5;
    public int MaxConcurrency { get; set; } = 2;
    public int LeaseSeconds { get; set; } = 120;
    public int ScanTimeoutSeconds { get; set; } = 30;
    public int ValidationTimeoutSeconds { get; set; } = 15;
    public int MaxAttempts { get; set; } = 3;
    public int InitialRetryDelaySeconds { get; set; } = 60;
    public int MaxRetryDelaySeconds { get; set; } = 900;
    public int JitterSeconds { get; set; } = 10;
    public long MaxContentLengthBytes { get; set; }
    public int MaxDecodedWidth { get; set; } = 6000;
    public int MaxDecodedHeight { get; set; } = 6000;
    public long MaxDecodedPixelCount { get; set; } = 36_000_000;
    public int MaxHeaderProbeBytes { get; set; } = 128 * 1024;
    public string ScannerProvider { get; set; } = StatutoryEvidenceScanConstants.ScannerProviderClamAvCompatible;
    public string? ScannerEndpoint { get; set; }
    public int ScannerPort { get; set; } = 3310;
    public int ScannerHealthTimeoutSeconds { get; set; } = 5;
    public string WorkerId { get; set; } = "central-pms-statutory-evidence-scan-worker";
    public Guid? WorkerServiceIdentityId { get; set; }

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 1, 3600));
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(Math.Clamp(LeaseSeconds, 5, 3600));
    public TimeSpan ScanTimeout => TimeSpan.FromSeconds(Math.Clamp(ScanTimeoutSeconds, 1, 600));
    public TimeSpan ValidationTimeout => TimeSpan.FromSeconds(Math.Clamp(ValidationTimeoutSeconds, 1, 300));

    public bool HasCriticalConfiguration() =>
        MaxContentLengthBytes > 0 &&
        MaxDecodedWidth > 0 &&
        MaxDecodedHeight > 0 &&
        MaxDecodedPixelCount > 0 &&
        MaxAttempts > 0 &&
        BatchSize > 0 &&
        MaxConcurrency > 0 &&
        !string.IsNullOrWhiteSpace(ScannerProvider) &&
        (!string.Equals(ScannerProvider, StatutoryEvidenceScanConstants.ScannerProviderClamAvCompatible, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(ScannerEndpoint));

    public string Readiness()
    {
        if (!Enabled)
        {
            return StatutoryEvidenceScanConstants.WorkerDisabled;
        }

        return HasCriticalConfiguration()
            ? StatutoryEvidenceScanConstants.WorkerReady
            : StatutoryEvidenceScanConstants.WorkerNotConfigured;
    }
}

public sealed record StatutoryEvidenceObjectContentRequest(
    string BucketName,
    string InternalObjectKey,
    long MaxContentLengthBytes);

public sealed record StatutoryEvidenceObjectContent(
    Stream Content,
    string ContentType,
    long ContentLength,
    string? ChecksumSha256,
    string? ObjectVersion,
    string? EncryptionClassification) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record StatutoryEvidenceScanWorkItem(
    Guid ScanAttemptId,
    Guid ScanAttemptReference,
    Guid ScanWorkIdentity,
    Guid EvidenceSetId,
    Guid EvidenceItemId,
    Guid UploadAuthorizationId,
    string WorkerId,
    string BucketReference,
    string InternalObjectKey,
    string ExpectedContentType,
    long ExpectedContentLength,
    string ExpectedChecksumSha256,
    string? ProviderObjectVersion,
    long ExpectedItemRowVersion,
    long ExpectedUploadAuthorizationRowVersion,
    int AttemptNumber,
    int RetryCount,
    int MaxAttempts,
    Guid SiteId,
    Guid SiteGroupId,
    Guid ParkingSessionId,
    string SourceChannel,
    Guid CorrelationId);

public sealed record StatutoryEvidenceStructuralValidationResult(
    string Classification,
    bool Passed,
    bool Retryable,
    string? SafeFailureCode,
    int? Width = null,
    int? Height = null);

public sealed record StatutoryEvidenceMalwareScanResult(
    string Classification,
    bool Clean,
    bool Retryable,
    string? SafeFailureCode);

public sealed record StatutoryEvidenceScanCompletion(
    string AttemptStatus,
    string ValidationStatus,
    string ValidationResult,
    string MalwareScanStatus,
    string MalwareScanResult,
    string? SafeFailureClassification,
    bool Retryable,
    bool Terminal);

public interface IStatutoryEvidenceScanner
{
    Task<StatutoryEvidenceMalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public interface IStatutoryEvidenceScanWorkerTestHook
{
    Task AfterClaimCommittedAsync(IReadOnlyList<StatutoryEvidenceScanWorkItem> workItems, CancellationToken cancellationToken);
}

public sealed class NoopStatutoryEvidenceScanWorkerTestHook : IStatutoryEvidenceScanWorkerTestHook
{
    public static readonly NoopStatutoryEvidenceScanWorkerTestHook Instance = new();

    private NoopStatutoryEvidenceScanWorkerTestHook()
    {
    }

    public Task AfterClaimCommittedAsync(IReadOnlyList<StatutoryEvidenceScanWorkItem> workItems, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public interface IStatutoryEvidenceScanRepository
{
    Task<IReadOnlyList<StatutoryEvidenceScanWorkItem>> ClaimDueWorkAsync(
        string workerId,
        Guid? workerServiceIdentityId,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task CompleteAttemptAsync(
        StatutoryEvidenceScanWorkItem workItem,
        StatutoryEvidenceScanCompletion completion,
        Guid? workerServiceIdentityId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task ScheduleRetryAsync(
        StatutoryEvidenceScanWorkItem workItem,
        StatutoryEvidenceScanCompletion completion,
        Guid? workerServiceIdentityId,
        DateTimeOffset nextRetryAt,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
