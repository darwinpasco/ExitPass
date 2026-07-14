using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Proves the browser-facing statutory discount UAT path against the aligned canonical DB seed.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountAlignedDbUatApiIntegrationTests
{
    private const string SessionLookupEndpoint = "/v1/ops/operator-console/sessions/lookup";
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string DraftDetailEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/drafts/{0}?correlationId={1}";
    private const string EvidenceEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/evidence";
    private const string DecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/decision";
    private const string ApplyEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";

    private static readonly Guid RequesterUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid ReviewerUserId = Guid.Parse("77000000-0000-0000-0000-000000000012");
    private static readonly Guid DeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid RequesterShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid ReviewerShiftId = Guid.Parse("77000000-0000-0000-0000-000000000052");
    private static readonly Guid ParkingSessionId = Guid.Parse("23100000-0000-0000-0000-000000000003");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("23100000-0000-0000-0000-000000000004");
    private const string TicketReference = "E2E-231-SESSION-001";

    [Fact]
    public async Task AlignedDbUatFixture_CompletesReviewApproveApplyWithoutUnsafeSideEffects()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await ApplySqlFileAsync("scripts", "operator-console", "Seed-StatutoryDiscountPilotFixture.sql");
        await ApplySqlFileAsync("scripts", "management-platform", "Seed-ManagementPlatformUatIdentityRbac.sql");
        await ApplySqlFileAsync("scripts", "management-platform", "Verify-ManagementPlatformUatIdentityRbac.sql");
        await ApplySqlFileAsync("scripts", "operator-console", "Verify-StatutoryDiscountPilotFixture.sql");

        using var factory = CreateAllowedAccessFactory();
        using var client = factory.CreateClient();
        var beforeUnsafeSideEffectCount = await CountUnsafeSideEffectRecordsAsync();

        var lookup = await PostOkAsync<OperatorConsoleSessionLookupResponse>(
            client,
            SessionLookupEndpoint,
            SessionLookupRequest());
        lookup.SessionFound.Should().BeTrue();
        lookup.SessionEligible.Should().BeTrue();
        lookup.TicketReference.Should().Be(TicketReference);
        lookup.CurrentPayableAmountMinorUnits.Should().Be(12500);
        lookup.CurrencyCode.Should().Be("PHP");

        var draft = await PostOkAsync<OperatorConsoleStatutoryDiscountDraftResponse>(
            client,
            DraftEndpoint,
            DraftRequest());
        draft.DraftAccepted.Should().BeTrue();
        draft.DraftPersisted.Should().BeTrue();
        draft.DraftId.Should().NotBeNull();
        draft.EvidenceRequired.Should().BeTrue();

        var draftId = draft.DraftId!.Value;
        var initialDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
            client,
            DraftDetailEndpoint(draftId),
            RequesterUserId,
            RequesterShiftId);
        initialDetail.TicketReference.Should().Be(TicketReference);
        initialDetail.ValidationStatus.Should().Be("REQUESTED");
        initialDetail.EvidenceRequired.Should().BeTrue();
        initialDetail.EvidenceRequiredSatisfied.Should().BeFalse();
        initialDetail.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);

        var evidence = await PostOkAsync<OperatorConsoleStatutoryDiscountEvidenceCaptureResponse>(
            client,
            EvidenceEndpoint(draftId),
            EvidenceRequest());
        evidence.EvidenceRequiredSatisfied.Should().BeTrue();
        evidence.VerificationStatus.Should().Be("CAPTURED");
        evidence.StorageReference.Should().Be("operator-confirmed");

        var sameRequesterApprove = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
            client,
            DecisionEndpoint(draftId),
            DecisionRequest("APPROVE", RequesterUserId, RequesterShiftId));
        sameRequesterApprove.DecisionAccepted.Should().BeFalse();
        sameRequesterApprove.DecisionPersisted.Should().BeFalse();
        sameRequesterApprove.CurrentValidationStatus.Should().Be("REQUESTED");
        sameRequesterApprove.ErrorCode.Should().Be("REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT");
        (await ReadDraftStatusAsync(draftId)).Should().Be("REQUESTED");
        (await CountApplicationsAsync(draftId)).Should().Be(0);

        var approved = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
            client,
            DecisionEndpoint(draftId),
            DecisionRequest("APPROVE", ReviewerUserId, ReviewerShiftId));
        approved.DecisionAccepted.Should().BeTrue();
        approved.DecisionPersisted.Should().BeTrue();
        approved.CurrentValidationStatus.Should().Be("APPROVED");
        (await ReadValidatedByUserIdAsync(draftId)).Should().Be(ReviewerUserId);

        var applied = await PostOkAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(
            client,
            ApplyEndpoint(draftId),
            ApplyRequest());
        applied.ApplicationAccepted.Should().BeTrue();
        applied.ApplicationPersisted.Should().BeTrue();
        applied.ApplicationStatus.Should().Be("APPLIED");
        applied.PayableBasisApplicationId.Should().NotBeNull();
        applied.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
        applied.AppliedTariffSnapshotId.Should().NotBeNull();
        applied.GrossAmountMinorUnits.Should().Be(12500);
        applied.VatExclusiveAmountMinorUnits.Should().Be(11161);
        applied.VatAmountMinorUnits.Should().Be(1339);
        applied.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        applied.FinalPayableAmountMinorUnits.Should().Be(8929);

        var finalDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
            client,
            DraftDetailEndpoint(draftId),
            ReviewerUserId,
            ReviewerShiftId);
        finalDetail.ValidationStatus.Should().Be("APPROVED");
        finalDetail.EvidenceRequiredSatisfied.Should().BeTrue();
        finalDetail.LatestEvidenceStatus.Should().Be("CAPTURED");
        finalDetail.PayableBasisApplicationStatus.Should().Be("APPLIED");
        finalDetail.PayableBasisApplicationId.Should().Be(applied.PayableBasisApplicationId);
        finalDetail.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        finalDetail.VatExclusiveAmountMinorUnits.Should().Be(11161);
        finalDetail.VatAmountMinorUnits.Should().Be(1339);
        finalDetail.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        finalDetail.FinalPayableAmountMinorUnits.Should().Be(8929);

        (await CountUnsafeSideEffectRecordsAsync()).Should().Be(beforeUnsafeSideEffectCount);
    }

    private static OperatorConsoleSessionLookupRequest SessionLookupRequest() =>
        new(
            RequesterUserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            RequesterShiftId,
            ParkingSessionId: null,
            TicketReference,
            PlateNumber: null,
            "TICKET_REFERENCE",
            $"operator-console-statutory-discount-aligned-uat-lookup-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest() =>
        new(
            RequesterUserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            RequesterShiftId,
            ParkingSessionId,
            TicketReference,
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "SC-UAT-****-0001",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: true,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "Aligned DB Operator Console UAT proof.",
            ReasonCode: "ALIGNED_DB_UAT",
            $"operator-console-statutory-discount-aligned-uat-draft-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureRequest EvidenceRequest() =>
        new(
            RequesterUserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            RequesterShiftId,
            "SENIOR_CITIZEN_ID",
            "OPERATOR_CONFIRMED",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            StorageReference: null,
            ReferenceNumber: null,
            Notes: "Aligned DB metadata-only evidence capture.",
            OperatorConfirmation: true,
            $"operator-console-statutory-discount-aligned-uat-evidence-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDecisionRequest DecisionRequest(
        string decision,
        Guid actorUserId,
        Guid shiftId) =>
        new(
            actorUserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            shiftId,
            decision,
            DecisionReasonCode: null,
            DecisionNotes: "Aligned DB Operator Console UAT decision.",
            ReviewerAttestation: true,
            $"operator-console-statutory-discount-aligned-uat-decision-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisRequest ApplyRequest() =>
        new(
            ReviewerUserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ReviewerShiftId,
            OriginalTariffSnapshotId,
            $"operator-console-statutory-discount-aligned-uat-apply-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static string DraftDetailEndpoint(Guid draftId) =>
        string.Format(DraftDetailEndpointTemplate, draftId, Guid.NewGuid());

    private static string EvidenceEndpoint(Guid draftId) =>
        string.Format(EvidenceEndpointTemplate, draftId);

    private static string DecisionEndpoint(Guid draftId) =>
        string.Format(DecisionEndpointTemplate, draftId);

    private static string ApplyEndpoint(Guid draftId) =>
        string.Format(ApplyEndpointTemplate, draftId);

    private static async Task<T> PostOkAsync<T>(HttpClient client, string endpoint, object body)
    {
        using var response = await client.PostAsJsonAsync(endpoint, body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        var parsed = await response.Content.ReadFromJsonAsync<T>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static async Task<T> GetOkAsync<T>(HttpClient client, string endpoint, Guid userId, Guid shiftId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddOperatorHeaders(request, userId, shiftId);
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        var parsed = await response.Content.ReadFromJsonAsync<T>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static void AddOperatorHeaders(HttpRequestMessage request, Guid userId, Guid shiftId)
    {
        request.Headers.Add("X-Operator-User-Id", userId.ToString());
        request.Headers.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        request.Headers.Add("X-Operator-Shift-Id", shiftId.ToString());
        request.Headers.Add("X-Site-Id", SiteId.ToString());
        request.Headers.Add("X-Site-Group-Id", SiteGroupId.ToString());
    }

    private static CustomWebApplicationFactory CreateAllowedAccessFactory() =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAllowedAccessEvaluationService());
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAllowedAccessEvaluationWriter());
            });

    private static async Task ApplySqlFileAsync(params string[] pathParts)
    {
        var sql = ReadRepoFile(pathParts);
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 120
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadDraftStatusAsync(Guid draftId)
    {
        const string sql = """
            SELECT validation_status::text
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task<Guid?> ReadValidatedByUserIdAsync(Guid draftId)
    {
        const string sql = """
            SELECT validated_by_user_id
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (Guid)value;
    }

    private static async Task<int> CountApplicationsAsync(Guid validationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountUnsafeSideEffectRecordsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM core.payment_confirmations pc
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.fiscal_issuance_references WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM gates.gate_authorization_consumptions gac
                   JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
                  WHERE ea.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM coupons.coupon_applications WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM payments.provider_outcomes po
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM reconciliation.reconciliation_items ri
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = ri.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id) AS boundary_count;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
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

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

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

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }

    private sealed class FakeAllowedAccessEvaluationService : IOperatorConsoleAccessEvaluationService
    {
        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(AllowedAccessResult(command) with { EvaluationId = Guid.Empty, Persisted = false });
    }

    private sealed class FakeAllowedAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
    {
        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with { EvaluationId = Guid.NewGuid(), Persisted = true });
    }

    private static OperatorConsoleAccessEvaluationResult AllowedAccessResult(
        OperatorConsoleAccessEvaluationCommand command) =>
        new(
            Guid.Empty,
            Allowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            "OPERATOR",
            new OperatorConsoleDeviceTrustResult(command.OperatorDeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(command.OperatorShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(command.SiteId, command.SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-07-14T10:00:00+08:00"),
            Persisted: false,
            command.CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                command.UserId,
                HrIdentityMappingId: null,
                command.OperatorDeviceBindingId,
                command.OperatorShiftId,
                ShiftTakeoverId: null,
                command.SiteGroupId,
                command.SiteId,
                command.ControlledActionCode,
                command.WorkflowCode,
                command.ParkingSessionId.HasValue ? "PARKING_SESSION" : null,
                command.ParkingSessionId));
}
