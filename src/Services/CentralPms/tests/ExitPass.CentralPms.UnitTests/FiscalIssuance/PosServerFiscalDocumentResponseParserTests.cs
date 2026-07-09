using System.Net;
using System.Text;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class PosServerFiscalDocumentResponseParserTests
{
    [Fact]
    public void ParseCreateResponse_WhenNewlyCreatedAccepted_MapsSuccessfulEvidence()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            202,
            AcceptedResponse("newly_created"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.Accepted);
        result.Succeeded.Should().BeTrue();
        result.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
        result.FiscalIssuanceEvidenceStatus.Should().Be(FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned);
        result.FiscalNumberAssignmentState.Should().Be(FiscalNumberAssignmentState.Assigned);
        result.FiscalDocumentNumber.Should().Be("SI-000001");
    }

    [Fact]
    public void ParseCreateResponse_WhenIdempotentReplayAccepted_MapsReplayClassification()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            202,
            AcceptedResponse("idempotent_replay"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.Accepted);
        result.ResultClassification.Should().Be(FiscalIssuanceResultClassification.IdempotentReplay);
        result.FiscalDocumentId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public void ParseCreateResponse_WhenConflict_MapsConflictOutcome()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            409,
            FailureResponse("fiscal_document_idempotency_conflict", "do_not_retry_without_request_change"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.Conflict);
        result.ErrorPosture.Should().Be(FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ParseCreateResponse_WhenBadRequestWithDoNotRetry_MapsRequestFailure()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            400,
            FailureResponse("missing_payable_basis", "do_not_retry_without_request_change"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.FailedRequest);
        result.ErrorPosture.Should().Be(FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange);
    }

    [Fact]
    public void ParseCreateResponse_WhenBadRequestWithConfigurationCorrection_MapsConfigurationFailure()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            400,
            FailureResponse("fiscal_sequence_policy_not_found", "retry_after_configuration_correction"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.FailedConfiguration);
        result.ErrorPosture.Should().Be(FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection);
    }

    [Fact]
    public void ParseCreateResponse_WhenServiceUnavailable_MapsServiceFailure()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            503,
            FailureResponse("persistence_write_failed", "retry_after_service_recovery"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.FailedService);
        result.ErrorPosture.Should().Be(FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);
    }

    [Fact]
    public void ParseCreateResponse_WhenFiscalNumberAssignmentIncomplete_MapsFailClosedServiceFailure()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            503,
            FailureResponse("fiscal_number_assignment_incomplete", "retry_after_service_recovery"));

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.FailedService);
        result.Code.Should().Be("fiscal_number_assignment_incomplete");
        result.FiscalIssuanceEvidenceStatus.Should().BeNull();
    }

    [Fact]
    public void ParseCreateResponse_WhenAcceptedResponseLacksFiscalNumber_MapsInvalidFailClosed()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseCreateResponse(
            202,
            """
            {
              "succeeded": true,
              "code": "accepted",
              "message": "accepted",
              "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
              "resultClassification": "newly_created",
              "fiscalIssuanceEvidenceStatus": "fiscal_document_number_assigned",
              "fiscalNumberAssignmentState": "assigned"
            }
            """);

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.InvalidResponse);
        result.Succeeded.Should().BeFalse();
        result.Code.Should().Be("fiscal_number_assignment_incomplete");
    }

    [Fact]
    public async Task HttpClient_WhenCreateReturnsAccepted_ParsesResponseWithoutOperationalFlowWiring()
    {
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(AcceptedResponse("newly_created"), Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://pos-server.local")
        };
        var sut = new HttpPosServerFiscalDocumentClient(client);

        var result = await sut.CreateFiscalDocumentAsync(
            new PosServerFiscalDocumentRequestMapper().Map(PosServerFiscalDocumentRequestMapperTests.ValidContext()),
            CancellationToken.None);

        result.Outcome.Should().Be(PosServerFiscalDocumentOutcome.Accepted);
        result.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
    }

    [Theory]
    [InlineData("newly_voided", PosServerFiscalDocumentVoidOutcome.NewlyVoided)]
    [InlineData("idempotent_replay", PosServerFiscalDocumentVoidOutcome.IdempotentReplay)]
    [InlineData("already_voided", PosServerFiscalDocumentVoidOutcome.AlreadyVoided)]
    public void ParseVoidResponse_WhenAccepted_MapsSafeClassifications(
        string classification,
        PosServerFiscalDocumentVoidOutcome expectedOutcome)
    {
        var result = PosServerFiscalDocumentResponseParser.ParseVoidResponse(
            200,
            VoidAcceptedResponse(classification));

        result.Succeeded.Should().BeTrue();
        result.Outcome.Should().Be(expectedOutcome);
        result.ResultClassification.Should().Be(classification);
        result.FiscalDocumentId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.FiscalDocumentNumber.Should().Be("SI-000001");
        result.FiscalSequenceValue.Should().Be(1);
        result.FiscalDocumentStatus.Should().Be("voided");
        result.VoidStatus.Should().Be("recorded");
    }

    [Theory]
    [InlineData(409, "fiscal_document_void_idempotency_conflict", PosServerFiscalDocumentVoidOutcome.Conflict)]
    [InlineData(400, "invalid_reason_code", PosServerFiscalDocumentVoidOutcome.Rejected)]
    [InlineData(404, "fiscal_document_not_found", PosServerFiscalDocumentVoidOutcome.NotFound)]
    [InlineData(503, "persistence_write_failed", PosServerFiscalDocumentVoidOutcome.FailedService)]
    public void ParseVoidResponse_WhenFailure_MapsFailClosedOutcome(
        int httpStatusCode,
        string code,
        PosServerFiscalDocumentVoidOutcome expectedOutcome)
    {
        var result = PosServerFiscalDocumentResponseParser.ParseVoidResponse(
            httpStatusCode,
            VoidFailureResponse(code));

        result.Succeeded.Should().BeFalse();
        result.Outcome.Should().Be(expectedOutcome);
        result.Code.Should().Be(code);
    }

    [Fact]
    public async Task HttpClient_WhenVoidCalled_PostsExpectedEndpointAndBody()
    {
        HttpRequestMessage? observedRequest = null;
        string? observedBody = null;
        var client = new HttpClient(new StubHandler(request =>
        {
            observedRequest = request;
            observedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(VoidAcceptedResponse("newly_voided"), Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://pos-server.local")
        };
        var sut = new HttpPosServerFiscalDocumentClient(client);
        var documentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var result = await sut.VoidFiscalDocumentAsync(
            documentId,
            new PosServerFiscalDocumentVoidRequest(
                "void-key",
                "CONTROLLED_UAT_REAL_VOID",
                null,
                "central-pms-controlled-uat",
                null,
                "corr-001",
                "central-pms",
                DateOnly.Parse("2026-07-09")),
            CancellationToken.None);

        result.Outcome.Should().Be(PosServerFiscalDocumentVoidOutcome.NewlyVoided);
        observedRequest!.Method.Should().Be(HttpMethod.Post);
        observedRequest.RequestUri!.PathAndQuery.Should().Be($"/v1/fiscal-documents/{documentId:D}/void");
        observedBody.Should().Contain("\"idempotencyKey\":\"void-key\"");
        observedBody.Should().Contain("\"reasonCode\":\"CONTROLLED_UAT_REAL_VOID\"");
        observedBody.Should().Contain("\"requestedByRef\":\"central-pms-controlled-uat\"");
        observedBody.Should().Contain("\"correlationId\":\"corr-001\"");
        observedBody.Should().Contain("\"sourceSystemRef\":\"central-pms\"");
    }

    [Fact]
    public void ParseReadResponse_WhenReadbackIncludesIdempotencyAndHashFields_ExposesSafeContractFields()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseReadResponse(
            200,
            """
            {
              "succeeded": true,
              "code": "found",
              "message": "Fiscal document found.",
              "document": {
                "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
                "idempotencyScope": "fiscal_document_creation:22222222222222222222222222222222:33333333333333333333333333333333",
                "idempotencyKey": "CPS-POS-UAT:READBACK",
                "idempotencyKeySource": "payableBasis.upstreamFinalityRef",
                "semanticRequestHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "semanticRequestHashVersion": "sha256:v1",
                "semanticRequestHashStatus": "available"
              },
              "fiscalIssuanceEvidenceStatus": "fiscal_document_number_assigned",
              "fiscalNumberAssignmentState": "assigned",
              "fiscalDocumentStatusCodeId": "33333333-3333-3333-3333-333333333333"
            }
            """);

        result.Succeeded.Should().BeTrue();
        result.IdempotencyKey.Should().Be("CPS-POS-UAT:READBACK");
        result.IdempotencyKeySource.Should().Be("payableBasis.upstreamFinalityRef");
        result.SemanticRequestHash.Should().Be(new string('a', 64));
        result.SemanticRequestHashVersion.Should().Be("sha256:v1");
        result.SemanticRequestHashStatus.Should().Be("available");
    }

    [Fact]
    public void ParseReadResponse_WhenReadbackIncludesFiscalNumberingFields_ExposesNumberingEvidence()
    {
        var result = PosServerFiscalDocumentResponseParser.ParseReadResponse(
            200,
            """
            {
              "succeeded": true,
              "code": "found",
              "message": "Fiscal document found.",
              "document": {
                "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
                "fiscalIdentityId": "22222222-2222-2222-2222-222222222222",
                "fiscalSequencePolicyId": "44444444-4444-4444-4444-444444444444",
                "fiscalSequenceValue": 7,
                "fiscalDocumentNumber": "SI-000007",
                "fiscalSeries": "SI",
                "fiscalNumberPrefixText": "SI-",
                "fiscalNumberSuffixText": null,
                "fiscalNumberAssignedAt": "2026-07-06T10:30:00+08:00",
                "fiscalNumberAssignedByRef": "pos-server-runtime"
              },
              "fiscalIssuanceEvidenceStatus": "fiscal_document_number_assigned",
              "fiscalNumberAssignmentState": "assigned"
            }
            """);

        result.FiscalIdentityId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        result.FiscalSequencePolicyId.Should().Be(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        result.FiscalSequenceValue.Should().Be(7);
        result.FiscalDocumentNumber.Should().Be("SI-000007");
        result.FiscalSeries.Should().Be("SI");
        result.FiscalNumberPrefixText.Should().Be("SI-");
        result.FiscalNumberAssignedAt.Should().Be(DateTimeOffset.Parse("2026-07-06T10:30:00+08:00"));
        result.FiscalNumberAssignedByRef.Should().Be("pos-server-runtime");
    }

    [Fact]
    public void ExistingPaymentAndExitFlows_DoNotDependOnPosServerFiscalDocumentClient()
    {
        var operationalTypes = new[]
        {
            typeof(RecordPaymentConfirmationService),
            typeof(ReportVerifiedPaymentOutcomeHandler),
            typeof(IssueExitAuthorizationHandler)
        };

        var constructorParameterTypes = operationalTypes
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameterTypes.Should().NotContain(typeof(IPosServerFiscalDocumentClient));
        constructorParameterTypes.Should().NotContain(typeof(IPosServerFiscalDocumentRequestMapper));
        constructorParameterTypes.Should().NotContain(typeof(HttpPosServerFiscalDocumentClient));
    }

    private static string AcceptedResponse(string resultClassification) =>
        $$"""
        {
          "succeeded": true,
          "code": "accepted",
          "message": "accepted",
          "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
          "resultClassification": "{{resultClassification}}",
          "fiscalIssuanceEvidenceStatus": "fiscal_document_number_assigned",
          "fiscalNumberAssignmentState": "assigned",
          "fiscalIdentityId": "22222222-2222-2222-2222-222222222222",
          "fiscalDocumentStatusCodeId": "33333333-3333-3333-3333-333333333333",
          "fiscalSequencePolicyId": "44444444-4444-4444-4444-444444444444",
          "fiscalSequenceValue": 1,
          "fiscalDocumentNumber": "SI-000001",
          "fiscalSeries": "SI",
          "fiscalNumberPrefixText": "SI-",
          "fiscalNumberSuffixText": null,
          "fiscalNumberAssignedAt": "2026-07-02T10:30:00+08:00",
          "fiscalNumberAssignedByRef": "pos-server-runtime"
        }
        """;

    private static string FailureResponse(string code, string errorPosture) =>
        $$"""
        {
          "succeeded": false,
          "code": "{{code}}",
          "message": "{{code}}",
          "errorPosture": "{{errorPosture}}"
        }
        """;

    private static string VoidAcceptedResponse(string resultClassification) =>
        $$"""
        {
          "succeeded": true,
          "code": "accepted",
          "message": "Fiscal document void was recorded.",
          "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
          "fiscalDocumentNumber": "SI-000001",
          "fiscalSequenceValue": 1,
          "fiscalDocumentStatus": "voided",
          "voidStatus": "recorded",
          "voidedAt": "2026-07-09T14:23:18Z",
          "voidReasonCode": "CONTROLLED_UAT_REAL_VOID",
          "requestedByRef": "central-pms-controlled-uat",
          "idempotencyKey": "void-key",
          "resultClassification": "{{resultClassification}}",
          "correlationId": "corr-001"
        }
        """;

    private static string VoidFailureResponse(string code) =>
        $$"""
        {
          "succeeded": false,
          "code": "{{code}}",
          "message": "{{code}}",
          "resultClassification": "rejected",
          "errorPosture": "do_not_retry_without_request_change"
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
