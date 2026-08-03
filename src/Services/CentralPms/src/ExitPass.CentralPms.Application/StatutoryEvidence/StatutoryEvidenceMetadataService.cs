namespace ExitPass.CentralPms.Application.StatutoryEvidence;

public sealed class StatutoryEvidenceMetadataService : IStatutoryEvidenceMetadataService
{
    private readonly IStatutoryEvidenceMetadataRepository _repository;

    public StatutoryEvidenceMetadataService(IStatutoryEvidenceMetadataRepository repository)
    {
        _repository = repository;
    }

    public async Task<StatutoryEvidenceOperationOutcome> CreateOrResolveSetAsync(StatutoryEvidenceCreateSetCommand command, CancellationToken cancellationToken)
    {
        var validation = ValidateCreateSet(command);
        if (validation is not null)
        {
            return Rejected(validation);
        }

        var binding = await _repository.ResolveRequestBindingAsync(command.StatutoryDiscountDecisionCommandId, cancellationToken);
        if (binding is null)
        {
            await _repository.RecordAccessDeniedAsync(null, null, null, command.ParkingSessionId, command.CorrelationId, command.Actor, "REQUEST_BINDING_NOT_FOUND", cancellationToken);
            return Rejected("REQUEST_BINDING_NOT_FOUND");
        }

        if (!MatchesDurableBinding(command, binding))
        {
            await _repository.RecordAccessDeniedAsync(null, binding.SiteId, binding.SiteGroupId, binding.ParkingSessionId, command.CorrelationId, command.Actor, "BINDING_MISMATCH", cancellationToken);
            return Rejected("BINDING_MISMATCH");
        }

        if (!await IsAuthorizedAsync(command.Actor, StatutoryEvidenceScopeOperations.Capture, binding.SiteId, binding.SiteGroupId, cancellationToken))
        {
            await _repository.RecordAccessDeniedAsync(null, binding.SiteId, binding.SiteGroupId, binding.ParkingSessionId, command.CorrelationId, command.Actor, "SCOPE_DENIED", cancellationToken);
            return Rejected("SCOPE_DENIED");
        }

        var boundCommand = command with
        {
            StatutoryDiscountValidationId = binding.StatutoryDiscountValidationId,
            ParkingSessionId = binding.ParkingSessionId,
            SiteId = binding.SiteId,
            SiteGroupId = binding.SiteGroupId,
            EntitlementType = binding.EntitlementType,
            Actor = command.Actor with { SourceChannel = binding.SourceChannel }
        };

        if (!await _repository.ApprovedRetentionPolicyExistsAsync(boundCommand.RetentionClassCode, boundCommand.RetentionPolicyVersion, boundCommand.EnvironmentScope, cancellationToken))
        {
            return Rejected("RETENTION_POLICY_REQUIRED");
        }

        var hash = StatutoryEvidenceSemanticHash.For(boundCommand);
        var replay = await ReplayAsync("CREATE_SET", boundCommand.IdempotencyScope, boundCommand.IdempotencyKey, hash, boundCommand.CorrelationId, boundCommand.Actor, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var created = await _repository.CreateEvidenceSetAsync(boundCommand, hash, cancellationToken);
        return Accepted(created.ReadModel);
    }

    public async Task<StatutoryEvidenceOperationOutcome> AddItemAsync(StatutoryEvidenceAddItemCommand command, CancellationToken cancellationToken)
    {
        var validation = ValidateAddItem(command);
        if (validation is not null)
        {
            return Rejected(validation);
        }

        var set = await _repository.GetEvidenceSetAsync(command.EvidenceSetReference, cancellationToken);
        if (set is null)
        {
            await _repository.RecordAccessDeniedAsync(command.EvidenceSetReference, null, null, null, command.CorrelationId, command.Actor, "UNKNOWN_REFERENCE", cancellationToken);
            return Rejected("INVALID_EVIDENCE_SET_REFERENCE");
        }

        if (!StatutoryEvidenceMetadataConstants.CodeComparer.Equals(command.Actor.SourceChannel, set.SourceChannel) ||
            !await IsAuthorizedAsync(command.Actor, StatutoryEvidenceScopeOperations.Capture, set.SiteId, set.SiteGroupId, cancellationToken))
        {
            await _repository.RecordAccessDeniedAsync(command.EvidenceSetReference, set.SiteId, set.SiteGroupId, set.ParkingSessionId, command.CorrelationId, command.Actor, "SCOPE_DENIED", cancellationToken);
            return Rejected("SCOPE_DENIED");
        }

        var hash = StatutoryEvidenceSemanticHash.For(command);
        var replay = await ReplayAsync("ADD_ITEM", command.IdempotencyScope, command.IdempotencyKey, hash, command.CorrelationId, command.Actor, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var created = await _repository.AddEvidenceItemAsync(command, hash, cancellationToken);
        return created is null
            ? Rejected("INVALID_EVIDENCE_SET_OR_ROLE_CONFLICT")
            : new StatutoryEvidenceOperationOutcome("ACCEPTED", false, null, created.SetReadModel, created.ItemReadModel);
    }

    public async Task<StatutoryEvidenceOperationOutcome> LockForReviewAsync(StatutoryEvidenceLockForReviewCommand command, CancellationToken cancellationToken)
    {
        if (command.EvidenceSetReference == Guid.Empty)
        {
            return Rejected("INVALID_EVIDENCE_SET_REFERENCE");
        }

        var set = await AuthorizeSetOperationAsync(command.EvidenceSetReference, command.Actor, StatutoryEvidenceScopeOperations.ReviewLock, command.CorrelationId, cancellationToken);
        if (set is null)
        {
            return Rejected("SCOPE_DENIED");
        }

        var hash = StatutoryEvidenceSemanticHash.For(command);
        var replay = await ReplayAsync("LOCK_FOR_REVIEW", command.IdempotencyScope, command.IdempotencyKey, hash, command.CorrelationId, command.Actor, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var model = await _repository.LockForReviewAsync(command, hash, cancellationToken);
        return model is null ? Rejected("INVALID_TRANSITION") : Accepted(model);
    }

    public async Task<StatutoryEvidenceOperationOutcome> PlaceHoldAsync(StatutoryEvidenceHoldCommand command, CancellationToken cancellationToken)
    {
        if (command.EvidenceSetReference == Guid.Empty || string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            return Rejected("INVALID_HOLD_REQUEST");
        }

        var set = await AuthorizeSetOperationAsync(command.EvidenceSetReference, command.Actor, StatutoryEvidenceScopeOperations.Hold, command.CorrelationId, cancellationToken);
        if (set is null)
        {
            return Rejected("SCOPE_DENIED");
        }

        var hash = StatutoryEvidenceSemanticHash.For(command);
        var replay = await ReplayAsync("PLACE_HOLD", command.IdempotencyScope, command.IdempotencyKey, hash, command.CorrelationId, command.Actor, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var model = await _repository.PlaceHoldAsync(command, hash, cancellationToken);
        return model is null ? Rejected("INVALID_HOLD_REQUEST") : Accepted(model);
    }

    public async Task<StatutoryEvidenceOperationOutcome> ReleaseHoldAsync(StatutoryEvidenceReleaseHoldCommand command, CancellationToken cancellationToken)
    {
        if (command.EvidenceSetReference == Guid.Empty)
        {
            return Rejected("INVALID_EVIDENCE_SET_REFERENCE");
        }

        var set = await AuthorizeSetOperationAsync(command.EvidenceSetReference, command.Actor, StatutoryEvidenceScopeOperations.Hold, command.CorrelationId, cancellationToken);
        if (set is null)
        {
            return Rejected("SCOPE_DENIED");
        }

        var hash = StatutoryEvidenceSemanticHash.For(command);
        var replay = await ReplayAsync("RELEASE_HOLD", command.IdempotencyScope, command.IdempotencyKey, hash, command.CorrelationId, command.Actor, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var model = await _repository.ReleaseHoldAsync(command, hash, cancellationToken);
        return model is null ? Rejected("INVALID_HOLD_REQUEST") : Accepted(model);
    }

    public async Task<StatutoryEvidenceOperationOutcome> RequestDeletionAsync(StatutoryEvidenceDeletionRequestCommand command, CancellationToken cancellationToken)
    {
        if (command.EvidenceSetReference == Guid.Empty)
        {
            return Rejected("INVALID_EVIDENCE_SET_REFERENCE");
        }

        var set = await AuthorizeSetOperationAsync(command.EvidenceSetReference, command.Actor, StatutoryEvidenceScopeOperations.DeletionRequest, command.CorrelationId, cancellationToken);
        if (set is null)
        {
            return Rejected("SCOPE_DENIED");
        }

        var hash = StatutoryEvidenceSemanticHash.For(command);
        var replay = await ReplayAsync("REQUEST_DELETION", command.IdempotencyScope, command.IdempotencyKey, hash, command.CorrelationId, command.Actor, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var model = await _repository.RequestDeletionAsync(command, hash, cancellationToken);
        return model is null ? Rejected("DELETION_BLOCKED_OR_INVALID") : Accepted(model);
    }

    public async Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetAsync(Guid evidenceSetReference, StatutoryEvidenceActor actor, Guid correlationId, CancellationToken cancellationToken)
    {
        if (evidenceSetReference == Guid.Empty)
        {
            await _repository.RecordAccessDeniedAsync(null, null, null, null, correlationId, actor, "MALFORMED_REFERENCE", cancellationToken);
            return null;
        }

        var model = await _repository.GetEvidenceSetAsync(evidenceSetReference, cancellationToken);
        if (model is null)
        {
            await _repository.RecordAccessDeniedAsync(evidenceSetReference, null, null, null, correlationId, actor, "UNKNOWN_REFERENCE", cancellationToken);
            return null;
        }

        if (!await IsAuthorizedAsync(actor, StatutoryEvidenceScopeOperations.View, model.SiteId, model.SiteGroupId, cancellationToken))
        {
            await _repository.RecordAccessDeniedAsync(evidenceSetReference, model.SiteId, model.SiteGroupId, model.ParkingSessionId, correlationId, actor, "SCOPE_DENIED", cancellationToken);
            return null;
        }

        return model;
    }

    private async Task<StatutoryEvidenceOperationOutcome?> ReplayAsync(string operationType, string scope, string key, string hash, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
    {
        var existing = await _repository.FindOperationAsync(scope, key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.SemanticRequestHash, hash, StringComparison.Ordinal))
        {
            await _repository.RecordSemanticConflictAsync(operationType, scope, key, correlationId, actor, cancellationToken);
            return new StatutoryEvidenceOperationOutcome("SEMANTIC_CONFLICT", false, "IDEMPOTENCY_SEMANTIC_CONFLICT", null, null);
        }

        var set = existing.EvidenceSetId.HasValue
            ? await _repository.GetEvidenceSetByIdAsync(existing.EvidenceSetId.Value, cancellationToken)
            : null;
        return new StatutoryEvidenceOperationOutcome("IDEMPOTENT_REPLAY", false, null, set, null);
    }

    private static string? ValidateCreateSet(StatutoryEvidenceCreateSetCommand command)
    {
        if (command.StatutoryDiscountDecisionCommandId == Guid.Empty || command.ParkingSessionId == Guid.Empty || command.SiteId == Guid.Empty || command.SiteGroupId == Guid.Empty)
        {
            return "BINDING_REQUIRED";
        }

        if (!StatutoryEvidenceMetadataConstants.SourceChannels.Contains(command.Actor.SourceChannel))
        {
            return "INVALID_SOURCE_CHANNEL";
        }

        if (!StatutoryEvidenceMetadataConstants.CodeComparer.Equals(command.EntitlementType, "SENIOR_CITIZEN") &&
            !StatutoryEvidenceMetadataConstants.CodeComparer.Equals(command.EntitlementType, "PWD"))
        {
            return "INVALID_ENTITLEMENT_TYPE";
        }

        if (string.IsNullOrWhiteSpace(command.RetentionClassCode) || string.IsNullOrWhiteSpace(command.RetentionPolicyVersion))
        {
            return "RETENTION_POLICY_REQUIRED";
        }

        return RequireIdempotency(command.IdempotencyScope, command.IdempotencyKey);
    }

    private static string? ValidateAddItem(StatutoryEvidenceAddItemCommand command)
    {
        if (command.EvidenceSetReference == Guid.Empty)
        {
            return "INVALID_EVIDENCE_SET_REFERENCE";
        }

        if (!StatutoryEvidenceMetadataConstants.DocumentTypes.Contains(command.DocumentType))
        {
            return "INVALID_DOCUMENT_TYPE";
        }

        if (!StatutoryEvidenceMetadataConstants.ItemRoles.Contains(command.ItemRole))
        {
            return "INVALID_ITEM_ROLE";
        }

        return RequireIdempotency(command.IdempotencyScope, command.IdempotencyKey);
    }

    private static string? RequireIdempotency(string scope, string key) =>
        string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(key)
            ? "IDEMPOTENCY_REQUIRED"
            : null;

    private async Task<StatutoryEvidenceSetReadModel?> AuthorizeSetOperationAsync(Guid evidenceSetReference, StatutoryEvidenceActor actor, string operation, Guid correlationId, CancellationToken cancellationToken)
    {
        var set = await _repository.GetEvidenceSetAsync(evidenceSetReference, cancellationToken);
        if (set is null)
        {
            await _repository.RecordAccessDeniedAsync(evidenceSetReference, null, null, null, correlationId, actor, "UNKNOWN_REFERENCE", cancellationToken);
            return null;
        }

        if (!await IsAuthorizedAsync(actor, operation, set.SiteId, set.SiteGroupId, cancellationToken))
        {
            await _repository.RecordAccessDeniedAsync(evidenceSetReference, set.SiteId, set.SiteGroupId, set.ParkingSessionId, correlationId, actor, "SCOPE_DENIED", cancellationToken);
            return null;
        }

        return set;
    }

    private Task<bool> IsAuthorizedAsync(StatutoryEvidenceActor actor, string operation, Guid siteId, Guid siteGroupId, CancellationToken cancellationToken) =>
        StatutoryEvidenceMetadataConstants.SourceChannels.Contains(actor.SourceChannel) &&
        (actor.UserId.HasValue || actor.ServiceIdentityId.HasValue)
            ? _repository.ActorHasScopeAsync(actor, operation, siteId, siteGroupId, cancellationToken)
            : Task.FromResult(false);

    private static bool MatchesDurableBinding(StatutoryEvidenceCreateSetCommand command, StatutoryEvidenceDurableRequestBinding binding) =>
        command.StatutoryDiscountDecisionCommandId == binding.StatutoryDiscountDecisionCommandId &&
        command.StatutoryDiscountValidationId == binding.StatutoryDiscountValidationId &&
        command.ParkingSessionId == binding.ParkingSessionId &&
        command.SiteId == binding.SiteId &&
        command.SiteGroupId == binding.SiteGroupId &&
        StatutoryEvidenceMetadataConstants.CodeComparer.Equals(command.EntitlementType, binding.EntitlementType) &&
        StatutoryEvidenceMetadataConstants.CodeComparer.Equals(command.Actor.SourceChannel, binding.SourceChannel);

    private static StatutoryEvidenceOperationOutcome Accepted(StatutoryEvidenceSetReadModel set) => new("ACCEPTED", false, null, set, null);
    private static StatutoryEvidenceOperationOutcome Rejected(string code) => new("REJECTED", false, code, null, null);
}
