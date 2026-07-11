using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Proves the controlled Operator Console statutory discount validation chain end to end.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountE2EIntegrationTests
{
    private const string SessionLookupEndpoint = "/v1/ops/operator-console/sessions/lookup";
    private const string PolicyResolutionEndpoint = "/v1/ops/operator-console/statutory-discounts/resolve-policy";
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string DraftDetailEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/drafts/{0}?correlationId={1}";
    private const string EvidenceEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/evidence";
    private const string EvidenceListEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/evidence?correlationId={1}";
    private const string DecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/decision";
    private const string ApplyEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";

    private static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid DeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid ShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid VendorSystemId = Guid.Parse("77000000-0000-0000-0000-000000000004");
    private static readonly Guid ServiceIdentityId = Guid.Parse("77000000-0000-0000-0000-000000000003");

    private static readonly Guid JurisdictionId = Guid.Parse("23100000-0000-0000-0000-000000000001");
    private const string E2ELguCode = "PH-INT-E2E-231";
    private static readonly Guid PolicyId = Guid.Parse("23100000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("23100000-0000-0000-0000-000000000003");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("23100000-0000-0000-0000-000000000004");

    /// <summary>
    /// Verifies lookup, policy resolution, evidence capture, approval, apply, and final read state as one controlled session.
    /// </summary>
    [Fact]
    public async Task EndToEnd_WhenOperatorCompletesRequiredEvidenceFlow_AppliesApprovedPayableBasis()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetE2EStateAsync();
        try
        {
            await InsertE2EPolicyFixtureAsync();
            await InsertParkingSessionAsync();
            await InsertBaseTariffSnapshotAsync();

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var beforeBoundaryCount = await CountPaymentProviderGateCouponReconciliationBoundaryRecordsAsync();

            var lookup = await PostOkAsync<OperatorConsoleSessionLookupResponse>(
                client,
                SessionLookupEndpoint,
                SessionLookupRequest());
            lookup.AccessAllowed.Should().BeTrue();
            lookup.SessionFound.Should().BeTrue();
            lookup.SessionEligible.Should().BeTrue();
            lookup.ParkingSessionId.Should().Be(ParkingSessionId);
            lookup.CurrentPayableAmountMinorUnits.Should().Be(12500);
            lookup.DiscountStatus.Should().Be("NOT_APPLIED");

            var policy = await PostOkAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>(
                client,
                PolicyResolutionEndpoint,
                PolicyResolutionRequest());
            policy.AccessAllowed.Should().BeTrue();
            policy.PolicyResolved.Should().BeTrue();
            policy.StatutoryDiscountPolicyId.Should().Be(PolicyId);
            policy.PolicyCode.Should().Be("PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231");
            policy.RequiresEvidence.Should().BeTrue();

            var draft = await PostOkAsync<OperatorConsoleStatutoryDiscountDraftResponse>(
                client,
                DraftEndpoint,
                DraftRequest(evidenceCaptureRequested: true));
            draft.AccessAllowed.Should().BeTrue();
            draft.DraftAccepted.Should().BeTrue();
            draft.DraftPersisted.Should().BeTrue();
            draft.DraftId.Should().NotBeNull();
            draft.EvidenceRequired.Should().BeTrue();
            draft.PolicyCode.Should().Be("PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231");

            var draftId = draft.DraftId!.Value;
            var initialDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
                client,
                DraftDetailEndpoint(draftId));
            initialDetail.ValidationStatus.Should().Be("REQUESTED");
            initialDetail.EvidenceRequired.Should().BeTrue();
            initialDetail.EvidenceRequiredSatisfied.Should().BeFalse();
            initialDetail.RequiredEvidenceTypes.Should().ContainSingle().Which.Should().Be("SENIOR_CITIZEN_ID");

            var applyBeforeApproval = await PostOkAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(
                client,
                ApplyEndpoint(draftId),
                ApplyRequest());
            applyBeforeApproval.ApplicationAccepted.Should().BeFalse();
            applyBeforeApproval.ApplicationPersisted.Should().BeFalse();
            applyBeforeApproval.ErrorCode.Should().Be("STATUTORY_DISCOUNT_NOT_APPROVED");
            (await CountApplicationsAsync(draftId)).Should().Be(0);

            var blockedApproval = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
                client,
                DecisionEndpoint(draftId),
                DecisionRequest("APPROVE"));
            blockedApproval.DecisionAccepted.Should().BeFalse();
            blockedApproval.DecisionPersisted.Should().BeFalse();
            blockedApproval.ErrorCode.Should().Be("EVIDENCE_REQUIRED_NOT_CAPTURED");
            (await ReadDraftStatusAsync(draftId)).Should().Be("REQUESTED");

            using (var wrongEvidenceResponse = await client.PostAsJsonAsync(
                EvidenceEndpoint(draftId),
                EvidenceRequest("PWD_ID")))
            {
                wrongEvidenceResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var error = await wrongEvidenceResponse.Content.ReadFromJsonAsync<ErrorResponse>();
                error.Should().NotBeNull();
                error!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST");
            }

            var afterWrongEvidenceDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
                client,
                DraftDetailEndpoint(draftId));
            afterWrongEvidenceDetail.EvidenceRequiredSatisfied.Should().BeFalse();
            (await CountCapturedEvidenceAsync(draftId, "PWD_ID")).Should().Be(0);

            var evidence = await PostOkAsync<OperatorConsoleStatutoryDiscountEvidenceCaptureResponse>(
                client,
                EvidenceEndpoint(draftId),
                EvidenceRequest("SENIOR_CITIZEN_ID"));
            evidence.AccessAllowed.Should().BeTrue();
            evidence.EvidenceRequiredSatisfied.Should().BeTrue();
            evidence.VerificationStatus.Should().Be("CAPTURED");
            evidence.StorageReference.Should().Be("operator-confirmed");
            evidence.ReferenceNumberMasked.Should().BeNull();

            var evidenceList = await GetOkAsync<OperatorConsoleStatutoryDiscountEvidenceListResponse>(
                client,
                EvidenceListEndpoint(draftId));
            evidenceList.EvidenceRequired.Should().BeTrue();
            evidenceList.EvidenceRequiredSatisfied.Should().BeTrue();
            evidenceList.EvidenceCount.Should().BeGreaterThanOrEqualTo(1);
            evidenceList.LatestEvidenceStatus.Should().Be("CAPTURED");
            evidenceList.RequiredEvidenceTypes.Should().Contain("SENIOR_CITIZEN_ID");
            evidenceList.Items.Should().Contain(item =>
                item.EvidenceType == "SENIOR_CITIZEN_ID" &&
                item.CaptureMethod == "OPERATOR_CONFIRMED" &&
                item.VerificationStatus == "CAPTURED");

            var approved = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
                client,
                DecisionEndpoint(draftId),
                DecisionRequest("APPROVE"));
            approved.AccessAllowed.Should().BeTrue();
            approved.DecisionAccepted.Should().BeTrue();
            approved.DecisionPersisted.Should().BeTrue();
            approved.CurrentValidationStatus.Should().Be("APPROVED");

            var applied = await PostOkAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(
                client,
                ApplyEndpoint(draftId),
                ApplyRequest());
            applied.AccessAllowed.Should().BeTrue();
            applied.ApplicationAccepted.Should().BeTrue();
            applied.ApplicationPersisted.Should().BeTrue();
            applied.ApplicationStatus.Should().Be("APPLIED");
            applied.PayableBasisApplicationId.Should().NotBeNull();
            applied.StatutoryDiscountValidationId.Should().Be(draftId);
            applied.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
            applied.AppliedTariffSnapshotId.Should().NotBeNull();
            applied.StatutoryDiscountPolicyId.Should().Be(PolicyId);
            applied.PolicyCode.Should().Be("PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231");
            applied.PolicySnapshotUsed.Should().BeTrue();
            applied.GrossAmountMinorUnits.Should().Be(12500);
            applied.VatAmountMinorUnits.Should().Be(1339);
            applied.VatExclusiveAmountMinorUnits.Should().Be(11161);
            applied.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
            applied.FinalPayableAmountMinorUnits.Should().Be(8929);

            var finalDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
                client,
                DraftDetailEndpoint(draftId));
            finalDetail.ValidationStatus.Should().Be("APPROVED");
            finalDetail.EvidenceRequiredSatisfied.Should().BeTrue();
            finalDetail.LatestEvidenceStatus.Should().Be("CAPTURED");
            finalDetail.PayableBasisApplicationId.Should().Be(applied.PayableBasisApplicationId);
            finalDetail.PayableBasisApplicationStatus.Should().Be("APPLIED");
            finalDetail.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
            finalDetail.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
            finalDetail.PayableAmountMinorUnits.Should().Be(8929);

            (await CountApplicationsAsync(draftId)).Should().Be(1);
            var afterBoundaryCount = await CountPaymentProviderGateCouponReconciliationBoundaryRecordsAsync();
            afterBoundaryCount.Should().Be(beforeBoundaryCount);
        }
        finally
        {
            await ResetE2EStateAsync();
        }
    }

    private static OperatorConsoleSessionLookupRequest SessionLookupRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "E2E-231-SESSION-001",
            PlateNumber: null,
            "PARKING_SESSION_ID",
            $"operator-console-statutory-discount-e2e-lookup-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountPolicyResolutionRequest PolicyResolutionRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            $"operator-console-statutory-discount-e2e-policy-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest(bool evidenceCaptureRequested) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "E2E-231-SESSION-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "1234",
            EntitlementFingerprint: null,
            evidenceCaptureRequested,
            evidenceCaptureRequested ? "SUPERVISOR_REVIEW" : null,
            OperatorAttestation: true,
            AttestationNotes: "Controlled E2E statutory discount validation session.",
            ReasonCode: "INTEGRATION_E2E_231",
            $"operator-console-statutory-discount-e2e-draft-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureRequest EvidenceRequest(string evidenceType) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            evidenceType,
            "OPERATOR_CONFIRMED",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            StorageReference: null,
            ReferenceNumber: null,
            Notes: "Controlled E2E metadata-only evidence capture.",
            OperatorConfirmation: true,
            $"operator-console-statutory-discount-e2e-evidence-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDecisionRequest DecisionRequest(string decision) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            DecisionReasonCode: null,
            DecisionNotes: "Controlled E2E statutory discount validation decision.",
            ReviewerAttestation: true,
            $"operator-console-statutory-discount-e2e-decision-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisRequest ApplyRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            OriginalTariffSnapshotId,
            $"operator-console-statutory-discount-e2e-apply-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static string DraftDetailEndpoint(Guid draftId) =>
        string.Format(DraftDetailEndpointTemplate, draftId, Guid.NewGuid());

    private static string EvidenceEndpoint(Guid draftId) =>
        string.Format(EvidenceEndpointTemplate, draftId);

    private static string EvidenceListEndpoint(Guid draftId) =>
        string.Format(EvidenceListEndpointTemplate, draftId, Guid.NewGuid());

    private static string DecisionEndpoint(Guid draftId) =>
        string.Format(DecisionEndpointTemplate, draftId);

    private static string ApplyEndpoint(Guid draftId) =>
        string.Format(ApplyEndpointTemplate, draftId);

    private static async Task<T> PostOkAsync<T>(HttpClient client, string endpoint, object body)
    {
        using var response = await client.PostAsJsonAsync(endpoint, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var parsed = await response.Content.ReadFromJsonAsync<T>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static async Task<T> GetOkAsync<T>(HttpClient client, string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddOperatorHeaders(request);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var parsed = await response.Content.ReadFromJsonAsync<T>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static void AddOperatorHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Operator-User-Id", UserId.ToString());
        request.Headers.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        request.Headers.Add("X-Operator-Shift-Id", ShiftId.ToString());
        request.Headers.Add("X-Site-Id", SiteId.ToString());
        request.Headers.Add("X-Site-Group-Id", SiteGroupId.ToString());
    }

    private static async Task SeedManualFixtureAsync()
    {
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
    }

    private static async Task ResetE2EStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM gates.gate_authorization_consumptions gac
            USING core.exit_authorizations ea
            WHERE gac.exit_authorization_id = ea.exit_authorization_id
              AND ea.parking_session_id = @parking_session_id;

            DELETE FROM core.exit_authorizations
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.payment_confirmations pc
            USING core.payment_attempts pa
            WHERE pc.payment_attempt_id = pa.payment_attempt_id
              AND pa.parking_session_id = @parking_session_id;

            DELETE FROM payments.provider_outcomes po
            USING core.payment_attempts pa
            WHERE po.payment_attempt_id = pa.payment_attempt_id
              AND pa.parking_session_id = @parking_session_id;

            DELETE FROM reconciliation.reconciliation_items ri
            USING core.payment_attempts pa
            WHERE ri.payment_attempt_id = pa.payment_attempt_id
              AND pa.parking_session_id = @parking_session_id;

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM coupons.coupon_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
               SET superseded_by_tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_policy_references
            WHERE policy_code = 'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231';

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE policy_code = 'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231';

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertE2EPolicyFixtureAsync()
    {
        const string sql = """
            BEGIN;

            UPDATE sites.sites
               SET lgu_code = @lgu_code,
                   updated_at = now()
             WHERE site_id = @site_id;

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
                'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231',
                'ATC Senior Citizen Site Policy 231',
                'Senior Citizen site policy requiring metadata-only evidence.',
                'SITE_POLICY',
                'SITE_POLICY',
                'SENIOR_CITIZEN',
                'ATC-ORD-231',
                @lgu_code,
                @site_id,
                0,
                'policy-v1',
                true,
                true,
                now() - interval '1 day',
                'ACTIVE'
            );

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
                'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231',
                'ATC Senior Citizen Site Policy 231',
                'Senior Citizen site policy requiring metadata-only evidence.',
                'SENIOR_CITIZEN'::discounts.statutory_entitlement_type_enum,
                'ACTIVE'::discounts.discount_policy_status_enum,
                'ACTIVE_APPROVED'::discounts.policy_verification_status_enum,
                'SITE_POLICY'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'SITE_POLICY_OPERATIONAL_ONLY'::discounts.policy_resolution_basis_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                @lgu_code,
                'ATC Jurisdiction',
                @site_group_id,
                @site_id,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                'ATC parking facility.',
                true,
                'SENIOR_CITIZEN_ID'::discounts.discount_evidence_type_enum,
                true,
                'ATC-ORD-231',
                'ATC-ORD-231',
                'policy-v1',
                'policy-reviewer-231',
                now() - interval '2 days',
                'policy-approver-231',
                now() - interval '1 day',
                now() - interval '1 day',
                NULL,
                'Senior Citizen site policy requiring evidence capture.',
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
                requires_evidence = EXCLUDED.requires_evidence,
                required_evidence_type = EXCLUDED.required_evidence_type,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                approved_by = EXCLUDED.approved_by,
                approved_at = EXCLUDED.approved_at,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_at = now();

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = E2ELguCode;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = SiteGroupId;
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = PolicyId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertParkingSessionAsync()
    {
        const string sql = """
            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                vendor_session_ref,
                plate_number_hash,
                plate_number_masked,
                ticket_number_hash,
                ticket_number_masked,
                entry_at,
                vendor_session_status,
                session_status,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_id,
                'E2E-231-SESSION-001',
                '2312312312312312312312312312312312312312312312312312312312312312',
                'E2E-231',
                'd6f5f9ecab9492c63d3dd2795db3f74d14fd2f071b7fc27a9c9d8fa6d341f199',
                'E2E-231-SESSION-001',
                '2026-05-29T00:00:00Z',
                'ACTIVE',
                'ACTIVE',
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = SiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = SiteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = VendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertBaseTariffSnapshotAsync()
    {
        const string sql = """
            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                snapshot_status,
                calculated_at,
                expires_at,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @tariff_snapshot_id,
                @parking_session_id,
                @vendor_system_id,
                'INTEGRATION-OPERATOR-CONSOLE-E2E-231',
                'ATC-POLICY-V1',
                'PHP',
                125.00,
                0,
                0,
                125.00,
                'ACTIVE'::core.tariff_snapshot_status_enum,
                now(),
                now() + interval '1 hour',
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = OriginalTariffSnapshotId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = VendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
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

    private static async Task<int> CountApplicationsAsync(Guid validationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountCapturedEvidenceAsync(Guid draftId, string evidenceType)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND evidence_capture_status = 'CAPTURED'::discounts.evidence_capture_status_enum
              AND purged_at IS NULL;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountPaymentProviderGateCouponReconciliationBoundaryRecordsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM core.payment_confirmations pc
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id)
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
}
