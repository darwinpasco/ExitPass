namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Internal staged command service for canonical statutory-discount decision-v2 and payable-basis-application-v1.
/// </summary>
public sealed class StatutoryDiscountStagedCommandService : IStatutoryDiscountStagedCommandService
{
    private readonly IStatutoryDiscountStagedCommandRepository _repository;

    public StatutoryDiscountStagedCommandService(IStatutoryDiscountStagedCommandRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>> CreateOrResolveDecisionAsync(
        StatutoryDiscountDecisionV2Command command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateDecisionCommand(command);

        var repositoryCommand = new StatutoryDiscountDecisionV2RepositoryCommand(
            command,
            StatutoryDiscountDecisionV2SemanticHash.BuildBusinessIdentity(command),
            StatutoryDiscountDecisionV2SemanticHash.BuildIdempotencyScope(command),
            StatutoryDiscountDecisionV2SemanticHash.Compute(command),
            StatutoryDiscountDecisionV2SemanticHash.SourceVersion,
            DateTimeOffset.UtcNow);

        return _repository.ExecuteWithDecisionLockAsync(
            repositoryCommand,
            async token =>
            {
                var begin = await _repository.BeginDecisionAsync(repositoryCommand, token).ConfigureAwait(false);
                return ToDecisionStartResult(begin, command.IdempotencyKey);
            },
            cancellationToken);
    }

    public Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken) =>
        _repository.GetDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken);

    public Task<StatutoryDiscountDecisionV2Record?> GetDecisionByBusinessIdentityAsync(
        string businessIdentity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(businessIdentity))
        {
            throw new ArgumentException("Decision business identity is required.", nameof(businessIdentity));
        }

        return _repository.GetDecisionByBusinessIdentityAsync(businessIdentity, cancellationToken);
    }

    public async Task<StatutoryDiscountDecisionV2Record> MarkDecisionProcessingAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateDecisionAsync(record with
        {
            CommandStatus = StatutoryDiscountDecisionV2CommandStates.Processing,
            Retryable = true,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey,
            CorrelationId = correlationId,
            ProcessingStartedAt = record.ProcessingStartedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionV2Record> MarkDecisionAwaitingReviewAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateDecisionAsync(record with
        {
            CommandStatus = StatutoryDiscountDecisionV2CommandStates.AwaitingReview,
            DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.NotDecided,
            ResultClassification = StatutoryDiscountOneShotResultClassifications.AwaitingReview,
            Retryable = false,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.AwaitingReview,
            SafeErrorCode = null,
            CorrelationId = correlationId,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionV2Record> CompleteDecisionApprovedAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid? statutoryDiscountValidationId,
        Guid? originalTariffSnapshotId,
        Guid? appliedPolicyReferenceId,
        Guid? fallbackPolicyReferenceId,
        string? policyResolutionBasis,
        bool localOrdinanceApplied,
        StatutoryDiscountDecisionV2TariffFacts? tariffFacts,
        string? reasonCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateDecisionAsync(record with
        {
            CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
            DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Approved,
            ResultClassification = StatutoryDiscountDecisionClientResultStatuses.Approved,
            Retryable = false,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            SafeErrorCode = null,
            StatutoryDiscountValidationId = statutoryDiscountValidationId,
            OriginalTariffSnapshotId = originalTariffSnapshotId ?? record.OriginalTariffSnapshotId,
            AppliedPolicyReferenceId = appliedPolicyReferenceId,
            FallbackPolicyReferenceId = fallbackPolicyReferenceId,
            PolicyResolutionBasis = NormalizeOptional(policyResolutionBasis),
            LocalOrdinanceApplied = localOrdinanceApplied,
            GrossAmountMinorUnits = tariffFacts?.GrossAmountMinorUnits,
            VatExclusiveAmountMinorUnits = tariffFacts?.VatExclusiveAmountMinorUnits,
            VatAmountMinorUnits = tariffFacts?.VatAmountMinorUnits,
            StatutoryDiscountAmountMinorUnits = tariffFacts?.StatutoryDiscountAmountMinorUnits,
            NetPayableAmountMinorUnits = tariffFacts?.NetPayableAmountMinorUnits,
            Currency = NormalizeOptional(tariffFacts?.Currency),
            ReasonCode = NormalizeOptional(reasonCode),
            CorrelationId = correlationId,
            DecidedAt = record.DecidedAt ?? DateTimeOffset.UtcNow,
            CompletedAt = record.CompletedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionV2Record> CompleteDecisionRejectedAsync(
        Guid statutoryDiscountDecisionCommandId,
        string? reasonCode,
        string? safeErrorCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateDecisionAsync(record with
        {
            CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
            DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Rejected,
            ResultClassification = StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved,
            Retryable = false,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            SafeErrorCode = NormalizeOptional(safeErrorCode),
            ReasonCode = NormalizeOptional(reasonCode),
            CorrelationId = correlationId,
            DecidedAt = record.DecidedAt ?? DateTimeOffset.UtcNow,
            CompletedAt = record.CompletedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionV2Record> RecordDecisionFailureAsync(
        Guid statutoryDiscountDecisionCommandId,
        bool retryable,
        string safeErrorCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateDecisionAsync(record with
        {
            CommandStatus = retryable
                ? StatutoryDiscountDecisionV2CommandStates.FailedRetryable
                : StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable,
            ResultClassification = retryable
                ? StatutoryDiscountDecisionClientResultStatuses.RetryableFailure
                : StatutoryDiscountDecisionClientResultStatuses.NonRetryableFailure,
            Retryable = retryable,
            RecoveryClassification = retryable
                ? StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey
                : StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable,
            SafeErrorCode = NormalizeRequired(safeErrorCode, nameof(safeErrorCode)),
            CorrelationId = correlationId,
            FailedAt = record.FailedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>> CreateOrResolveApplicationAsync(
        StatutoryDiscountPayableBasisApplicationV1Command command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateApplicationCommand(command);

        var decision = await _repository.GetDecisionAsync(command.StatutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);
        if (decision is null)
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.DecisionNotFound,
                Existing: false,
                SemanticConflict: false,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable,
                Record: null,
                SafeErrorCode: "STATUTORY_DISCOUNT_DECISION_NOT_FOUND");
        }

        if (!string.Equals(decision.CommandStatus, StatutoryDiscountDecisionV2CommandStates.Completed, StringComparison.Ordinal)
            || !string.Equals(decision.DecisionResultStatus, StatutoryDiscountDecisionV2ResultStates.Approved, StringComparison.Ordinal))
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.DecisionNotApproved,
                Existing: false,
                SemanticConflict: false,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
                Record: null,
                SafeErrorCode: "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED");
        }

        var repositoryCommand = new StatutoryDiscountPayableBasisApplicationV1RepositoryCommand(
            command,
            StatutoryDiscountPayableBasisApplicationV1SemanticHash.BuildBusinessIdentity(command),
            StatutoryDiscountPayableBasisApplicationV1SemanticHash.BuildIdempotencyScope(command),
            StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(command),
            StatutoryDiscountPayableBasisApplicationV1SemanticHash.SourceVersion,
            DateTimeOffset.UtcNow);

        return await _repository.ExecuteWithApplicationLockAsync(
            repositoryCommand,
            async token =>
            {
                var begin = await _repository.BeginApplicationAsync(repositoryCommand, token).ConfigureAwait(false);
                return ToApplicationStartResult(begin, command.IdempotencyKey);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken) =>
        _repository.GetApplicationAsync(statutoryDiscountPayableBasisApplicationCommandId, cancellationToken);

    public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationByDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken) =>
        _repository.GetApplicationByDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken);

    public async Task<StatutoryDiscountPayableBasisApplicationV1Record> MarkApplicationProcessingAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireApplicationAsync(statutoryDiscountPayableBasisApplicationCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateApplicationAsync(record with
        {
            CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing,
            ResultClassification = StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
            Retryable = true,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey,
            CorrelationId = correlationId,
            ProcessingStartedAt = record.ProcessingStartedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountPayableBasisApplicationV1Record> CompleteApplicationAppliedAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        Guid? statutoryDiscountPayableBasisApplicationId,
        Guid? appliedTariffSnapshotId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireApplicationAsync(statutoryDiscountPayableBasisApplicationCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateApplicationAsync(record with
        {
            StatutoryDiscountPayableBasisApplicationId = statutoryDiscountPayableBasisApplicationId,
            AppliedTariffSnapshotId = appliedTariffSnapshotId ?? record.AppliedTariffSnapshotId,
            CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied,
            ResultClassification = StatutoryDiscountPayableBasisApplicationV1ResultClassifications.Applied,
            Retryable = false,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            SafeErrorCode = null,
            CorrelationId = correlationId,
            AppliedAt = record.AppliedAt ?? DateTimeOffset.UtcNow,
            CompletedAt = record.CompletedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountPayableBasisApplicationV1Record> RecordApplicationFailureAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        bool retryable,
        string safeErrorCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var record = await RequireApplicationAsync(statutoryDiscountPayableBasisApplicationCommandId, cancellationToken)
            .ConfigureAwait(false);

        return await _repository.UpdateApplicationAsync(record with
        {
            CommandStatus = retryable
                ? StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedRetryable
                : StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable,
            ResultClassification = retryable
                ? StatutoryDiscountPayableBasisApplicationV1ResultClassifications.RetryableFailure
                : StatutoryDiscountPayableBasisApplicationV1ResultClassifications.NonRetryableFailure,
            Retryable = retryable,
            RecoveryClassification = retryable
                ? StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey
                : StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable,
            SafeErrorCode = NormalizeRequired(safeErrorCode, nameof(safeErrorCode)),
            CorrelationId = correlationId,
            FailedAt = record.FailedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    private static StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record> ToDecisionStartResult(
        StatutoryDiscountDecisionV2BeginResult begin,
        string idempotencyKey)
    {
        if (begin.SemanticConflict)
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                StatutoryDiscountDecisionClientResultStatuses.SemanticConflict,
                begin.Existing,
                SemanticConflict: true,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
                begin.Record,
                "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT");
        }

        if (begin.Existing
            && IsProcessing(begin.Record.CommandStatus)
            && !string.Equals(begin.Record.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                StatutoryDiscountDecisionClientResultStatuses.InProgress,
                Existing: true,
                SemanticConflict: false,
                Retryable: true,
                StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
                begin.Record,
                "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS");
        }

        if (begin.Existing)
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                begin.RecoverableWithOriginalKey
                    ? StatutoryDiscountDecisionClientResultStatuses.RecoverableUsingOriginalKey
                    : StatutoryDiscountDecisionClientResultStatuses.IdempotentReplay,
                Existing: true,
                SemanticConflict: false,
                Retryable: begin.RecoverableWithOriginalKey,
                begin.RecoverableWithOriginalKey
                    ? StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey
                    : StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                begin.Record,
                null);
        }

        return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
            StatutoryDiscountDecisionClientResultStatuses.CreatedDurablyCompleted,
            Existing: false,
            SemanticConflict: false,
            Retryable: false,
            StatutoryDiscountDecisionRecoveryClassifications.None,
            begin.Record,
            null);
    }

    private static StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record> ToApplicationStartResult(
        StatutoryDiscountPayableBasisApplicationV1BeginResult begin,
        string idempotencyKey)
    {
        if (begin.SemanticConflict)
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.SemanticConflict,
                begin.Existing,
                SemanticConflict: true,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
                begin.Record,
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT");
        }

        if (begin.Existing
            && IsProcessing(begin.Record.CommandStatus)
            && !string.Equals(begin.Record.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
                Existing: true,
                SemanticConflict: false,
                Retryable: true,
                StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
                begin.Record,
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS");
        }

        if (begin.Existing)
        {
            return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                begin.RecoverableWithOriginalKey
                    ? StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress
                    : StatutoryDiscountPayableBasisApplicationV1ResultClassifications.IdempotentReplay,
                Existing: true,
                SemanticConflict: false,
                Retryable: begin.RecoverableWithOriginalKey,
                begin.RecoverableWithOriginalKey
                    ? StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey
                    : StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                begin.Record,
                null);
        }

        return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
            StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
            Existing: false,
            SemanticConflict: false,
            Retryable: false,
            StatutoryDiscountDecisionRecoveryClassifications.None,
            begin.Record,
            null);
    }

    private async Task<StatutoryDiscountDecisionV2Record> RequireDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetDecisionAsync(statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);
        return record ?? throw new StatutoryDiscountDecisionRejectedException(
            "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
            "Statutory discount decision command was not found.",
            isNotFound: true);
    }

    private async Task<StatutoryDiscountPayableBasisApplicationV1Record> RequireApplicationAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetApplicationAsync(statutoryDiscountPayableBasisApplicationCommandId, cancellationToken)
            .ConfigureAwait(false);
        return record ?? throw new StatutoryDiscountDecisionRejectedException(
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_FOUND",
            "Statutory discount payable-basis application command was not found.",
            isNotFound: true);
    }

    private static void ValidateDecisionCommand(StatutoryDiscountDecisionV2Command command)
    {
        EnsureRequired(command.SourceChannel, nameof(command.SourceChannel));
        if (!StatutoryDiscountSourceChannels.IsSupported(command.SourceChannel))
        {
            throw new ArgumentException("Unsupported source channel.", nameof(command));
        }

        EnsureRequired(command.EntitlementType, nameof(command.EntitlementType));
        if (StatutoryDiscountDecisionV2SemanticHash.Normalize(command.EntitlementType) is not ("SENIOR_CITIZEN" or "PWD"))
        {
            throw new ArgumentException("Unsupported entitlement type.", nameof(command));
        }

        EnsureRequired(command.IdempotencyKey, nameof(command.IdempotencyKey));
        EnsureRequired(command.Decision.Decision, nameof(command.Decision.Decision));
    }

    private static void ValidateApplicationCommand(StatutoryDiscountPayableBasisApplicationV1Command command)
    {
        EnsureRequired(command.EntitlementType, nameof(command.EntitlementType));
        EnsureRequired(command.Currency, nameof(command.Currency));
        EnsureRequired(command.SourceChannel, nameof(command.SourceChannel));
        EnsureRequired(command.IdempotencyKey, nameof(command.IdempotencyKey));

        if (!StatutoryDiscountSourceChannels.IsSupported(command.SourceChannel))
        {
            throw new ArgumentException("Unsupported source channel.", nameof(command));
        }

        if (command.ApprovedDiscountAmountMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Approved discount amount must not be negative.");
        }

        if (command.ApprovedFinalPayableAmountMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Approved final payable amount must not be negative.");
        }
    }

    private static bool IsProcessing(string status) =>
        status is StatutoryDiscountDecisionV2CommandStates.Received
            or StatutoryDiscountDecisionV2CommandStates.Processing
            or StatutoryDiscountPayableBasisApplicationV1CommandStates.Received
            or StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing;

    private static string NormalizeRequired(string value, string parameterName)
    {
        EnsureRequired(value, parameterName);
        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static void EnsureRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
