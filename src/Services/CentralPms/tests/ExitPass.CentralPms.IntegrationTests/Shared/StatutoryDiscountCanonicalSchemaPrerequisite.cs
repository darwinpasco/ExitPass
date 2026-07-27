using Npgsql;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

internal static class StatutoryDiscountCanonicalSchemaPrerequisite
{
    private const string MissingSchemaMessage =
        "Statutory discount canonical schema is missing. Rebuild or upgrade the test database from " +
        "D:\\SourceCodes\\exitpassdb_v1.2\\build\\generated\\exitpass-full-object.generated.sql " +
        "or the canonical database repository migration before running statutory-discount integration tests.";

    public static async Task EnsurePresentAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var missing = new List<string>();
        await AddMissingAsync(connection, missing, "discounts.statutory_discount_decision_commands",
            "SELECT to_regclass('discounts.statutory_discount_decision_commands') IS NOT NULL;");
        await AddMissingAsync(connection, missing, "discounts.statutory_discount_payable_basis_application_commands",
            "SELECT to_regclass('discounts.statutory_discount_payable_basis_application_commands') IS NOT NULL;");
        await AddMissingAsync(connection, missing, "operator_console.statutory_discount_service_channel_reviews",
            "SELECT to_regclass('operator_console.statutory_discount_service_channel_reviews') IS NOT NULL;");
        await AddMissingAsync(connection, missing, "discounts.statutory_discount_validations.id_document_type",
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'discounts'
                  AND table_name = 'statutory_discount_validations'
                  AND column_name = 'id_document_type'
            );
            """);
        await AddMissingAsync(connection, missing, "discounts.statutory_discount_validations.masked_id_reference",
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'discounts'
                  AND table_name = 'statutory_discount_validations'
                  AND column_name = 'masked_id_reference'
            );
            """);
        await AddMissingAsync(connection, missing, "operator_console.statutory_discount_service_channel_reviews.statutory_discount_validation_id",
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'operator_console'
                  AND table_name = 'statutory_discount_service_channel_reviews'
                  AND column_name = 'statutory_discount_validation_id'
            );
            """);
        await AddMissingAsync(connection, missing, "AWAITING_REVIEW decision command status support",
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint con
                JOIN pg_class cls ON cls.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = cls.relnamespace
                WHERE n.nspname = 'discounts'
                  AND cls.relname = 'statutory_discount_decision_commands'
                  AND con.conname = 'ck_statutory_discount_decision_commands__command_status'
                  AND pg_get_constraintdef(con.oid) LIKE '%AWAITING_REVIEW%'
            );
            """);
        await AddMissingAsync(connection, missing, "NOT_DECIDED decision result support",
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint con
                JOIN pg_class cls ON cls.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = cls.relnamespace
                WHERE n.nspname = 'discounts'
                  AND cls.relname = 'statutory_discount_decision_commands'
                  AND con.conname = 'ck_statutory_discount_decision_commands__decision_result_status'
                  AND pg_get_constraintdef(con.oid) LIKE '%NOT_DECIDED%'
            );
            """);

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"{MissingSchemaMessage} Missing: {string.Join(", ", missing)}");
        }
    }

    private static async Task AddMissingAsync(
        NpgsqlConnection connection,
        ICollection<string> missing,
        string name,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);
        if (!exists)
        {
            missing.Add(name);
        }
    }
}
