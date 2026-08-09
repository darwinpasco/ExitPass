using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using Microsoft.Extensions.Options;
using Npgsql;

var connectionString = Required("I022_PROOF_CONNECTION_STRING");
var username = Required("I022_PROOF_USERNAME");
var password = Required("I022_PROOF_PASSWORD");
var builder = new NpgsqlConnectionStringBuilder(connectionString);
if (string.IsNullOrWhiteSpace(builder.Database) ||
    !builder.Database.StartsWith("exitpass_i022_", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("I-022 proof seed requires an exitpass_i022_-prefixed disposable database.");
}

var options = Options.Create(new HumanAuthenticationOptions
{
    Argon2Iterations = 1,
    Argon2MemoryKiB = 19456,
    Argon2Parallelism = 1,
    PasswordMinimumLength = 15
});
var material = await new Argon2idHumanPasswordHasher(options).HashAsync(password, CancellationToken.None);
var userId = Guid.NewGuid();
var roleId = Guid.NewGuid();
var assignmentId = Guid.NewGuid();
var serviceId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");

const string sql = """
    INSERT INTO identity.users (user_id,username,display_name,user_type,user_status,effective_from,
        created_by_service_identity_id,updated_by_service_identity_id)
    VALUES (@user_id,@username,'I-022 Browser Proof User','INTERNAL_ADMIN','ACTIVE',now()-interval '1 minute',@service_id,@service_id);

    INSERT INTO identity.local_credentials (local_credential_id,user_id,credential_status,password_verifier,
        verifier_salt,verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
        verifier_memory_kib,verifier_parallelism,activated_at,last_changed_at,
        created_by_service_identity_id,updated_by_service_identity_id)
    VALUES (gen_random_uuid(),@user_id,'ACTIVE',@verifier,@salt,@algorithm,@algorithm_version,@work_factor,
        @memory_kib,@parallelism,now(),now(),@service_id,@service_id);

    INSERT INTO identity.roles (role_id,role_code,role_name,role_type,role_status,is_privileged,
        requires_elevated_approval,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
    VALUES (@role_id,@role_code,'I-022 Browser Proof Role','OTHER','ACTIVE',false,false,
        now()-interval '1 minute',@service_id,@service_id);

    INSERT INTO identity.user_roles (user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
        assigned_by_service_identity_id,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
    VALUES (@assignment_id,@user_id,@role_id,'ACTIVE','I022_BROWSER_PROOF',@service_id,
        now()-interval '1 minute',@service_id,@service_id);

    INSERT INTO identity.role_permissions (role_permission_id,role_id,permission_id,binding_status,
        binding_reason_code,assigned_by_service_identity_id,effective_from,
        created_by_service_identity_id,updated_by_service_identity_id)
    SELECT gen_random_uuid(),@role_id,p.permission_id,'ACTIVE','I022_BROWSER_PROOF',@service_id,
        now()-interval '1 minute',@service_id,@service_id
    FROM identity.permissions p
    WHERE p.permission_code=ANY(@permissions);

    INSERT INTO identity.user_role_scope_grants (user_role_scope_grant_id,user_role_id,scope_type,site_id,
        grant_status,grant_reason_code,effective_from,granted_by_service_identity_id,
        created_by_service_identity_id,updated_by_service_identity_id)
    SELECT gen_random_uuid(),@assignment_id,'SITE',s.site_id,'ACTIVE','I022_BROWSER_PROOF',
        now()-interval '1 minute',@service_id,@service_id,@service_id
    FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id=s.site_group_id
    WHERE s.site_status='ACTIVE' AND sg.site_group_status='ACTIVE'
    ORDER BY s.site_code LIMIT 1;

    INSERT INTO identity.user_role_scope_grants (user_role_scope_grant_id,user_role_id,scope_type,site_group_id,
        grant_status,grant_reason_code,effective_from,granted_by_service_identity_id,
        created_by_service_identity_id,updated_by_service_identity_id)
    SELECT gen_random_uuid(),@assignment_id,'SITE_GROUP',sg.site_group_id,'ACTIVE','I022_BROWSER_PROOF',
        now()-interval '1 minute',@service_id,@service_id,@service_id
    FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id=s.site_group_id
    WHERE s.site_status='ACTIVE' AND sg.site_group_status='ACTIVE'
    ORDER BY s.site_code LIMIT 1;
    """;

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var command = new NpgsqlCommand(sql, connection);
command.Parameters.AddWithValue("user_id", userId);
command.Parameters.AddWithValue("username", username);
command.Parameters.AddWithValue("service_id", serviceId);
command.Parameters.AddWithValue("verifier", material.Verifier);
command.Parameters.AddWithValue("salt", material.Salt);
command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
command.Parameters.AddWithValue("work_factor", material.Iterations);
command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
command.Parameters.AddWithValue("parallelism", material.Parallelism);
command.Parameters.AddWithValue("role_id", roleId);
command.Parameters.AddWithValue("role_code", $"I022_BROWSER_{roleId:N}"[..40]);
command.Parameters.AddWithValue("assignment_id", assignmentId);
command.Parameters.AddWithValue("permissions", new[]
{
    "management-platform.overview.read",
    "user.view",
    "user.manage",
    "role.view",
    "permission.view",
    "statutory-discounts.review.queue.read",
    "statutory-discounts.review.detail.read",
    "statutory-discounts.decision.review",
    "statutory-discounts.decision.approve",
    "statutory-discounts.decision.reject",
    "statutory-discounts.evidence.review.view"
});
await command.ExecuteNonQueryAsync();
Console.WriteLine("I-022 hosted browser fixture seeded.");

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required environment variable {name} is missing.");
