using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount validation draft behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("47000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("47000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("47000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("47000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("47000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("47000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("47000000-0000-0000-0000-000000000007");
    private static readonly Guid DraftId = Guid.Parse("47000000-0000-0000-0000-000000000008");
    private static readonly Guid EvidenceReferenceId = Guid.Parse("47000000-0000-0000-0000-000000000011");
    private static readonly Guid CorrelationId = Guid.Parse("47000000-0000-0000-0000-000000000009");
    private static readonly Guid PolicyId = Guid.Parse("47000000-0000-0000-0000-000000000012");
    private static readonly Guid JurisdictionId = Guid.Parse("47000000-0000-0000-0000-000000000013");

    /// <summary>
    /// Verifies access denial is persisted and prevents draft creation.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenAccessDenied_DoesNotCreateDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessDecision.Should().Be("DENIED");
        result.AccessPersisted.Should().BeTrue();
        result.AccessEvaluationId.Should().Be(EvaluationId);
        result.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.DraftId.Should().BeNull();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");

        await repository.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies a valid access-allowed draft creates a requested validation row without applying a discount.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenAccessAllowedAndSessionActive_PersistsRequestedDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
                DraftId,
                "REQUESTED",
                Persisted: true,
                ReusedExistingDraft: false,
                EvidenceRequired: true,
                EvidenceReferenceCreated: true,
                EvidenceReferenceId,
                Policy()));

        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.AccessPersisted.Should().BeTrue();
        result.DraftAccepted.Should().BeTrue();
        result.DraftPersisted.Should().BeTrue();
        result.DraftId.Should().Be(DraftId);
        result.ValidationStatus.Should().Be("REQUESTED");
        result.EntitlementType.Should().Be("SENIOR_CITIZEN");
        result.EvidenceCaptureRequired.Should().BeTrue();
        result.EvidenceRequired.Should().BeTrue();
        result.EvidenceReferenceCreated.Should().BeTrue();
        result.EvidenceReferenceId.Should().Be(EvidenceReferenceId);
        result.ReusedExistingDraft.Should().BeFalse();
        result.Policy.Should().NotBeNull();
        result.Policy!.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(request =>
                request.ParkingSessionId == ParkingSessionId &&
                request.EntitlementType == "SENIOR_CITIZEN" &&
                request.EvidenceRequired &&
                request.RequestedByUserId == UserId &&
                request.CorrelationId == CorrelationId &&
                request.Policy.StatutoryDiscountPolicyId == PolicyId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies duplicate active drafts return the existing draft deterministically.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenEquivalentDraftExists_ReturnsReusedDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
                DraftId,
                "REQUESTED",
                Persisted: true,
                ReusedExistingDraft: true,
                EvidenceRequired: true,
                EvidenceReferenceCreated: false,
                EvidenceReferenceId,
                Policy()));

        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.AccessPersisted.Should().BeTrue();
        result.DraftAccepted.Should().BeTrue();
        result.DraftPersisted.Should().BeTrue();
        result.DraftId.Should().Be(DraftId);
        result.ValidationStatus.Should().Be("REQUESTED");
        result.EvidenceRequired.Should().BeTrue();
        result.EvidenceReferenceCreated.Should().BeFalse();
        result.EvidenceReferenceId.Should().Be(EvidenceReferenceId);
        result.ReusedExistingDraft.Should().BeTrue();
        result.Policy.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies evidence metadata is not requested when the draft request does not ask for capture.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenEvidenceNotRequested_ReturnsNoEvidenceReference()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
                DraftId,
                "REQUESTED",
                Persisted: true,
                ReusedExistingDraft: false,
                EvidenceRequired: false,
                EvidenceReferenceCreated: false,
                EvidenceReferenceId: null,
                Policy()));

        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer);

        var result = await sut.DraftAsync(Command(evidenceCaptureRequested: false), CancellationToken.None);

        result.DraftAccepted.Should().BeTrue();
        result.EvidenceCaptureRequired.Should().BeFalse();
        result.EvidenceRequired.Should().BeFalse();
        result.EvidenceReferenceCreated.Should().BeFalse();
        result.EvidenceReferenceId.Should().BeNull();

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(request =>
                request.EvidenceRequired == false),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies policy-required evidence marks the draft evidence-required even when capture was not requested.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenResolvedPolicyRequiresEvidence_PersistsEvidenceRequired()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var policyRepository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        policyRepository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: true,
                Policy(requiresEvidence: true),
                SiteId,
                SiteGroupId,
                JurisdictionId,
                IneligibilityReason: null,
                ErrorCode: null));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
                DraftId,
                "REQUESTED",
                Persisted: true,
                ReusedExistingDraft: false,
                EvidenceRequired: true,
                EvidenceReferenceCreated: true,
                EvidenceReferenceId,
                Policy(requiresEvidence: true)));

        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer, policyRepository);

        var result = await sut.DraftAsync(Command(evidenceCaptureRequested: false), CancellationToken.None);

        result.EvidenceCaptureRequired.Should().BeFalse();
        result.EvidenceRequired.Should().BeTrue();
        result.Policy!.RequiresEvidence.Should().BeTrue();

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(request =>
                request.EvidenceRequired &&
                request.Policy.RequiresEvidence),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the draft requires a parking session ID.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenParkingSessionIdMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(parkingSessionId: Guid.Empty), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ParkingSessionId is required*");
    }

    /// <summary>
    /// Verifies entitlement type is required.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenEntitlementTypeMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(entitlementType: ""), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EntitlementType is required*");
    }

    /// <summary>
    /// Verifies unsupported entitlement types are rejected before draft creation.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenEntitlementTypeUnsupported_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(entitlementType: "OTHER_STATUTORY"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EntitlementType must be SENIOR_CITIZEN or PWD*");
    }

    /// <summary>
    /// Verifies masked ID reference is required.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenMaskedIdReferenceMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(maskedIdReference: ""), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*MaskedIdReference is required*");
    }

    /// <summary>
    /// Verifies full ID-looking references are rejected.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenMaskedIdReferenceLooksRaw_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(maskedIdReference: "123456789012"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*masked or last-four style*");
    }

    /// <summary>
    /// Verifies operator attestation is required.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenOperatorAttestationFalse_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(operatorAttestation: false), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*OperatorAttestation must be true*");
    }

    /// <summary>
    /// Verifies missing sessions are not drafted.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenSessionMissing_ReturnsNotFoundWithoutDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns((OperatorConsoleSessionReadModel?)null);
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.ErrorCode.Should().Be("SESSION_NOT_FOUND");
        result.IneligibilityReason.Should().Be("SESSION_NOT_FOUND");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies unresolved policy blocks draft creation after access and session checks.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenPolicyNotResolved_DoesNotCreateDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var policyRepository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        policyRepository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: false,
                Policy: null,
                SiteId,
                SiteGroupId,
                JurisdictionId,
                "SITE_JURISDICTION_NOT_CONFIGURED",
                "SITE_JURISDICTION_NOT_CONFIGURED"));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer, policyRepository);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.ErrorCode.Should().Be("SITE_JURISDICTION_NOT_CONFIGURED");
        result.Policy.Should().BeNull();
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies production draft creation cannot persist against sandbox-only policy rows.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenProductionAndSandboxPolicyResolved_DoesNotCreateDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var policyRepository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        policyRepository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: true,
                Policy(
                    policyId: Guid.Parse("23100000-0000-0000-0000-000000000002"),
                    policyCode: "SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A",
                    policyName: "Sandbox Operator Console Senior Citizen Required Evidence Policy",
                    policyLevel: "SITE_POLICY",
                    policyType: "SITE_POLICY",
                    policyResolutionBasis: "SITE_POLICY_OPERATIONAL_ONLY",
                    ordinanceReference: "SANDBOX-OC-SD-ORD-235A",
                    nationalLawReference: null,
                    sourceReference: "SANDBOX_METADATA_ONLY"),
                SiteId,
                SiteGroupId,
                JurisdictionId,
                IneligibilityReason: null,
                ErrorCode: null));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(
            AccessResult(allowed: true, []),
            repository,
            writer,
            policyRepository,
            environmentName: "Production");

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.Policy.Should().BeNull();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        result.RequiresManualReview.Should().BeTrue();
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies production draft creation is blocked for dedicated-registry rows that are not production verified.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenProductionAndDedicatedRegistryPolicyProposedOnly_DoesNotCreateDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var policyRepository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        policyRepository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: true,
                Policy(verificationStatus: "PROPOSED_ONLY", sourceReference: "Registry proposed row"),
                SiteId,
                SiteGroupId,
                JurisdictionId,
                IneligibilityReason: null,
                ErrorCode: null));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(
            AccessResult(allowed: true, []),
            repository,
            writer,
            policyRepository,
            environmentName: "Production");

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.Policy.Should().BeNull();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified);
        result.RequiresManualReview.Should().BeTrue();
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies non-production draft validation can still use sandbox fixture policies.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenDevelopmentAndSandboxPolicyResolved_CreatesDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var sandboxPolicy = Policy(
            policyId: Guid.Parse("23100000-0000-0000-0000-000000000002"),
            policyCode: "SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A",
            policyName: "Sandbox Operator Console Senior Citizen Required Evidence Policy",
            policyLevel: "SITE_POLICY",
            policyType: "SITE_POLICY",
            policyResolutionBasis: "SITE_POLICY_OPERATIONAL_ONLY",
            ordinanceReference: "SANDBOX-OC-SD-ORD-235A",
            nationalLawReference: null,
            sourceReference: "SANDBOX_METADATA_ONLY");
        var policyRepository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        policyRepository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: true,
                sandboxPolicy,
                SiteId,
                SiteGroupId,
                JurisdictionId,
                IneligibilityReason: null,
                ErrorCode: null));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
                DraftId,
                "REQUESTED",
                Persisted: true,
                ReusedExistingDraft: false,
                EvidenceRequired: true,
                EvidenceReferenceCreated: true,
                EvidenceReferenceId,
                sandboxPolicy));

        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer, policyRepository);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.DraftAccepted.Should().BeTrue();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        result.RequiresManualReview.Should().BeFalse();
        await writer.Received(1).PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>());
    }

    private static OperatorConsoleStatutoryDiscountDraftService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleSessionLookupReadRepository? sessionRepository = null,
        IOperatorConsoleStatutoryDiscountDraftWriter? draftWriter = null,
        IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository? policyRepository = null,
        string environmentName = "Development")
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        sessionRepository ??= Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        draftWriter ??= Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        if (policyRepository is null)
        {
            policyRepository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
            policyRepository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
                .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                    Resolved: true,
                    Policy(requiresEvidence: false),
                    SiteId,
                    SiteGroupId,
                    JurisdictionId,
                    IneligibilityReason: null,
                    ErrorCode: null));
        }
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-05-29T08:00:00Z"));

        return new OperatorConsoleStatutoryDiscountDraftService(
            accessService,
            accessWriter,
            sessionRepository,
            policyRepository,
            draftWriter,
            clock,
            new OperatorConsolePolicyReadinessEnvironment(environmentName));
    }

    private static OperatorConsoleStatutoryDiscountDraftCommand Command(
        Guid? parkingSessionId = null,
        string entitlementType = "SENIOR_CITIZEN",
        string maskedIdReference = "****1234",
        bool operatorAttestation = true,
        bool evidenceCaptureRequested = true) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            parkingSessionId ?? ParkingSessionId,
            "TICKET-001",
            PlateNumber: null,
            entitlementType,
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            maskedIdReference,
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: evidenceCaptureRequested,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            operatorAttestation,
            AttestationNotes: "Manual API test attestation.",
            ReasonCode: "OPERATOR_DRAFT_REQUESTED",
            "operator-console-statutory-discount-draft-test",
            CorrelationId);

    private static OperatorConsoleAccessEvaluationResult AccessResult(
        bool allowed,
        IReadOnlyList<string> reasons) =>
        new(
            Guid.Empty,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            reasons,
            allowed ? "OPERATOR" : null,
            new OperatorConsoleDeviceTrustResult(DeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(ShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"),
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                Guid.Parse("47000000-0000-0000-0000-000000000010"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.CreateStatutoryDiscountDraft,
                "STATUTORY_DISCOUNT_VALIDATION",
                "PARKING_SESSION",
                ParkingSessionId));

    private static OperatorConsoleSessionReadModel Session(string status) =>
        new(
            ParkingSessionId,
            "TICKET-001",
            "ABC-1234",
            SiteId,
            SiteGroupId,
            status,
            DateTimeOffset.Parse("2026-05-29T04:00:00Z"),
            CurrentPayableAmountMinorUnits: 12500,
            CurrencyCode: "PHP",
            PaymentStatus: null,
            DiscountStatus: "NOT_APPLIED",
            ExitAuthorizationStatus: null);

    private static OperatorConsoleResolvedStatutoryDiscountPolicy Policy(
        bool requiresEvidence = true,
        Guid? policyId = null,
        string policyCode = "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
        string policyName = "RA 9994 Senior Citizen National Fallback",
        string policyResolutionBasis = "NATIONAL_LAW_FALLBACK",
        string policyLevel = "NATIONAL_LAW",
        string policyType = "LEGAL_REFERENCE",
        string? ordinanceReference = null,
        string? nationalLawReference = "RA 9994",
        string verificationStatus = "VERIFIED_OFFICIAL",
        string? sourceReference = "Unit test policy.") =>
        new(
            policyId ?? PolicyId,
            JurisdictionId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            policyCode,
            policyName,
            policyResolutionBasis,
            policyLevel,
            policyType,
            ordinanceReference ?? "Expanded Senior Citizens Act of 2010",
            ordinanceReference,
            nationalLawReference,
            verificationStatus,
            "NON_RESIDENT_ALLOWED",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            null,
            false,
            false,
            false,
            false,
            false,
            false,
            "NOT_APPLICABLE",
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "CHARGEABLE_PORTION_ONLY",
            "NO_STACKING_ON_FREE_PERIOD",
            "NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY",
            true,
            requiresEvidence,
            DateOnly.Parse("2026-01-01"),
            null,
            sourceReference,
            JsonSerializer.SerializeToElement(new
            {
                policyCode,
                nationalLawReference,
                benefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT",
                freeDurationMinutes = (int?)null
            }));
}
