using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;
using System.Security.Cryptography;
using System.Text;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class PostgresManagementConfigurationRepository(string connectionString) : IManagementConfigurationRepository
{
    public async Task<IReadOnlyList<ManagementSiteGroup>> ListSiteGroupsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT site_group_id, site_group_code, site_group_name, timezone_name,
                   default_currency_code, site_group_status::text
            FROM sites.site_groups
            WHERE site_group_status IN ('ACTIVE', 'DRAFT')
            ORDER BY site_group_name;
            """;
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<ManagementSiteGroup>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return rows;
    }

    public async Task<IReadOnlyList<ManagementSite>> ListSitesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT site_id, site_group_id, site_code, site_name, site_type::text, timezone_name,
                   address_line1, address_line2, city, province, country_code,
                   local_government_unit_id, site_status::text, public_lookup_enabled,
                   payment_enabled, effective_from, effective_to, row_version
            FROM sites.sites ORDER BY site_name;
            """;
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<ManagementSite>();
        while (await reader.ReadAsync(ct)) rows.Add(ReadSite(reader));
        return rows;
    }

    public async Task<string?> GetSiteTimezoneAsync(Guid siteId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT timezone_name FROM sites.sites WHERE site_id=@id", connection);
        command.Parameters.AddWithValue("id", siteId);
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    public async Task<ManagementSite> SaveSiteAsync(ManagementSite site, Guid actor, CancellationToken ct)
    {
        if (site.SiteGroupId == Guid.Empty || string.IsNullOrWhiteSpace(site.SiteCode) || string.IsNullOrWhiteSpace(site.SiteName))
            throw new ManagementConfigurationException("SITE_CONFIGURATION_INVALID");
        var id = site.SiteId == Guid.Empty ? Guid.NewGuid() : site.SiteId;
        const string sql = """
            INSERT INTO sites.sites (
              site_id, site_group_id, site_code, site_name, site_type, timezone_name,
              address_line1, address_line2, city, province, country_code, local_government_unit_id,
              site_status, public_lookup_enabled, payment_enabled, effective_from, effective_to,
              created_by_user_id, updated_by_user_id)
            VALUES (@id,@group,@code,@name,@type::sites.site_type_enum,@timezone,@address1,@address2,@city,@province,
                    @country,@lgu,@status::sites.site_status_enum,@public,@payment,@from,@to,@actor,@actor)
            ON CONFLICT (site_id) DO UPDATE SET
              site_group_id=EXCLUDED.site_group_id, site_name=EXCLUDED.site_name,
              timezone_name=EXCLUDED.timezone_name, address_line1=EXCLUDED.address_line1,
              address_line2=EXCLUDED.address_line2, city=EXCLUDED.city, province=EXCLUDED.province,
              country_code=EXCLUDED.country_code, local_government_unit_id=EXCLUDED.local_government_unit_id,
              site_status=EXCLUDED.site_status, public_lookup_enabled=EXCLUDED.public_lookup_enabled,
              payment_enabled=EXCLUDED.payment_enabled, effective_from=EXCLUDED.effective_from,
              effective_to=EXCLUDED.effective_to, updated_at=now(), updated_by_user_id=@actor,
              row_version=sites.sites.row_version+1
            WHERE sites.sites.row_version=@version
            RETURNING site_id, site_group_id, site_code, site_name, site_type::text, timezone_name,
              address_line1,address_line2,city,province,country_code,local_government_unit_id,
              site_status::text,public_lookup_enabled,payment_enabled,effective_from,effective_to,row_version;
            """;
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, "id", id); Add(command, "group", site.SiteGroupId); Add(command, "code", site.SiteCode.Trim().ToUpperInvariant());
        Add(command, "name", site.SiteName.Trim()); Add(command, "type", site.SiteType); Add(command, "timezone", site.TimezoneName);
        AddNullable(command, "address1", site.AddressLine1, NpgsqlDbType.Text); AddNullable(command, "address2", site.AddressLine2, NpgsqlDbType.Text);
        AddNullable(command, "city", site.City, NpgsqlDbType.Text); AddNullable(command, "province", site.Province, NpgsqlDbType.Text); Add(command, "country", site.CountryCode.ToUpperInvariant());
        AddNullable(command, "lgu", site.LocalGovernmentUnitId, NpgsqlDbType.Uuid); Add(command, "status", site.Status); Add(command, "public", site.PublicLookupEnabled);
        Add(command, "payment", site.PaymentEnabled); Add(command, "from", site.EffectiveFrom); AddNullable(command, "to", site.EffectiveTo, NpgsqlDbType.TimestampTz);
        Add(command, "actor", actor); Add(command, "version", site.RowVersion);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ManagementConfigurationException("SITE_CONFIGURATION_CONFLICT");
        return ReadSite(reader);
    }

    public async Task<IReadOnlyList<ManagementJurisdiction>> ListJurisdictionsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT jurisdiction_id,jurisdiction_code,jurisdiction_type::text,display_name,psgc_code,
                   province_name,region_name,country_code,jurisdiction_status::text,effective_from,effective_to,row_version
            FROM sites.jurisdictions ORDER BY display_name;
            """;
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<ManagementJurisdiction>(); while (await reader.ReadAsync(ct)) rows.Add(ReadJurisdiction(reader)); return rows;
    }

    public async Task<ManagementJurisdiction> SaveJurisdictionAsync(ManagementJurisdiction item, Guid actor, CancellationToken ct)
    {
        var id = item.JurisdictionId == Guid.Empty ? Guid.NewGuid() : item.JurisdictionId;
        const string sql = """
            INSERT INTO sites.jurisdictions(jurisdiction_id,jurisdiction_code,jurisdiction_type,display_name,psgc_code,
              province_name,region_name,country_code,jurisdiction_status,effective_from,effective_to,created_by_user_id,updated_by_user_id)
            VALUES(@id,@code,@type::sites.jurisdiction_type_enum,@name,@psgc,@province,@region,@country,
              @status::sites.jurisdiction_status_enum,@from,@to,@actor,@actor)
            ON CONFLICT(jurisdiction_id) DO UPDATE SET display_name=EXCLUDED.display_name,psgc_code=EXCLUDED.psgc_code,
              province_name=EXCLUDED.province_name,region_name=EXCLUDED.region_name,country_code=EXCLUDED.country_code,
              jurisdiction_status=EXCLUDED.jurisdiction_status,effective_from=EXCLUDED.effective_from,effective_to=EXCLUDED.effective_to,
              updated_at=now(),updated_by_user_id=@actor,row_version=sites.jurisdictions.row_version+1
            WHERE sites.jurisdictions.row_version=@version
            RETURNING jurisdiction_id,jurisdiction_code,jurisdiction_type::text,display_name,psgc_code,
              province_name,region_name,country_code,jurisdiction_status::text,effective_from,effective_to,row_version;
            """;
        await using var connection = await OpenAsync(ct); await using var command = new NpgsqlCommand(sql, connection);
        Add(command, "id", id); Add(command, "code", item.JurisdictionCode.Trim().ToUpperInvariant()); Add(command, "type", item.JurisdictionType);
        Add(command, "name", item.DisplayName.Trim()); AddNullable(command, "psgc", item.PsgcCode, NpgsqlDbType.Text); AddNullable(command, "province", item.ProvinceName, NpgsqlDbType.Text);
        AddNullable(command, "region", item.RegionName, NpgsqlDbType.Text); Add(command, "country", item.CountryCode.ToUpperInvariant()); Add(command, "status", item.Status);
        AddNullable(command, "from", item.EffectiveFrom, NpgsqlDbType.TimestampTz); AddNullable(command, "to", item.EffectiveTo, NpgsqlDbType.TimestampTz); Add(command, "actor", actor); Add(command, "version", item.RowVersion);
        await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new ManagementConfigurationException("JURISDICTION_CONFIGURATION_CONFLICT"); return ReadJurisdiction(reader);
    }

    public async Task<IReadOnlyList<ManagementStatutoryPolicy>> ListStatutoryPoliciesAsync(Guid? jurisdictionId, CancellationToken ct)
    {
        const string sql = """
            SELECT statutory_discount_policy_registry_id,policy_code,policy_name,entitlement_type::text,
              local_government_unit_id,benefit_type::text,beneficiary_residency_scope::text,requires_evidence,
              required_evidence_type::text,COALESCE(v.source_verification_status,r.verification_status)::text,policy_status::text,effective_from,effective_to,r.row_version,
              v.statutory_discount_policy_version_id,v.policy_version,v.transaction_publication_status::text
            FROM discounts.statutory_discount_policy_registry r
            LEFT JOIN LATERAL (
              SELECT version.statutory_discount_policy_version_id,version.policy_version,version.transaction_publication_status,version.source_verification_status
              FROM discounts.statutory_discount_policy_versions version
              WHERE version.statutory_discount_policy_registry_id=r.statutory_discount_policy_registry_id
              ORDER BY version.created_at DESC,version.policy_version DESC LIMIT 1
            ) v ON true
            WHERE @jurisdiction IS NULL OR local_government_unit_id=@jurisdiction
            ORDER BY policy_code,effective_from DESC;
            """;
        await using var connection = await OpenAsync(ct); await using var command = new NpgsqlCommand(sql, connection); AddNullable(command, "jurisdiction", jurisdictionId, NpgsqlDbType.Uuid);
        await using var reader = await command.ExecuteReaderAsync(ct); var rows = new List<ManagementStatutoryPolicy>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
          reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetString(8),
          reader.GetString(9), reader.GetString(10), reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12), reader.GetInt64(13),
          reader.IsDBNull(14) ? null : reader.GetGuid(14), reader.IsDBNull(15) ? null : reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetString(16)));
        return rows;
    }

    public async Task<ManagementStatutoryPolicy> CreateStatutoryPolicyDraftAsync(ManagementStatutoryPolicyDraft draft, Guid actor, Guid correlation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(draft.PolicyCode) || string.IsNullOrWhiteSpace(draft.PolicyName) || string.IsNullOrWhiteSpace(draft.PolicyVersion) || draft.LocalGovernmentUnitId == Guid.Empty || string.IsNullOrWhiteSpace(draft.OrdinanceReference) || string.IsNullOrWhiteSpace(draft.SourceReference) || (draft.RequiresEvidence && string.IsNullOrWhiteSpace(draft.RequiredEvidenceType))) throw new ManagementConfigurationException("STATUTORY_POLICY_DRAFT_INVALID");
        var registryId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var code = draft.PolicyCode.Trim().ToUpperInvariant(); var existingRegistry = false;
        await using var connection = await OpenAsync(ct); await using var tx = await connection.BeginTransactionAsync(ct);
        const string jurisdictionSql = "SELECT jurisdiction_code,display_name FROM sites.jurisdictions WHERE jurisdiction_id=@id AND jurisdiction_status='ACTIVE'";
        string jurisdictionCode; string jurisdictionName; await using (var command = new NpgsqlCommand(jurisdictionSql, connection, tx)) { Add(command, "id", draft.LocalGovernmentUnitId); await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new ManagementConfigurationException("STATUTORY_POLICY_LGU_INVALID"); jurisdictionCode = reader.GetString(0); jurisdictionName = reader.GetString(1); }
        const string existingRegistrySql = "SELECT statutory_discount_policy_registry_id,local_government_unit_id,entitlement_type::text FROM discounts.statutory_discount_policy_registry WHERE policy_code=@code FOR UPDATE";
        await using (var command = new NpgsqlCommand(existingRegistrySql, connection, tx)) { Add(command, "code", code); await using var reader = await command.ExecuteReaderAsync(ct); if (await reader.ReadAsync(ct)) { if (reader.IsDBNull(1) || reader.GetGuid(1) != draft.LocalGovernmentUnitId || reader.GetString(2) != draft.EntitlementType) throw new ManagementConfigurationException("STATUTORY_POLICY_VERSION_SCOPE_CONFLICT"); registryId = reader.GetGuid(0); existingRegistry = true; } }
        const string registrySql = """
          INSERT INTO discounts.statutory_discount_policy_registry(
            statutory_discount_policy_registry_id,policy_code,policy_name,entitlement_type,policy_status,verification_status,
            policy_level,policy_type,policy_resolution_basis,benefit_type,discount_base_scope,jurisdiction_id,local_government_unit_id,
            jurisdiction_code,jurisdiction_name,beneficiary_residency_scope,free_duration_minutes,full_fee_exempt,requires_evidence,
            required_evidence_type,requires_operator_validation,legal_basis_reference,ordinance_reference,source_reference,effective_from,
            correlation_id,created_by_user_id,updated_by_user_id)
          VALUES(@id,@code,@name,@entitlement::discounts.statutory_entitlement_type_enum,'DRAFT','PROPOSED_ONLY','LOCAL_ORDINANCE',
            'LOCAL_ORDINANCE','LOCAL_ORDINANCE_APPLIED',@benefit::discounts.parking_benefit_type_enum,@base::discounts.discount_base_scope_enum,
            @lgu,@lgu,@jurisdiction_code,@jurisdiction_name,@residency::discounts.beneficiary_residency_scope_enum,@free,@full,@evidence,
            @evidence_type::discounts.discount_evidence_type_enum,true,@ordinance,@ordinance,@source,@effective,@correlation,@actor,@actor);
          """;
        if (!existingRegistry) await using (var command = new NpgsqlCommand(registrySql, connection, tx)) { Add(command, "id", registryId); Add(command, "code", code); Add(command, "name", draft.PolicyName.Trim()); Add(command, "entitlement", draft.EntitlementType); Add(command, "benefit", draft.BenefitType); Add(command, "base", draft.DiscountBaseScope); Add(command, "lgu", draft.LocalGovernmentUnitId); Add(command, "jurisdiction_code", jurisdictionCode); Add(command, "jurisdiction_name", jurisdictionName); Add(command, "residency", draft.ResidencyScope); AddNullable(command, "free", draft.FreeDurationMinutes, NpgsqlDbType.Integer); Add(command, "full", draft.FullFeeExempt); Add(command, "evidence", draft.RequiresEvidence); AddNullable(command, "evidence_type", draft.RequiredEvidenceType, NpgsqlDbType.Text); Add(command, "ordinance", draft.OrdinanceReference!); Add(command, "source", draft.SourceReference); Add(command, "effective", draft.EffectiveFrom); Add(command, "correlation", correlation); Add(command, "actor", actor); await command.ExecuteNonQueryAsync(ct); }
        var semantic = SemanticHash(draft, jurisdictionCode);
        const string versionSql = """
          INSERT INTO discounts.statutory_discount_policy_versions(
            statutory_discount_policy_version_id,statutory_discount_policy_registry_id,policy_code,policy_version,policy_version_label,
            entitlement_type,jurisdiction_id,local_government_unit_id,jurisdiction_code,jurisdiction_display_name,policy_scope_type,
            policy_level,policy_type,policy_resolution_basis,source_verification_status,transaction_publication_status,
            detailed_rule_verification_status,parking_service_applicability,benefit_type,policy_effect_support_status,discount_base_scope,
            beneficiary_residency_scope,ordinance_number,legal_basis_reference,source_type,source_reference,free_duration_minutes,
            discount_percentage_basis_points,full_fee_exempt,transaction_use_effective_from,policy_semantic_hash,correlation_id,
            created_by_user_id,updated_by_user_id)
          VALUES(@id,@registry,@code,@version,@label,@entitlement::discounts.statutory_entitlement_type_enum,@lgu,@lgu,@jurisdiction_code,
            @jurisdiction_name,'JURISDICTION','LOCAL_ORDINANCE','LOCAL_ORDINANCE','LOCAL_ORDINANCE_APPLIED','PROPOSED_ONLY','DRAFT',
            'STATUS_UNRESOLVED','UNRESOLVED',@benefit::discounts.parking_benefit_type_enum,'UNRESOLVED',
            @base::discounts.discount_base_scope_enum,@residency::discounts.beneficiary_residency_scope_enum,@ordinance,@ordinance,
            'STATUS_UNRESOLVED',@source,@free,@percentage,@full,@effective,@hash,@correlation,@actor,@actor);
          """;
        await using (var command = new NpgsqlCommand(versionSql, connection, tx)) { Add(command, "id", versionId); Add(command, "registry", registryId); Add(command, "code", code); Add(command, "version", draft.PolicyVersion); Add(command, "label", draft.PolicyName + " " + draft.PolicyVersion); Add(command, "entitlement", draft.EntitlementType); Add(command, "lgu", draft.LocalGovernmentUnitId); Add(command, "jurisdiction_code", jurisdictionCode); Add(command, "jurisdiction_name", jurisdictionName); Add(command, "benefit", draft.BenefitType); Add(command, "base", draft.DiscountBaseScope); Add(command, "residency", draft.ResidencyScope); Add(command, "ordinance", draft.OrdinanceReference!); Add(command, "source", draft.SourceReference); AddNullable(command, "free", draft.FreeDurationMinutes, NpgsqlDbType.Integer); AddNullable(command, "percentage", draft.DiscountPercentageBasisPoints, NpgsqlDbType.Integer); Add(command, "full", draft.FullFeeExempt); Add(command, "effective", draft.EffectiveFrom); Add(command, "hash", semantic); Add(command, "correlation", correlation); Add(command, "actor", actor); await command.ExecuteNonQueryAsync(ct); }
        if (draft.RequiresEvidence) { const string evidenceSql = """INSERT INTO discounts.statutory_discount_policy_version_evidence_requirements(statutory_discount_policy_version_evidence_requirement_id,statutory_discount_policy_version_id,evidence_type,requirement_status,safe_requirement_label,created_by_user_id,updated_by_user_id) VALUES(gen_random_uuid(),@version,@type::discounts.discount_evidence_type_enum,'REQUIRED',@label,@actor,@actor)"""; await using var command = new NpgsqlCommand(evidenceSql, connection, tx); Add(command, "version", versionId); Add(command, "type", draft.RequiredEvidenceType!); Add(command, "label", draft.RequiredEvidenceType == "SENIOR_CITIZEN_ID" ? "Valid Senior Citizen ID" : draft.RequiredEvidenceType == "PWD_ID" ? "Valid PWD ID" : "Required supporting document"); Add(command, "actor", actor); await command.ExecuteNonQueryAsync(ct); }
        if (existingRegistry) { await using var command = new NpgsqlCommand("UPDATE discounts.statutory_discount_policy_registry SET updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1 WHERE statutory_discount_policy_registry_id=@id", connection, tx); Add(command, "actor", actor); Add(command, "id", registryId); await command.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct); return (await ListStatutoryPoliciesAsync(draft.LocalGovernmentUnitId, ct)).Single(x => x.PolicyId == registryId);
    }

    public async Task<ManagementStatutoryPolicy> ChangeStatutoryPolicyStatusAsync(Guid id, long expectedVersion, string status, Guid actor, CancellationToken ct)
    {
        if (status is not ("ACTIVE" or "RETIRED")) throw new ManagementConfigurationException("STATUTORY_POLICY_STATUS_INVALID");
        await using var connection = await OpenAsync(ct); await using var tx = await connection.BeginTransactionAsync(ct);
        Guid? lgu;
        if (status == "ACTIVE")
        {
            const string activateVersion = """
              WITH candidate AS (
                SELECT statutory_discount_policy_version_id
                FROM discounts.statutory_discount_policy_versions
                WHERE statutory_discount_policy_registry_id=@id
                  AND transaction_publication_status IN ('DRAFT','APPROVED_FOR_CONTROLLED_TRANSACTION_USE')
                  AND source_verification_status IN ('VERIFIED_OFFICIAL','VERIFIED_ACTIVE_OPERATIONAL','ACTIVE_APPROVED')
                  AND parking_service_applicability='COVERED'
                  AND policy_effect_support_status='SUPPORTED_BY_CURRENT_CALCULATION'
                ORDER BY created_at DESC,policy_version DESC LIMIT 1 FOR UPDATE
              ), superseded AS (
                UPDATE discounts.statutory_discount_policy_versions old
                SET transaction_publication_status='SUPERSEDED',superseded_by_policy_version_id=candidate.statutory_discount_policy_version_id,
                    transaction_use_effective_to=COALESCE(old.transaction_use_effective_to,now()),updated_at=now(),updated_by_user_id=@actor,row_version=old.row_version+1
                FROM candidate
                WHERE old.statutory_discount_policy_registry_id=@id
                  AND old.statutory_discount_policy_version_id<>candidate.statutory_discount_policy_version_id
                  AND old.transaction_publication_status='ACTIVE_FOR_TRANSACTION_USE'
                RETURNING old.statutory_discount_policy_version_id
              )
              UPDATE discounts.statutory_discount_policy_versions version
              SET transaction_publication_status='ACTIVE_FOR_TRANSACTION_USE',approved_at=COALESCE(approved_at,now()),
                  approved_by_user_id=COALESCE(approved_by_user_id,@actor),transaction_use_effective_from=COALESCE(transaction_use_effective_from,now()),
                  supersedes_policy_version_id=COALESCE(supersedes_policy_version_id,(SELECT statutory_discount_policy_version_id FROM superseded LIMIT 1)),
                  updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1
              FROM candidate WHERE version.statutory_discount_policy_version_id=candidate.statutory_discount_policy_version_id
              RETURNING version.source_verification_status::text;
              """;
            string? verification; await using (var command = new NpgsqlCommand(activateVersion, connection, tx)) { Add(command, "id", id); Add(command, "actor", actor); verification = (string?)await command.ExecuteScalarAsync(ct); if (verification is null) throw new ManagementConfigurationException("STATUTORY_POLICY_VERIFICATION_REQUIRED"); }
            const string activateRegistry = """
              UPDATE discounts.statutory_discount_policy_registry SET policy_status='ACTIVE',verification_status=@verification::discounts.policy_verification_status_enum,
                coverage_available=true,approved_at=COALESCE(approved_at,now()),approved_by_user_id=COALESCE(approved_by_user_id,@actor),
                updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1
              WHERE statutory_discount_policy_registry_id=@id AND row_version=@version AND policy_status IN ('DRAFT','SUSPENDED','ACTIVE')
              RETURNING local_government_unit_id;
              """;
            await using var activationCommand = new NpgsqlCommand(activateRegistry, connection, tx); Add(activationCommand, "verification", verification); Add(activationCommand, "actor", actor); Add(activationCommand, "id", id); Add(activationCommand, "version", expectedVersion); lgu = (Guid?)await activationCommand.ExecuteScalarAsync(ct); if (lgu is null) throw new ManagementConfigurationException("STATUTORY_POLICY_LIFECYCLE_CONFLICT");
        }
        else
        {
            await using (var command = new NpgsqlCommand("UPDATE discounts.statutory_discount_policy_versions SET transaction_publication_status='RETIRED',retired_at=now(),transaction_use_effective_to=COALESCE(transaction_use_effective_to,now()),updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1 WHERE statutory_discount_policy_registry_id=@id AND transaction_publication_status IN ('ACTIVE_FOR_TRANSACTION_USE','SUSPENDED')", connection, tx)) { Add(command, "actor", actor); Add(command, "id", id); await command.ExecuteNonQueryAsync(ct); }
            const string retireRegistry = """UPDATE discounts.statutory_discount_policy_registry SET policy_status='RETIRED',effective_to=COALESCE(effective_to,now()),updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1 WHERE statutory_discount_policy_registry_id=@id AND row_version=@version AND policy_status IN ('ACTIVE','SUSPENDED') RETURNING local_government_unit_id;""";
            await using var retirementCommand = new NpgsqlCommand(retireRegistry, connection, tx); Add(retirementCommand, "actor", actor); Add(retirementCommand, "id", id); Add(retirementCommand, "version", expectedVersion); lgu = (Guid?)await retirementCommand.ExecuteScalarAsync(ct); if (lgu is null) throw new ManagementConfigurationException("STATUTORY_POLICY_LIFECYCLE_CONFLICT");
        }
        await tx.CommitAsync(ct); return (await ListStatutoryPoliciesAsync(lgu, ct)).Single(x => x.PolicyId == id);
    }

    public async Task<IReadOnlyList<ManagementTariff>> ListTariffsAsync(Guid? siteId, string? vehicle, CancellationToken ct)
    {
        const string sql = """
            SELECT d.site_tariff_definition_id,d.site_id,s.site_name,d.vehicle_type_code,d.tariff_code,d.tariff_name,
              d.version,d.currency_code,d.parking_grace_period_minutes,d.post_payment_exit_grace_minutes,
              d.daily_max_fee_minor_units,d.quote_validity_minutes,d.effective_from,d.effective_to,d.status,
              (d.verified_at IS NOT NULL) AS verified,d.row_version,
              r.site_tariff_rule_id,r.sequence,r.rule_scope,r.charge_type,r.duration_start_minutes,r.duration_end_minutes,
              r.clock_start_time,r.clock_end_time,r.amount_minor_units,r.billing_unit_minutes,r.rounding_rule
            FROM sites.site_tariff_definitions d JOIN sites.sites s ON s.site_id=d.site_id
            LEFT JOIN sites.site_tariff_rules r ON r.site_tariff_definition_id=d.site_tariff_definition_id
            WHERE (@site IS NULL OR d.site_id=@site) AND (@vehicle IS NULL OR d.vehicle_type_code=@vehicle)
            ORDER BY s.site_name,d.vehicle_type_code,d.effective_from DESC,r.sequence;
            """;
        await using var connection = await OpenAsync(ct); await using var command = new NpgsqlCommand(sql, connection); AddNullable(command, "site", siteId, NpgsqlDbType.Uuid); AddNullable(command, "vehicle", string.IsNullOrWhiteSpace(vehicle) ? null : vehicle.Trim().ToUpperInvariant(), NpgsqlDbType.Varchar);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<ManagementTariff>(); var rules = new List<ManagementTariffRule>(); ManagementTariff? current = null;
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0); if (current?.TariffId != id) { if (current is not null) result.Add(current); rules = []; current = new(id, reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetInt64(10), reader.GetInt32(11), reader.GetFieldValue<DateTimeOffset>(12), reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13), reader.GetString(14), reader.GetBoolean(15), reader.GetInt64(16), rules); }
            if (!reader.IsDBNull(17)) rules.Add(new(reader.GetGuid(17), reader.GetInt32(18), reader.GetString(19), reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetInt32(21), reader.IsDBNull(22) ? null : reader.GetInt32(22), reader.IsDBNull(23) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(23)), reader.IsDBNull(24) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(24)), reader.GetInt64(25), reader.IsDBNull(26) ? null : reader.GetInt32(26), reader.IsDBNull(27) ? null : reader.GetString(27)));
        }
        if (current is not null) result.Add(current); return result;
    }

    public async Task<ManagementTariff> SaveTariffDraftAsync(ManagementTariff tariff, Guid actor, CancellationToken ct)
    {
        ValidateDraft(tariff); var id = tariff.TariffId ?? Guid.NewGuid(); await using var connection = await OpenAsync(ct); await using var tx = await connection.BeginTransactionAsync(ct);
        const string upsert = """
          INSERT INTO sites.site_tariff_definitions(site_tariff_definition_id,site_id,vehicle_type_code,tariff_code,tariff_name,version,currency_code,
            parking_grace_period_minutes,post_payment_exit_grace_minutes,daily_max_fee_minor_units,quote_validity_minutes,effective_from,effective_to,status,created_by_user_id,updated_by_user_id)
          VALUES(@id,@site,@vehicle,@code,@name,@version,@currency,@grace,@exit_grace,@cap,@quote,@from,@to,'DRAFT',@actor,@actor)
          ON CONFLICT(site_tariff_definition_id) DO UPDATE SET tariff_name=EXCLUDED.tariff_name,currency_code=EXCLUDED.currency_code,
            parking_grace_period_minutes=EXCLUDED.parking_grace_period_minutes,post_payment_exit_grace_minutes=EXCLUDED.post_payment_exit_grace_minutes,
            daily_max_fee_minor_units=EXCLUDED.daily_max_fee_minor_units,quote_validity_minutes=EXCLUDED.quote_validity_minutes,
            effective_from=EXCLUDED.effective_from,effective_to=EXCLUDED.effective_to,
            verified_at=NULL,verified_by_user_id=NULL,updated_at=now(),updated_by_user_id=@actor,
            row_version=sites.site_tariff_definitions.row_version+1
          WHERE sites.site_tariff_definitions.status='DRAFT' AND sites.site_tariff_definitions.row_version=@row_version;
          """;
        await using (var command = new NpgsqlCommand(upsert, connection, tx)) { Add(command, "id", id); Add(command, "site", tariff.SiteId); Add(command, "vehicle", tariff.VehicleTypeCode.ToUpperInvariant()); Add(command, "code", tariff.TariffCode.ToUpperInvariant()); Add(command, "name", tariff.TariffName); Add(command, "version", tariff.Version.ToUpperInvariant()); Add(command, "currency", tariff.CurrencyCode.ToUpperInvariant()); Add(command, "grace", tariff.ParkingGracePeriodMinutes); AddNullable(command, "exit_grace", tariff.PostPaymentExitGraceMinutes, NpgsqlDbType.Integer); AddNullable(command, "cap", tariff.DailyMaxFeeMinorUnits, NpgsqlDbType.Bigint); Add(command, "quote", tariff.QuoteValidityMinutes); Add(command, "from", tariff.EffectiveFrom); AddNullable(command, "to", tariff.EffectiveTo, NpgsqlDbType.TimestampTz); Add(command, "actor", actor); Add(command, "row_version", tariff.RowVersion); if (await command.ExecuteNonQueryAsync(ct) != 1) throw new ManagementConfigurationException("SITE_TARIFF_DRAFT_CONFLICT"); }
        await using (var delete = new NpgsqlCommand("DELETE FROM sites.site_tariff_rules WHERE site_tariff_definition_id=@id", connection, tx)) { Add(delete, "id", id); await delete.ExecuteNonQueryAsync(ct); }
        foreach (var rule in tariff.Rules) { const string insert = """INSERT INTO sites.site_tariff_rules(site_tariff_rule_id,site_tariff_definition_id,sequence,rule_scope,charge_type,duration_start_minutes,duration_end_minutes,clock_start_time,clock_end_time,amount_minor_units,billing_unit_minutes,rounding_rule,created_by_user_id,updated_by_user_id) VALUES(@rule,@id,@sequence,@scope,@charge,@start,@end,@clock_start,@clock_end,@amount,@unit,@rounding,@actor,@actor)"""; await using var command = new NpgsqlCommand(insert, connection, tx); Add(command, "rule", rule.RuleId ?? Guid.NewGuid()); Add(command, "id", id); Add(command, "sequence", rule.Sequence); Add(command, "scope", rule.RuleScope); Add(command, "charge", rule.ChargeType); AddNullable(command, "start", rule.DurationStartMinutes, NpgsqlDbType.Integer); AddNullable(command, "end", rule.DurationEndMinutes, NpgsqlDbType.Integer); AddNullable(command, "clock_start", rule.ClockStartTime?.ToTimeSpan(), NpgsqlDbType.Time); AddNullable(command, "clock_end", rule.ClockEndTime?.ToTimeSpan(), NpgsqlDbType.Time); Add(command, "amount", rule.AmountMinorUnits); AddNullable(command, "unit", rule.BillingUnitMinutes, NpgsqlDbType.Integer); AddNullable(command, "rounding", rule.RoundingRule, NpgsqlDbType.Varchar); Add(command, "actor", actor); await command.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct); return (await ListTariffsAsync(tariff.SiteId, tariff.VehicleTypeCode, ct)).Single(x => x.TariffId == id);
    }

    public async Task<ManagementTariff> VerifyTariffAsync(Guid id, long version, Guid actor, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        const string sql = """
          UPDATE sites.site_tariff_definitions
          SET verified_at=now(),verified_by_user_id=@actor,updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1
          WHERE site_tariff_definition_id=@id AND row_version=@version AND status='DRAFT'
          RETURNING site_id,vehicle_type_code;
          """;
        await using var command = new NpgsqlCommand(sql, connection); Add(command, "actor", actor); Add(command, "id", id); Add(command, "version", version);
        await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new ManagementConfigurationException("SITE_TARIFF_VERIFY_CONFLICT"); var site = reader.GetGuid(0); var vehicle = reader.GetString(1); await reader.CloseAsync(); return (await ListTariffsAsync(site, vehicle, ct)).Single(x => x.TariffId == id);
    }

    public Task<ManagementTariff> ActivateTariffAsync(Guid id, long version, Guid actor, CancellationToken ct) => ChangeStatusAsync(id, version, actor, "ACTIVE", ct);
    public Task<ManagementTariff> RetireTariffAsync(Guid id, long version, Guid actor, CancellationToken ct) => ChangeStatusAsync(id, version, actor, "RETIRED", ct);

    private async Task<ManagementTariff> ChangeStatusAsync(Guid id, long version, Guid actor, string status, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct); const string sql = """
          UPDATE sites.site_tariff_definitions d SET status=@status,
            activated_at=CASE WHEN @status='ACTIVE' THEN now() ELSE activated_at END,
            activated_by_user_id=CASE WHEN @status='ACTIVE' THEN @actor ELSE activated_by_user_id END,
            retired_at=CASE WHEN @status='RETIRED' THEN now() ELSE retired_at END,
            retired_by_user_id=CASE WHEN @status='RETIRED' THEN @actor ELSE retired_by_user_id END,
            updated_at=now(),updated_by_user_id=@actor,row_version=row_version+1
          WHERE site_tariff_definition_id=@id AND row_version=@version
            AND ((@status='ACTIVE' AND status='DRAFT' AND verified_at IS NOT NULL
                  AND EXISTS(SELECT 1 FROM sites.site_tariff_rules r WHERE r.site_tariff_definition_id=d.site_tariff_definition_id)
                  AND EXISTS(SELECT 1 FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id=s.site_group_id
                    WHERE s.site_id=d.site_id AND s.site_status='ACTIVE' AND sg.default_currency_code=d.currency_code))
              OR (@status='RETIRED' AND status='ACTIVE'))
          RETURNING site_id,vehicle_type_code;
          """; await using var command = new NpgsqlCommand(sql, connection); Add(command, "status", status); Add(command, "actor", actor); Add(command, "id", id); Add(command, "version", version); await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new ManagementConfigurationException("SITE_TARIFF_LIFECYCLE_CONFLICT"); var site = reader.GetGuid(0); var vehicle = reader.GetString(1); await reader.CloseAsync(); return (await ListTariffsAsync(site, vehicle, ct)).Single(x => x.TariffId == id);
    }

    private static void ValidateDraft(ManagementTariff tariff)
    {
        if (tariff.SiteId == Guid.Empty || string.IsNullOrWhiteSpace(tariff.VehicleTypeCode) || !System.Text.RegularExpressions.Regex.IsMatch(tariff.CurrencyCode, "^[A-Z]{3}$") || tariff.ParkingGracePeriodMinutes < 0 || tariff.ParkingGracePeriodMinutes > 1440 || tariff.PostPaymentExitGraceMinutes is < 0 or > 1440 || tariff.DailyMaxFeeMinorUnits < 0 || tariff.QuoteValidityMinutes is < 1 or > 1440 || tariff.EffectiveTo <= tariff.EffectiveFrom || tariff.Rules.Count == 0 || tariff.Rules.Select(x => x.Sequence).Distinct().Count() != tariff.Rules.Count) throw new ManagementConfigurationException("SITE_TARIFF_DEFINITION_INVALID");
        if (tariff.Rules.Any(rule => rule.Sequence <= 0 || rule.AmountMinorUnits < 0) || tariff.Rules.Select(rule => rule.RuleScope).Distinct(StringComparer.Ordinal).Count() != 1) throw new ManagementConfigurationException("SITE_TARIFF_RULE_INVALID");
        var ordered = tariff.Rules.OrderBy(rule => rule.Sequence).ToArray();
        if (ordered.Where((rule, index) => rule.Sequence != index + 1).Any()) throw new ManagementConfigurationException("SITE_TARIFF_RULE_SEQUENCE_INVALID");
        foreach (var rule in ordered)
        {
            var unit = rule.ChargeType == "UNIT_DURATION";
            if (unit != (rule.BillingUnitMinutes is > 0 && rule.RoundingRule == "WHOLE_STARTED_HOUR") || (!unit && (rule.BillingUnitMinutes is not null || rule.RoundingRule is not null))) throw new ManagementConfigurationException("SITE_TARIFF_RULE_INVALID");
        }
        if (ordered[0].RuleScope == "DURATION")
        {
            var expectedStart = 0;
            foreach (var rule in ordered)
            {
                if (rule.DurationStartMinutes != expectedStart || rule.ClockStartTime is not null || rule.ClockEndTime is not null || rule.DurationEndMinutes <= rule.DurationStartMinutes) throw new ManagementConfigurationException("SITE_TARIFF_RULE_GAP");
                if (rule.DurationEndMinutes is null)
                {
                    if (rule != ordered[^1]) throw new ManagementConfigurationException("SITE_TARIFF_RULE_AMBIGUOUS");
                    return;
                }
                expectedStart = rule.DurationEndMinutes.Value;
            }
            throw new ManagementConfigurationException("SITE_TARIFF_RULE_GAP");
        }
        if (ordered[0].RuleScope != "CLOCK_TIME") throw new ManagementConfigurationException("SITE_TARIFF_RULE_INVALID");
        var intervals = new List<(int Start, int End)>();
        foreach (var rule in ordered)
        {
            if (rule.DurationStartMinutes is not null || rule.DurationEndMinutes is not null || rule.ClockStartTime is null || rule.ClockEndTime is null || rule.ClockStartTime == rule.ClockEndTime) throw new ManagementConfigurationException("SITE_TARIFF_RULE_INVALID");
            var start = rule.ClockStartTime.Value.Hour * 60 + rule.ClockStartTime.Value.Minute; var end = rule.ClockEndTime.Value.Hour * 60 + rule.ClockEndTime.Value.Minute;
            if (start < end) intervals.Add((start, end)); else { intervals.Add((start, 1440)); intervals.Add((0, end)); }
        }
        var clock = intervals.OrderBy(x => x.Start).ThenBy(x => x.End).ToArray(); var covered = 0;
        foreach (var interval in clock) { if (interval.Start < covered) throw new ManagementConfigurationException("SITE_TARIFF_RULE_AMBIGUOUS"); if (interval.Start > covered) throw new ManagementConfigurationException("SITE_TARIFF_RULE_GAP"); covered = interval.End; }
        if (covered != 1440) throw new ManagementConfigurationException("SITE_TARIFF_RULE_GAP");
    }
    private static string SemanticHash(ManagementStatutoryPolicyDraft draft, string jurisdictionCode)
    {
        var material = string.Join('|', draft.PolicyCode.ToUpperInvariant(), draft.PolicyVersion, draft.EntitlementType, jurisdictionCode, "DRAFT", "PROPOSED_ONLY", "UNRESOLVED", draft.BenefitType, draft.ResidencyScope, draft.DiscountBaseScope, draft.RequiredEvidenceType ?? "", draft.OrdinanceReference ?? "", draft.FullFeeExempt, draft.FreeDurationMinutes?.ToString() ?? "", draft.DiscountPercentageBasisPoints?.ToString() ?? "");
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct) { var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct); return c; }
    private static ManagementSite ReadSite(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetString(10), r.IsDBNull(11) ? null : r.GetGuid(11), r.GetString(12), r.GetBoolean(13), r.GetBoolean(14), r.GetFieldValue<DateTimeOffset>(15), r.IsDBNull(16) ? null : r.GetFieldValue<DateTimeOffset>(16), r.GetInt64(17));
    private static ManagementJurisdiction ReadJurisdiction(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9), r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10), r.GetInt64(11));
    private static void Add(NpgsqlCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    private static void AddNullable(NpgsqlCommand c, string n, object? v, NpgsqlDbType type) => c.Parameters.Add(n, type).Value = v ?? DBNull.Value;
}
