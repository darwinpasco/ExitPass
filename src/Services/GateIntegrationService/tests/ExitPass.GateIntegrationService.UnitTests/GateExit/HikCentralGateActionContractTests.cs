using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class HikCentralGateActionContractTests
{
    private static readonly Guid CommandId = Guid.Parse("e1000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceProcessingId = Guid.Parse("e1000000-0000-0000-0000-000000000002");
    private static readonly Guid SourceEventId = Guid.Parse("e1000000-0000-0000-0000-000000000003");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("e2000000-0000-0000-0000-000000000001");
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("e3000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("e4000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("e5000000-0000-0000-0000-000000000001");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("e6000000-0000-0000-0000-000000000001");
    private static readonly Guid GateDeviceId = Guid.Parse("e7000000-0000-0000-0000-000000000001");
    private static readonly Guid LaneId = Guid.Parse("e8000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("e9000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorSystemId = Guid.Parse("ea000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("eb000000-0000-0000-0000-000000000001");

    [Fact]
    public void Classify_WhenHikCentralEnvelopeAndDoorResultSucceed_ReturnsSuccess()
    {
        var response = HikCentralGateActionResultClassifier.Classify(
            Transport(
                200,
                new HikCentralGateActionEnvelope(
                    "0",
                    "Success",
                    [new HikCentralDoorControlResult("exit-gate-01", 0, "Success")])));

        Assert.Equal(HikCentralGateActionOutcome.Succeeded, response.Outcome);
        Assert.False(response.Retryable);
        Assert.False(response.TerminalFailure);
        Assert.Equal("0", response.VendorResponseCode);
    }

    [Fact]
    public void Classify_WhenTransportTimedOut_ReturnsRetryableTimeout()
    {
        var response = HikCentralGateActionResultClassifier.Classify(
            Transport(null, null, timedOut: true, transportError: "request timed out"));

        Assert.Equal(HikCentralGateActionOutcome.Timeout, response.Outcome);
        Assert.True(response.Retryable);
        Assert.False(response.TerminalFailure);
        Assert.Equal("TIMEOUT", response.RawStatusCategory);
    }

    [Fact]
    public void Classify_WhenVendorUnavailable_ReturnsRetryableVendorUnavailable()
    {
        var response = HikCentralGateActionResultClassifier.Classify(
            Transport(503, null, vendorUnavailable: true));

        Assert.Equal(HikCentralGateActionOutcome.VendorUnavailable, response.Outcome);
        Assert.True(response.Retryable);
        Assert.False(response.TerminalFailure);
        Assert.Equal("HTTP_503", response.RawStatusCategory);
    }

    [Theory]
    [InlineData(401, null, null)]
    [InlineData(200, "0x02401006", "Token Exception.")]
    [InlineData(200, "SIGNATURE_INVALID", "Signature verification failed.")]
    public void Classify_WhenUnauthorizedOrSignatureFailure_ReturnsTerminalUnauthorized(
        int httpStatus,
        string? code,
        string? message)
    {
        var response = HikCentralGateActionResultClassifier.Classify(
            Transport(httpStatus, new HikCentralGateActionEnvelope(code, message, [])));

        Assert.Equal(HikCentralGateActionOutcome.Unauthorized, response.Outcome);
        Assert.False(response.Retryable);
        Assert.True(response.TerminalFailure);
    }

    [Theory]
    [InlineData(400, null, null)]
    [InlineData(200, "INVALID_RESOURCE", "Door resource not found.")]
    public void Classify_WhenInvalidRequestOrResource_ReturnsTerminalInvalidRequest(
        int httpStatus,
        string? code,
        string? message)
    {
        var response = HikCentralGateActionResultClassifier.Classify(
            Transport(httpStatus, new HikCentralGateActionEnvelope(code, message, [])));

        Assert.Equal(HikCentralGateActionOutcome.InvalidRequest, response.Outcome);
        Assert.False(response.Retryable);
        Assert.True(response.TerminalFailure);
    }

    [Fact]
    public void Classify_WhenVendorErrorIsUnknown_ReturnsDeterministicRetryableUnknown()
    {
        var response = HikCentralGateActionResultClassifier.Classify(
            Transport(200, new HikCentralGateActionEnvelope("0x99999999", "Unexpected vendor result.", [])));

        Assert.Equal(HikCentralGateActionOutcome.Unknown, response.Outcome);
        Assert.True(response.Retryable);
        Assert.False(response.TerminalFailure);
        Assert.Equal("Unexpected vendor result.", response.DiagnosticMessage);
    }

    [Fact]
    public void CreateOpenExitBarrierRequest_PreservesCommandAndHandoffIdentity()
    {
        var command = CreateCommand();
        var handoff = CreateHandoff();

        var request = HikCentralGateActionRequestFactory.CreateOpenExitBarrierRequest(command, handoff);

        Assert.Equal(CommandId, request.CommandId);
        Assert.Equal(SourceProcessingId, request.SourceProcessingId);
        Assert.Equal(ExitAuthorizationId, request.ExitAuthorizationId);
        Assert.Equal(GateAuthorizationConsumptionId, request.GateAuthorizationConsumptionId);
        Assert.Equal(ParkingSessionId, request.ParkingSessionId);
        Assert.Equal(PaymentAttemptId, request.PaymentAttemptId);
        Assert.Equal(AppliedTariffSnapshotId, request.TariffSnapshotId);
        Assert.Equal(GateDeviceId, request.GateDeviceId);
        Assert.Equal("exit-gate-01", request.GateDeviceIdentifier);
        Assert.Equal("exit-gate-01", request.DoorIndexCode);
        Assert.Equal(LaneId, request.LaneId);
        Assert.Equal(SiteId, request.SiteId);
        Assert.Equal(VendorSystemId, request.VendorSystemId);
        Assert.Equal(CorrelationId, request.CorrelationId);
        Assert.Equal(2, request.CommandAttemptNumber);
        Assert.Equal(HikCentralDoorControlType.Open, request.ControlType);
        Assert.Equal(HikCentralDoorControlDirection.Exit, request.ControlDirection);
    }

    [Fact]
    public void ContractTypes_DoNotRequireLiveHttpClientOrCredentials()
    {
        var clientInterface = typeof(IHikCentralGateActionClient);
        var contractAssemblyTypes = typeof(HikCentralGateActionRequest).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(HikCentralGateActionRequest).Namespace)
            .ToArray();

        Assert.True(clientInterface.IsInterface);
        Assert.DoesNotContain(contractAssemblyTypes, type => type.Name.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(contractAssemblyTypes, type => type.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(contractAssemblyTypes, type => type.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    private static HikCentralGateActionTransportResult Transport(
        int? httpStatus,
        HikCentralGateActionEnvelope? envelope,
        bool timedOut = false,
        bool vendorUnavailable = false,
        string? transportError = null)
    {
        return new HikCentralGateActionTransportResult(
            httpStatus,
            envelope,
            VendorRequestId: "hik-request-1",
            VendorCorrelationId: CorrelationId.ToString("D"),
            timedOut,
            vendorUnavailable,
            transportError,
            DateTimeOffset.Parse("2026-05-31T08:10:00Z"));
    }

    private static GateCommandLifecycleRecord CreateCommand()
    {
        return new GateCommandLifecycleRecord(
            CommandId,
            SourceProcessingId,
            SourceEventId,
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            AppliedTariffSnapshotId,
            GateDeviceId,
            "exit-gate-01",
            LaneId,
            SiteId,
            VendorSystemId,
            GateCommandStatus.InProgress,
            AttemptCount: 2,
            GateCommandRetryPolicy.Default.MaxAttempts,
            GateCommandRetryPolicy.Default.PolicyCode,
            RequestedAtUtc: DateTimeOffset.Parse("2026-05-31T08:05:00Z"),
            LastAttemptedAtUtc: DateTimeOffset.Parse("2026-05-31T08:09:00Z"),
            StartedAtUtc: DateTimeOffset.Parse("2026-05-31T08:09:00Z"),
            CompletedAtUtc: null,
            NextAttemptAtUtc: null,
            TerminalFailureAtUtc: null,
            FailureCode: null,
            FailureReason: null,
            LastFailureCode: null,
            LastFailureReason: null,
            CorrelationId);
    }

    private static GateAuthorizationConsumedHandoff CreateHandoff()
    {
        return new GateAuthorizationConsumedHandoff(
            SourceEventId,
            SourceEventRef: $"central-pms://integration-events/{SourceEventId}",
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            AppliedTariffSnapshotId,
            GateDeviceId,
            GateDeviceIdentifier: "exit-gate-01",
            LaneId,
            SiteId,
            VendorSystemId,
            ConsumedAtUtc: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            CorrelationId);
    }
}

#pragma warning restore CS1591
