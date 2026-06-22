using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Internal;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

/// <summary>
/// Verifies the v1.2 internal verified payment outcome contract.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Verified provider outcomes must record payment confirmation evidence.
/// - Confirmed outcomes must finalize the PaymentAttempt and issue ExitAuthorization.
/// - Non-success provider outcomes must finalize deterministically without issuing ExitAuthorization.
/// - Duplicate provider references must return deterministic conflict responses.
/// - Required idempotency and correlation headers must fail closed.
/// </summary>
public sealed class ReportVerifiedPaymentOutcomeContractTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    public ReportVerifiedPaymentOutcomeContractTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies BRD 9.10 and SDD 10.5.3 unknown-payment-attempt behavior.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_404_not_found_for_unknown_payment_attempt()
    {
        var context = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_404_not_found_for_unknown_payment_attempt));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for verified payment outcome contract tests");

        try
        {
            using var client = CreateClient();
            using var response = await PostOutcomeAsync(
                client,
                BuildRequest(Guid.NewGuid(), context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_NOT_FOUND");
            payload.CorrelationId.Should().Be(context.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.10, BRD 9.12, SDD 6.4, and SDD 6.5 success response shape.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_200_ok_for_valid_confirmed_request()
    {
        var context = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_200_ok_for_valid_confirmed_request));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for verified payment outcome contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var response = await PostOutcomeAsync(
                client,
                BuildRequest(created.PaymentAttemptId, context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            payload.PaymentConfirmationId.Should().NotBe(Guid.Empty);
            payload.AttemptStatus.Should().Be("CONFIRMED");
            payload.ExitAuthorizationId.Should().NotBeNull();
            payload.AuthorizationStatus.Should().Be("ISSUED");
            payload.AuthorizationToken.Should().NotBeNullOrWhiteSpace();
            payload.VerifiedTimestamp.Should().BeAfter(DateTimeOffset.MinValue);
            payload.IssuedAt.Should().NotBeNull();
            payload.ExpirationTimestamp.Should().NotBeNull();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.10 and SDD 10.5.3 terminal non-success provider outcomes do not issue exit authorization.
    /// </summary>
    [Theory]
    [InlineData("FAILED")]
    [InlineData("CANCELLED")]
    [InlineData("EXPIRED")]
    public async Task ReportVerifiedPaymentOutcome_returns_200_ok_without_exit_authorization_for_non_success_provider_outcome(
        string providerStatus)
    {
        var context = PaymentTestContext.Create(
            $"{nameof(ReportVerifiedPaymentOutcome_returns_200_ok_without_exit_authorization_for_non_success_provider_outcome)}_{providerStatus}");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for non-success verified payment outcome contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var response = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference: $"prov-{providerStatus.ToLowerInvariant()}-{Guid.NewGuid():N}",
                    providerStatus: providerStatus,
                    finalAttemptStatus: "FAILED"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            payload.PaymentConfirmationId.Should().NotBe(Guid.Empty);
            payload.AttemptStatus.Should().Be("FAILED");
            payload.ExitAuthorizationId.Should().BeNull();
            payload.AuthorizationStatus.Should().BeNull();
            payload.AuthorizationToken.Should().BeNull();
            payload.IssuedAt.Should().BeNull();
            payload.ExpirationTimestamp.Should().BeNull();

            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            confirmationCount.Should().Be(1);
            issuedAuthorizationCount.Should().Be(0, "non-success provider finality must not issue exit authorization");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.5.3 replay behavior for the same provider outcome and idempotency key.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_replay_with_same_provider_reference_and_idempotency_key_returns_existing_result()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_replay_with_same_provider_reference_and_idempotency_key_returns_existing_result));
        var providerReference = $"prov-{Guid.NewGuid():N}";
        var idempotencyKey = $"ctest-outcome-{Guid.NewGuid():N}";
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for verified payment outcome replay contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: idempotencyKey);

            using var replay = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: idempotencyKey);

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstPayload = await first.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            var replayPayload = await replay.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            var persistedAttempt = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            replayPayload.Should().NotBeNull();
            replayPayload!.PaymentConfirmationId.Should().Be(firstPayload!.PaymentConfirmationId);
            replayPayload.PaymentAttemptId.Should().Be(firstPayload.PaymentAttemptId);
            replayPayload.AttemptStatus.Should().Be(firstPayload.AttemptStatus);
            replayPayload.ExitAuthorizationId.Should().Be(firstPayload.ExitAuthorizationId);
            replayPayload.AuthorizationToken.Should().Be(firstPayload.AuthorizationToken);
            replayPayload.AuthorizationStatus.Should().Be(firstPayload.AuthorizationStatus);
            persistedAttempt.Should().NotBeNull();
            persistedAttempt!.AttemptStatus.Should().Be("CONFIRMED");
            confirmationCount.Should().Be(1, "same provider outcome replay must not double-confirm");
            issuedAuthorizationCount.Should().Be(1, "same provider outcome replay must return the existing authorization, not issue another one");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.5.3 replay behavior for the same provider outcome with a new idempotency key.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_replay_with_same_provider_reference_and_new_idempotency_key_returns_existing_result()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_replay_with_same_provider_reference_and_new_idempotency_key_returns_existing_result));
        var providerReference = $"prov-{Guid.NewGuid():N}";
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for verified payment outcome provider-reference replay contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var replay = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstPayload = await first.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            var replayPayload = await replay.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            replayPayload.Should().NotBeNull();
            replayPayload!.PaymentConfirmationId.Should().Be(firstPayload!.PaymentConfirmationId);
            replayPayload.ExitAuthorizationId.Should().Be(firstPayload.ExitAuthorizationId);
            confirmationCount.Should().Be(1);
            issuedAuthorizationCount.Should().Be(1);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 deterministic rejection for semantically mismatched replay after confirmation.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_replay_with_same_provider_reference_but_conflicting_final_status_is_rejected()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_replay_with_same_provider_reference_but_conflicting_final_status_is_rejected));
        var providerReference = $"prov-{Guid.NewGuid():N}";
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for conflicting verified payment outcome replay contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var conflict = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference,
                    providerStatus: "FAILED",
                    finalAttemptStatus: "FAILED"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
            var persistedAttempt = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_ALREADY_FINAL");
            payload.CorrelationId.Should().Be(context.CorrelationId);
            persistedAttempt.Should().NotBeNull();
            persistedAttempt!.AttemptStatus.Should().Be("CONFIRMED");
            confirmationCount.Should().Be(1);
            issuedAuthorizationCount.Should().Be(1);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 replay behavior for non-success provider outcomes.
    /// </summary>
    [Theory]
    [InlineData("FAILED")]
    [InlineData("CANCELLED")]
    [InlineData("EXPIRED")]
    public async Task ReportVerifiedPaymentOutcome_non_success_replay_is_deterministic_and_does_not_authorize_exit(
        string providerStatus)
    {
        var context = PaymentTestContext.Create(
            $"{nameof(ReportVerifiedPaymentOutcome_non_success_replay_is_deterministic_and_does_not_authorize_exit)}_{providerStatus}");
        var providerReference = $"prov-{providerStatus.ToLowerInvariant()}-{Guid.NewGuid():N}";
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for non-success verified payment outcome replay contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference,
                    providerStatus: providerStatus,
                    finalAttemptStatus: "FAILED"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var replay = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference,
                    providerStatus: providerStatus,
                    finalAttemptStatus: "FAILED"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstPayload = await first.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            var replayPayload = await replay.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            var persistedAttempt = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            replayPayload.Should().NotBeNull();
            replayPayload!.PaymentConfirmationId.Should().Be(firstPayload!.PaymentConfirmationId);
            replayPayload.AttemptStatus.Should().Be("FAILED");
            replayPayload.ExitAuthorizationId.Should().BeNull();
            persistedAttempt.Should().NotBeNull();
            persistedAttempt!.AttemptStatus.Should().Be("FAILED");
            confirmationCount.Should().Be(1);
            issuedAuthorizationCount.Should().Be(0);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies provider evidence is not accepted as platform finality until Central PMS validation accepts it.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_400_and_does_not_finalize_when_provider_reference_missing()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_returns_400_and_does_not_finalize_when_provider_reference_missing));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for invalid verified payment outcome contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var response = await PostOutcomeAsync(
                client,
                BuildRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    context.RequestedByUserId,
                    providerReference: " "),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("INVALID_REQUEST");
            payload.CorrelationId.Should().Be(context.CorrelationId);

            var persisted = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            persisted.Should().NotBeNull();
            persisted!.AttemptStatus.Should().Be(created.AttemptStatus);
            persisted.FinalizedAt.Should().BeNull();
            confirmationCount.Should().Be(0);
            issuedAuthorizationCount.Should().Be(0);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.5.3 duplicate provider-reference behavior.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_409_conflict_for_duplicate_provider_reference()
    {
        var firstContext = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_409_conflict_for_duplicate_provider_reference) + "First");
        var secondContext = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_409_conflict_for_duplicate_provider_reference) + "Second");
        var providerReference = $"prov-{Guid.NewGuid():N}";

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            firstContext,
            "Seed first data set for duplicate provider-reference contract tests");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            secondContext,
            "Seed second data set for duplicate provider-reference contract tests");

        try
        {
            var firstCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                firstContext,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");
            var secondCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                secondContext,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(firstCreated.PaymentAttemptId, firstContext.ParkingSessionId, firstContext.RequestedByUserId, providerReference),
                includeCorrelationId: true,
                correlationId: firstContext.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var second = await PostOutcomeAsync(
                client,
                BuildRequest(secondCreated.PaymentAttemptId, secondContext.ParkingSessionId, secondContext.RequestedByUserId, providerReference),
                includeCorrelationId: true,
                correlationId: secondContext.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await second.Content.ReadFromJsonAsync<ErrorResponse>();
            var firstConfirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                firstCreated.PaymentAttemptId);
            var secondConfirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                secondCreated.PaymentAttemptId);
            var secondAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                secondContext.ParkingSessionId,
                issuedOnly: true);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PROVIDER_REFERENCE_ALREADY_RECORDED");
            payload.CorrelationId.Should().Be(secondContext.CorrelationId);
            firstConfirmationCount.Should().Be(1);
            secondConfirmationCount.Should().Be(0, "cross-attempt provider reference replay must not confirm the second attempt");
            secondAuthorizationCount.Should().Be(0, "cross-attempt provider reference replay must not authorize exit for the second attempt");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.5.3 conflict behavior when the attempt already has confirmation evidence.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_409_conflict_when_payment_confirmation_already_exists()
    {
        var context = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_409_conflict_when_payment_confirmation_already_exists));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for duplicate confirmation contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(created.PaymentAttemptId, context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var second = await PostOutcomeAsync(
                client,
                BuildRequest(created.PaymentAttemptId, context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await second.Content.ReadFromJsonAsync<ErrorResponse>();
            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_CONFIRMATION_ALREADY_EXISTS");
            confirmationCount.Should().Be(1);
            issuedAuthorizationCount.Should().Be(1, "duplicate outcome conflict must not issue a second authorization");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing idempotency metadata.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_400_bad_request_without_idempotency_key()
    {
        using var client = CreateClient();
        using var response = await PostOutcomeAsync(
            client,
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            includeCorrelationId: true,
            correlationId: Guid.NewGuid(),
            includeIdempotencyKey: false,
            idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing correlation metadata.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_400_bad_request_without_correlation_id()
    {
        using var client = CreateClient();
        using var response = await PostOutcomeAsync(
            client,
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            includeCorrelationId: false,
            correlationId: Guid.Empty,
            includeIdempotencyKey: true,
            idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static ReportVerifiedPaymentOutcomeRequest BuildRequest(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid requestedByUserId,
        string? providerReference = null,
        string providerStatus = "SUCCESS",
        string finalAttemptStatus = "CONFIRMED")
    {
        return new ReportVerifiedPaymentOutcomeRequest(
            PaymentAttemptId: paymentAttemptId,
            ParkingSessionId: parkingSessionId,
            ProviderReference: providerReference ?? $"prov-{Guid.NewGuid():N}",
            ProviderStatus: providerStatus,
            FinalAttemptStatus: finalAttemptStatus,
            RequestedBy: "payment-orchestrator",
            RequestedByUserId: requestedByUserId);
    }

    private static async Task<HttpResponseMessage> PostOutcomeAsync(
        HttpClient client,
        ReportVerifiedPaymentOutcomeRequest request,
        bool includeCorrelationId,
        Guid correlationId,
        bool includeIdempotencyKey,
        string? idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/internal/payments/outcome")
        {
            Content = JsonContent.Create(request)
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        if (includeIdempotencyKey)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(message);
    }
}
