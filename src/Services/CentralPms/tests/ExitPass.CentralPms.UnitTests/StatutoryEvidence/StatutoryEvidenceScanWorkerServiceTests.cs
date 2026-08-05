using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryEvidence;

public sealed class StatutoryEvidenceScanWorkerServiceTests
{
    [Fact]
    public async Task RunOnceAsync_CleanJpeg_CompletesAsCleanAndReviewableCandidate()
    {
        var repo = new RecordingRepository(Work());
        var service = CreateService(repo, new MemoryStorage(JpegBytes(), "image/jpeg", "abc"), new StaticScanner(new("CLEAN", true, false, null)));

        var processed = await service.RunOnceAsync(CancellationToken.None);

        processed.Should().Be(1);
        repo.Completed.Should().NotBeNull();
        repo.Completed!.ValidationStatus.Should().Be("PASSED");
        repo.Completed.MalwareScanStatus.Should().Be("CLEAN");
        repo.Completed.MalwareScanResult.Should().Be("CLEAN");
    }

    [Fact]
    public async Task RunOnceAsync_CleanPng_CompletesAsClean()
    {
        var repo = new RecordingRepository(Work(contentType: "image/png"));
        var service = CreateService(repo, new MemoryStorage(PngBytes(width: 2, height: 3), "image/png", "abc"), new StaticScanner(new("CLEAN", true, false, null)));

        await service.RunOnceAsync(CancellationToken.None);

        repo.Completed.Should().NotBeNull();
        repo.Completed!.ValidationResult.Should().Be("PASSED");
        repo.Completed.MalwareScanStatus.Should().Be("CLEAN");
    }

    [Fact]
    public async Task RunOnceAsync_SignatureMismatch_FailsClosedBeforeScanner()
    {
        var scanner = new RecordingScanner(new("CLEAN", true, false, null));
        var repo = new RecordingRepository(Work());
        var invalidJpeg = JpegBytes();
        invalidJpeg[0] = 0x00;
        var service = CreateService(repo, new MemoryStorage(invalidJpeg, "image/jpeg", "abc"), scanner);

        await service.RunOnceAsync(CancellationToken.None);

        scanner.Calls.Should().Be(0);
        repo.Completed.Should().NotBeNull();
        repo.Completed!.ValidationResult.Should().Be("SIGNATURE_MISMATCH");
        repo.Completed.MalwareScanStatus.Should().Be("ERROR_TERMINAL");
    }

    [Fact]
    public async Task RunOnceAsync_MaliciousScan_FailsClosedAndDoesNotBecomeClean()
    {
        var repo = new RecordingRepository(Work());
        var service = CreateService(repo, new MemoryStorage(JpegBytes(), "image/jpeg", "abc"), new StaticScanner(new("MALICIOUS", false, false, "MALWARE_DETECTED")));

        await service.RunOnceAsync(CancellationToken.None);

        repo.Completed.Should().NotBeNull();
        repo.Completed!.ValidationStatus.Should().Be("PASSED");
        repo.Completed.MalwareScanStatus.Should().Be("MALICIOUS");
        repo.Completed.SafeFailureClassification.Should().Be("MALWARE_DETECTED");
    }

    [Fact]
    public async Task RunOnceAsync_ScannerUnavailable_SchedulesRetry()
    {
        var repo = new RecordingRepository(Work());
        var service = CreateService(repo, new MemoryStorage(JpegBytes(), "image/jpeg", "abc"), new StaticScanner(new("SCANNER_UNAVAILABLE", false, true, "SCANNER_UNAVAILABLE")));

        await service.RunOnceAsync(CancellationToken.None);

        repo.Retry.Should().NotBeNull();
        repo.Retry!.MalwareScanStatus.Should().Be("ERROR_RETRYABLE");
        repo.Completed.Should().BeNull();
    }

    [Fact]
    public async Task RunOnceAsync_ScannerUnavailable_WhenRetryLimitReached_FailsTerminalWithoutSchedulingRetry()
    {
        var repo = new RecordingRepository(Work(retryCount: 2, maxAttempts: 3));
        var service = CreateService(repo, new MemoryStorage(JpegBytes(), "image/jpeg", "abc"), new StaticScanner(new("SCANNER_UNAVAILABLE", false, true, "SCANNER_UNAVAILABLE")));

        await service.RunOnceAsync(CancellationToken.None);

        repo.Retry.Should().BeNull();
        repo.Completed.Should().NotBeNull();
        repo.Completed!.AttemptStatus.Should().Be("FAILED_TERMINAL");
        repo.Completed.Retryable.Should().BeFalse();
        repo.Completed.Terminal.Should().BeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_ScannerUnavailable_WhenRetryable_PersistsBoundedBackoffWindow()
    {
        var repo = new RecordingRepository(Work(retryCount: 0, maxAttempts: 3));
        var service = CreateService(
            repo,
            new MemoryStorage(JpegBytes(), "image/jpeg", "abc"),
            new StaticScanner(new("SCANNER_UNAVAILABLE", false, true, "SCANNER_UNAVAILABLE")),
            configureOptions: options =>
            {
                options.InitialRetryDelaySeconds = 2;
                options.MaxRetryDelaySeconds = 4;
                options.JitterSeconds = 0;
            });

        await service.RunOnceAsync(CancellationToken.None);

        repo.Retry.Should().NotBeNull();
        repo.NextRetryAt.Should().NotBeNull();
        repo.RetryScheduledAt.Should().NotBeNull();
        (repo.NextRetryAt!.Value - repo.RetryScheduledAt!.Value).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RunOnceAsync_DisabledWorker_ClaimsNoWork()
    {
        var repo = new RecordingRepository(Work());
        var service = CreateService(repo, new MemoryStorage(JpegBytes(), "image/jpeg", "abc"), new StaticScanner(new("CLEAN", true, false, null)), enabled: false);

        var processed = await service.RunOnceAsync(CancellationToken.None);

        processed.Should().Be(0);
        repo.ClaimCalls.Should().Be(0);
    }
    [Fact]
    public async Task RunOnceAsync_TestHookPausesAfterClaimBeforeObjectRetrieval()
    {
        var repo = new RecordingRepository(Work());
        var storage = new MemoryStorage(JpegBytes(), "image/jpeg", "abc");
        var scanner = new RecordingScanner(new("CLEAN", true, false, null));
        var hook = new PausingScanWorkerTestHook();
        var service = CreateService(repo, storage, scanner, testHook: hook);
        using var cancellation = new CancellationTokenSource();

        var runTask = service.RunOnceAsync(cancellation.Token);
        await hook.WaitForPauseAsync(TimeSpan.FromSeconds(5));

        repo.ClaimCalls.Should().Be(1);
        hook.ClaimedItems.Should().ContainSingle();
        storage.ContentCalls.Should().Be(0);
        scanner.Calls.Should().Be(0);
        repo.Completed.Should().BeNull();
        repo.Retry.Should().BeNull();

        cancellation.Cancel();
        await runTask.Invoking(static task => task).Should().ThrowAsync<OperationCanceledException>();
    }


    [Fact]
    public void ScanExecutePolicy_UsesDedicatedPermissionOnly()
    {
        var permissions = ExitPass.CentralPms.Application.Security.CentralPmsRbacPolicyCatalog.ResolvePermissions(StatutoryEvidenceScanConstants.ExecutePolicy);

        permissions.Should().ContainSingle(StatutoryEvidenceScanConstants.ExecutePermission);
        permissions.Should().NotContain("statutory-discounts.evidence.capture");
        permissions.Should().NotContain("statutory-discounts.evidence.view");
        permissions.Should().NotContain("reconciliation.manage");
    }

    private static StatutoryEvidenceScanWorkerService CreateService(
        RecordingRepository repository,
        IStatutoryEvidenceProtectedObjectStorageAdapter storage,
        IStatutoryEvidenceScanner scanner,
        bool enabled = true,
        Action<StatutoryEvidenceScanWorkerOptions>? configureOptions = null,
        IStatutoryEvidenceScanWorkerTestHook? testHook = null)
    {
        var options = new StatutoryEvidenceScanWorkerOptions
        {
            Enabled = enabled,
            ScannerProvider = StatutoryEvidenceScanConstants.ScannerProviderNoopTestOnly,
            MaxContentLengthBytes = 4096,
            MaxAttempts = 3,
            ScannerEndpoint = "127.0.0.1"
        };
        configureOptions?.Invoke(options);
        return
        new(
            repository,
            storage,
            scanner,
            new StatutoryEvidenceUploadOptions { BucketName = "private", MaxContentLengthBytes = 4096 },
            options,
            TimeProvider.System,
            testHook);
    }

    private static StatutoryEvidenceScanWorkItem Work(string contentType = "image/jpeg", int retryCount = 0, int maxAttempts = 3) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "worker-a",
            "bucket-ref",
            "internal/object",
            contentType,
            contentType == "image/png" ? PngBytes().Length : JpegBytes().Length,
            "abc",
            null,
            2,
            2,
            1,
            retryCount,
            maxAttempts,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "WEBPAY",
            Guid.NewGuid());

    private static byte[] PngBytes(int width = 1, int height = 1)
    {
        var bytes = new byte[33];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        bytes[8] = 0;
        bytes[9] = 0;
        bytes[10] = 0;
        bytes[11] = 13;
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        WriteBigEndian(bytes.AsSpan(16, 4), width);
        WriteBigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 2;
        return bytes;
    }

    private static byte[] JpegBytes()
    {
        return
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48, 0x00, 0x48,
            0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x02, 0x00, 0x03, 0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01,
            0x03, 0x11, 0x01,
            0xFF, 0xD9
        ];
    }

    private static void WriteBigEndian(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private sealed class RecordingRepository : IStatutoryEvidenceScanRepository
    {
        private readonly StatutoryEvidenceScanWorkItem _work;

        public RecordingRepository(StatutoryEvidenceScanWorkItem work)
        {
            _work = work;
        }

        public int ClaimCalls { get; private set; }
        public StatutoryEvidenceScanCompletion? Completed { get; private set; }
        public StatutoryEvidenceScanCompletion? Retry { get; private set; }
        public DateTimeOffset? NextRetryAt { get; private set; }
        public DateTimeOffset? RetryScheduledAt { get; private set; }

        public Task<IReadOnlyList<StatutoryEvidenceScanWorkItem>> ClaimDueWorkAsync(string workerId, Guid? workerServiceIdentityId, int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ClaimCalls++;
            return Task.FromResult<IReadOnlyList<StatutoryEvidenceScanWorkItem>>([_work]);
        }

        public Task CompleteAttemptAsync(StatutoryEvidenceScanWorkItem workItem, StatutoryEvidenceScanCompletion completion, Guid? workerServiceIdentityId, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            Completed = completion;
            return Task.CompletedTask;
        }

        public Task ScheduleRetryAsync(StatutoryEvidenceScanWorkItem workItem, StatutoryEvidenceScanCompletion completion, Guid? workerServiceIdentityId, DateTimeOffset nextRetryAt, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Retry = completion;
            NextRetryAt = nextRetryAt;
            RetryScheduledAt = now;
            return Task.CompletedTask;
        }
    }

    private sealed class PausingScanWorkerTestHook : IStatutoryEvidenceScanWorkerTestHook
    {
        private readonly TaskCompletionSource _pauseReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<StatutoryEvidenceScanWorkItem> ClaimedItems { get; private set; } = [];

        public Task AfterClaimCommittedAsync(IReadOnlyList<StatutoryEvidenceScanWorkItem> workItems, CancellationToken cancellationToken)
        {
            ClaimedItems = workItems;
            _pauseReached.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task WaitForPauseAsync(TimeSpan timeout) => _pauseReached.Task.WaitAsync(timeout);
    }
    private sealed class MemoryStorage : IStatutoryEvidenceProtectedObjectStorageAdapter
    {
        private readonly byte[] _bytes;
        private readonly string _contentType;
        private readonly string _checksum;

        public MemoryStorage(byte[] bytes, string contentType, string checksum)
        {
            _bytes = bytes;
            _contentType = contentType;
            _checksum = checksum;
        }

        public Task<StatutoryEvidenceObjectUploadAuthorization> CreateUploadAuthorizationAsync(StatutoryEvidenceObjectUploadAuthorizationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StatutoryEvidenceObjectMetadata?> GetObjectMetadataAsync(StatutoryEvidenceObjectMetadataRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public int ContentCalls { get; private set; }

        public Task<StatutoryEvidenceObjectContent> GetObjectContentAsync(StatutoryEvidenceObjectContentRequest request, CancellationToken cancellationToken)
        {
            ContentCalls++;
            return Task.FromResult(new StatutoryEvidenceObjectContent(new MemoryStream(_bytes), _contentType, _bytes.Length, _checksum, null, null));
        }
    }

    private sealed class StaticScanner : IStatutoryEvidenceScanner
    {
        private readonly StatutoryEvidenceMalwareScanResult _result;

        public StaticScanner(StatutoryEvidenceMalwareScanResult result)
        {
            _result = result;
        }

        public Task<StatutoryEvidenceMalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class RecordingScanner : IStatutoryEvidenceScanner
    {
        private readonly StatutoryEvidenceMalwareScanResult _result;

        public RecordingScanner(StatutoryEvidenceMalwareScanResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public Task<StatutoryEvidenceMalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
