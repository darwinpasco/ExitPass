using System.Data;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

public sealed class PostgresStatutoryDiscountParkingEligibilityRepository
    : IStatutoryDiscountParkingEligibilityRepository
{
    private static readonly HashSet<string> VerifiedForTransactionUse = new(StringComparer.Ordinal)
    {
        "VERIFIED_OFFICIAL",
        "VERIFIED_ACTIVE_OPERATIONAL",
        "ACTIVE_APPROVED"
    };

    private readonly string _connectionString;

    public PostgresStatutoryDiscountParkingEligibilityRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<StatutoryDiscountParkingAvailabilityResult> ResolveAsync(
        StatutoryDiscountParkingAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var session = await ReadSessionAsync(connection, request.ParkingSessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Unavailable(
                request,
                StatutoryDiscountParkingAvailabilityStatuses.SiteNotResolved,
                "STATUTORY_DISCOUNT_PARKING_SESSION_NOT_FOUND",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        var assignments = await ReadJurisdictionAssignmentsAsync(connection, session, cancellationToken).ConfigureAwait(false);
        if (assignments.Count == 0)
        {
            return Unavailable(
                request,
                StatutoryDiscountParkingAvailabilityStatuses.SiteJurisdictionNotConfigured,
                "STATUTORY_DISCOUNT_SITE_JURISDICTION_NOT_CONFIGURED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ConfigureSiteJurisdiction,
                session);
        }

        if (assignments.Count > 1)
        {
            return Unavailable(
                request,
                StatutoryDiscountParkingAvailabilityStatuses.SiteJurisdictionAmbiguous,
                "STATUTORY_DISCOUNT_SITE_JURISDICTION_AMBIGUOUS",
                StatutoryDiscountParkingAvailabilityRemediationActions.ResolveJurisdictionAmbiguity,
                session,
                assignments[0]);
        }

        var assignment = assignments[0];
        var candidates = await ReadPolicyCandidatesAsync(connection, request, session, assignment, cancellationToken)
            .ConfigureAwait(false);
        var coveredEntitlements = ResolveCoveredEntitlementTypes(candidates, request, session.TransactionAt);

        if (request.RequestedEntitlementType is not null &&
            candidates.All(candidate => !string.Equals(candidate.EntitlementType, request.RequestedEntitlementType, StringComparison.Ordinal)))
        {
            var status = candidates.Count == 0
                ? StatutoryDiscountParkingAvailabilityStatuses.NoApplicableLocalOrdinance
                : StatutoryDiscountParkingAvailabilityStatuses.EntitlementNotCovered;
            return Unavailable(
                request,
                status,
                status == StatutoryDiscountParkingAvailabilityStatuses.EntitlementNotCovered
                    ? "STATUTORY_DISCOUNT_ENTITLEMENT_NOT_COVERED"
                    : "STATUTORY_DISCOUNT_NO_APPLICABLE_LOCAL_ORDINANCE",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment,
                session,
                assignment,
                coveredEntitlements: coveredEntitlements);
        }

        if (candidates.Count == 0)
        {
            return Unavailable(
                request,
                StatutoryDiscountParkingAvailabilityStatuses.NoApplicableLocalOrdinance,
                "STATUTORY_DISCOUNT_NO_APPLICABLE_LOCAL_ORDINANCE",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment,
                session,
                assignment,
                coveredEntitlements: coveredEntitlements);
        }

        var candidateReason = FirstBlockingReason(candidates, request, session.TransactionAt);
        var active = candidates
            .Where(candidate => IsTransactionActive(candidate, session.TransactionAt))
            .Where(candidate => request.RequestedEntitlementType is null ||
                string.Equals(candidate.EntitlementType, request.RequestedEntitlementType, StringComparison.Ordinal))
            .Where(candidate => request.BeneficiaryResidencySatisfied == true ||
                !string.Equals(candidate.BeneficiaryResidencyScope, "RESIDENT_ONLY", StringComparison.Ordinal))
            .Where(candidate => string.Equals(candidate.PolicyEffectSupportStatus, "SUPPORTED_BY_CURRENT_CALCULATION", StringComparison.Ordinal))
            .ToArray();

        if (active.Length == 0)
        {
            var blocked = candidateReason ?? BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.NoApplicableLocalOrdinance,
                "STATUTORY_DISCOUNT_NO_APPLICABLE_LOCAL_ORDINANCE",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
            var representative = candidates
                .OrderByDescending(PolicyScopeWeight)
                .ThenBy(candidate => candidate.PrecedenceRank)
                .FirstOrDefault();

            return FromPolicy(
                request,
                session,
                assignment,
                representative,
                blocked.Status,
                available: false,
                coveredEntitlements,
                blocked.SafeReasonCode,
                retryable: false,
                blocked.RemediationAction,
                evidenceRequirements: []);
        }

        var ordered = active
            .OrderByDescending(PolicyScopeWeight)
            .ThenBy(candidate => candidate.PrecedenceRank)
            .ThenByDescending(candidate => candidate.TransactionUseEffectiveFrom ?? DateTimeOffset.MinValue)
            .ThenBy(candidate => candidate.PolicyCode, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.PolicyVersion, StringComparer.Ordinal)
            .ToArray();
        var selected = ordered[0];
        var samePrecedence = ordered
            .Where(candidate => PolicyScopeWeight(candidate) == PolicyScopeWeight(selected) &&
                candidate.PrecedenceRank == selected.PrecedenceRank)
            .ToArray();
        if (samePrecedence.Length > 1)
        {
            return FromPolicy(
                request,
                session,
                assignment,
                selected,
                StatutoryDiscountParkingAvailabilityStatuses.PolicyConflict,
                available: false,
                coveredEntitlements,
                "STATUTORY_DISCOUNT_POLICY_CONFLICT",
                retryable: false,
                StatutoryDiscountParkingAvailabilityRemediationActions.ResolvePolicyConflict,
                evidenceRequirements: []);
        }

        var requirements = await ReadEvidenceRequirementsAsync(
                connection,
                selected.StatutoryDiscountPolicyVersionId,
                cancellationToken)
            .ConfigureAwait(false);

        return FromPolicy(
            request,
            session,
            assignment,
            selected,
            StatutoryDiscountParkingAvailabilityStatuses.Available,
            available: true,
            coveredEntitlements,
            safeReasonCode: null,
            retryable: false,
            remediationAction: StatutoryDiscountDecisionRecoveryActions.ReadCanonicalDecision,
            requirements);
    }

    public async Task BindDecisionPolicyAuthorityAsync(
        Guid statutoryDiscountDecisionCommandId,
        StatutoryDiscountParkingAvailabilityResult availability,
        CancellationToken cancellationToken)
    {
        if (statutoryDiscountDecisionCommandId == Guid.Empty)
        {
            throw new ArgumentException("Decision command id is required.", nameof(statutoryDiscountDecisionCommandId));
        }

        if (!availability.IsAvailable ||
            availability.PolicyVersionId is null ||
            availability.JurisdictionId is null ||
            string.IsNullOrWhiteSpace(availability.PolicyCode) ||
            string.IsNullOrWhiteSpace(availability.PolicyVersion) ||
            string.IsNullOrWhiteSpace(availability.VerificationStatus) ||
            string.IsNullOrWhiteSpace(availability.PublicationStatus) ||
            string.IsNullOrWhiteSpace(availability.DetailedRuleVerificationStatus) ||
            string.IsNullOrWhiteSpace(availability.ParkingServiceApplicability) ||
            string.IsNullOrWhiteSpace(availability.BenefitEffectClassification) ||
            string.IsNullOrWhiteSpace(availability.ResidencyRequirement) ||
            string.IsNullOrWhiteSpace(availability.SourceReference))
        {
            throw new InvalidOperationException("Available statutory parking policy authority is required before binding a decision.");
        }

        const string sql = """
            INSERT INTO discounts.statutory_discount_decision_policy_authorities (
                statutory_discount_decision_command_id,
                statutory_discount_policy_version_id,
                jurisdiction_id,
                jurisdiction_code,
                jurisdiction_display_name,
                policy_code,
                policy_version,
                entitlement_type,
                source_verification_status,
                transaction_publication_status,
                detailed_rule_verification_status,
                parking_service_applicability,
                benefit_type,
                beneficiary_residency_scope,
                official_source_available,
                ordinance_text_available,
                ordinance_number_available,
                ordinance_number,
                ordinance_title,
                legal_basis_reference,
                source_reference,
                transaction_use_effective_from,
                transaction_use_effective_to,
                resolved_at,
                policy_authority_semantic_hash,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @statutory_discount_decision_command_id,
                @statutory_discount_policy_version_id,
                @jurisdiction_id,
                @jurisdiction_code,
                @jurisdiction_display_name,
                @policy_code,
                @policy_version,
                @entitlement_type,
                CAST(@source_verification_status AS discounts.policy_verification_status_enum),
                CAST(@transaction_publication_status AS discounts.statutory_policy_publication_status_enum),
                CAST(@detailed_rule_verification_status AS discounts.policy_detail_verification_status_enum),
                CAST(@parking_service_applicability AS discounts.parking_service_applicability_status_enum),
                CAST(@benefit_type AS discounts.parking_benefit_type_enum),
                CAST(@beneficiary_residency_scope AS discounts.beneficiary_residency_scope_enum),
                @official_source_available,
                @ordinance_text_available,
                @ordinance_number_available,
                @ordinance_number,
                @ordinance_title,
                @legal_basis_reference,
                @source_reference,
                @transaction_use_effective_from,
                @transaction_use_effective_to,
                @resolved_at,
                @policy_authority_semantic_hash,
                @correlation_id,
                now(),
                now()
            )
            ON CONFLICT (statutory_discount_decision_command_id) DO NOTHING;

            UPDATE operator_console.statutory_discount_service_channel_reviews
               SET statutory_discount_policy_version_id = COALESCE(statutory_discount_policy_version_id, @statutory_discount_policy_version_id),
                   statutory_discount_decision_policy_authority_id = COALESCE(statutory_discount_decision_policy_authority_id, @statutory_discount_decision_command_id),
                   updated_at = now()
             WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        command.Parameters.Add("statutory_discount_policy_version_id", NpgsqlDbType.Uuid).Value = availability.PolicyVersionId.Value;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = availability.JurisdictionId.Value;
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Varchar).Value = availability.JurisdictionCode!;
        command.Parameters.Add("jurisdiction_display_name", NpgsqlDbType.Varchar).Value = availability.JurisdictionDisplayName!;
        command.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = availability.PolicyCode!;
        command.Parameters.Add("policy_version", NpgsqlDbType.Varchar).Value = availability.PolicyVersion!;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Varchar).Value = availability.RequestedEntitlementType!;
        command.Parameters.Add("source_verification_status", NpgsqlDbType.Varchar).Value = availability.VerificationStatus!;
        command.Parameters.Add("transaction_publication_status", NpgsqlDbType.Varchar).Value = availability.PublicationStatus!;
        command.Parameters.Add("detailed_rule_verification_status", NpgsqlDbType.Varchar).Value = availability.DetailedRuleVerificationStatus!;
        command.Parameters.Add("parking_service_applicability", NpgsqlDbType.Varchar).Value = availability.ParkingServiceApplicability!;
        command.Parameters.Add("benefit_type", NpgsqlDbType.Varchar).Value = availability.BenefitEffectClassification!;
        command.Parameters.Add("beneficiary_residency_scope", NpgsqlDbType.Varchar).Value = availability.ResidencyRequirement!;
        AddNullable(command, "official_source_available", NpgsqlDbType.Boolean, availability.OfficialSourceAvailable);
        AddNullable(command, "ordinance_text_available", NpgsqlDbType.Boolean, availability.OrdinanceTextAvailable);
        AddNullable(command, "ordinance_number_available", NpgsqlDbType.Boolean, availability.OrdinanceNumberAvailable);
        AddNullable(command, "ordinance_number", NpgsqlDbType.Varchar, availability.OrdinanceNumber);
        AddNullable(command, "ordinance_title", NpgsqlDbType.Varchar, availability.OrdinanceTitle);
        AddNullable(command, "legal_basis_reference", NpgsqlDbType.Varchar, availability.LegalBasisReference);
        command.Parameters.Add("source_reference", NpgsqlDbType.Text).Value = availability.SourceReference!;
        AddNullable(command, "transaction_use_effective_from", NpgsqlDbType.TimestampTz, availability.EffectiveFrom);
        AddNullable(command, "transaction_use_effective_to", NpgsqlDbType.TimestampTz, availability.EffectiveTo);
        command.Parameters.Add("resolved_at", NpgsqlDbType.TimestampTz).Value = availability.TransactionAt ?? DateTimeOffset.UtcNow;
        command.Parameters.Add("policy_authority_semantic_hash", NpgsqlDbType.Varchar).Value =
            StatutoryDiscountDecisionPolicyAuthorityHash.Compute(availability);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = availability.CorrelationId;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionPolicyAuthority?> GetDecisionPolicyAuthorityAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_policy_authorities
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountDecisionCommandId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadPolicyAuthority(reader)
            : null;
    }

    private static async Task<SessionRow?> ReadSessionAsync(
        NpgsqlConnection connection,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                parking_session_id,
                site_id,
                site_group_id,
                COALESCE(entry_at, created_at) AS transaction_at
            FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new SessionRow(
                reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                reader.GetGuid(reader.GetOrdinal("site_id")),
                reader.GetGuid(reader.GetOrdinal("site_group_id")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("transaction_at")))
            : null;
    }

    private static async Task<IReadOnlyList<AssignmentRow>> ReadJurisdictionAssignmentsAsync(
        NpgsqlConnection connection,
        SessionRow session,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                a.site_jurisdiction_assignment_id,
                a.site_id,
                a.jurisdiction_id,
                j.jurisdiction_code,
                j.display_name AS jurisdiction_display_name
            FROM sites.site_jurisdiction_assignments AS a
            JOIN sites.jurisdictions AS j
              ON j.jurisdiction_id = a.jurisdiction_id
            WHERE a.site_id = @site_id
              AND a.assignment_status = 'ACTIVE'
              AND a.effective_from <= @transaction_at
              AND (a.effective_to IS NULL OR a.effective_to > @transaction_at)
              AND j.jurisdiction_status = 'ACTIVE'
              AND j.jurisdiction_type IN ('CITY', 'MUNICIPALITY')
            ORDER BY a.effective_from DESC, a.site_jurisdiction_assignment_id
            LIMIT 2;
            """;

        var rows = new List<AssignmentRow>();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = session.SiteId;
        command.Parameters.Add("transaction_at", NpgsqlDbType.TimestampTz).Value = session.TransactionAt;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new AssignmentRow(
                reader.GetGuid(reader.GetOrdinal("site_jurisdiction_assignment_id")),
                reader.GetGuid(reader.GetOrdinal("site_id")),
                reader.GetGuid(reader.GetOrdinal("jurisdiction_id")),
                reader.GetString(reader.GetOrdinal("jurisdiction_code")),
                reader.GetString(reader.GetOrdinal("jurisdiction_display_name"))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<PolicyCandidateRow>> ReadPolicyCandidatesAsync(
        NpgsqlConnection connection,
        StatutoryDiscountParkingAvailabilityRequest request,
        SessionRow session,
        AssignmentRow assignment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                statutory_discount_policy_version_id,
                policy_code,
                policy_version,
                policy_version_label,
                entitlement_type::text,
                jurisdiction_id,
                jurisdiction_code,
                jurisdiction_display_name,
                policy_scope_type::text,
                site_group_id,
                site_id,
                source_verification_status::text,
                transaction_publication_status::text,
                detailed_rule_verification_status::text,
                parking_service_applicability::text,
                benefit_type::text,
                policy_effect_support_status::text,
                beneficiary_residency_scope::text,
                official_source_available,
                ordinance_text_available,
                ordinance_number_available,
                ordinance_number,
                ordinance_title,
                legal_basis_reference,
                source_reference,
                transaction_use_effective_from,
                transaction_use_effective_to,
                suspension_starts_at,
                suspension_ends_at,
                withdrawn_at,
                retired_at,
                superseded_by_policy_version_id,
                precedence_rank,
                policy_semantic_hash
            FROM discounts.statutory_discount_policy_versions
            WHERE jurisdiction_id = @jurisdiction_id
              AND (@requested_entitlement_type IS NULL OR entitlement_type::text = @requested_entitlement_type OR @include_other_entitlements)
              AND (
                  (policy_scope_type = 'JURISDICTION' AND site_group_id IS NULL AND site_id IS NULL)
                  OR (policy_scope_type = 'SITE_GROUP' AND site_group_id = @site_group_id AND site_id IS NULL)
                  OR (policy_scope_type = 'SITE' AND site_id = @site_id)
              )
            ORDER BY
                CASE policy_scope_type
                    WHEN 'SITE' THEN 3
                    WHEN 'SITE_GROUP' THEN 2
                    ELSE 1
                END DESC,
                precedence_rank ASC,
                transaction_use_effective_from DESC NULLS LAST,
                policy_code ASC,
                policy_version ASC;
            """;

        var rows = new List<PolicyCandidateRow>();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = assignment.JurisdictionId;
        AddNullable(command, "requested_entitlement_type", NpgsqlDbType.Varchar, request.RequestedEntitlementType);
        command.Parameters.Add("include_other_entitlements", NpgsqlDbType.Boolean).Value = true;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = session.SiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = session.SiteId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadPolicyCandidate(reader));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<StatutoryDiscountPolicyEvidenceRequirement>> ReadEvidenceRequirementsAsync(
        NpgsqlConnection connection,
        Guid policyVersionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                evidence_type::text,
                requirement_status::text,
                safe_requirement_label,
                safe_requirement_notes
            FROM discounts.statutory_discount_policy_version_evidence_requirements
            WHERE statutory_discount_policy_version_id = @statutory_discount_policy_version_id
              AND requirement_status IN ('REQUIRED', 'OPTIONAL')
            ORDER BY evidence_type::text;
            """;

        var rows = new List<StatutoryDiscountPolicyEvidenceRequirement>();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_policy_version_id", NpgsqlDbType.Uuid).Value = policyVersionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new StatutoryDiscountPolicyEvidenceRequirement(
                reader.GetString(reader.GetOrdinal("evidence_type")),
                reader.GetString(reader.GetOrdinal("requirement_status")),
                reader.GetString(reader.GetOrdinal("safe_requirement_label")),
                GetNullableString(reader, "safe_requirement_notes")));
        }

        return rows;
    }

    private static IReadOnlyList<string> ResolveCoveredEntitlementTypes(
        IReadOnlyList<PolicyCandidateRow> candidates,
        StatutoryDiscountParkingAvailabilityRequest request,
        DateTimeOffset transactionAt) =>
        candidates
            .Where(candidate => IsTransactionActive(candidate, transactionAt))
            .Where(candidate => request.BeneficiaryResidencySatisfied == true ||
                !string.Equals(candidate.BeneficiaryResidencyScope, "RESIDENT_ONLY", StringComparison.Ordinal))
            .Where(candidate => string.Equals(candidate.PolicyEffectSupportStatus, "SUPPORTED_BY_CURRENT_CALCULATION", StringComparison.Ordinal))
            .Select(candidate => candidate.EntitlementType)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static BlockingDecision? FirstBlockingReason(
        IReadOnlyList<PolicyCandidateRow> candidates,
        StatutoryDiscountParkingAvailabilityRequest request,
        DateTimeOffset transactionAt)
    {
        var requested = candidates
            .Where(candidate => request.RequestedEntitlementType is null ||
                string.Equals(candidate.EntitlementType, request.RequestedEntitlementType, StringComparison.Ordinal))
            .ToArray();
        if (requested.Length == 0)
        {
            return null;
        }

        if (requested.Any(candidate => !VerifiedForTransactionUse.Contains(candidate.SourceVerificationStatus)))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.PolicyUnverified,
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        if (requested.Any(candidate => !string.Equals(candidate.TransactionPublicationStatus, "ACTIVE_FOR_TRANSACTION_USE", StringComparison.Ordinal)))
        {
            var status = requested.Select(candidate => candidate.TransactionPublicationStatus).FirstOrDefault();
            return status switch
            {
                "SUSPENDED" => BlockingReason(StatutoryDiscountParkingAvailabilityStatuses.PolicySuspended, "STATUTORY_DISCOUNT_POLICY_SUSPENDED", StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment),
                "WITHDRAWN" => BlockingReason(StatutoryDiscountParkingAvailabilityStatuses.PolicyWithdrawn, "STATUTORY_DISCOUNT_POLICY_WITHDRAWN", StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment),
                "SUPERSEDED" => BlockingReason(StatutoryDiscountParkingAvailabilityStatuses.PolicySupersededWithoutSuccessor, "STATUTORY_DISCOUNT_POLICY_SUPERSEDED_WITHOUT_SUCCESSOR", StatutoryDiscountParkingAvailabilityRemediationActions.PublishApplicablePolicy),
                _ => BlockingReason(StatutoryDiscountParkingAvailabilityStatuses.PolicyNotPublished, "STATUTORY_DISCOUNT_POLICY_NOT_PUBLISHED", StatutoryDiscountParkingAvailabilityRemediationActions.PublishApplicablePolicy)
            };
        }

        if (requested.Any(candidate => !string.Equals(candidate.ParkingServiceApplicability, "COVERED", StringComparison.Ordinal)))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.ParkingServiceNotCovered,
                "STATUTORY_DISCOUNT_PARKING_SERVICE_NOT_COVERED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        if (requested.Any(candidate => candidate.TransactionUseEffectiveFrom.HasValue && candidate.TransactionUseEffectiveFrom.Value > transactionAt))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.PolicyNotYetEffective,
                "STATUTORY_DISCOUNT_POLICY_NOT_YET_EFFECTIVE",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        if (requested.Any(candidate => candidate.TransactionUseEffectiveTo.HasValue && candidate.TransactionUseEffectiveTo.Value <= transactionAt))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.PolicyExpired,
                "STATUTORY_DISCOUNT_POLICY_EXPIRED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        if (requested.Any(candidate => candidate.SuspensionStartsAt.HasValue &&
            candidate.SuspensionStartsAt.Value <= transactionAt &&
            (!candidate.SuspensionEndsAt.HasValue || candidate.SuspensionEndsAt.Value > transactionAt)))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.PolicySuspended,
                "STATUTORY_DISCOUNT_POLICY_SUSPENDED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        if (requested.Any(candidate => candidate.SupersededByPolicyVersionId.HasValue))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.PolicySupersededWithoutSuccessor,
                "STATUTORY_DISCOUNT_POLICY_SUPERSEDED_WITHOUT_SUCCESSOR",
                StatutoryDiscountParkingAvailabilityRemediationActions.PublishApplicablePolicy);
        }

        if (requested.Any(candidate => string.Equals(candidate.BeneficiaryResidencyScope, "RESIDENT_ONLY", StringComparison.Ordinal)) &&
            request.BeneficiaryResidencySatisfied != true)
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.ResidencyRequirementNotSatisfied,
                "STATUTORY_DISCOUNT_RESIDENCY_REQUIREMENT_NOT_SATISFIED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ProvideResidencyEvidence);
        }

        if (requested.Any(candidate => string.Equals(candidate.PolicyEffectSupportStatus, "UNRESOLVED", StringComparison.Ordinal)))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.RequiredPolicyFactsIncomplete,
                "STATUTORY_DISCOUNT_REQUIRED_POLICY_FACTS_INCOMPLETE",
                StatutoryDiscountParkingAvailabilityRemediationActions.PublishApplicablePolicy);
        }

        if (requested.Any(candidate => !string.Equals(candidate.PolicyEffectSupportStatus, "SUPPORTED_BY_CURRENT_CALCULATION", StringComparison.Ordinal)))
        {
            return BlockingReason(
                StatutoryDiscountParkingAvailabilityStatuses.BenefitEffectNotSupported,
                "STATUTORY_DISCOUNT_BENEFIT_EFFECT_NOT_SUPPORTED",
                StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment);
        }

        return null;
    }

    private static bool IsTransactionActive(PolicyCandidateRow candidate, DateTimeOffset transactionAt) =>
        VerifiedForTransactionUse.Contains(candidate.SourceVerificationStatus) &&
        string.Equals(candidate.TransactionPublicationStatus, "ACTIVE_FOR_TRANSACTION_USE", StringComparison.Ordinal) &&
        string.Equals(candidate.ParkingServiceApplicability, "COVERED", StringComparison.Ordinal) &&
        (!candidate.TransactionUseEffectiveFrom.HasValue || candidate.TransactionUseEffectiveFrom.Value <= transactionAt) &&
        (!candidate.TransactionUseEffectiveTo.HasValue || candidate.TransactionUseEffectiveTo.Value > transactionAt) &&
        (!candidate.SuspensionStartsAt.HasValue || candidate.SuspensionStartsAt.Value > transactionAt ||
            (candidate.SuspensionEndsAt.HasValue && candidate.SuspensionEndsAt.Value <= transactionAt)) &&
        candidate.WithdrawnAt is null &&
        candidate.RetiredAt is null &&
        candidate.SupersededByPolicyVersionId is null;

    private static StatutoryDiscountParkingAvailabilityResult FromPolicy(
        StatutoryDiscountParkingAvailabilityRequest request,
        SessionRow session,
        AssignmentRow assignment,
        PolicyCandidateRow? policy,
        string status,
        bool available,
        IReadOnlyList<string> coveredEntitlements,
        string? safeReasonCode,
        bool retryable,
        string remediationAction,
        IReadOnlyList<StatutoryDiscountPolicyEvidenceRequirement> evidenceRequirements) =>
        new(
            request.RequestReference,
            request.ParkingSessionId,
            session.SiteId,
            session.SiteGroupId,
            assignment.JurisdictionId,
            assignment.JurisdictionCode,
            assignment.JurisdictionDisplayName,
            status,
            available,
            coveredEntitlements,
            request.RequestedEntitlementType ?? policy?.EntitlementType,
            assignment.SiteJurisdictionAssignmentId,
            policy?.StatutoryDiscountPolicyVersionId,
            policy?.PolicyCode,
            policy?.PolicyVersion,
            policy?.OrdinanceNumber,
            policy?.OrdinanceTitle,
            policy?.PolicyVersionLabel,
            policy?.SourceVerificationStatus,
            policy?.TransactionPublicationStatus,
            policy?.DetailedRuleVerificationStatus,
            policy?.TransactionUseEffectiveFrom,
            policy?.TransactionUseEffectiveTo,
            policy?.BeneficiaryResidencyScope,
            evidenceRequirements,
            policy?.ParkingServiceApplicability,
            policy?.BenefitType,
            policy?.PolicyEffectSupportStatus,
            policy?.OfficialSourceAvailable,
            policy?.OrdinanceTextAvailable,
            policy?.OrdinanceNumberAvailable,
            policy?.LegalBasisReference,
            policy?.SourceReference,
            safeReasonCode,
            retryable,
            remediationAction,
            session.TransactionAt,
            policy?.PolicySemanticHash,
            request.CorrelationId);

    private static StatutoryDiscountParkingAvailabilityResult Unavailable(
        StatutoryDiscountParkingAvailabilityRequest request,
        string status,
        string safeReasonCode,
        string remediationAction,
        SessionRow? session = null,
        AssignmentRow? assignment = null,
        IReadOnlyList<string>? coveredEntitlements = null) =>
        new(
            request.RequestReference,
            request.ParkingSessionId,
            session?.SiteId,
            session?.SiteGroupId,
            assignment?.JurisdictionId,
            assignment?.JurisdictionCode,
            assignment?.JurisdictionDisplayName,
            status,
            false,
            coveredEntitlements ?? [],
            request.RequestedEntitlementType,
            assignment?.SiteJurisdictionAssignmentId,
            PolicyVersionId: null,
            PolicyCode: null,
            PolicyVersion: null,
            OrdinanceNumber: null,
            OrdinanceTitle: null,
            PolicyDisplayName: null,
            VerificationStatus: null,
            PublicationStatus: null,
            DetailedRuleVerificationStatus: null,
            EffectiveFrom: null,
            EffectiveTo: null,
            ResidencyRequirement: null,
            RequiredEvidenceTypes: [],
            ParkingServiceApplicability: null,
            BenefitEffectClassification: null,
            BenefitEffectSupportStatus: null,
            OfficialSourceAvailable: null,
            OrdinanceTextAvailable: null,
            OrdinanceNumberAvailable: null,
            LegalBasisReference: null,
            SourceReference: null,
            safeReasonCode,
            Retryable: false,
            remediationAction,
            session?.TransactionAt,
            PolicySemanticHash: null,
            request.CorrelationId);

    private static BlockingDecision BlockingReason(string status, string safeReasonCode, string remediationAction) =>
        new(status, safeReasonCode, remediationAction);

    private static int PolicyScopeWeight(PolicyCandidateRow row) =>
        row.PolicyScopeType switch
        {
            "SITE" => 3,
            "SITE_GROUP" => 2,
            _ => 1
        };

    private static PolicyCandidateRow ReadPolicyCandidate(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_policy_version_id")),
            reader.GetString(reader.GetOrdinal("policy_code")),
            reader.GetString(reader.GetOrdinal("policy_version")),
            GetNullableString(reader, "policy_version_label"),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetGuid(reader.GetOrdinal("jurisdiction_id")),
            reader.GetString(reader.GetOrdinal("jurisdiction_code")),
            reader.GetString(reader.GetOrdinal("jurisdiction_display_name")),
            reader.GetString(reader.GetOrdinal("policy_scope_type")),
            GetNullableGuid(reader, "site_group_id"),
            GetNullableGuid(reader, "site_id"),
            reader.GetString(reader.GetOrdinal("source_verification_status")),
            reader.GetString(reader.GetOrdinal("transaction_publication_status")),
            reader.GetString(reader.GetOrdinal("detailed_rule_verification_status")),
            reader.GetString(reader.GetOrdinal("parking_service_applicability")),
            reader.GetString(reader.GetOrdinal("benefit_type")),
            reader.GetString(reader.GetOrdinal("policy_effect_support_status")),
            reader.GetString(reader.GetOrdinal("beneficiary_residency_scope")),
            GetNullableBool(reader, "official_source_available"),
            GetNullableBool(reader, "ordinance_text_available"),
            GetNullableBool(reader, "ordinance_number_available"),
            GetNullableString(reader, "ordinance_number"),
            GetNullableString(reader, "ordinance_title"),
            GetNullableString(reader, "legal_basis_reference"),
            reader.GetString(reader.GetOrdinal("source_reference")),
            GetNullableDateTimeOffset(reader, "transaction_use_effective_from"),
            GetNullableDateTimeOffset(reader, "transaction_use_effective_to"),
            GetNullableDateTimeOffset(reader, "suspension_starts_at"),
            GetNullableDateTimeOffset(reader, "suspension_ends_at"),
            GetNullableDateTimeOffset(reader, "withdrawn_at"),
            GetNullableDateTimeOffset(reader, "retired_at"),
            GetNullableGuid(reader, "superseded_by_policy_version_id"),
            reader.GetInt32(reader.GetOrdinal("precedence_rank")),
            reader.GetString(reader.GetOrdinal("policy_semantic_hash")));

    private static StatutoryDiscountDecisionPolicyAuthority ReadPolicyAuthority(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("statutory_discount_policy_version_id")),
            reader.GetGuid(reader.GetOrdinal("jurisdiction_id")),
            reader.GetString(reader.GetOrdinal("jurisdiction_code")),
            reader.GetString(reader.GetOrdinal("jurisdiction_display_name")),
            reader.GetString(reader.GetOrdinal("policy_code")),
            reader.GetString(reader.GetOrdinal("policy_version")),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetString(reader.GetOrdinal("source_verification_status")),
            reader.GetString(reader.GetOrdinal("transaction_publication_status")),
            reader.GetString(reader.GetOrdinal("detailed_rule_verification_status")),
            reader.GetString(reader.GetOrdinal("parking_service_applicability")),
            reader.GetString(reader.GetOrdinal("benefit_type")),
            reader.GetString(reader.GetOrdinal("beneficiary_residency_scope")),
            GetNullableBool(reader, "official_source_available"),
            GetNullableBool(reader, "ordinance_text_available"),
            GetNullableBool(reader, "ordinance_number_available"),
            GetNullableString(reader, "ordinance_number"),
            GetNullableString(reader, "ordinance_title"),
            GetNullableString(reader, "legal_basis_reference"),
            reader.GetString(reader.GetOrdinal("source_reference")),
            GetNullableDateTimeOffset(reader, "transaction_use_effective_from"),
            GetNullableDateTimeOffset(reader, "transaction_use_effective_to"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("resolved_at")),
            reader.GetString(reader.GetOrdinal("policy_authority_semantic_hash")),
            GetNullableGuid(reader, "correlation_id") ?? Guid.Empty);

    private static string? GetNullableString(NpgsqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetGuid(reader.GetOrdinal(name));

    private static bool? GetNullableBool(NpgsqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetBoolean(reader.GetOrdinal(name));

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(name));

    private static void AddNullable<T>(NpgsqlCommand command, string name, NpgsqlDbType type, T? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private sealed record SessionRow(
        Guid ParkingSessionId,
        Guid SiteId,
        Guid SiteGroupId,
        DateTimeOffset TransactionAt);

    private sealed record AssignmentRow(
        Guid SiteJurisdictionAssignmentId,
        Guid SiteId,
        Guid JurisdictionId,
        string JurisdictionCode,
        string JurisdictionDisplayName);

    private sealed record BlockingDecision(string Status, string SafeReasonCode, string RemediationAction);

    private sealed record PolicyCandidateRow(
        Guid StatutoryDiscountPolicyVersionId,
        string PolicyCode,
        string PolicyVersion,
        string? PolicyVersionLabel,
        string EntitlementType,
        Guid JurisdictionId,
        string JurisdictionCode,
        string JurisdictionDisplayName,
        string PolicyScopeType,
        Guid? SiteGroupId,
        Guid? SiteId,
        string SourceVerificationStatus,
        string TransactionPublicationStatus,
        string DetailedRuleVerificationStatus,
        string ParkingServiceApplicability,
        string BenefitType,
        string PolicyEffectSupportStatus,
        string BeneficiaryResidencyScope,
        bool? OfficialSourceAvailable,
        bool? OrdinanceTextAvailable,
        bool? OrdinanceNumberAvailable,
        string? OrdinanceNumber,
        string? OrdinanceTitle,
        string? LegalBasisReference,
        string SourceReference,
        DateTimeOffset? TransactionUseEffectiveFrom,
        DateTimeOffset? TransactionUseEffectiveTo,
        DateTimeOffset? SuspensionStartsAt,
        DateTimeOffset? SuspensionEndsAt,
        DateTimeOffset? WithdrawnAt,
        DateTimeOffset? RetiredAt,
        Guid? SupersededByPolicyVersionId,
        int PrecedenceRank,
        string PolicySemanticHash);
}
