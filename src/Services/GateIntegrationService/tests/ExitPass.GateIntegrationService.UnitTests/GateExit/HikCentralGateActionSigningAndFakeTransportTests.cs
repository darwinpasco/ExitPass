using System.Security.Cryptography;
using System.Text;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class HikCentralGateActionSigningAndFakeTransportTests
{
    private static readonly Guid CommandId = Guid.Parse("e1000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceProcessingId = Guid.Parse("e1000000-0000-0000-0000-000000000002");
    private static readonly Guid SourceEventId = Guid.Parse("e1000000-0000-0000-0000-000000000003");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("e2000000-0000-0000-0000-000000000001");
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("e3000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("e4000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("e5000000-0000-0000-0000-000000000001");
    private static readonly Guid TariffSnapshotId = Guid.Parse("e6000000-0000-0000-0000-000000000001");
    private static readonly Guid GateDeviceId = Guid.Parse("e7000000-0000-0000-0000-000000000001");
    private static readonly Guid LaneId = Guid.Parse("e8000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("e9000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorSystemId = Guid.Parse("ea000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("eb000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset FixedTimestamp =
        DateTimeOffset.FromUnixTimeMilliseconds(1479968678000);

    [Fact]
    public void SignDoorControlRequest_BuildsDeterministicCanonicalString()
    {
        var signer = CreateSigner();
        var request = HikCentralGateActionRequestFactory.CreateOpenExitBarrierRequest(
            CreateCommand(),
            CreateHandoff());

        var signed = signer.SignDoorControlRequest(request);

        var expectedBody = "{\"doorIndexCodes\":[\"exit-gate-01\"],\"controlType\":2,\"controlDirection\":1}";
        var expectedContentMd5 = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(expectedBody)));
        var expectedCanonical = string.Join(
            "\n",
            [
                "POST",
                "*/*",
                expectedContentMd5,
                "application/json",
                "x-ca-key:test-ak",
                "x-ca-nonce:fixed-nonce",
                "x-ca-timestamp:1479968678000",
                "/artemis/api/acs/v1/door/doControl"
            ]);

        Assert.Equal(expectedBody, signed.Body);
        Assert.Equal(expectedCanonical, signed.CanonicalRequest.StringToSign);
        Assert.Equal(HikCentralRequestSigner.DoorControlPath, signed.PathAndQuery);
    }

    [Fact]
    public void SignDoorControlRequest_ProducesDeterministicHmacSignature()
    {
        var signer = CreateSigner();
        var request = HikCentralGateActionRequestFactory.CreateOpenExitBarrierRequest(
            CreateCommand(),
            CreateHandoff());

        var signed = signer.SignDoorControlRequest(request);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-secret"));
        var expected = Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(signed.CanonicalRequest.StringToSign)));

        Assert.Equal(expected, signed.Signature);
        Assert.Equal(expected, signed.Headers["X-Ca-Signature"]);
    }

    [Fact]
    public void SignDoorControlRequest_IncludesRequiredHeaders()
    {
        var signer = CreateSigner();
        var request = HikCentralGateActionRequestFactory.CreateOpenExitBarrierRequest(
            CreateCommand(),
            CreateHandoff());

        var signed = signer.SignDoorControlRequest(request);

        Assert.Equal("test-ak", signed.Headers["X-Ca-Key"]);
        Assert.Equal("fixed-nonce", signed.Headers["X-Ca-Nonce"]);
        Assert.Equal("1479968678000", signed.Headers["X-Ca-Timestamp"]);
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", signed.Headers["X-Ca-Signature-Headers"]);
        Assert.Equal("application/json", signed.Headers["Content-Type"]);
        Assert.Equal("*/*", signed.Headers["Accept"]);
        Assert.Equal("exitpass-gate-integration", signed.Headers["userId"]);
        Assert.False(string.IsNullOrWhiteSpace(signed.Headers["X-Ca-Signature"]));
    }

    [Fact]
    public async Task FakeTransport_CapturesSignedRequestAndPerformsNoHttpCall()
    {
        var transport = new FakeHikCentralGateActionTransport();
        var signed = CreateSigner().SignDoorControlRequest(
            HikCentralGateActionRequestFactory.CreateOpenExitBarrierRequest(CreateCommand(), CreateHandoff()));

        var result = await transport.SendAsync(signed, CancellationToken.None);

        Assert.Single(transport.Requests);
        Assert.Same(signed, transport.Requests.Single());
        Assert.Equal(200, result.HttpStatusCode);
        Assert.False(result.TimedOut);
        Assert.False(result.VendorUnavailable);
    }

    [Fact]
    public async Task Adapter_WhenFakeSuccess_ReturnsVendorNeutralSuccessAndPreservesIdentity()
    {
        var adapter = CreateAdapter(new FakeHikCentralGateActionTransport());

        var result = await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Retryable);
        Assert.False(result.TerminalFailure);
        Assert.Equal(CommandId, result.VendorRequest.CommandId);
        Assert.Equal("exit-gate-01", result.VendorRequest.GateDeviceIdentifier);
        Assert.Equal("exit-gate-01", result.VendorRequest.DoorIndexCode);
        Assert.Equal(LaneId, result.VendorRequest.LaneId);
        Assert.Equal(SiteId, result.VendorRequest.SiteId);
        Assert.Equal(VendorSystemId, result.VendorRequest.VendorSystemId);
        Assert.Equal(ExitAuthorizationId, result.VendorRequest.ExitAuthorizationId);
        Assert.Equal(GateAuthorizationConsumptionId, result.VendorRequest.GateAuthorizationConsumptionId);
        Assert.Equal(TariffSnapshotId, result.VendorRequest.TariffSnapshotId);
        Assert.Equal(CorrelationId, result.VendorRequest.CorrelationId);
    }

    [Theory]
    [MemberData(nameof(FakeFailureCases))]
    public async Task Adapter_WhenFakeFailure_ReturnsClassifierOutcome(
        HikCentralGateActionTransportResult transportResult,
        HikCentralGateActionOutcome expectedOutcome,
        bool retryable,
        bool terminalFailure,
        string resultCode)
    {
        var transport = new FakeHikCentralGateActionTransport();
        transport.Enqueue(transportResult);
        var adapter = CreateAdapter(transport);

        var result = await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedOutcome, result.VendorResponse.Outcome);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal(terminalFailure, result.TerminalFailure);
        Assert.Equal(resultCode, result.ResultCode);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public void DefaultOptions_DoNotRequireCredentialsAndStayFake()
    {
        var options = new HikCentralGateActionOptions();

        Assert.Null(options.BaseUrl);
        Assert.Null(options.AppKey);
        Assert.Null(options.AppSecret);
        Assert.Equal("Fake", options.TransportMode);
    }

    public static IEnumerable<object[]> FakeFailureCases()
    {
        yield return
        [
            Transport(null, null, timedOut: true, transportError: "request timed out"),
            HikCentralGateActionOutcome.Timeout,
            true,
            false,
            "HIKCENTRAL_GATE_ACTION_TIMEOUT"
        ];
        yield return
        [
            Transport(503, null, vendorUnavailable: true),
            HikCentralGateActionOutcome.VendorUnavailable,
            true,
            false,
            "HIKCENTRAL_GATE_ACTION_VENDOR_UNAVAILABLE"
        ];
        yield return
        [
            Transport(401, new HikCentralGateActionEnvelope("SIGNATURE_INVALID", "Signature verification failed.", [])),
            HikCentralGateActionOutcome.Unauthorized,
            false,
            true,
            "HIKCENTRAL_GATE_ACTION_UNAUTHORIZED"
        ];
        yield return
        [
            Transport(400, new HikCentralGateActionEnvelope("INVALID_RESOURCE", "Door resource not found.", [])),
            HikCentralGateActionOutcome.InvalidRequest,
            false,
            true,
            "HIKCENTRAL_GATE_ACTION_INVALID_REQUEST"
        ];
        yield return
        [
            Transport(200, new HikCentralGateActionEnvelope("0x99999999", "Unexpected vendor result.", [])),
            HikCentralGateActionOutcome.Unknown,
            true,
            false,
            "HIKCENTRAL_GATE_ACTION_UNKNOWN_FAILURE"
        ];
    }

    private static HikCentralConsumedAuthorizationGateActionAdapter CreateAdapter(
        FakeHikCentralGateActionTransport transport) =>
        new(CreateSigner(), transport, new InMemoryHikCentralGateActionAuditRecorder());

    private static HikCentralRequestSigner CreateSigner() =>
        new(
            new HikCentralGateActionOptions
            {
                AppKey = "test-ak",
                AppSecret = "test-secret",
                UserId = "exitpass-gate-integration"
            },
            new FixedClock(FixedTimestamp),
            new FixedNonceProvider("fixed-nonce"));

    private static HikCentralGateActionTransportResult Transport(
        int? httpStatus,
        HikCentralGateActionEnvelope? envelope,
        bool timedOut = false,
        bool vendorUnavailable = false,
        string? transportError = null) =>
        new(
            httpStatus,
            envelope,
            VendorRequestId: "hik-request-1",
            VendorCorrelationId: CorrelationId.ToString("D"),
            timedOut,
            vendorUnavailable,
            transportError,
            DateTimeOffset.Parse("2026-05-31T08:10:00Z"));

    private static GateCommandLifecycleRecord CreateCommand() =>
        new(
            CommandId,
            SourceProcessingId,
            SourceEventId,
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            TariffSnapshotId,
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

    private static GateAuthorizationConsumedHandoff CreateHandoff() =>
        new(
            SourceEventId,
            SourceEventRef: $"central-pms://integration-events/{SourceEventId}",
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            TariffSnapshotId,
            GateDeviceId,
            GateDeviceIdentifier: "exit-gate-01",
            LaneId,
            SiteId,
            VendorSystemId,
            ConsumedAtUtc: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            CorrelationId);

    private sealed class FixedClock(DateTimeOffset utcNow) : IHikCentralClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedNonceProvider(string nonce) : IHikCentralNonceProvider
    {
        public string CreateNonce() => nonce;
    }
}

#pragma warning restore CS1591
