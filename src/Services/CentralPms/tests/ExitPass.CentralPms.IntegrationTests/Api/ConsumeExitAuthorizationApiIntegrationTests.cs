using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the HTTP contract for consuming ExitAuthorization through the gate-facing endpoint.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4 Gate / Site Integration APIs
///
/// Invariants Enforced:
/// - ExitAuthorization consume is the hard control point before physical exit
/// - A valid authorization may be consumed only once
/// - Expired or replayed authorizations must fail closed
/// </summary>
public sealed class ConsumeExitAuthorizationApiIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Creates the consume-authorization API integration test fixture.
    /// </summary>
    public ConsumeExitAuthorizationApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies that a valid issued authorization can be consumed successfully.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsValid_ReturnsOk()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsValid_ReturnsOk));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-success");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            AddGateIdentityHeaders(request, context);

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();
            Assert.NotNull(body);
            Assert.Equal(authorization.ExitAuthorizationId, body!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", body.AuthorizationStatus);
            Assert.True(body.ConsumedAt > DateTimeOffset.MinValue);

            var handoff = await ReadGateConsumeHandoffStateAsync(
                authorization.ExitAuthorizationId,
                context.CorrelationId);

            Assert.Equal(1, handoff.DomainEventCount);
            Assert.Equal(1, handoff.OutboxEventCount);
            Assert.Equal(1, handoff.GateEventCount);
            Assert.Equal(authorization.PaymentAttemptId, handoff.PaymentAttemptId);
            Assert.Equal(context.TariffSnapshotId, handoff.TariffSnapshotId);
            Assert.Equal(PaymentTestDataHelper.GateDeviceCode(context), handoff.GateDeviceIdentifier);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that an issued authorization backed by an APPLIED effective tariff snapshot can be consumed.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAppliedEffectiveTariffWasPaid_ReturnsOk()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAppliedEffectiveTariffWasPaid_ReturnsOk));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for applied-tariff consume-exit-authorization API tests");

        try
        {
            var originalTariffSnapshotId = context.TariffSnapshotId;
            var appliedTariffSnapshotId = await CreateAppliedPayableBasisAsync(context);

            var attempt = await CreateAttemptForTariffSnapshotAsync(
                context,
                appliedTariffSnapshotId,
                $"idem-consume-applied-{Guid.NewGuid():N}");

            await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            var confirmation = await RecordPaymentConfirmationAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                providerReference: $"prov-{Guid.NewGuid():N}",
                requestedBy: "consume-auth-api-test",
                correlationId: context.CorrelationId);

            Assert.NotNull(confirmation);

            var authorization = await IssueExitAuthorizationAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                attempt.PaymentAttemptId,
                context.RequestedByUserId,
                context.CorrelationId);

            Assert.NotNull(authorization);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization!.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            AddGateIdentityHeaders(request, context);

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();
            Assert.NotNull(body);
            Assert.Equal(authorization.ExitAuthorizationId, body!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", body.AuthorizationStatus);

            var paidBasis = await ReadPaidBasisAsync(attempt.PaymentAttemptId);
            Assert.NotNull(paidBasis);
            Assert.Equal(appliedTariffSnapshotId, paidBasis!.TariffSnapshotId);
            Assert.Equal(71.43m, paidBasis.Amount);
            Assert.Equal("PHP", paidBasis.CurrencyCode.Trim());

            var tariffState = await ReadAppliedTariffStateAsync(
                originalTariffSnapshotId,
                appliedTariffSnapshotId);

            Assert.Equal("SUPERSEDED", tariffState.OriginalStatus);
            Assert.Equal("CONSUMED", tariffState.AppliedStatus);
            Assert.Equal(appliedTariffSnapshotId, tariffState.OriginalSupersededBy);
            Assert.Equal(1, await CountGateConsumptionsAsync(authorization.ExitAuthorizationId));
            Assert.Equal(0, await CountCouponApplicationsAsync(context.ParkingSessionId));
            Assert.Equal(0, await CountReconciliationItemsAsync(attempt.PaymentAttemptId));

            var handoff = await ReadGateConsumeHandoffStateAsync(
                authorization.ExitAuthorizationId,
                context.CorrelationId);

            Assert.Equal(1, handoff.DomainEventCount);
            Assert.Equal(1, handoff.OutboxEventCount);
            Assert.Equal(1, handoff.GateEventCount);
            Assert.Equal(attempt.PaymentAttemptId, handoff.PaymentAttemptId);
            Assert.Equal(appliedTariffSnapshotId, handoff.TariffSnapshotId);
            Assert.Equal(PaymentTestDataHelper.GateDeviceCode(context), handoff.GateDeviceIdentifier);
        }
        finally
        {
            await CleanupAppliedPayableBasisAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that paid amount drift is rejected before a gate consumption row is written.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenPaymentAmountDrifts_ReturnsConflictWithoutConsumption()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenPaymentAmountDrifts_ReturnsConflictWithoutConsumption));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume amount mismatch API tests");

        try
        {
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-amount-mismatch");

            await MutatePaymentAttemptAmountAsync(authorization.PaymentAttemptId, 101.00m);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            AddGateIdentityHeaders(request, context);
            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("PAYMENT_AMOUNT_MISMATCH", body!.ErrorCode);
            Assert.Equal(0, await CountGateConsumptionsAsync(authorization.ExitAuthorizationId));
            Assert.Equal(
                0,
                await CountGateAuthorizationConsumedEventsAsync(
                    authorization.ExitAuthorizationId,
                    context.CorrelationId));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests without a correlation header.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenCorrelationHeaderIsMissing_ReturnsBadRequest()
    {
        var exitAuthorizationId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume");

        request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("X-Correlation-Id", body.Message);
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests with an empty requesting-user identifier.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenRequestedByUserIdIsEmpty_ReturnsBadRequest()
    {
        var correlationId = Guid.NewGuid();
        var exitAuthorizationId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
            RequestedByUserId: Guid.Empty));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("RequestedByUserId", body.Message);
    }

    /// <summary>
    /// Verifies that consume returns not found for a non-existent authorization identifier.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFound()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFound));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for missing authorization gate identity tests");

        try
        {
            var exitAuthorizationId = Guid.NewGuid();

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{exitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            AddGateIdentityHeaders(request, context);

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("EXIT_AUTHORIZATION_NOT_FOUND", body!.ErrorCode);
            Assert.Equal(context.CorrelationId, body.CorrelationId);
            Assert.Equal(
                0,
                await CountGateAuthorizationConsumedEventsAsync(
                    exitAuthorizationId,
                    context.CorrelationId));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that replaying consume for an already consumed authorization returns conflict.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-replay");

            using var firstRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            firstRequest.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            AddGateIdentityHeaders(firstRequest, context);
            firstRequest.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var firstResponse = await _client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var replayCorrelationId = Guid.NewGuid();

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", replayCorrelationId.ToString());
            AddGateIdentityHeaders(request, context);

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("EXIT_AUTHORIZATION_ALREADY_CONSUMED", body!.ErrorCode);
            Assert.Equal(1, await CountGateConsumptionsAsync(authorization.ExitAuthorizationId));
            Assert.Equal(
                1,
                await CountGateAuthorizationConsumedEventsAsync(
                    authorization.ExitAuthorizationId,
                    context.CorrelationId));
            Assert.Equal(
                0,
                await CountGateAuthorizationConsumedEventsAsync(
                    authorization.ExitAuthorizationId,
                    replayCorrelationId));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that consume returns conflict for an expired authorization.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-expired");

            await ExpireAuthorizationDirectAsync(authorization.ExitAuthorizationId);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            AddGateIdentityHeaders(request, context);

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("EXIT_AUTHORIZATION_EXPIRED", body!.ErrorCode);
            Assert.Equal(0, await CountGateConsumptionsAsync(authorization.ExitAuthorizationId));
            Assert.Equal(
                0,
                await CountGateAuthorizationConsumedEventsAsync(
                    authorization.ExitAuthorizationId,
                    context.CorrelationId));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static async Task<IssuedAuthorizationContext> CreateConfirmedIssuedAuthorizationAsync(
        PaymentTestContext context,
        string idempotencyKey)
    {
        var attempt = await CreateAttemptAsync(
            ConnectionString,
            context,
            $"{idempotencyKey}-{Guid.NewGuid():N}",
            "consume-auth-api-test");

        await FinalizeAttemptAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            "CONFIRMED",
            "central-pms-finalizer",
            context.CorrelationId);

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            providerReference: $"prov-{Guid.NewGuid():N}",
            requestedBy: "consume-auth-api-test",
            correlationId: context.CorrelationId);

        Assert.NotNull(confirmation);

        var authorization = await IssueExitAuthorizationAsync(
            ConnectionString,
            attempt.ParkingSessionId,
            attempt.PaymentAttemptId,
            context.RequestedByUserId,
            context.CorrelationId);

        Assert.NotNull(authorization);

        return new IssuedAuthorizationContext(
            authorization!.ExitAuthorizationId,
            authorization.ParkingSessionId,
            authorization.PaymentAttemptId,
            authorization.AuthorizationToken);
    }

    private static async Task<Guid> CreateAppliedPayableBasisAsync(PaymentTestContext context)
    {
        var validationId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var appliedTariffSnapshotId = Guid.NewGuid();

        const string sql = """
            UPDATE core.tariff_snapshots
            SET snapshot_status = 'SUPERSEDED',
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @original_tariff_snapshot_id;

            INSERT INTO discounts.statutory_discount_validations (
                statutory_discount_validation_id,
                parking_session_id,
                tariff_snapshot_id,
                entitlement_type,
                policy_resolution_basis,
                local_ordinance_applied,
                national_law_fallback_applied,
                validation_channel,
                validation_status,
                currency_code,
                gross_amount_at_validation,
                statutory_discount_amount,
                net_amount_after_discount,
                evidence_required,
                evidence_captured,
                requested_at,
                validated_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @validation_id,
                @parking_session_id,
                @original_tariff_snapshot_id,
                'SENIOR_CITIZEN',
                'NATIONAL_LAW_FALLBACK',
                FALSE,
                TRUE,
                'OPERATOR_ASSISTED',
                'APPROVED',
                'PHP',
                100.00,
                17.86,
                71.43,
                FALSE,
                TRUE,
                NOW(),
                NOW(),
                @correlation_id,
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            );

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
                statutory_discount_validation_id,
                coupon_application_id,
                snapshot_status,
                calculated_at,
                expires_at,
                consumed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            SELECT
                @applied_tariff_snapshot_id,
                ts.parking_session_id,
                ts.vendor_system_id,
                ts.vendor_tariff_ref,
                ts.tariff_version_reference || '|APPLIED',
                ts.currency_code,
                100.00,
                17.86,
                0.00,
                71.43,
                @validation_id,
                NULL,
                'ACTIVE',
                NOW(),
                ts.expires_at,
                NULL,
                @correlation_id,
                NOW(),
                ts.created_by_service_identity_id,
                NOW(),
                ts.updated_by_service_identity_id,
                1
            FROM core.tariff_snapshots AS ts
            WHERE ts.tariff_snapshot_id = @original_tariff_snapshot_id;

            UPDATE core.tariff_snapshots
            SET superseded_by_tariff_snapshot_id = @applied_tariff_snapshot_id,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @original_tariff_snapshot_id;

            INSERT INTO discounts.statutory_discount_payable_basis_applications (
                statutory_discount_payable_basis_application_id,
                statutory_discount_validation_id,
                parking_session_id,
                original_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                application_status,
                application_channel,
                gross_amount_minor_units,
                vat_amount_minor_units,
                vat_exclusive_amount_minor_units,
                statutory_discount_amount_minor_units,
                final_payable_amount_minor_units,
                currency_code,
                computation_basis_json,
                rounding_mode,
                applied_at,
                applied_by_service_identity_id,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @application_id,
                @validation_id,
                @parking_session_id,
                @original_tariff_snapshot_id,
                @applied_tariff_snapshot_id,
                'APPLIED',
                'OPERATOR_CONSOLE',
                10000,
                1071,
                8929,
                1786,
                7143,
                'PHP',
                '{"policyContext":{"benefitType":"STATUTORY_DISCOUNT_VAT_EXEMPT"}}'::jsonb,
                'HALF_AWAY_FROM_ZERO',
                NOW(),
                @service_identity_id,
                @correlation_id,
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("validation_id", validationId);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("applied_tariff_snapshot_id", appliedTariffSnapshotId);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("original_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("service_identity_id", context.RequestedByUserId);

        await command.ExecuteNonQueryAsync();

        return appliedTariffSnapshotId;
    }

    private static async Task<CreateAttemptResult> CreateAttemptForTariffSnapshotAsync(
        PaymentTestContext context,
        Guid tariffSnapshotId,
        string idempotencyKey)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                attempt_status,
                payment_provider_code
            FROM core.create_or_reuse_payment_attempt(
                @p_parking_session_id,
                @p_tariff_snapshot_id,
                @p_payment_provider_code,
                @p_idempotency_key,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("p_tariff_snapshot_id", tariffSnapshotId);
        command.Parameters.AddWithValue("p_payment_provider_code", "GCASH");
        command.Parameters.AddWithValue("p_idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("p_requested_by", "consume-auth-api-test");
        command.Parameters.AddWithValue("p_correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new CreateAttemptResult(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
            PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")));
    }

    private static async Task ConsumeAuthorizationDirectAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId)
    {
        const string sql = """
            SELECT *
            FROM core.consume_exit_authorization(
                @p_exit_authorization_id,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("p_requested_by", requestedByUserId);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
    }

    private static async Task ExpireAuthorizationDirectAsync(Guid exitAuthorizationId)
    {
        const string sql = """
            UPDATE core.exit_authorizations
            SET issued_at = now() - interval '16 minutes',
                expires_at = now() - interval '1 minute',
                updated_at = now(),
                row_version = row_version + 1
            WHERE exit_authorization_id = @p_exit_authorization_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_exit_authorization_id", exitAuthorizationId);

        var affected = await command.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
    }

    private static async Task MutatePaymentAttemptAmountAsync(Guid paymentAttemptId, decimal amount)
    {
        const string sql = """
            UPDATE core.payment_attempts
            SET amount = @amount,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int> CountGateConsumptionsAsync(Guid exitAuthorizationId)
    {
        const string sql = """
            SELECT COUNT(*)::int
            FROM gates.gate_authorization_consumptions
            WHERE exit_authorization_id = @exit_authorization_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountGateAuthorizationConsumedEventsAsync(
        Guid exitAuthorizationId,
        Guid correlationId)
    {
        const string sql = """
            SELECT COUNT(*)::int
            FROM events.outbox_events
            WHERE event_type = 'GateAuthorizationConsumed'
              AND aggregate_id = @exit_authorization_id
              AND correlation_id = @correlation_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<GateConsumeHandoffState> ReadGateConsumeHandoffStateAsync(
        Guid exitAuthorizationId,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                (
                    SELECT COUNT(*)::int
                    FROM events.domain_events
                    WHERE event_type = 'GateAuthorizationConsumed'
                      AND aggregate_id = @exit_authorization_id
                      AND correlation_id = @correlation_id
                ) AS domain_event_count,
                (
                    SELECT COUNT(*)::int
                    FROM events.outbox_events
                    WHERE event_type = 'GateAuthorizationConsumed'
                      AND aggregate_id = @exit_authorization_id
                      AND correlation_id = @correlation_id
                ) AS outbox_event_count,
                (
                    SELECT COUNT(*)::int
                    FROM gates.gate_events
                    WHERE event_type = 'AUTHORIZATION_CONSUMED'
                      AND exit_authorization_id = @exit_authorization_id
                      AND correlation_id = @correlation_id
                ) AS gate_event_count,
                gac.gate_authorization_consumption_id,
                ea.parking_session_id,
                ea.payment_attempt_id,
                pa.tariff_snapshot_id,
                gac.gate_device_id,
                gd.device_code AS gate_device_identifier,
                gac.lane_id,
                gac.site_id,
                ps.vendor_system_id
            FROM gates.gate_authorization_consumptions AS gac
            JOIN core.exit_authorizations AS ea
              ON ea.exit_authorization_id = gac.exit_authorization_id
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = ea.parking_session_id
            JOIN core.payment_attempts AS pa
              ON pa.payment_attempt_id = ea.payment_attempt_id
            LEFT JOIN gates.gate_devices AS gd
              ON gd.gate_device_id = gac.gate_device_id
            WHERE gac.exit_authorization_id = @exit_authorization_id
              AND gac.consume_status = 'CONSUMED'
              AND gac.correlation_id = @correlation_id
            ORDER BY gac.consumed_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected a persisted gate consumption row for the handoff state.");

        return new GateConsumeHandoffState(
            DomainEventCount: reader.GetInt32(reader.GetOrdinal("domain_event_count")),
            OutboxEventCount: reader.GetInt32(reader.GetOrdinal("outbox_event_count")),
            GateEventCount: reader.GetInt32(reader.GetOrdinal("gate_event_count")),
            GateAuthorizationConsumptionId: reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            GateDeviceId: ReadGuidNullable(reader, "gate_device_id"),
            GateDeviceIdentifier: ReadStringNullable(reader, "gate_device_identifier"),
            LaneId: ReadGuidNullable(reader, "lane_id"),
            SiteId: reader.GetGuid(reader.GetOrdinal("site_id")),
            VendorSystemId: reader.GetGuid(reader.GetOrdinal("vendor_system_id")));
    }

    private static async Task<int> CountCouponApplicationsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)::int
            FROM coupons.coupon_applications
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountReconciliationItemsAsync(Guid paymentAttemptId)
    {
        const string sql = """
            SELECT COUNT(*)::int
            FROM reconciliation.reconciliation_items
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<PaidBasisRow?> ReadPaidBasisAsync(Guid paymentAttemptId)
    {
        const string sql = """
            SELECT tariff_snapshot_id, amount, currency_code::text AS currency_code
            FROM core.payment_attempts
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaidBasisRow(
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetDecimal(reader.GetOrdinal("amount")),
            reader.GetString(reader.GetOrdinal("currency_code")));
    }

    private static async Task<AppliedTariffState> ReadAppliedTariffStateAsync(
        Guid originalTariffSnapshotId,
        Guid appliedTariffSnapshotId)
    {
        const string sql = """
            SELECT
                original.snapshot_status::text AS original_status,
                original.superseded_by_tariff_snapshot_id,
                applied.snapshot_status::text AS applied_status
            FROM core.tariff_snapshots AS original
            JOIN core.tariff_snapshots AS applied
                ON applied.tariff_snapshot_id = @applied_tariff_snapshot_id
            WHERE original.tariff_snapshot_id = @original_tariff_snapshot_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("original_tariff_snapshot_id", originalTariffSnapshotId);
        command.Parameters.AddWithValue("applied_tariff_snapshot_id", appliedTariffSnapshotId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new AppliedTariffState(
            reader.GetString(reader.GetOrdinal("original_status")),
            reader.GetGuid(reader.GetOrdinal("superseded_by_tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("applied_status")));
    }

    private static async Task CleanupAppliedPayableBasisAsync(Guid parkingSessionId)
    {
        const string sql = """
            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
            SET statutory_discount_validation_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id
              AND statutory_discount_validation_id IS NOT NULL;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        await command.ExecuteNonQueryAsync();
    }

    private static void AddGateIdentityHeaders(HttpRequestMessage request, PaymentTestContext context)
    {
        request.Headers.Add("X-Service-Identity-Id", context.RequestedByUserId.ToString());
        request.Headers.Add("X-Gate-Device-Id", PaymentTestDataHelper.GateDeviceCode(context));
    }

    private static Guid? ReadGuidNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetGuid(ordinal);
    }

    private static string? ReadStringNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private sealed record ConsumeExitAuthorizationRequest(
        Guid RequestedByUserId);

    private sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);

    private sealed record IssuedAuthorizationContext(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken);

    private sealed record PaidBasisRow(
        Guid TariffSnapshotId,
        decimal Amount,
        string CurrencyCode);

    private sealed record AppliedTariffState(
        string OriginalStatus,
        Guid OriginalSupersededBy,
        string AppliedStatus);

    private sealed record GateConsumeHandoffState(
        int DomainEventCount,
        int OutboxEventCount,
        int GateEventCount,
        Guid GateAuthorizationConsumptionId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        Guid TariffSnapshotId,
        Guid? GateDeviceId,
        string? GateDeviceIdentifier,
        Guid? LaneId,
        Guid SiteId,
        Guid VendorSystemId);
}
