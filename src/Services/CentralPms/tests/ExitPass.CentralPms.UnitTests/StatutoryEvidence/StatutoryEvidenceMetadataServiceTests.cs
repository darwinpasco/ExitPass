using ExitPass.CentralPms.Application.StatutoryEvidence;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryEvidence;

public sealed class StatutoryEvidenceMetadataServiceTests
{
    private readonly FakeEvidenceRepository _repository = new();
    private readonly StatutoryEvidenceMetadataService _sut;

    public StatutoryEvidenceMetadataServiceTests()
    {
        _sut = new StatutoryEvidenceMetadataService(_repository);
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenRetentionPolicyMissing_FailsClosed()
    {
        _repository.RetentionPolicyApproved = false;

        var result = await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("RETENTION_POLICY_REQUIRED");
        _repository.CreatedSets.Should().Be(0);
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenValid_CreatesOpaqueSetReference()
    {
        var result = await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.EvidenceSet.Should().NotBeNull();
        result.EvidenceSet!.EvidenceSetReference.Should().NotBe(Guid.Empty);
        result.EvidenceSet.EvidenceSetReference.ToString("N").Should().NotContain(result.EvidenceSet.SiteId.ToString("N"));
        result.EvidenceSet.EvidenceSetReference.ToString("N").Should().NotContain(result.EvidenceSet.ParkingSessionId.ToString("N"));
        result.EvidenceSet.EntitlementType.Should().Be("SENIOR_CITIZEN");
        result.EvidenceSet.SourceChannel.Should().Be("OPERATOR_CONSOLE");
    }

    [Theory]
    [InlineData("site")]
    [InlineData("site-group")]
    [InlineData("parking-session")]
    [InlineData("entitlement")]
    public async Task CreateOrResolveSet_WhenCallerBindingDoesNotMatchDurableRequest_Rejects(string mismatch)
    {
        var command = mismatch switch
        {
            "site" => CreateSet() with { SiteId = Guid.NewGuid() },
            "site-group" => CreateSet() with { SiteGroupId = Guid.NewGuid() },
            "parking-session" => CreateSet() with { ParkingSessionId = Guid.NewGuid() },
            "entitlement" => CreateSet() with { EntitlementType = "PWD" },
            _ => CreateSet()
        };

        var result = await _sut.CreateOrResolveSetAsync(command, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("BINDING_MISMATCH");
        _repository.CreatedSets.Should().Be(0);
        _repository.AccessDeniedEvents.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenStatutoryRequestIsUnknown_RejectsWithoutMetadata()
    {
        _repository.RequestBinding = null;

        var result = await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("REQUEST_BINDING_NOT_FOUND");
        _repository.CreatedSets.Should().Be(0);
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenCapturePrincipalOutsideSiteScope_RejectsWithoutOperationRow()
    {
        _repository.ScopeAllowed = false;

        var result = await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("SCOPE_DENIED");
        _repository.CreatedSets.Should().Be(0);
        _repository.OperationCount.Should().Be(0);
        _repository.AccessDeniedEvents.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenSameIdempotencyAndSameSemantics_ReplaysOriginal()
    {
        var first = await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);
        var replay = await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);

        replay.Classification.Should().Be("IDEMPOTENT_REPLAY");
        replay.EvidenceSet!.EvidenceSetReference.Should().Be(first.EvidenceSet!.EvidenceSetReference);
        _repository.CreatedSets.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenSameIdempotencyAndDifferentSemantics_ReturnsConflict()
    {
        await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None);

        var changed = CreateSet() with { RequiredDocumentProfileVersion = "v2" };
        var result = await _sut.CreateOrResolveSetAsync(changed, CancellationToken.None);

        result.Classification.Should().Be("SEMANTIC_CONFLICT");
        result.ErrorCode.Should().Be("IDEMPOTENCY_SEMANTIC_CONFLICT");
        _repository.SemanticConflictEvents.Should().Be(1);
    }

    [Fact]
    public async Task AddItem_WhenControlledTypeAndRoleAreValid_AddsSafeMetadataOnlyItem()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference), CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.EvidenceItem.Should().NotBeNull();
        result.EvidenceItem!.DocumentType.Should().Be("SENIOR_CITIZEN_ID");
        result.EvidenceItem.ItemRole.Should().Be("FRONT");
        result.EvidenceItem.UploadStatus.Should().Be("NOT_AUTHORIZED");
        result.EvidenceItem.ValidationStatus.Should().Be("NOT_STARTED");
        result.EvidenceItem.ScanStatus.Should().Be("NOT_STARTED");
    }

    [Fact]
    public async Task AddItem_WhenOpaqueReferenceIsInAnotherScope_RejectsAndAudits()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;
        _repository.ScopeAllowed = false;

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { IdempotencyKey = "cross-scope-item" }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("SCOPE_DENIED");
        _repository.ItemCount.Should().Be(0);
        _repository.OperationCount.Should().Be(1);
        _repository.AccessDeniedEvents.Should().Be(1);
    }

    [Fact]
    public async Task AddItem_WhenWebPayActorMatchesDurableChannelAndScope_CapturesOnlyItsRequest()
    {
        var webpayActor = new StatutoryEvidenceActor(null, Guid.Parse("20000000-0000-0000-0000-000000000010"), "WEBPAY");
        _repository.RequestBinding = BindingFrom(CreateSet(), "WEBPAY");
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet() with { Actor = webpayActor }, CancellationToken.None)).EvidenceSet!;

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { Actor = webpayActor }, CancellationToken.None);

        result.Classification.Should().Be("ACCEPTED");
        result.EvidenceSet!.SourceChannel.Should().Be("WEBPAY");
    }

    [Fact]
    public async Task AddItem_WhenAptActorUsesDifferentDurableChannel_Rejects()
    {
        var aptActor = new StatutoryEvidenceActor(null, Guid.Parse("20000000-0000-0000-0000-000000000011"), "ASSISTED_PAYMENT_TERMINAL");
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { Actor = aptActor }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("SCOPE_DENIED");
    }

    [Fact]
    public async Task AddItem_WhenUnknownDocumentType_Rejects()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { DocumentType = "FREE_TEXT_ID" }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("INVALID_DOCUMENT_TYPE");
    }

    [Fact]
    public async Task AddItem_WhenOtherDocumentTypeIsNotGoverned_Rejects()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { DocumentType = "OTHER" }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("INVALID_DOCUMENT_TYPE");
    }

    [Fact]
    public async Task AddItem_WhenDuplicateRole_Rejects()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;
        await _sut.AddItemAsync(AddItem(set.EvidenceSetReference), CancellationToken.None);

        var result = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { IdempotencyKey = "item-2" }, CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("INVALID_EVIDENCE_SET_OR_ROLE_CONFLICT");
    }

    [Fact]
    public async Task LockForReview_MakesSetImmutableForFurtherItems()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var locked = await _sut.LockForReviewAsync(new StatutoryEvidenceLockForReviewCommand(
            set.EvidenceSetReference,
            "lock-scope",
            "lock-key",
            Guid.NewGuid(),
            Actor()), CancellationToken.None);
        var addAfterLock = await _sut.AddItemAsync(AddItem(set.EvidenceSetReference) with { IdempotencyKey = "after-lock", ItemRole = "BACK" }, CancellationToken.None);

        locked.EvidenceSet!.SetStatus.Should().Be("LOCKED_FOR_REVIEW");
        addAfterLock.Classification.Should().Be("REJECTED");
    }

    [Fact]
    public async Task Hold_BlocksDeletionButDoesNotGrantView()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var held = await _sut.PlaceHoldAsync(new StatutoryEvidenceHoldCommand(
            set.EvidenceSetReference,
            "INVESTIGATION",
            "hold-scope",
            "hold-key",
            Guid.NewGuid(),
            Actor()), CancellationToken.None);
        var deletion = await _sut.RequestDeletionAsync(new StatutoryEvidenceDeletionRequestCommand(
            set.EvidenceSetReference,
            "delete-scope",
            "delete-key",
            Guid.NewGuid(),
            Actor()), CancellationToken.None);

        held.EvidenceSet!.HoldActive.Should().BeTrue();
        held.EvidenceSet.RetentionStatus.Should().Be("HELD");
        deletion.Classification.Should().Be("REJECTED");
        deletion.ErrorCode.Should().Be("DELETION_BLOCKED_OR_INVALID");
    }

    [Fact]
    public async Task HoldPermissionWithoutViewPermission_DoesNotGrantReadAccess()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;
        _repository.AllowedOperations = [StatutoryEvidenceScopeOperations.Hold];

        var held = await _sut.PlaceHoldAsync(new StatutoryEvidenceHoldCommand(
            set.EvidenceSetReference,
            "INVESTIGATION",
            "hold-only-scope",
            "hold-only-key",
            Guid.NewGuid(),
            Actor()), CancellationToken.None);
        var read = await _sut.GetEvidenceSetAsync(set.EvidenceSetReference, Actor(), Guid.NewGuid(), CancellationToken.None);

        held.Classification.Should().Be("ACCEPTED");
        read.Should().BeNull();
        _repository.AccessDeniedEvents.Should().Be(1);
    }

    [Fact]
    public async Task RequestDeletion_WhenPermissionDoesNotMatchScope_RejectsWithoutMutation()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;
        _repository.ScopeAllowed = false;

        var result = await _sut.RequestDeletionAsync(new StatutoryEvidenceDeletionRequestCommand(
            set.EvidenceSetReference,
            "delete-denied-scope",
            "delete-denied-key",
            Guid.NewGuid(),
            Actor()), CancellationToken.None);

        result.Classification.Should().Be("REJECTED");
        result.ErrorCode.Should().Be("SCOPE_DENIED");
        _repository.Sets[set.EvidenceSetReference].Model.DeletionStatus.Should().Be("NOT_REQUESTED");
        _repository.AccessDeniedEvents.Should().Be(1);
    }

    [Fact]
    public async Task GetEvidenceSet_WhenUnknownReference_RecordsAccessDenied()
    {
        var result = await _sut.GetEvidenceSetAsync(Guid.NewGuid(), Actor(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
        _repository.AccessDeniedEvents.Should().Be(1);
    }

    [Fact]
    public async Task GetEvidenceSet_WhenOpaqueReferenceInScope_ReturnsSafeMetadata()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;

        var result = await _sut.GetEvidenceSetAsync(set.EvidenceSetReference, Actor(), Guid.NewGuid(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.EvidenceSetReference.Should().Be(set.EvidenceSetReference);
    }

    [Fact]
    public async Task GetEvidenceSet_WhenOpaqueReferenceOutOfScope_ReturnsNullAndAuditsSafeDenial()
    {
        var set = (await _sut.CreateOrResolveSetAsync(CreateSet(), CancellationToken.None)).EvidenceSet!;
        _repository.ScopeAllowed = false;

        var result = await _sut.GetEvidenceSetAsync(set.EvidenceSetReference, Actor(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
        _repository.AccessDeniedEvents.Should().Be(1);
        _repository.LastDeniedReason.Should().Be("SCOPE_DENIED");
        _repository.LastDeniedReference.Should().BeNull("denial events must not retain the opaque reference");
    }

    private static StatutoryEvidenceCreateSetCommand CreateSet() =>
        new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Guid.Parse("10000000-0000-0000-0000-000000000004"),
            Guid.Parse("10000000-0000-0000-0000-000000000005"),
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID_V1",
            "v1",
            "STATUTORY_EVIDENCE_REVIEW",
            "v1",
            "LOCAL_TEST",
            "set-scope",
            "set-key",
            Guid.Parse("10000000-0000-0000-0000-000000000006"),
            Actor());

    private static StatutoryEvidenceAddItemCommand AddItem(Guid setReference) =>
        new(
            setReference,
            "SENIOR_CITIZEN_ID",
            "FRONT",
            "DOCUMENT_PROFILE_ONLY",
            null,
            "SENIOR_CITIZEN_ID_FRONT_V1",
            "item-scope",
            "item-key",
            Guid.NewGuid(),
            Actor());

    private static StatutoryEvidenceActor Actor() =>
        new(Guid.Parse("20000000-0000-0000-0000-000000000001"), null, "OPERATOR_CONSOLE");

    private static StatutoryEvidenceDurableRequestBinding BindingFrom(StatutoryEvidenceCreateSetCommand command, string? sourceChannel = null) =>
        new(
            command.StatutoryDiscountDecisionCommandId,
            command.StatutoryDiscountValidationId,
            command.ParkingSessionId,
            command.SiteId,
            command.SiteGroupId,
            command.EntitlementType,
            sourceChannel ?? command.Actor.SourceChannel);

    private sealed class FakeEvidenceRepository : IStatutoryEvidenceMetadataRepository
    {
        private readonly Dictionary<Guid, (Guid Id, StatutoryEvidenceSetReadModel Model)> _sets = new();
        private readonly Dictionary<(string Scope, string Key), StatutoryEvidenceOperationReplay> _operations = new();

        public Dictionary<Guid, (Guid Id, StatutoryEvidenceSetReadModel Model)> Sets => _sets;
        public StatutoryEvidenceDurableRequestBinding? RequestBinding { get; set; } = BindingFrom(CreateSet());
        public bool ScopeAllowed { get; set; } = true;
        public HashSet<string> AllowedOperations { get; set; } =
        [
            StatutoryEvidenceScopeOperations.Capture,
            StatutoryEvidenceScopeOperations.View,
            StatutoryEvidenceScopeOperations.ReviewLock,
            StatutoryEvidenceScopeOperations.Hold,
            StatutoryEvidenceScopeOperations.DeletionRequest
        ];
        public bool RetentionPolicyApproved { get; set; } = true;
        public int CreatedSets { get; private set; }
        public int ItemCount => _sets.Values.Sum(set => set.Model.Items.Count);
        public int OperationCount => _operations.Count;
        public int SemanticConflictEvents { get; private set; }
        public int AccessDeniedEvents { get; private set; }
        public string? LastDeniedReason { get; private set; }
        public Guid? LastDeniedReference { get; private set; }

        public Task<bool> ApprovedRetentionPolicyExistsAsync(string retentionClassCode, string retentionPolicyVersion, string environmentScope, CancellationToken cancellationToken) =>
            Task.FromResult(RetentionPolicyApproved);

        public Task<StatutoryEvidenceDurableRequestBinding?> ResolveRequestBindingAsync(Guid statutoryDiscountDecisionCommandId, CancellationToken cancellationToken) =>
            Task.FromResult(RequestBinding?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId ? RequestBinding : null);

        public Task<bool> ActorHasScopeAsync(StatutoryEvidenceActor actor, string operation, Guid siteId, Guid siteGroupId, CancellationToken cancellationToken) =>
            Task.FromResult(ScopeAllowed &&
                (actor.UserId.HasValue || actor.ServiceIdentityId.HasValue) &&
                AllowedOperations.Contains(operation) &&
                RequestBinding is not null &&
                RequestBinding.SiteId == siteId &&
                RequestBinding.SiteGroupId == siteGroupId);

        public Task<StatutoryEvidenceOperationReplay?> FindOperationAsync(string idempotencyScope, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(_operations.TryGetValue((idempotencyScope, idempotencyKey), out var replay) ? replay : null);

        public Task<StatutoryEvidenceCreatedSet> CreateEvidenceSetAsync(StatutoryEvidenceCreateSetCommand command, string semanticRequestHash, CancellationToken cancellationToken)
        {
            CreatedSets++;
            var id = Guid.NewGuid();
            var reference = Guid.NewGuid();
            var model = new StatutoryEvidenceSetReadModel(reference, command.StatutoryDiscountDecisionCommandId, command.StatutoryDiscountValidationId, command.ParkingSessionId, command.SiteId, command.SiteGroupId, command.EntitlementType, command.Actor.SourceChannel, "OPEN", command.RequiredDocumentProfileCode, command.RequiredDocumentProfileVersion, command.RetentionClassCode, command.RetentionPolicyVersion, "ACTIVE", "NOT_REQUESTED", false, null, command.CorrelationId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
            _sets[reference] = (id, model);
            _operations[(command.IdempotencyScope, command.IdempotencyKey)] = new StatutoryEvidenceOperationReplay("ACCEPTED", semanticRequestHash, id, null);
            return Task.FromResult(new StatutoryEvidenceCreatedSet(id, model));
        }

        public Task<StatutoryEvidenceCreatedItem?> AddEvidenceItemAsync(StatutoryEvidenceAddItemCommand command, string semanticRequestHash, CancellationToken cancellationToken)
        {
            if (!_sets.TryGetValue(command.EvidenceSetReference, out var set) || set.Model.SetStatus != "OPEN")
            {
                return Task.FromResult<StatutoryEvidenceCreatedItem?>(null);
            }

            if (set.Model.Items.Any(item => item.DocumentType == command.DocumentType && item.ItemRole == command.ItemRole))
            {
                return Task.FromResult<StatutoryEvidenceCreatedItem?>(null);
            }

            var itemId = Guid.NewGuid();
            var item = new StatutoryEvidenceItemReadModel(Guid.NewGuid(), command.DocumentType, command.ItemRole, "NOT_AUTHORIZED", "NOT_STARTED", "NOT_STARTED", "NOT_REVIEWABLE", "UNBOUND", "ACTIVE", "NOT_REQUESTED", false, command.ExpectedMediaClass, command.DeclaredContentType, command.ProfileCode, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var updated = set.Model with { Items = set.Model.Items.Concat([item]).ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
            _sets[command.EvidenceSetReference] = (set.Id, updated);
            _operations[(command.IdempotencyScope, command.IdempotencyKey)] = new StatutoryEvidenceOperationReplay("ACCEPTED", semanticRequestHash, set.Id, itemId);
            return Task.FromResult<StatutoryEvidenceCreatedItem?>(new StatutoryEvidenceCreatedItem(set.Id, itemId, updated, item));
        }

        public Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetAsync(Guid evidenceSetReference, CancellationToken cancellationToken) =>
            Task.FromResult(_sets.TryGetValue(evidenceSetReference, out var set) ? set.Model : null);

        public Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetByIdAsync(Guid evidenceSetId, CancellationToken cancellationToken) =>
            Task.FromResult(_sets.Values.SingleOrDefault(set => set.Id == evidenceSetId).Model);

        public Task<StatutoryEvidenceSetReadModel?> LockForReviewAsync(StatutoryEvidenceLockForReviewCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
            Transition(command.EvidenceSetReference, model => model.SetStatus == "OPEN" ? model with { SetStatus = "LOCKED_FOR_REVIEW" } : null, command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash);

        public Task<StatutoryEvidenceSetReadModel?> PlaceHoldAsync(StatutoryEvidenceHoldCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
            Transition(command.EvidenceSetReference, model => model.HoldActive ? null : model with { HoldActive = true, HoldReasonCode = command.ReasonCode, RetentionStatus = "HELD" }, command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash);

        public Task<StatutoryEvidenceSetReadModel?> ReleaseHoldAsync(StatutoryEvidenceReleaseHoldCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
            Transition(command.EvidenceSetReference, model => !model.HoldActive ? null : model with { HoldActive = false, HoldReasonCode = null, RetentionStatus = "ACTIVE" }, command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash);

        public Task<StatutoryEvidenceSetReadModel?> RequestDeletionAsync(StatutoryEvidenceDeletionRequestCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
            Transition(command.EvidenceSetReference, model => model.HoldActive ? null : model with { DeletionStatus = "REQUESTED" }, command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash);

        public Task RecordSemanticConflictAsync(string operationType, string idempotencyScope, string idempotencyKey, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
        {
            SemanticConflictEvents++;
            return Task.CompletedTask;
        }

        public Task RecordAccessDeniedAsync(Guid? evidenceSetReference, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, Guid correlationId, StatutoryEvidenceActor actor, string reasonCode, CancellationToken cancellationToken)
        {
            AccessDeniedEvents++;
            LastDeniedReason = reasonCode;
            LastDeniedReference = null;
            return Task.CompletedTask;
        }

        private Task<StatutoryEvidenceSetReadModel?> Transition(Guid reference, Func<StatutoryEvidenceSetReadModel, StatutoryEvidenceSetReadModel?> transition, string scope, string key, string hash)
        {
            if (!_sets.TryGetValue(reference, out var set))
            {
                return Task.FromResult<StatutoryEvidenceSetReadModel?>(null);
            }

            var updated = transition(set.Model);
            if (updated is null)
            {
                return Task.FromResult<StatutoryEvidenceSetReadModel?>(null);
            }

            _sets[reference] = (set.Id, updated);
            _operations[(scope, key)] = new StatutoryEvidenceOperationReplay("ACCEPTED", hash, set.Id, null);
            return Task.FromResult<StatutoryEvidenceSetReadModel?>(updated);
        }
    }
}
