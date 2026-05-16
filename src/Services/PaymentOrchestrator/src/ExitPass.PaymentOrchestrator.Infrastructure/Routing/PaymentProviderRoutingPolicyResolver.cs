using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Routing;

/// <summary>
/// Resolves payment provider routes from the database-backed routing policy table.
/// </summary>
public sealed class PaymentProviderRoutingPolicyResolver : IPaymentProviderRoutingPolicyResolver
{
    private readonly string _connectionString;
    private readonly PaymentProviderRoutingPolicyEvaluator _evaluator;
    private readonly ILogger<PaymentProviderRoutingPolicyResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentProviderRoutingPolicyResolver"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Structured logger.</param>
    public PaymentProviderRoutingPolicyResolver(
        IConfiguration configuration,
        ILogger<PaymentProviderRoutingPolicyResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is required.");

        _evaluator = new PaymentProviderRoutingPolicyEvaluator();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ResolvePaymentProviderRouteResponse> ResolveAsync(
        ResolvePaymentProviderRouteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policies = await LoadPoliciesAsync(request, cancellationToken);
        var result = _evaluator.Resolve(request, policies);

        _logger.LogInformation(
            "Resolved payment provider route. PaymentMethod {PaymentMethod}, Currency {Currency}, AmountMinorUnits {AmountMinorUnits}, SelectedProviderCode {SelectedProviderCode}, RoutingReason {RoutingReason}, CorrelationId {CorrelationId}",
            request.PaymentMethod,
            request.Currency,
            request.AmountMinorUnits,
            result.SelectedProviderCode,
            result.RoutingReason,
            request.CorrelationId);

        return result;
    }

    private async Task<IReadOnlyCollection<PaymentProviderRoutingPolicy>> LoadPoliciesAsync(
        ResolvePaymentProviderRouteRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                payment_routing_policy_id,
                payment_method_code,
                primary_provider_code,
                fallback_provider_code,
                currency_code,
                min_amount_minor_units,
                max_amount_minor_units,
                is_enabled,
                primary_provider_enabled,
                fallback_provider_enabled
            from payments.payment_provider_routing_policies
            where upper(payment_method_code) = upper(@payment_method_code)
              and (site_id is null or site_id = cast(@site_id as uuid))
              and (site_group_id is null or site_group_id = cast(@site_group_id as uuid))
              and effective_from <= now()
              and (effective_until is null or effective_until > now())
            order by
                case when site_id = cast(@site_id as uuid) then 0 when site_id is null then 1 else 2 end,
                case when site_group_id = cast(@site_group_id as uuid) then 0 when site_group_id is null then 1 else 2 end,
                effective_from desc,
                created_at desc;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_method_code", request.PaymentMethod);
        command.Parameters.AddWithValue("site_id", (object?)request.SiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("site_group_id", (object?)request.SiteGroupId ?? DBNull.Value);

        var policies = new List<PaymentProviderRoutingPolicy>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            policies.Add(new PaymentProviderRoutingPolicy(
                RoutingPolicyId: reader.GetGuid(reader.GetOrdinal("payment_routing_policy_id")),
                PaymentMethod: reader.GetString(reader.GetOrdinal("payment_method_code")),
                PrimaryProviderCode: reader.GetString(reader.GetOrdinal("primary_provider_code")),
                FallbackProviderCode: reader.IsDBNull(reader.GetOrdinal("fallback_provider_code"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("fallback_provider_code")),
                Currency: reader.GetString(reader.GetOrdinal("currency_code")),
                MinAmountMinorUnits: reader.IsDBNull(reader.GetOrdinal("min_amount_minor_units"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("min_amount_minor_units")),
                MaxAmountMinorUnits: reader.IsDBNull(reader.GetOrdinal("max_amount_minor_units"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("max_amount_minor_units")),
                IsEnabled: reader.GetBoolean(reader.GetOrdinal("is_enabled")),
                PrimaryProviderEnabled: reader.GetBoolean(reader.GetOrdinal("primary_provider_enabled")),
                FallbackProviderEnabled: reader.GetBoolean(reader.GetOrdinal("fallback_provider_enabled"))));
        }

        return policies;
    }
}
