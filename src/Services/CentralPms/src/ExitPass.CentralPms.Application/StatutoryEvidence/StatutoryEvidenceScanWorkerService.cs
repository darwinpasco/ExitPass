namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public interface IStatutoryEvidenceScanWorkerService
{
    Task<int> RunOnceAsync(CancellationToken cancellationToken);
}

public sealed class StatutoryEvidenceScanWorkerService : IStatutoryEvidenceScanWorkerService
{
    private readonly IStatutoryEvidenceScanRepository _repository;
    private readonly IStatutoryEvidenceProtectedObjectStorageAdapter _storage;
    private readonly IStatutoryEvidenceScanner _scanner;
    private readonly StatutoryEvidenceUploadOptions _uploadOptions;
    private readonly StatutoryEvidenceScanWorkerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IStatutoryEvidenceScanWorkerTestHook _testHook;

    public StatutoryEvidenceScanWorkerService(
        IStatutoryEvidenceScanRepository repository,
        IStatutoryEvidenceProtectedObjectStorageAdapter storage,
        IStatutoryEvidenceScanner scanner,
        StatutoryEvidenceUploadOptions uploadOptions,
        StatutoryEvidenceScanWorkerOptions options,
        TimeProvider timeProvider,
        IStatutoryEvidenceScanWorkerTestHook? testHook = null)
    {
        _repository = repository;
        _storage = storage;
        _scanner = scanner;
        _uploadOptions = uploadOptions;
        _options = options;
        _timeProvider = timeProvider;
        _testHook = testHook ?? NoopStatutoryEvidenceScanWorkerTestHook.Instance;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.HasCriticalConfiguration())
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow();
        var work = await _repository.ClaimDueWorkAsync(
            _options.WorkerId,
            _options.WorkerServiceIdentityId,
            Math.Clamp(_options.BatchSize, 1, 100),
            _options.LeaseDuration,
            now,
            cancellationToken);

        await _testHook.AfterClaimCommittedAsync(work, cancellationToken);

        var processed = 0;
        using var semaphore = new SemaphoreSlim(Math.Clamp(_options.MaxConcurrency, 1, 16));
        var tasks = work.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await ProcessOneAsync(item, cancellationToken);
                Interlocked.Increment(ref processed);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return processed;
    }

    private async Task ProcessOneAsync(StatutoryEvidenceScanWorkItem item, CancellationToken cancellationToken)
    {
        StatutoryEvidenceObjectContent? content = null;
        try
        {
            content = await _storage.GetObjectContentAsync(
                new StatutoryEvidenceObjectContentRequest(
                    ResolveBucketName(),
                    item.InternalObjectKey,
                    Math.Min(_options.MaxContentLengthBytes, _uploadOptions.MaxContentLengthBytes > 0 ? _uploadOptions.MaxContentLengthBytes : _options.MaxContentLengthBytes)),
                cancellationToken);

            var metadataFailure = ValidateMetadata(item, content);
            if (metadataFailure is not null)
            {
                await CompleteOrRetryAsync(item, metadataFailure, cancellationToken);
                return;
            }

            await using (content)
            {
                var validation = await ValidateStructureAsync(item, content.Content, cancellationToken);
                if (!validation.Passed)
                {
                    await CompleteOrRetryAsync(item, ToCompletion(validation), cancellationToken);
                    return;
                }

                content.Content.Position = 0;
                var scan = await _scanner.ScanAsync(content.Content, cancellationToken);
                await CompleteOrRetryAsync(item, ToCompletion(validation, scan), cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            await CompleteOrRetryAsync(item, Retryable("UNAVAILABLE", "NOT_RUN", "ERROR_RETRYABLE", "SCANNER_ERROR_RETRYABLE", "STORAGE_UNAVAILABLE"), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (content is not null)
            {
                await content.DisposeAsync();
            }
        }
    }

    private async Task CompleteOrRetryAsync(
        StatutoryEvidenceScanWorkItem item,
        StatutoryEvidenceScanCompletion completion,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (completion.Retryable && item.RetryCount + 1 < item.MaxAttempts)
        {
            await _repository.ScheduleRetryAsync(
                item,
                completion,
                _options.WorkerServiceIdentityId,
                now.Add(ComputeBackoff(item.AttemptNumber)),
                now,
                cancellationToken);
            return;
        }

        var terminal = completion with
        {
            Retryable = false,
            Terminal = true,
            AttemptStatus = completion.AttemptStatus == "COMPLETED" ? "COMPLETED" : "FAILED_TERMINAL"
        };
        await _repository.CompleteAttemptAsync(item, terminal, _options.WorkerServiceIdentityId, now, cancellationToken);
    }

    private TimeSpan ComputeBackoff(int attemptNumber)
    {
        var initial = Math.Max(1, _options.InitialRetryDelaySeconds);
        var max = Math.Max(initial, _options.MaxRetryDelaySeconds);
        var seconds = Math.Min(max, initial * Math.Pow(2, Math.Max(0, attemptNumber - 1)));
        var jitter = _options.JitterSeconds <= 0 ? 0 : Random.Shared.Next(0, _options.JitterSeconds + 1);
        return TimeSpan.FromSeconds(seconds + jitter);
    }

    private StatutoryEvidenceScanCompletion? ValidateMetadata(
        StatutoryEvidenceScanWorkItem item,
        StatutoryEvidenceObjectContent content)
    {
        if (!string.Equals(content.ContentType, item.ExpectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return Terminal("FAILED", "METADATA_MISMATCH", "ERROR_TERMINAL", "NOT_RUN", "METADATA_MISMATCH");
        }

        if (content.ContentLength != item.ExpectedContentLength || content.ContentLength > _options.MaxContentLengthBytes)
        {
            return Terminal("FAILED", "CONTENT_TOO_LARGE", "ERROR_TERMINAL", "NOT_RUN", "CONTENT_TOO_LARGE");
        }

        if (!string.Equals(content.ChecksumSha256, item.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Terminal("FAILED", "METADATA_MISMATCH", "ERROR_TERMINAL", "NOT_RUN", "METADATA_MISMATCH");
        }

        if (!string.IsNullOrWhiteSpace(item.ProviderObjectVersion) &&
            !string.Equals(content.ObjectVersion, item.ProviderObjectVersion, StringComparison.Ordinal))
        {
            return Terminal("FAILED", "STALE_OBJECT_VERSION", "ERROR_TERMINAL", "NOT_RUN", "STALE_OBJECT_VERSION");
        }

        return null;
    }

    private async Task<StatutoryEvidenceStructuralValidationResult> ValidateStructureAsync(
        StatutoryEvidenceScanWorkItem item,
        Stream content,
        CancellationToken cancellationToken)
    {
        var bufferLength = (int)Math.Min(Math.Max(_options.MaxHeaderProbeBytes, 64), Math.Min(item.ExpectedContentLength, 1024 * 1024));
        var buffer = new byte[bufferLength];
        var read = 0;
        while (read < buffer.Length)
        {
            var current = await content.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (current == 0)
            {
                break;
            }

            read += current;
        }

        if (read == 0)
        {
            return FailedValidation("MALFORMED_IMAGE");
        }

        return item.ExpectedContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            ? ValidatePng(buffer.AsSpan(0, read))
            : item.ExpectedContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                ? ValidateJpeg(buffer.AsSpan(0, read))
                : FailedValidation("UNSUPPORTED_MEDIA");
    }

    private StatutoryEvidenceStructuralValidationResult ValidatePng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature))
        {
            return FailedValidation("SIGNATURE_MISMATCH");
        }

        if (!bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return FailedValidation("MALFORMED_IMAGE");
        }

        var width = ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = ReadInt32BigEndian(bytes.Slice(20, 4));
        return ValidateDimensions(width, height);
    }

    private StatutoryEvidenceStructuralValidationResult ValidateJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return FailedValidation("SIGNATURE_MISMATCH");
        }

        var offset = 2;
        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = bytes[offset + 1];
            var length = ReadInt16BigEndian(bytes.Slice(offset + 2, 2));
            if (length < 2 || offset + 2 + length > bytes.Length)
            {
                return FailedValidation("MALFORMED_IMAGE");
            }

            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                var height = ReadInt16BigEndian(bytes.Slice(offset + 5, 2));
                var width = ReadInt16BigEndian(bytes.Slice(offset + 7, 2));
                return ValidateDimensions(width, height);
            }

            offset += 2 + length;
        }

        return FailedValidation("MALFORMED_IMAGE");
    }

    private StatutoryEvidenceStructuralValidationResult ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return FailedValidation("MALFORMED_IMAGE");
        }

        if (width > _options.MaxDecodedWidth || height > _options.MaxDecodedHeight)
        {
            return FailedValidation("DIMENSION_LIMIT_EXCEEDED");
        }

        if ((long)width * height > _options.MaxDecodedPixelCount)
        {
            return FailedValidation("PIXEL_LIMIT_EXCEEDED");
        }

        return new("PASSED", true, false, null, width, height);
    }

    private static StatutoryEvidenceScanCompletion ToCompletion(StatutoryEvidenceStructuralValidationResult validation) =>
        validation.Retryable
            ? Retryable("RETRY_PENDING", validation.Classification, "ERROR_RETRYABLE", "NOT_RUN", validation.SafeFailureCode ?? validation.Classification)
            : Terminal("FAILED", validation.Classification, "ERROR_TERMINAL", "NOT_RUN", validation.SafeFailureCode ?? validation.Classification);

    private static StatutoryEvidenceScanCompletion ToCompletion(
        StatutoryEvidenceStructuralValidationResult validation,
        StatutoryEvidenceMalwareScanResult scan)
    {
        if (scan.Clean)
        {
            return new("COMPLETED", "PASSED", validation.Classification, "CLEAN", "CLEAN", null, false, true);
        }

        if (scan.Retryable)
        {
            return Retryable("PASSED", validation.Classification, "ERROR_RETRYABLE", scan.Classification, scan.SafeFailureCode ?? scan.Classification);
        }

        var scanStatus = scan.Classification is "MALICIOUS" or "SUSPICIOUS" ? scan.Classification : "ERROR_TERMINAL";
        return Terminal("PASSED", validation.Classification, scanStatus, scan.Classification, scan.SafeFailureCode ?? scan.Classification);
    }

    private static StatutoryEvidenceScanCompletion Retryable(
        string validationStatus,
        string validationResult,
        string scanStatus,
        string scanResult,
        string failure) =>
        new("RETRY_PENDING", validationStatus, validationResult, scanStatus, scanResult, failure, true, false);

    private static StatutoryEvidenceScanCompletion Terminal(
        string validationStatus,
        string validationResult,
        string scanStatus,
        string scanResult,
        string failure) =>
        new("FAILED_TERMINAL", validationStatus, validationResult, scanStatus, scanResult, failure, false, true);

    private static StatutoryEvidenceStructuralValidationResult FailedValidation(string code) =>
        new(code, false, false, code);

    private string ResolveBucketName() =>
        !string.IsNullOrWhiteSpace(_uploadOptions.BucketName)
            ? _uploadOptions.BucketName
            : throw new InvalidOperationException("Evidence storage bucket is not configured.");

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static int ReadInt16BigEndian(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 8) | bytes[1];
}
