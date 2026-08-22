using ExitPass.CentralPms.Application.VendorParking.Routing;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorParking.Routing;

/// <summary>Authoritative effective-dated Site adapter resolver using canonical integration tables.</summary>
public sealed class PostgresSiteVendorAdapterRouteRegistry(
    string connectionString,
    string requiredEnvironment,
    Guid centralPmsServiceIdentityId) : ISiteVendorAdapterRouteRegistry
{
    public async Task<SiteVendorAdapterRoute> ResolveAsync(Guid siteId, Guid siteGroupId,
        Guid? vendorSystemId, CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty || siteGroupId == Guid.Empty)
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_SCOPE_REQUIRED");

        const string sql = """
            SELECT vs.vendor_system_id,
                   vs.base_url_ref,
                   vs.environment_code,
                   ve.credential_reference_id,
                   adapter_identity.service_identity_id,
                   cr.secret_reference,
                   GREATEST(vs.effective_from, ve.effective_from, am.effective_from, cr.created_at),
                   LEAST(vs.effective_to, ve.effective_to, am.effective_to, cr.expires_at)
              FROM integration.vendor_systems vs
              JOIN integration.adapter_mappings am
                ON am.vendor_system_id = vs.vendor_system_id
              JOIN integration.vendor_endpoints ve
                ON ve.vendor_system_id = vs.vendor_system_id
               AND ve.endpoint_code = 'SITE_ADAPTER_API'
              JOIN integration.integration_credential_references cr
                ON cr.integration_credential_reference_id = ve.credential_reference_id
              JOIN identity.service_identities adapter_identity
                ON adapter_identity.service_identity_id = CASE
                    WHEN am.vendor_object_ref ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                    THEN am.vendor_object_ref::uuid
                    ELSE NULL
                END
             WHERE am.site_id = @site_id
               AND am.site_group_id = @site_group_id
               AND (@vendor_system_id IS NULL OR vs.vendor_system_id = @vendor_system_id)
               AND am.vendor_object_type = 'SITE_ADAPTER'
               AND vs.vendor_system_status::text = 'ACTIVE'
               AND ve.endpoint_status::text = 'ACTIVE'
               AND am.mapping_status::text = 'ACTIVE'
               AND cr.credential_status::text = 'ACTIVE'
               AND cr.service_identity_id = @central_pms_service_identity_id
               AND adapter_identity.identity_status::text = 'ACTIVE'
               AND vs.environment_code = @environment
               AND vs.effective_from <= NOW()
               AND (vs.effective_to IS NULL OR vs.effective_to > NOW())
               AND ve.effective_from <= NOW()
               AND (ve.effective_to IS NULL OR ve.effective_to > NOW())
               AND am.effective_from <= NOW()
               AND (am.effective_to IS NULL OR am.effective_to > NOW())
               AND cr.revoked_at IS NULL
               AND (cr.expires_at IS NULL OR cr.expires_at > NOW())
             ORDER BY vs.vendor_system_id, ve.vendor_endpoint_id, am.adapter_mapping_id
             LIMIT 2;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value =
            (object?)vendorSystemId ?? DBNull.Value;
        command.Parameters.Add("central_pms_service_identity_id", NpgsqlDbType.Uuid).Value =
            centralPmsServiceIdentityId;
        command.Parameters.AddWithValue("environment", requiredEnvironment);
        var rows = new List<SiteVendorAdapterRoute>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1) || reader.IsDBNull(4) || reader.IsDBNull(5) ||
                !Uri.TryCreate(reader.GetString(1), UriKind.Absolute, out var endpoint))
                throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_MAPPING_INVALID");
            rows.Add(new(siteId, siteGroupId, reader.GetGuid(0), reader.GetGuid(4), endpoint,
                reader.GetString(5), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return rows.Count switch
        {
            1 => rows[0],
            0 => throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_MAPPING_NOT_FOUND"),
            _ => throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_MAPPING_AMBIGUOUS")
        };
    }
}
