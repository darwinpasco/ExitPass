using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;
using System.Security.Cryptography;
using System.Text;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

internal sealed record SeededServiceChannelReview(
    PaymentTestContext Context,
    StatutoryDiscountDecisionV2Record Decision,
    StatutoryDiscountServiceChannelReviewDetail Review);

internal static class StatutoryDiscountReviewIntegrationTestSupport
{
    private static readonly SemaphoreSlim PatchLock = new(1, 1);

    public static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    public static async Task EnsureSchemaAsync()
    {
        await PatchLock.WaitAsync();
        try
        {
            await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(ConnectionString);
        }
        finally
        {
            PatchLock.Release();
        }
    }

    public static async Task<SeededServiceChannelReview> SeedAwaitingReviewAsync(
        string scenarioName,
        string sourceChannel,
        Guid? siteId = null,
        Guid? siteGroupId = null,
        string entitlementType = "SENIOR_CITIZEN",
        bool seedPaymentContext = true,
        Guid? reviewerUserId = null)
    {
        await EnsureSchemaAsync();
        var context = PaymentTestContext.Create(scenarioName);
        if (siteId.HasValue || siteGroupId.HasValue)
        {
            context = context with
            {
                SiteId = siteId ?? context.SiteId,
                SiteGroupId = siteGroupId ?? context.SiteGroupId
            };
        }

        if (seedPaymentContext)
        {
            await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, $"Seed {scenarioName}.");
            await SeedSupportedLocalOrdinancePolicyAsync(context, entitlementType);
            await SeedReviewerUserAsync(context, context.RequestedByUserId);
        }

        if (reviewerUserId.HasValue && reviewerUserId.Value != context.RequestedByUserId)
        {
            await SeedReviewerUserAsync(context, reviewerUserId.Value);
        }

        var staged = CreateStagedService();
        var policy = await SeedSupportedLocalOrdinancePolicyAsync(context, entitlementType);
        var created = await staged.CreateOrResolveDecisionAsync(
            DecisionCommand(context, sourceChannel, entitlementType, policy.PolicyVersionId),
            CancellationToken.None);
        var awaiting = await staged.MarkDecisionAwaitingReviewAsync(
            created.Record!.StatutoryDiscountDecisionCommandId,
            context.CorrelationId,
            CancellationToken.None);
        await SeedDecisionPolicyAuthorityAsync(context, awaiting, policy, entitlementType);

        var repository = CreateReviewRepository();
        await repository.UpsertIntakeAsync(IntakeCommand(context, awaiting, sourceChannel, entitlementType), CancellationToken.None);
        var detail = await repository.GetAsync(awaiting.StatutoryDiscountDecisionCommandId, context.CorrelationId, CancellationToken.None);

        return new SeededServiceChannelReview(context, awaiting, detail!);
    }

    public static async Task<PaymentTestContext> SeedPaymentContextAsync(string scenarioName)
    {
        await EnsureSchemaAsync();
        var context = PaymentTestContext.Create(scenarioName);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, $"Seed {scenarioName}.");
        await SeedSupportedLocalOrdinancePolicyAsync(context);
        await SeedReviewerUserAsync(context, context.RequestedByUserId);
        return context;
    }

    public static IStatutoryDiscountStagedCommandService CreateStagedService() =>
        new StatutoryDiscountStagedCommandService(new PostgresStatutoryDiscountStagedCommandRepository(ConnectionString));

    public static IStatutoryDiscountServiceChannelReviewRepository CreateReviewRepository() =>
        new PostgresStatutoryDiscountServiceChannelReviewRepository(ConnectionString);

    public static StatutoryDiscountDecisionV2Command DecisionCommand(
        PaymentTestContext context,
        string sourceChannel,
        string entitlementType = "SENIOR_CITIZEN",
        Guid? policyVersionId = null,
        string idempotencyKey = "review-linkage-decision-key") =>
        new(
            Guid.NewGuid(),
            sourceChannel,
            context.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            $"TICKET-{context.SiteCode}",
            "ABC1234",
            entitlementType,
            new StatutoryDiscountDecisionV2BeneficiaryMetadata("beneficiary-ref", entitlementType, "DRIVER", 1),
            new StatutoryDiscountDecisionV2IdentityMetadata("SENIOR_CITIZEN_ID", "OSCA", DateOnly.Parse("2030-01-01"), "SC-****-1234", null),
            [Evidence("VERIFIED")],
            new StatutoryDiscountDecisionV2AttestationFacts(true, "attestation-ref", "CUSTOMER_REQUEST", ReviewerAttested: false),
            context.RequestedByUserId,
            ReviewerUserId: null,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            new StatutoryDiscountDecisionV2DecisionFacts(StatutoryDiscountDecisionV2ResultStates.NotDecided, null, null),
            PolicyResolutionReferenceId: policyVersionId,
            AppliedPolicyReferenceId: policyVersionId,
            FallbackPolicyReferenceId: null,
            PolicyResolutionBasis: policyVersionId.HasValue ? "LOCAL_ORDINANCE_APPLIED" : "NATIONAL_DEFAULT",
            LocalOrdinanceApplied: policyVersionId.HasValue,
            context.TariffSnapshotId,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
            idempotencyKey,
            context.CorrelationId);

    public static StatutoryDiscountServiceChannelReviewIntakeCommand IntakeCommand(
        PaymentTestContext context,
        StatutoryDiscountDecisionV2Record decision,
        string sourceChannel,
        string entitlementType = "SENIOR_CITIZEN") =>
        new(
            decision.StatutoryDiscountDecisionCommandId,
            decision.RequestReference,
            context.ParkingSessionId,
            sourceChannel,
            context.SiteId,
            context.SiteGroupId,
            $"TICKET-{context.SiteCode}",
            "ABC1234",
            entitlementType,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            "SC-****-1234",
            [new StatutoryDiscountServiceChannelReviewEvidenceFact(
                "SENIOR_CITIZEN_ID",
                "MANUAL_REFERENCE",
                "evidence-ref-001",
                "SC-****-1234",
                "VERIFIED")],
            RequesterAttestation: true,
            AttestationNotes: "Customer attested statutory discount eligibility.",
            ReasonCode: "CUSTOMER_REQUEST",
            context.TariffSnapshotId,
            context.CorrelationId,
            DateTimeOffset.UtcNow);

    public static async Task CleanupAsync(PaymentTestContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM operator_console.statutory_discount_service_channel_reviews
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_decision_policy_authorities
            WHERE statutory_discount_decision_command_id IN (
                SELECT statutory_discount_decision_command_id
                FROM discounts.statutory_discount_decision_commands
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            UPDATE discounts.statutory_discount_validations
            SET
                tariff_snapshot_id = NULL,
                updated_at = NOW()
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
            SET
                superseded_by_tariff_snapshot_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id
              AND statutory_discount_validation_id IN (
                  SELECT statutory_discount_validation_id
                  FROM discounts.statutory_discount_validations
                  WHERE parking_session_id = @parking_session_id
              );

            UPDATE core.tariff_snapshots
            SET
                snapshot_status = CASE
                    WHEN snapshot_status = 'SUPERSEDED' THEN 'ACTIVE'
                    ELSE snapshot_status
                END,
                statutory_discount_validation_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM identity.users
            WHERE user_id = @requested_by_user_id;

            DELETE FROM discounts.statutory_discount_policy_version_evidence_requirements
            WHERE statutory_discount_policy_version_id IN (
                SELECT statutory_discount_policy_version_id
                FROM discounts.statutory_discount_policy_versions
                WHERE source_reference = @policy_source_reference
            );

            DELETE FROM discounts.statutory_discount_policy_versions
            WHERE source_reference = @policy_source_reference;

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE source_reference = @policy_source_reference;

            DELETE FROM sites.site_jurisdiction_assignments
            WHERE source_reference = @policy_source_reference;

            DELETE FROM sites.jurisdictions
            WHERE source_reference = @policy_source_reference;
            """,
            connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("requested_by_user_id", context.RequestedByUserId);
        command.Parameters.AddWithValue("policy_source_reference", PolicySourceReference(context));
        await command.ExecuteNonQueryAsync();

        await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
    }

    public static async Task<int> DecisionRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @id;
            """,
            parkingSessionId);

    public static async Task<int> ApplicationCommandRowCountAsync(Guid decisionCommandId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_decision_command_id = @id;
            """,
            decisionCommandId);

    public static async Task<int> PayableBasisApplicationRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @id;
            """,
            parkingSessionId);

    public static async Task<int> AppliedTariffSnapshotRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM core.tariff_snapshots
            WHERE parking_session_id = @id
              AND statutory_discount_validation_id IS NOT NULL;
            """,
            parkingSessionId);

    public static async Task<int> PaymentBoundaryRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT
                (
                    SELECT COUNT(*)::int
                    FROM core.payment_attempts
                    WHERE parking_session_id = @id
                )
                +
                (
                    SELECT COUNT(*)::int
                    FROM core.payment_confirmations AS pc
                    INNER JOIN core.payment_attempts AS pa
                        ON pa.payment_attempt_id = pc.payment_attempt_id
                    WHERE pa.parking_session_id = @id
                )
                +
                (
                    SELECT COUNT(*)::int
                    FROM core.exit_authorizations
                    WHERE parking_session_id = @id
                )
                +
                (
                    SELECT COUNT(*)::int
                    FROM core.fiscal_issuance_references
                    WHERE parking_session_id = @id
                );
            """,
            parkingSessionId);

    public static async Task<Guid?> ValidationIdForDecisionAsync(Guid statutoryDiscountDecisionCommandId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT statutory_discount_validation_id
            FROM discounts.statutory_discount_decision_commands
            WHERE statutory_discount_decision_command_id = @id;
            """,
            connection);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? null : (Guid)value;
    }

    public static async Task<int> ReviewRowCountAsync(Guid decisionCommandId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id = @id;
            """,
            decisionCommandId);

    public static async Task<IReadOnlyList<string>> SensitiveReviewColumnNamesAsync()
    {
        const string sql = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'operator_console'
              AND table_name = 'statutory_discount_service_channel_reviews'
              AND (
                  column_name ILIKE '%base64%'
                  OR column_name ILIKE '%image%'
                  OR column_name ILIKE '%raw%'
                  OR column_name ILIKE '%full%id%'
                  OR column_name ILIKE '%identity_value%'
              )
            ORDER BY column_name;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static StatutoryDiscountDecisionV2EvidenceReference Evidence(string status) =>
        new(
            "SENIOR_CITIZEN_ID",
            "MANUAL_REFERENCE",
            "evidence-ref-001",
            "SC-****-1234",
            status,
            "verification-ref-001",
            DateTimeOffset.Parse("2026-07-21T01:00:00Z"));

    private static async Task<int> CountAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task SeedReviewerUserAsync(PaymentTestContext context, Guid reviewerUserId)
    {
        const string sql = """
            INSERT INTO identity.users (
                user_id,
                username,
                email,
                email_normalized,
                display_name,
                user_type,
                user_status,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @user_id,
                @username,
                @email,
                @email_normalized,
                @display_name,
                'SITE_OPERATOR'::identity.user_type_enum,
                'ACTIVE'::identity.user_status_enum,
                NOW() - INTERVAL '1 minute',
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            )
            ON CONFLICT (user_id) DO UPDATE
            SET
                username = EXCLUDED.username,
                email = EXCLUDED.email,
                email_normalized = EXCLUDED.email_normalized,
                display_name = EXCLUDED.display_name,
                user_type = EXCLUDED.user_type,
                user_status = EXCLUDED.user_status,
                updated_at = NOW(),
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = identity.users.row_version + 1;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        command.Parameters.AddWithValue("username", $"stat-disc-reviewer-{reviewerUserId:N}");
        command.Parameters.AddWithValue("email", $"stat-disc-reviewer-{reviewerUserId:N}@example.test");
        command.Parameters.AddWithValue("email_normalized", $"STAT-DISC-REVIEWER-{reviewerUserId:N}@EXAMPLE.TEST");
        command.Parameters.AddWithValue("display_name", $"Statutory discount reviewer {context.SiteCode}");
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = context.RequestedByUserId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SeededPolicyAuthority> SeedSupportedLocalOrdinancePolicyAsync(
        PaymentTestContext context,
        string entitlementType = "SENIOR_CITIZEN")
    {
        var jurisdictionId = StableGuid(context.ParkingSessionId, "jurisdiction");
        var assignmentId = StableGuid(context.ParkingSessionId, "assignment");
        var registryId = StableGuid(context.ParkingSessionId, $"registry-{entitlementType}");
        var policyVersionId = StableGuid(context.ParkingSessionId, $"policy-version-{entitlementType}");
        var policyCode = $"POLICY_{context.ParkingSessionId:N}"[..39].ToUpperInvariant();
        var sourceReference = PolicySourceReference(context);
        var jurisdictionCode = $"PH_{context.ParkingSessionId:N}"[..18].ToUpperInvariant();
        var displayName = $"Canonical Test City {context.SiteCode}";
        var evidenceType = entitlementType == "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID";

        const string sql = """
            INSERT INTO sites.jurisdictions (
                jurisdiction_id,
                jurisdiction_code,
                jurisdiction_type,
                display_name,
                province_name,
                region_name,
                psgc_code,
                jurisdiction_status,
                effective_from,
                source_reference
            )
            VALUES (
                @jurisdiction_id,
                @jurisdiction_code,
                'CITY'::sites.jurisdiction_type_enum,
                @display_name,
                'Canonical Test Province',
                'Canonical Test Region',
                NULL,
                'ACTIVE'::sites.jurisdiction_status_enum,
                NOW() - INTERVAL '1 day',
                @source_reference
            )
            ON CONFLICT (jurisdiction_id) DO NOTHING;

            INSERT INTO sites.site_jurisdiction_assignments (
                site_jurisdiction_assignment_id,
                site_id,
                jurisdiction_id,
                assignment_status,
                effective_from,
                source_reference,
                approval_reference
            )
            VALUES (
                @assignment_id,
                @site_id,
                @jurisdiction_id,
                'ACTIVE'::sites.site_jurisdiction_assignment_status_enum,
                NOW() - INTERVAL '1 day',
                @source_reference,
                'canonical-test-approval'
            )
            ON CONFLICT (site_jurisdiction_assignment_id) DO NOTHING;

            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_registry_id,
                policy_code,
                policy_name,
                entitlement_type,
                policy_status,
                verification_status,
                policy_level,
                policy_type,
                policy_resolution_basis,
                benefit_type,
                discount_base_scope,
                jurisdiction_id,
                jurisdiction_code,
                jurisdiction_name,
                beneficiary_residency_scope,
                full_fee_exempt,
                requires_evidence,
                required_evidence_type,
                legal_basis_reference,
                source_reference,
                reviewed_by,
                reviewed_at,
                approved_by,
                approved_at,
                effective_from,
                correlation_id
            )
            VALUES (
                @registry_id,
                @policy_code,
                @policy_name,
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                'ACTIVE'::discounts.discount_policy_status_enum,
                'VERIFIED_ACTIVE_OPERATIONAL'::discounts.policy_verification_status_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                @jurisdiction_id,
                @jurisdiction_code,
                @display_name,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                false,
                true,
                @evidence_type::discounts.discount_evidence_type_enum,
                'CANONICAL_TEST_LOCAL_ORDINANCE',
                @source_reference,
                'canonical-test-reviewer',
                NOW() - INTERVAL '1 hour',
                'canonical-test-approver',
                NOW() - INTERVAL '30 minutes',
                NOW() - INTERVAL '1 day',
                @correlation_id
            )
            ON CONFLICT (statutory_discount_policy_registry_id) DO NOTHING;

            INSERT INTO discounts.statutory_discount_policy_versions (
                statutory_discount_policy_version_id,
                statutory_discount_policy_registry_id,
                policy_code,
                policy_version,
                policy_version_label,
                entitlement_type,
                jurisdiction_id,
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
                correlation_id
            )
            VALUES (
                @policy_version_id,
                @registry_id,
                @policy_code,
                'v1',
                'Canonical test local ordinance v1',
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @jurisdiction_id,
                @jurisdiction_code,
                @display_name,
                'JURISDICTION'::discounts.policy_scope_type_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                'VERIFIED_ACTIVE_OPERATIONAL'::discounts.policy_verification_status_enum,
                'ACTIVE_FOR_TRANSACTION_USE'::discounts.statutory_policy_publication_status_enum,
                'PARTIALLY_VERIFIED'::discounts.policy_detail_verification_status_enum,
                'COVERED'::discounts.parking_service_applicability_status_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'SUPPORTED_BY_CURRENT_CALCULATION'::discounts.policy_effect_support_status_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                true,
                false,
                false,
                false,
                false,
                'CANONICAL_TEST_LOCAL_ORDINANCE',
                'CONTROLLED_OFFLINE_AUTHORITY'::discounts.policy_source_type_enum,
                @source_reference,
                'Canonical test statutory parking policy.',
                'Review under frozen canonical test policy authority.',
                false,
                NOW() - INTERVAL '1 day',
                100,
                @policy_semantic_hash,
                'canonical-test-reviewer',
                NOW() - INTERVAL '1 hour',
                'canonical-test-approver',
                NOW() - INTERVAL '30 minutes',
                @correlation_id
            )
            ON CONFLICT (statutory_discount_policy_version_id) DO NOTHING;

            INSERT INTO discounts.statutory_discount_policy_version_evidence_requirements (
                statutory_discount_policy_version_id,
                evidence_type,
                requirement_status,
                safe_requirement_label
            )
            VALUES (
                @policy_version_id,
                @evidence_type::discounts.discount_evidence_type_enum,
                'REQUIRED'::discounts.policy_requirement_status_enum,
                'Masked statutory ID reference'
            )
            ON CONFLICT (statutory_discount_policy_version_id, evidence_type) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = jurisdictionId;
        command.Parameters.Add("assignment_id", NpgsqlDbType.Uuid).Value = assignmentId;
        command.Parameters.Add("registry_id", NpgsqlDbType.Uuid).Value = registryId;
        command.Parameters.Add("policy_version_id", NpgsqlDbType.Uuid).Value = policyVersionId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = context.SiteId;
        command.Parameters.AddWithValue("jurisdiction_code", jurisdictionCode);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("policy_code", policyCode);
        command.Parameters.AddWithValue("policy_name", $"Canonical statutory parking policy {context.SiteCode}");
        command.Parameters.AddWithValue("entitlement_type", entitlementType);
        command.Parameters.AddWithValue("evidence_type", evidenceType);
        command.Parameters.AddWithValue("source_reference", sourceReference);
        command.Parameters.AddWithValue("policy_semantic_hash", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = context.CorrelationId;
        await command.ExecuteNonQueryAsync();

        return new SeededPolicyAuthority(
            policyVersionId,
            jurisdictionId,
            jurisdictionCode,
            displayName,
            policyCode,
            "v1",
            sourceReference,
            "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    }

    private static async Task SeedDecisionPolicyAuthorityAsync(
        PaymentTestContext context,
        StatutoryDiscountDecisionV2Record decision,
        SeededPolicyAuthority policy,
        string entitlementType)
    {
        var repository = new PostgresStatutoryDiscountParkingEligibilityRepository(ConnectionString);
        await repository.BindDecisionPolicyAuthorityAsync(
            decision.StatutoryDiscountDecisionCommandId,
            new StatutoryDiscountParkingAvailabilityResult(
                decision.RequestReference,
                context.ParkingSessionId,
                context.SiteId,
                context.SiteGroupId,
                policy.JurisdictionId,
                policy.JurisdictionCode,
                policy.JurisdictionDisplayName,
                StatutoryDiscountParkingAvailabilityStatuses.Available,
                StatutoryParkingBenefitAvailable: true,
                [entitlementType],
                entitlementType,
                SiteJurisdictionAssignmentId: null,
                policy.PolicyVersionId,
                policy.PolicyCode,
                policy.PolicyVersion,
                OrdinanceNumber: null,
                OrdinanceTitle: null,
                "Canonical test statutory parking policy",
                "VERIFIED_ACTIVE_OPERATIONAL",
                "ACTIVE_FOR_TRANSACTION_USE",
                "PARTIALLY_VERIFIED",
                EffectiveFrom: null,
                EffectiveTo: null,
                "NON_RESIDENT_ALLOWED",
                [new StatutoryDiscountPolicyEvidenceRequirement(
                    entitlementType == "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID",
                    "REQUIRED",
                    "Masked statutory ID reference",
                    SafeRequirementNotes: null)],
                "COVERED",
                "STATUTORY_DISCOUNT_VAT_EXEMPT",
                "SUPPORTED_BY_CURRENT_CALCULATION",
                OfficialSourceAvailable: false,
                OrdinanceTextAvailable: false,
                OrdinanceNumberAvailable: false,
                "CANONICAL_TEST_LOCAL_ORDINANCE",
                policy.SourceReference,
                SafeReasonCode: null,
                Retryable: false,
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment,
                DateTimeOffset.UtcNow,
                policy.PolicySemanticHash,
                context.CorrelationId),
            CancellationToken.None);
    }

    private static string PolicySourceReference(PaymentTestContext context) =>
        $"statutory-test:{context.ParkingSessionId:N}";

    private static Guid StableGuid(Guid seed, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed:N}:{purpose}"));
        var guidBytes = bytes[..16];
        return new Guid(guidBytes);
    }

    private sealed record SeededPolicyAuthority(
        Guid PolicyVersionId,
        Guid JurisdictionId,
        string JurisdictionCode,
        string JurisdictionDisplayName,
        string PolicyCode,
        string PolicyVersion,
        string SourceReference,
        string PolicySemanticHash);

}
