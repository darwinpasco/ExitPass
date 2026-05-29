using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console statutory discount draft API route and response mapping.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private static readonly Guid EvaluationId = Guid.Parse("48000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("48000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("48000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("48000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("48000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("48000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("48000000-0000-0000-0000-000000000007");
    private static readonly Guid DraftId = Guid.Parse("48000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("48000000-0000-0000-0000-000000000009");
    private static readonly Guid EvidenceReferenceId = Guid.Parse("48000000-0000-0000-0000-000000000011");

    /// <summary>
    /// Verifies the documented Operator Console statutory discount draft route exists.
    /// </summary>
    [Fact]
    public void EndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Endpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>
    /// Verifies the documented route is discoverable through Swagger/OpenAPI.
    /// </summary>
    [Fact]
    public async Task EndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/draft");
        swaggerJson.Should().Contain("DraftOperatorConsoleStatutoryDiscount");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies denied access returns a deterministic 200 response without draft details.
    /// </summary>
    [Fact]
    public async Task Draft_WhenAccessDenied_ReturnsDeniedEnvelopeWithoutDraft()
    {
        using var factory = CreateFactory(DeniedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessEvaluationId.Should().Be(EvaluationId);
        body.AccessAllowed.Should().BeFalse();
        body.AccessDecision.Should().Be("DENIED");
        body.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        body.AccessPersisted.Should().BeTrue();
        body.DraftAccepted.Should().BeFalse();
        body.DraftPersisted.Should().BeFalse();
        body.DraftId.Should().BeNull();
    }

    /// <summary>
    /// Verifies accepted drafts return persisted draft evidence.
    /// </summary>
    [Fact]
    public async Task Draft_WhenAccepted_ReturnsDraftEnvelope()
    {
        using var factory = CreateFactory(AcceptedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DraftAccepted.Should().BeTrue();
        body.DraftPersisted.Should().BeTrue();
        body.DraftId.Should().Be(DraftId);
        body.ValidationStatus.Should().Be("REQUESTED");
        body.EntitlementType.Should().Be("SENIOR_CITIZEN");
        body.EvidenceRequired.Should().BeTrue();
        body.EvidenceReferenceCreated.Should().BeTrue();
        body.EvidenceReferenceId.Should().Be(EvidenceReferenceId);
        body.ReusedExistingDraft.Should().BeFalse();
    }

    /// <summary>
    /// Verifies replaying the same valid draft request reuses the active draft instead of returning a generic failure.
    /// </summary>
    [Fact]
    public async Task Draft_WhenEquivalentActiveDraftAlreadyExists_ReusesExistingDraft()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var request = ManualFixtureRequest();

        using var firstResponse = await client.PostAsJsonAsync(Endpoint, request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        first.Should().NotBeNull();
        first!.DraftAccepted.Should().BeTrue();
        first.DraftPersisted.Should().BeTrue();
        first.ReusedExistingDraft.Should().BeFalse();

        using var secondResponse = await client.PostAsJsonAsync(Endpoint, request);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        second.Should().NotBeNull();
        second!.DraftAccepted.Should().BeTrue();
        second.DraftPersisted.Should().BeTrue();
        second.ReusedExistingDraft.Should().BeTrue();
        second.DraftId.Should().Be(first.DraftId);
        second.ValidationStatus.Should().Be("REQUESTED");

        var activeDraftCount = await CountActiveDraftsAsync(request.ParkingSessionId, request.EntitlementType);
        activeDraftCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies evidence-requested drafts persist one metadata-only evidence reference and replay reuses it.
    /// </summary>
    [Fact]
    public async Task Draft_WhenEvidenceRequested_PersistsMetadataOnlyEvidenceReferenceAndReplayDoesNotDuplicate()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var request = ManualFixtureRequest(evidenceCaptureRequested: true);

        using var firstResponse = await client.PostAsJsonAsync(Endpoint, request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        first.Should().NotBeNull();
        first!.DraftAccepted.Should().BeTrue();
        first.DraftPersisted.Should().BeTrue();
        first.EvidenceRequired.Should().BeTrue();
        first.EvidenceReferenceCreated.Should().BeTrue();
        first.EvidenceReferenceId.Should().NotBeNull();

        var firstEvidence = await ReadEvidenceReferenceAsync(first.DraftId!.Value, "SENIOR_CITIZEN_ID");
        firstEvidence.Should().NotBeNull();
        firstEvidence!.EvidenceReferenceId.Should().Be(first.EvidenceReferenceId!.Value);
        firstEvidence.EvidenceStorageType.Should().Be("EXTERNAL_REFERENCE");
        firstEvidence.EvidenceStorageRef.Should().BeNull();
        firstEvidence.EvidenceHash.Should().BeNull();
        firstEvidence.EvidenceCaptureStatus.Should().Be("REFERENCED");
        firstEvidence.AccessClassification.Should().Be("RESTRICTED");
        firstEvidence.RedactionStatus.Should().Be("NOT_REDACTED");
        firstEvidence.EvidenceCaptured.Should().BeFalse();

        using var secondResponse = await client.PostAsJsonAsync(Endpoint, request);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        second.Should().NotBeNull();
        second!.DraftAccepted.Should().BeTrue();
        second.ReusedExistingDraft.Should().BeTrue();
        second.DraftId.Should().Be(first.DraftId);
        second.EvidenceRequired.Should().BeTrue();
        second.EvidenceReferenceCreated.Should().BeFalse();
        second.EvidenceReferenceId.Should().Be(first.EvidenceReferenceId);

        var evidenceReferenceCount = await CountEvidenceReferencesAsync(first.DraftId!.Value, "SENIOR_CITIZEN_ID");
        evidenceReferenceCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies session-not-found maps to 404 without draft persistence.
    /// </summary>
    [Fact]
    public async Task Draft_WhenSessionMissing_ReturnsNotFoundEnvelope()
    {
        using var factory = CreateFactory(NotFoundResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DraftAccepted.Should().BeFalse();
        body.DraftPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("SESSION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies validation errors map to Central PMS error envelopes.
    /// </summary>
    [Fact]
    public async Task Draft_WhenRequestInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(AcceptedResult(), throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DRAFT_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    private static CustomWebApplicationFactory CreateFactory(
        OperatorConsoleStatutoryDiscountDraftResult result,
        bool throwValidation = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountDraftService>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountDraftService>(
                    new FakeStatutoryDiscountDraftService(result, throwValidation));
            });

    private static OperatorConsoleStatutoryDiscountDraftRequest Request() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "TICKET-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            "****1234",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: true,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "Manual operator attestation.",
            ReasonCode: "OPERATOR_DRAFT_REQUESTED",
            "operator-console-statutory-discount-draft-api-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftRequest ManualFixtureRequest(bool evidenceCaptureRequested = false) =>
        new(
            Guid.Parse("77000000-0000-0000-0000-000000000010"),
            Guid.Parse("77000000-0000-0000-0000-000000000030"),
            Guid.Parse("77000000-0000-0000-0000-000000000002"),
            Guid.Parse("77000000-0000-0000-0000-000000000001"),
            Guid.Parse("77000000-0000-0000-0000-000000000050"),
            Guid.Parse("77000000-0000-0000-0000-000000000090"),
            "MANUAL-SESSION-LOOKUP-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "1234",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: evidenceCaptureRequested,
            EvidenceAccessIntent: null,
            OperatorAttestation: true,
            AttestationNotes: "Integration replay test draft only.",
            ReasonCode: "INTEGRATION_DUPLICATE_REPLAY",
            "operator-console-statutory-discount-draft-replay-test",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftResult DeniedResult() =>
        new(
            EvaluationId,
            AccessAllowed: false,
            "DENIED",
            ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            ValidationStatus: null,
            EvidenceCaptureRequired: true,
            EvidenceRequired: false,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            ReusedExistingDraft: false,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult AcceptedResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DraftAccepted: true,
            DraftPersisted: true,
            DraftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            EvidenceCaptureRequired: true,
            EvidenceRequired: true,
            EvidenceReferenceCreated: true,
            EvidenceReferenceId,
            ReusedExistingDraft: false,
            IneligibilityReason: null,
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult NotFoundResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            ValidationStatus: null,
            EvidenceCaptureRequired: true,
            EvidenceRequired: false,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            ReusedExistingDraft: false,
            IneligibilityReason: "SESSION_NOT_FOUND",
            ErrorCode: "SESSION_NOT_FOUND",
            CorrelationId);

    private sealed class FakeStatutoryDiscountDraftService : IOperatorConsoleStatutoryDiscountDraftService
    {
        private readonly OperatorConsoleStatutoryDiscountDraftResult _result;
        private readonly bool _throwValidation;

        public FakeStatutoryDiscountDraftService(
            OperatorConsoleStatutoryDiscountDraftResult result,
            bool throwValidation)
        {
            _result = result;
            _throwValidation = throwValidation;
        }

        public Task<OperatorConsoleStatutoryDiscountDraftResult> DraftAsync(
            OperatorConsoleStatutoryDiscountDraftCommand command,
            CancellationToken cancellationToken)
        {
            if (_throwValidation)
            {
                throw new ArgumentException("EntitlementType is required.");
            }

            return Task.FromResult(_result);
        }
    }

    private static async Task SeedManualFixtureAsync()
    {
        var sql = ReadRepoFile(
            "infra",
            "db",
            "fixtures",
            "operator-console-access-evaluation",
            "Seed-OperatorConsoleAccessEvaluationManualFixtures.sql");

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountActiveDraftsAsync(Guid parkingSessionId, string entitlementType)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
              AND validation_status IN (
                    'REQUESTED'::discounts.statutory_discount_validations_status_enum,
                    'PENDING_OPERATOR_REVIEW'::discounts.statutory_discount_validations_status_enum
              );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;

        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count);
    }

    private static async Task<int> CountEvidenceReferencesAsync(Guid draftId, string evidenceType)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND purged_at IS NULL;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;

        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count);
    }

    private static async Task<EvidenceReferenceRow?> ReadEvidenceReferenceAsync(Guid draftId, string evidenceType)
    {
        const string sql = """
            SELECT
                der.discount_evidence_reference_id,
                der.evidence_storage_type::text,
                der.evidence_storage_ref,
                der.evidence_hash,
                der.evidence_capture_status::text,
                der.access_classification::text,
                der.redaction_status::text,
                sdv.evidence_captured
            FROM discounts.discount_evidence_references der
            JOIN discounts.statutory_discount_validations sdv
              ON sdv.statutory_discount_validation_id = der.statutory_discount_validation_id
            WHERE der.statutory_discount_validation_id = @statutory_discount_validation_id
              AND der.evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND der.purged_at IS NULL
            ORDER BY der.created_at DESC, der.discount_evidence_reference_id DESC
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new EvidenceReferenceRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetBoolean(7));
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

    private sealed record EvidenceReferenceRow(
        Guid EvidenceReferenceId,
        string EvidenceStorageType,
        string? EvidenceStorageRef,
        string? EvidenceHash,
        string EvidenceCaptureStatus,
        string AccessClassification,
        string RedactionStatus,
        bool EvidenceCaptured);
}
