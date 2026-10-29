using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Infrastructure.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies Operator Console statutory discount behavior against the dedicated policy registry.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleDedicatedPolicyRegistryIntegrationTests
{
    private const string ResolveEndpoint = "/v1/ops/operator-console/statutory-discounts/resolve-policy";
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string ProtectedPhotoReceipt = "dedicated-policy-protected-photo-receipt";
    private const string FixtureLguCode = "PH-INT-DR-258";
    private static readonly Guid FixtureUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid FixtureDeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid FixtureSiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid FixtureSiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid FixtureShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid FixtureParkingSessionId = Guid.Parse("77000000-0000-0000-0000-000000000090");
    private static readonly Guid SeniorReadyPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000001");
    private static readonly Guid PwdReadyPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000002");
    private static readonly Guid PilotPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000003");
    private static readonly Guid LeadPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000004");
    private static readonly Guid SandboxPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000005");
    private static readonly Guid CompatibilityPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000006");
    private static readonly Guid DraftReadyPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000007");
    private static readonly Guid FixtureJurisdictionId = Guid.Parse("25800000-0000-0000-0000-000000000010");
    private static readonly Guid FixtureJurisdictionAssignmentId = Guid.Parse("25800000-0000-0000-0000-000000000011");
    private static readonly Guid SeniorVersionedPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000020");
    private static readonly Guid SeniorPolicyVersionId = Guid.Parse("25800000-0000-0000-0000-000000000021");
    private static readonly Guid PwdVersionedPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000022");
    private static readonly Guid PwdPolicyVersionId = Guid.Parse("25800000-0000-0000-0000-000000000023");
    private static readonly Guid ObsoleteSeniorPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000024");
    private static readonly Guid ObsoletePwdPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000025");
    private static readonly Guid ConflictingSeniorPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000026");
    private static readonly Guid ConflictingSeniorPolicyVersionId = Guid.Parse("25800000-0000-0000-0000-000000000027");

    /// <summary>
    /// Verifies repository resolution prefers the dedicated registry even when a compatibility row also matches.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenDedicatedAndCompatibilityRowsMatch_ReturnsDedicatedRegistryRow()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SeniorReadyPolicyId,
                "DUMMY_OC_POLICY_SC_READY_VERIFIED",
                "Dummy OC Senior Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertCompatibilityLocalPolicyAsync(
                CompatibilityPolicyId,
                "DUMMY_OC_POLICY_COMPATIBILITY_SHOULD_NOT_WIN",
                "SENIOR_CITIZEN");

            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());

            var result = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "SENIOR_CITIZEN",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);

            result.Resolved.Should().BeTrue();
            result.Policy.Should().NotBeNull();
            result.Policy!.StatutoryDiscountPolicyId.Should().Be(SeniorReadyPolicyId);
            result.Policy.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_READY_VERIFIED");
            result.Policy.VerificationStatus.Should().Be("ACTIVE_APPROVED");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies canonical versioned Senior Citizen and PWD policies win when obsolete registry rows coexist.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenVersionedAndObsoletePoliciesCoexist_ReturnsCanonicalVersionedPolicies()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareParanaqueFixtureAsync();
            await InsertDedicatedPolicyAsync(
                ObsoleteSeniorPolicyId,
                "I006_FIXTURE_SC",
                "Obsolete Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertDedicatedPolicyAsync(
                ObsoletePwdPolicyId,
                "I006_FIXTURE_PWD",
                "Obsolete PWD Policy",
                "PWD",
                "ACTIVE_APPROVED",
                "PWD_ID");
            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());

            var senior = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "SENIOR_CITIZEN",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);
            var pwd = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "PWD",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);

            senior.Resolved.Should().BeTrue();
            senior.Policy!.PolicyCode.Should().Be("PH_PARANAQUE_SENIOR_FREE_PARKING");
            senior.Policy.JurisdictionId.Should().NotBeNull();
            senior.Policy.BenefitType.Should().Be("FULL_FEE_EXEMPTION");

            pwd.Resolved.Should().BeTrue();
            pwd.Policy!.PolicyCode.Should().Be("PH_PARANAQUE_PWD_FREE_PARKING");
            pwd.Policy.JurisdictionId.Should().Be(senior.Policy.JurisdictionId);
            pwd.Policy.BenefitType.Should().Be("FULL_FEE_EXEMPTION");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies an unready canonical version blocks fallback to an obsolete matching registry policy.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenVersionedAuthorityIsNotPublished_DoesNotFallBackToObsoletePolicy()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                ObsoleteSeniorPolicyId,
                "I006_FIXTURE_SC",
                "Obsolete Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertVersionedPolicyAsync(
                SeniorVersionedPolicyId,
                SeniorPolicyVersionId,
                "PH_FIXTURE_UNREADY_SENIOR_POLICY",
                "Fixture Unready Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "SENIOR_CITIZEN_ID");
            await SetPolicyVersionPublicationStatusAsync(SeniorPolicyVersionId, "DRAFT");

            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());

            var result = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "SENIOR_CITIZEN",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);

            result.Resolved.Should().BeFalse();
            result.Policy.Should().BeNull();
            result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_UNVERIFIED");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies competing canonical policies at the same scope and precedence fail closed.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenVersionedPoliciesConflictAtSamePrecedence_FailsClosed()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertVersionedPolicyAsync(
                SeniorVersionedPolicyId,
                SeniorPolicyVersionId,
                "PH_FIXTURE_READY_SENIOR_POLICY",
                "Fixture Ready Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "SENIOR_CITIZEN_ID");
            await InsertVersionedPolicyAsync(
                ConflictingSeniorPolicyId,
                ConflictingSeniorPolicyVersionId,
                "PH_FIXTURE_CONFLICTING_SENIOR_POLICY",
                "Fixture Conflicting Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "SENIOR_CITIZEN_ID");

            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());

            var result = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "SENIOR_CITIZEN",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);

            result.Resolved.Should().BeFalse();
            result.Policy.Should().BeNull();
            result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_UNVERIFIED");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies an entitlement without a policy for the Site jurisdiction remains unavailable.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenVersionedPolicyDoesNotCoverRequestedEntitlement_RemainsBlocked()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertVersionedPolicyAsync(
                SeniorVersionedPolicyId,
                SeniorPolicyVersionId,
                "PH_FIXTURE_READY_SENIOR_POLICY",
                "Fixture Ready Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "SENIOR_CITIZEN_ID");

            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());

            var result = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "PWD",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);

            result.Resolved.Should().BeFalse();
            result.Policy.Should().BeNull();
            result.ErrorCode.Should().Be("LOCAL_PARKING_ORDINANCE_NOT_APPLICABLE");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies a versioned policy creates a draft without resolving through an obsolete registry code.
    /// </summary>
    [Fact]
    public async Task DraftWriter_WhenCanonicalVersionedPolicyCoexistsWithObsoletePolicy_CreatesDraft()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareParanaqueFixtureAsync();
            await InsertDedicatedPolicyAsync(
                ObsoleteSeniorPolicyId,
                "I006_FIXTURE_SC",
                "Obsolete Senior Citizen Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(connectionString);
            var policyResolution = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "SENIOR_CITIZEN",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);
            policyResolution.Resolved.Should().BeTrue();

            var writer = new OperatorConsoleStatutoryDiscountDraftWriter(connectionString);
            var draft = await writer.PersistAsync(
                new OperatorConsoleStatutoryDiscountDraftPersistenceCommand(
                    FixtureParkingSessionId,
                    "SENIOR_CITIZEN",
                    "SENIOR_CITIZEN_ID",
                    "OSCA",
                    ExpiryDate: null,
                    "****2580",
                    EvidenceRequired: true,
                    "VERSIONED_POLICY_RESOLUTION_TEST",
                    OperatorAttestation: true,
                    "Versioned policy resolution integration test.",
                    FixtureUserId,
                    Guid.NewGuid(),
                    policyResolution.Policy!,
                    $"test/operator-console/statutory-id/{FixtureSiteId:N}/{FixtureParkingSessionId:N}/photo.jpg",
                    new string('A', 64),
                    "image/jpeg",
                    1024)
                {
                    IdControlReference = "SC-2580",
                    BirthDate = new DateOnly(1950, 1, 1)
                },
                CancellationToken.None);

            draft.Persisted.Should().BeTrue();
            draft.Policy.Should().NotBeNull();
            draft.Policy!.PolicyCode.Should().Be("PH_PARANAQUE_SENIOR_FREE_PARKING");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies the policy resolution API returns dedicated Senior Citizen and PWD ready policies.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedReadyPoliciesExist_ReturnsReadyVerifiedForSeniorAndPwd()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SeniorReadyPolicyId,
                "DUMMY_OC_POLICY_SC_READY_VERIFIED",
                "Dummy OC Senior Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertDedicatedPolicyAsync(
                PwdReadyPolicyId,
                "DUMMY_OC_POLICY_PWD_READY_VERIFIED",
                "Dummy OC PWD Ready Verified Policy",
                "PWD",
                "ACTIVE_APPROVED",
                "PWD_ID");

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var seniorResponse = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));
            using var pwdResponse = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("PWD"));

            seniorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            pwdResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var senior = await seniorResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            var pwd = await pwdResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();

            senior.Should().NotBeNull();
            senior!.PolicyResolved.Should().BeTrue();
            senior.StatutoryDiscountPolicyId.Should().Be(SeniorReadyPolicyId);
            senior.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_READY_VERIFIED");
            senior.VerificationStatus.Should().Be("ACTIVE_APPROVED");
            senior.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
            senior.RequiresManualReview.Should().BeFalse();

            pwd.Should().NotBeNull();
            pwd!.PolicyResolved.Should().BeTrue();
            pwd.StatutoryDiscountPolicyId.Should().Be(PwdReadyPolicyId);
            pwd.PolicyCode.Should().Be("DUMMY_OC_POLICY_PWD_READY_VERIFIED");
            pwd.VerificationStatus.Should().Be("ACTIVE_APPROVED");
            pwd.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
            pwd.RequiresManualReview.Should().BeFalse();
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry pilot rows remain manual-review classified.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedPilotPolicyExists_ReturnsReadyWithManualReview()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                PilotPolicyId,
                "DUMMY_OC_POLICY_SC_APPROVED_FOR_PILOT",
                "Dummy OC Senior Pilot Policy",
                "SENIOR_CITIZEN",
                "APPROVED_FOR_PILOT",
                "SENIOR_CITIZEN_ID");

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            body.Should().NotBeNull();
            body!.PolicyResolved.Should().BeTrue();
            body.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_APPROVED_FOR_PILOT");
            body.VerificationStatus.Should().Be("APPROVED_FOR_PILOT");
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry unverified/proposed rows do not classify as production-ready.
    /// </summary>
    [Theory]
    [InlineData("LEAD_UNVERIFIED", "DUMMY_OC_POLICY_SC_LEAD_UNVERIFIED")]
    [InlineData("PROPOSED_ONLY", "DUMMY_OC_POLICY_SC_PROPOSED_ONLY")]
    public async Task ResolvePolicyApi_WhenDedicatedPolicyIsNotVerified_ReturnsConfiguredButUnverified(
        string verificationStatus,
        string policyCode)
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            var policyStatus = verificationStatus == "PROPOSED_ONLY" ? "DRAFT" : "ACTIVE";
            await InsertDedicatedPolicyAsync(
                LeadPolicyId,
                policyCode,
                $"Dummy OC Senior {verificationStatus} Policy",
                "SENIOR_CITIZEN",
                verificationStatus,
                "SENIOR_CITIZEN_ID",
                policyStatus);

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            body.Should().NotBeNull();
            body!.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified);
            if (verificationStatus == "PROPOSED_ONLY")
            {
                body.PolicyResolved.Should().BeFalse();
                body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_UNVERIFIED");
            }
            else
            {
                body.PolicyResolved.Should().BeTrue();
                body.PolicyCode.Should().Be(policyCode);
            }

            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry sandbox/test markers remain sandbox-only.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedPolicyHasTestMarker_ReturnsSandboxOnly()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SandboxPolicyId,
                "TEST_OC_POLICY_SC_SANDBOX_ONLY",
                "Test OC Senior Sandbox Only Policy",
                "SENIOR_CITIZEN",
                "VERIFIED_OFFICIAL",
                "SENIOR_CITIZEN_ID");

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            body.Should().NotBeNull();
            body!.PolicyResolved.Should().BeTrue();
            body.PolicyCode.Should().Be("TEST_OC_POLICY_SC_SANDBOX_ONLY");
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies production draft creation can use a ready dedicated policy during the hybrid FK transition.
    /// </summary>
    [Fact]
    public async Task DraftApi_WhenProductionAndDedicatedReadyPolicyExists_CreatesDraft()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                DraftReadyPolicyId,
                "DUMMY_OC_POLICY_SC_DRAFT_READY_VERIFIED",
                "Dummy OC Senior Draft Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertCompatibilityLocalPolicyAsync(
                DraftReadyPolicyId,
                "DUMMY_OC_POLICY_SC_DRAFT_READY_VERIFIED",
                "SENIOR_CITIZEN");

            using var factory = CreateProductionFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
            body.Should().NotBeNull();
            body!.DraftAccepted.Should().BeTrue();
            body.DraftPersisted.Should().BeTrue();
            body.StatutoryDiscountPolicyId.Should().Be(DraftReadyPolicyId);
            body.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_DRAFT_READY_VERIFIED");
            body.VerificationStatus.Should().Be("ACTIVE_APPROVED");
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
            body.RequiresManualReview.Should().BeFalse();
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies production draft creation fails closed for dedicated registry sandbox/test policies.
    /// </summary>
    [Fact]
    public async Task DraftApi_WhenProductionAndDedicatedPolicyHasTestMarker_DoesNotCreateDraft()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SandboxPolicyId,
                "TEST_OC_POLICY_SC_SANDBOX_ONLY",
                "Test OC Senior Sandbox Only Policy",
                "SENIOR_CITIZEN",
                "VERIFIED_OFFICIAL",
                "SENIOR_CITIZEN_ID");

            using var factory = CreateProductionFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
            body.Should().NotBeNull();
            body!.DraftAccepted.Should().BeFalse();
            body.DraftPersisted.Should().BeFalse();
            body.DraftId.Should().BeNull();
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
            body.RequiresManualReview.Should().BeTrue();
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry resolution does not mutate payment, gate, coupon, or reconciliation boundaries.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedPolicyResolved_DoesNotMutateBoundaryTables()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SeniorReadyPolicyId,
                "DUMMY_OC_POLICY_SC_READY_VERIFIED",
                "Dummy OC Senior Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            var before = await CountBoundaryRowsAsync();

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var after = await CountBoundaryRowsAsync();
            after.Should().Be(before);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies the production policy readiness SQL remains read-only.
    /// </summary>
    [Fact]
    public void ProductionPolicyReadinessSql_ContainsNoMutationKeywords()
    {
        var sql = ReadRepoFile("scripts", "operator-console", "Verify-ProductionPolicyRegistryReadiness.sql");
        var withoutBlockComments = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var withoutLineComments = Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

        withoutLineComments.Should().NotMatchRegex(
            @"\b(INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|TRUNCATE|MERGE|GRANT|REVOKE|CALL|DO|EXECUTE)\b");
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionRequest ResolveRequest(string entitlementType) =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureParkingSessionId,
            entitlementType,
            $"operator-console-dedicated-registry-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest() =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureParkingSessionId,
            "DEDICATED-REGISTRY-TEST-TICKET",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            "****2580",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: false,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "Dedicated registry integration test attestation.",
            ReasonCode: "DEDICATED_REGISTRY_TEST",
            $"operator-console-dedicated-draft-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            EvidenceUploadReceipt: ProtectedPhotoReceipt);

    private static CustomWebApplicationFactory CreateProductionFactory() =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<OperatorConsolePolicyReadinessEnvironment>();
                services.AddSingleton(new OperatorConsolePolicyReadinessEnvironment("Production"));
                services.RemoveAll<IOperatorConsoleStatutoryIdPhotoService>();
                services.AddSingleton<IOperatorConsoleStatutoryIdPhotoService>(new FakeStatutoryIdPhotoService());
            });

    private sealed class FakeStatutoryIdPhotoService : IOperatorConsoleStatutoryIdPhotoService
    {
        public Task<OperatorConsoleStatutoryIdPhotoUploadOutcome> UploadAsync(
            OperatorConsoleStatutoryIdPhotoUploadCommand command,
            Stream content,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The policy integration test consumes an already-issued protected receipt.");

        public OperatorConsoleStatutoryIdPhotoReceipt? ResolveReceipt(
            string? receipt,
            Guid expectedUserId,
            Guid expectedSiteId,
            Guid expectedParkingSessionId,
            string expectedEntitlementType) =>
            receipt == ProtectedPhotoReceipt
                ? new OperatorConsoleStatutoryIdPhotoReceipt(
                    expectedUserId,
                    expectedSiteId,
                    expectedParkingSessionId,
                    expectedEntitlementType,
                    $"test/operator-console/statutory-id/{expectedSiteId:N}/{expectedParkingSessionId:N}/photo.jpg",
                    new string('A', 64),
                    "image/jpeg",
                    1024)
                : null;
    }

    private static async Task PrepareBaseFixtureAsync()
    {
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
        await CleanupDedicatedRegistryFixtureAsync();

        const string sql = """
            INSERT INTO sites.jurisdictions (
                jurisdiction_id,
                jurisdiction_code,
                jurisdiction_type,
                display_name,
                province_name,
                region_name,
                country_code,
                psgc_code,
                jurisdiction_status,
                effective_from,
                source_reference)
            VALUES (
                @jurisdiction_id,
                @jurisdiction_code,
                'CITY'::sites.jurisdiction_type_enum,
                'Paranaque Integration Jurisdiction',
                'Metro Manila',
                'National Capital Region',
                'PH',
                NULL,
                'ACTIVE'::sites.jurisdiction_status_enum,
                now() - interval '1 day',
                'controlled authority integration fixture')
            ON CONFLICT (jurisdiction_id) DO UPDATE
            SET jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum,
                effective_to = NULL,
                updated_at = now();

            INSERT INTO sites.site_jurisdiction_assignments (
                site_jurisdiction_assignment_id,
                site_id,
                jurisdiction_id,
                assignment_status,
                effective_from,
                source_reference,
                approval_reference)
            VALUES (
                @jurisdiction_assignment_id,
                @site_id,
                @jurisdiction_id,
                'ACTIVE'::sites.site_jurisdiction_assignment_status_enum,
                now() - interval '1 day',
                'controlled authority integration fixture',
                'operator-console-versioned-policy-resolution')
            ON CONFLICT (site_jurisdiction_assignment_id) DO UPDATE
            SET assignment_status = 'ACTIVE'::sites.site_jurisdiction_assignment_status_enum,
                effective_to = NULL,
                updated_at = now();

            UPDATE sites.sites
               SET lgu_code = @lgu_code,
                   updated_at = now()
             WHERE site_id = @site_id;

            INSERT INTO identity.user_roles (
                user_role_id,
                user_id,
                role_id,
                assignment_status,
                assignment_reason_code,
                assigned_by_service_identity_id,
                effective_from,
                created_by_service_identity_id,
                updated_by_service_identity_id)
            SELECT
                '77000000-0000-0000-0000-000000000060',
                @user_id,
                role_id,
                'ACTIVE',
                'OPERATOR_CONSOLE_DIRECT_SITE_TEST',
                '77000000-0000-0000-0000-000000000003',
                now() - interval '1 minute',
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003'
            FROM identity.roles
            WHERE role_code = 'SITE_OPERATOR'
            ON CONFLICT (user_role_id) DO UPDATE
            SET assignment_status = 'ACTIVE',
                revoked_at = NULL,
                effective_to = NULL,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();

            INSERT INTO identity.user_role_scope_grants (
                user_role_scope_grant_id,
                user_role_id,
                scope_type,
                site_id,
                grant_status,
                grant_reason_code,
                effective_from,
                granted_by_service_identity_id,
                created_by_service_identity_id,
                updated_by_service_identity_id)
            VALUES (
                '77000000-0000-0000-0000-000000000061',
                '77000000-0000-0000-0000-000000000060',
                'SITE',
                @site_id,
                'ACTIVE',
                'OPERATOR_CONSOLE_DIRECT_SITE_TEST',
                now() - interval '1 minute',
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003')
            ON CONFLICT (user_role_scope_grant_id) DO UPDATE
            SET site_id = EXCLUDED.site_id,
                grant_status = 'ACTIVE',
                revoked_at = NULL,
                effective_to = NULL,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionId;
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("jurisdiction_assignment_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionAssignmentId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = FixtureUserId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PrepareParanaqueFixtureAsync()
    {
        await PrepareBaseFixtureAsync();

        const string sql = """
            UPDATE sites.site_jurisdiction_assignments
               SET jurisdiction_id = (
                       SELECT jurisdiction_id
                       FROM sites.jurisdictions
                       WHERE jurisdiction_code = 'PH-PSGC-1381000000'
                         AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                       LIMIT 1
                   ),
                   updated_at = now()
             WHERE site_jurisdiction_assignment_id = @jurisdiction_assignment_id
               AND EXISTS (
                       SELECT 1
                       FROM sites.jurisdictions
                       WHERE jurisdiction_code = 'PH-PSGC-1381000000'
                         AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                   );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_assignment_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionAssignmentId;
        var updated = await command.ExecuteNonQueryAsync();
        updated.Should().Be(1, "the disposable canonical database must contain the accepted Parañaque jurisdiction");
    }

    private static async Task InsertDedicatedPolicyAsync(
        Guid policyId,
        string policyCode,
        string policyName,
        string entitlementType,
        string verificationStatus,
        string requiredEvidenceType,
        string policyStatus = "ACTIVE")
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_registry_id,
                policy_code,
                policy_name,
                policy_description,
                entitlement_type,
                policy_status,
                verification_status,
                policy_level,
                policy_type,
                policy_resolution_basis,
                benefit_type,
                discount_base_scope,
                jurisdiction_code,
                jurisdiction_name,
                site_group_id,
                site_id,
                beneficiary_residency_scope,
                facility_scope,
                requires_evidence,
                required_evidence_type,
                requires_operator_validation,
                legal_basis_reference,
                ordinance_reference,
                source_reference,
                reviewed_by,
                reviewed_at,
                approved_by,
                approved_at,
                effective_from,
                effective_to,
                notes,
                correlation_id
            )
            VALUES (
                @policy_id,
                @policy_code,
                @policy_name,
                'Dedicated registry integration fixture row.',
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @policy_status::discounts.discount_policy_status_enum,
                @verification_status::discounts.policy_verification_status_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                @jurisdiction_code,
                'Dummy Integration Jurisdiction',
                @site_group_id,
                @site_id,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                'Dedicated registry integration fixture only.',
                true,
                @required_evidence_type::discounts.discount_evidence_type_enum,
                true,
                'DUMMY-LEGAL-258',
                'DUMMY-ORD-258',
                'DUMMY-SOURCE-258',
                'test-reviewer-258',
                now() - interval '2 days',
                CASE
                    WHEN @verification_status IN ('APPROVED_FOR_PILOT', 'ACTIVE_APPROVED') THEN 'test-approver-258'
                    ELSE NULL
                END,
                CASE
                    WHEN @verification_status IN ('APPROVED_FOR_PILOT', 'ACTIVE_APPROVED') THEN now() - interval '1 day'
                    ELSE NULL
                END,
                now() - interval '1 day',
                NULL,
                'Non-production dedicated registry integration fixture.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                required_evidence_type = EXCLUDED.required_evidence_type,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                approved_by = EXCLUDED.approved_by,
                approved_at = EXCLUDED.approved_at,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId;
        command.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = policyCode;
        command.Parameters.Add("policy_name", NpgsqlDbType.Varchar).Value = policyName;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("policy_status", NpgsqlDbType.Text).Value = policyStatus;
        command.Parameters.Add("verification_status", NpgsqlDbType.Text).Value = verificationStatus;
        command.Parameters.Add("required_evidence_type", NpgsqlDbType.Text).Value = requiredEvidenceType;
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = FixtureSiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertVersionedPolicyAsync(
        Guid policyId,
        Guid policyVersionId,
        string policyCode,
        string policyName,
        string entitlementType,
        string requiredEvidenceType)
    {
        await InsertDedicatedPolicyAsync(
            policyId,
            policyCode,
            policyName,
            entitlementType,
            "VERIFIED_ACTIVE_OPERATIONAL",
            requiredEvidenceType);

        const string sql = """
            UPDATE discounts.statutory_discount_policy_registry
               SET jurisdiction_id = @jurisdiction_id,
                   local_government_unit_id = @jurisdiction_id,
                   jurisdiction_code = @jurisdiction_code,
                   beneficiary_residency_scope = 'RESIDENT_ONLY'::discounts.beneficiary_residency_scope_enum,
                   benefit_type = 'FULL_FEE_EXEMPTION'::discounts.parking_benefit_type_enum,
                   discount_base_scope = 'NOT_APPLICABLE'::discounts.discount_base_scope_enum,
                   full_fee_exempt = true,
                   legal_basis_reference = 'CONTROLLED_FIXTURE_AUTHORITY',
                   ordinance_reference = NULL,
                   source_reference = @source_reference,
                   updated_at = now()
             WHERE statutory_discount_policy_registry_id = @policy_id;

            INSERT INTO discounts.statutory_discount_policy_versions (
                statutory_discount_policy_version_id,
                statutory_discount_policy_registry_id,
                policy_code,
                policy_version,
                policy_version_label,
                entitlement_type,
                jurisdiction_id,
                local_government_unit_id,
                jurisdiction_code,
                jurisdiction_display_name,
                policy_scope_type,
                policy_level,
                policy_type,
                policy_resolution_basis,
                source_verification_status,
                transaction_publication_status,
                detailed_rule_verification_status,
                parking_service_applicability,
                benefit_type,
                policy_effect_support_status,
                discount_base_scope,
                beneficiary_residency_scope,
                official_source_identified,
                official_source_available,
                ordinance_text_available,
                ordinance_number_available,
                ordinance_title_available,
                ordinance_number,
                ordinance_title,
                legal_basis_reference,
                source_type,
                source_reference,
                safe_channel_summary,
                safe_reviewer_guidance,
                full_fee_exempt,
                transaction_use_effective_from,
                precedence_rank,
                policy_semantic_hash,
                reviewed_by,
                reviewed_at,
                approved_by,
                approved_at,
                correlation_id)
            VALUES (
                @policy_version_id,
                @policy_id,
                @policy_code,
                '2026.09.1',
                @policy_name,
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @jurisdiction_id,
                @jurisdiction_id,
                @jurisdiction_code,
                'Paranaque Integration Jurisdiction',
                'JURISDICTION'::discounts.policy_scope_type_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                'VERIFIED_ACTIVE_OPERATIONAL'::discounts.policy_verification_status_enum,
                'ACTIVE_FOR_TRANSACTION_USE'::discounts.statutory_policy_publication_status_enum,
                'PARTIALLY_VERIFIED'::discounts.policy_detail_verification_status_enum,
                'COVERED'::discounts.parking_service_applicability_status_enum,
                'FULL_FEE_EXEMPTION'::discounts.parking_benefit_type_enum,
                'SUPPORTED_BY_CURRENT_CALCULATION'::discounts.policy_effect_support_status_enum,
                'NOT_APPLICABLE'::discounts.discount_base_scope_enum,
                'RESIDENT_ONLY'::discounts.beneficiary_residency_scope_enum,
                true,
                false,
                false,
                false,
                false,
                NULL,
                NULL,
                NULL,
                'CONTROLLED_OFFLINE_AUTHORITY'::discounts.policy_source_type_enum,
                @source_reference,
                'Verified local full-fee parking authority.',
                'Confirm resident eligibility and required statutory evidence.',
                true,
                now() - interval '1 day',
                10,
                @policy_semantic_hash,
                'integration-test-reviewer',
                now() - interval '2 days',
                'integration-test-approver',
                now() - interval '1 day',
                gen_random_uuid())
            ON CONFLICT (statutory_discount_policy_version_id) DO UPDATE
            SET source_verification_status = EXCLUDED.source_verification_status,
                transaction_publication_status = EXCLUDED.transaction_publication_status,
                parking_service_applicability = EXCLUDED.parking_service_applicability,
                policy_effect_support_status = EXCLUDED.policy_effect_support_status,
                transaction_use_effective_from = EXCLUDED.transaction_use_effective_from,
                transaction_use_effective_to = NULL,
                withdrawn_at = NULL,
                retired_at = NULL,
                superseded_by_policy_version_id = NULL,
                updated_at = now();

            INSERT INTO discounts.statutory_discount_policy_version_evidence_requirements (
                statutory_discount_policy_version_evidence_requirement_id,
                statutory_discount_policy_version_id,
                evidence_type,
                requirement_status,
                safe_requirement_label)
            VALUES (
                gen_random_uuid(),
                @policy_version_id,
                @required_evidence_type::discounts.discount_evidence_type_enum,
                'REQUIRED',
                'Required statutory identification')
            ON CONFLICT (statutory_discount_policy_version_id, evidence_type) DO UPDATE
            SET requirement_status = 'REQUIRED',
                safe_requirement_label = EXCLUDED.safe_requirement_label,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId;
        command.Parameters.Add("policy_version_id", NpgsqlDbType.Uuid).Value = policyVersionId;
        command.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = policyCode;
        command.Parameters.Add("policy_name", NpgsqlDbType.Varchar).Value = policyName;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("required_evidence_type", NpgsqlDbType.Text).Value = requiredEvidenceType;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionId;
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("source_reference", NpgsqlDbType.Varchar).Value = "controlled-authority:paranaque-parking-benefit:v1";
        command.Parameters.Add("policy_semantic_hash", NpgsqlDbType.Varchar).Value =
            $"sha256:{new string(entitlementType == "PWD" ? 'd' : 'c', 64)}";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCompatibilityLocalPolicyAsync(Guid policyId, string policyCode, string entitlementType)
    {
        const string sql = """
            INSERT INTO discounts.discount_policy_references (
                discount_policy_reference_id,
                policy_code,
                policy_name,
                policy_description,
                policy_type,
                policy_level,
                entitlement_type,
                local_ordinance_reference,
                lgu_code,
                site_group_id,
                site_id,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                effective_from,
                policy_status
            )
            VALUES (
                @policy_id,
                @policy_code,
                @policy_name,
                'Compatibility mirror for dedicated registry integration test.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                'TEST-ORD-258',
                @lgu_code,
                @site_group_id,
                @site_id,
                10,
                'dedicated-registry-test-v1',
                true,
                true,
                now() - interval '1 day',
                'ACTIVE'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET discount_policy_reference_id = EXCLUDED.discount_policy_reference_id,
                lgu_code = EXCLUDED.lgu_code,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                policy_status = EXCLUDED.policy_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId;
        command.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = policyCode;
        command.Parameters.Add("policy_name", NpgsqlDbType.Varchar).Value = $"{policyCode} Compatibility Mirror";
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = FixtureSiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetPolicyVersionPublicationStatusAsync(Guid policyVersionId, string publicationStatus)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_policy_versions
               SET transaction_publication_status = @publication_status::discounts.statutory_policy_publication_status_enum,
                   updated_at = now()
             WHERE statutory_discount_policy_version_id = @policy_version_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_version_id", NpgsqlDbType.Uuid).Value = policyVersionId;
        command.Parameters.Add("publication_status", NpgsqlDbType.Text).Value = publicationStatus;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupDedicatedRegistryFixtureAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            UPDATE core.tariff_snapshots
               SET statutory_discount_validation_id = NULL,
                   superseded_by_tariff_snapshot_id = NULL
             WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND requested_by_user_id = @user_id
             );

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND requested_by_user_id = @user_id
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id
              AND statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND requested_by_user_id = @user_id
            );

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id
              AND requested_by_user_id = @user_id;

            DELETE FROM discounts.discount_policy_references
            WHERE policy_code LIKE 'DUMMY_OC_POLICY_%'
               OR policy_code LIKE 'TEST_OC_POLICY_%'
               OR policy_code LIKE 'E2E_OC_POLICY_%'
               OR policy_code LIKE 'SANDBOX_OC_POLICY_%';

            DELETE FROM discounts.statutory_discount_policy_version_evidence_requirements
            WHERE statutory_discount_policy_version_id IN (
                @senior_policy_version_id,
                @pwd_policy_version_id,
                @conflicting_senior_policy_version_id
            );

            DELETE FROM discounts.statutory_discount_policy_versions
            WHERE statutory_discount_policy_version_id IN (
                @senior_policy_version_id,
                @pwd_policy_version_id,
                @conflicting_senior_policy_version_id
            );

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE policy_code LIKE 'DUMMY_OC_POLICY_%'
               OR policy_code LIKE 'TEST_OC_POLICY_%'
               OR policy_code LIKE 'E2E_OC_POLICY_%'
               OR policy_code LIKE 'SANDBOX_OC_POLICY_%'
               OR statutory_discount_policy_registry_id IN (
                    @senior_versioned_policy_id,
                    @pwd_versioned_policy_id,
                    @obsolete_senior_policy_id,
                    @obsolete_pwd_policy_id,
                    @conflicting_senior_policy_id
               );

            DELETE FROM sites.site_jurisdiction_assignments
            WHERE site_jurisdiction_assignment_id = @jurisdiction_assignment_id;

            DELETE FROM sites.jurisdictions
            WHERE jurisdiction_id = @jurisdiction_id;

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = FixtureUserId;
        command.Parameters.Add("senior_policy_version_id", NpgsqlDbType.Uuid).Value = SeniorPolicyVersionId;
        command.Parameters.Add("pwd_policy_version_id", NpgsqlDbType.Uuid).Value = PwdPolicyVersionId;
        command.Parameters.Add("conflicting_senior_policy_version_id", NpgsqlDbType.Uuid).Value = ConflictingSeniorPolicyVersionId;
        command.Parameters.Add("senior_versioned_policy_id", NpgsqlDbType.Uuid).Value = SeniorVersionedPolicyId;
        command.Parameters.Add("pwd_versioned_policy_id", NpgsqlDbType.Uuid).Value = PwdVersionedPolicyId;
        command.Parameters.Add("obsolete_senior_policy_id", NpgsqlDbType.Uuid).Value = ObsoleteSeniorPolicyId;
        command.Parameters.Add("obsolete_pwd_policy_id", NpgsqlDbType.Uuid).Value = ObsoletePwdPolicyId;
        command.Parameters.Add("conflicting_senior_policy_id", NpgsqlDbType.Uuid).Value = ConflictingSeniorPolicyId;
        command.Parameters.Add("jurisdiction_assignment_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionAssignmentId;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasDedicatedRegistryAsync()
    {
        const string sql = """
            SELECT to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> CountBoundaryRowsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts)
              + (SELECT COUNT(*) FROM core.payment_confirmations)
              + (SELECT COUNT(*) FROM core.exit_authorizations)
              + (SELECT COUNT(*) FROM coupons.coupon_applications)
              + (SELECT COUNT(*) FROM gates.gate_authorization_consumptions)
              + (SELECT COUNT(*) FROM payments.provider_outcomes)
              + (SELECT COUNT(*) FROM reconciliation.reconciliation_items) AS boundary_count;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<bool> CanOpenDatabaseAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static string ReadRepoFile(
        string pathPart1,
        string pathPart2,
        string pathPart3,
        [CallerFilePath] string sourceFilePath = "")
    {
        var pathParts = new[] { pathPart1, pathPart2, pathPart3 };
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            var fromSource = TryReadRepoFile(new DirectoryInfo(sourceDirectory), pathParts);
            if (fromSource is not null)
            {
                return fromSource;
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        var fromOutput = TryReadRepoFile(current, pathParts);
        if (fromOutput is not null)
        {
            return fromOutput;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        var fromWorkingDirectory = TryReadRepoFile(current, pathParts);
        if (fromWorkingDirectory is not null)
        {
            return fromWorkingDirectory;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }

    private static string? TryReadRepoFile(DirectoryInfo? current, string[] pathParts)
    {
        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        return null;
    }
}
