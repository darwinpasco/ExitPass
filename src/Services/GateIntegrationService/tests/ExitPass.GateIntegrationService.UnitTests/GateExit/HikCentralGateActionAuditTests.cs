using System.Security.Cryptography;
using System.Text;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class HikCentralGateActionAuditTests
{
    private static readonly Guid CommandId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceProcessingId = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid SourceEventId = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("a2000000-0000-0000-0000-000000000001");
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("a3000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("a4000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("a5000000-0000-0000-0000-000000000001");
    private static readonly Guid TariffSnapshotId = Guid.Parse("a6000000-0000-0000-0000-000000000001");
    private static readonly Guid GateDeviceId = Guid.Parse("a7000000-0000-0000-0000-000000000001");
    private static readonly Guid LaneId = Guid.Parse("a8000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("a9000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorSystemId = Guid.Parse("aa000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("ab000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset FixedTimestamp =
        DateTimeOffset.Parse("2026-05-31T08:10:00Z");

    [Fact]
    public async Task FakeSuccess_WritesSafeAuditMetadata()
    {
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var adapter = CreateAdapter(new FakeHikCentralGateActionTransport(), audit);

        var result = await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var record = Assert.Single(audit.Records);
        Assert.Equal(CommandId, record.GateCommandId);
        Assert.Equal(SourceProcessingId, record.SourceProcessingId);
        Assert.Equal(ExitAuthorizationId, record.ExitAuthorizationId);
        Assert.Equal(GateAuthorizationConsumptionId, record.GateAuthorizationConsumptionId);
        Assert.Equal(ParkingSessionId, record.ParkingSessionId);
        Assert.Equal(PaymentAttemptId, record.PaymentAttemptId);
        Assert.Equal(TariffSnapshotId, record.TariffSnapshotId);
        Assert.Equal(GateDeviceId, record.GateDeviceId);
        Assert.Equal("exit-gate-01", record.GateDeviceIdentifier);
        Assert.Equal("exit-gate-01", record.DoorIndexCode);
        Assert.Equal(LaneId, record.LaneId);
        Assert.Equal(SiteId, record.SiteId);
        Assert.Equal(VendorSystemId, record.VendorSystemId);
        Assert.Equal("HikCentral", record.VendorCode);
        Assert.Equal("doorControl", record.Operation);
        Assert.Equal("POST", record.RequestMethod);
        Assert.Equal(HikCentralRequestSigner.DoorControlPath, record.RequestPath);
        Assert.Equal(Sha256Hex(result.SignedRequest.Body), record.RequestBodySha256);
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", record.SignedHeadersList);
        Assert.Equal(CorrelationId, record.RequestCorrelationId);
        Assert.Equal("fake-hikcentral-request", record.VendorRequestId);
        Assert.Equal(200, record.HttpStatusCode);
        Assert.Equal("0", record.VendorResponseCode);
        Assert.Equal("Success", record.VendorResponseMessage);
        Assert.Equal(nameof(HikCentralGateActionOutcome.Succeeded), record.OutcomeCategory);
        Assert.False(record.Retryable);
        Assert.False(record.TerminalFailure);
        Assert.False(record.TimeoutOccurred);
        Assert.False(record.VendorUnavailable);
        Assert.True(record.DurationMs >= 0);
    }

    [Theory]
    [MemberData(nameof(AuditFailureCases))]
    public async Task FakeFailure_WritesClassifiedAuditMetadata(
        HikCentralGateActionTransportResult transportResult,
        HikCentralGateActionOutcome expectedOutcome,
        bool retryable,
        bool terminalFailure,
        bool timeout,
        bool vendorUnavailable,
        string? transportErrorCode)
    {
        var transport = new FakeHikCentralGateActionTransport();
        transport.Enqueue(transportResult);
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var adapter = CreateAdapter(transport, audit);

        var result = await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);

        Assert.False(result.Succeeded);
        var record = Assert.Single(audit.Records);
        Assert.Equal(expectedOutcome.ToString(), record.OutcomeCategory);
        Assert.Equal(retryable, record.Retryable);
        Assert.Equal(terminalFailure, record.TerminalFailure);
        Assert.Equal(timeout, record.TimeoutOccurred);
        Assert.Equal(vendorUnavailable, record.VendorUnavailable);
        Assert.Equal(transportErrorCode, record.TransportErrorCode);
        Assert.Equal(transportResult.HttpStatusCode, record.HttpStatusCode);
        Assert.Equal(result.VendorResponse.VendorResponseCode, record.VendorResponseCode);
        Assert.Equal(result.VendorResponse.VendorResponseMessage, record.VendorResponseMessage);
    }

    [Fact]
    public async Task Audit_DoesNotStoreSecretsSignaturesOrRawBody()
    {
        const string appSecret = "test-secret-never-persist";
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var adapter = CreateAdapter(new FakeHikCentralGateActionTransport(), audit, appSecret);

        var result = await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);
        var record = Assert.Single(audit.Records);
        var persisted = string.Join(
            "|",
            record.RequestBodySha256,
            record.SignedHeadersList,
            record.TransportErrorMessage,
            record.VendorResponseMessage,
            record.VendorRequestId);

        Assert.DoesNotContain(appSecret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(result.SignedRequest.Signature, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(result.SignedRequest.Body, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Ca-Signature", record.SignedHeadersList, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{64}$", record.RequestBodySha256);
    }

    [Fact]
    public async Task MultipleAttempts_ForSameCommand_CreateMultipleAuditRecords()
    {
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var adapter = CreateAdapter(new FakeHikCentralGateActionTransport(), audit);

        await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);
        await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);

        Assert.Equal(2, audit.Records.Count);
        Assert.All(audit.Records, record => Assert.Equal(CommandId, record.GateCommandId));
        Assert.Equal(2, audit.Records.Select(record => record.AuditId).Distinct().Count());
    }

    [Fact]
    public async Task LiveTransport_WithFakeHttpHandler_WritesAuditWithoutNetwork()
    {
        var handler = new CapturingHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hikcentral.test"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var transport = new LiveHikCentralGateActionTransport(
            httpClient,
            new HikCentralGateActionOptions
            {
                BaseUrl = "https://hikcentral.test",
                AppKey = "test-ak",
                AppSecret = "test-secret",
                LiveTransportEnabled = true,
                RequestTimeoutSeconds = 10
            });
        var audit = new InMemoryHikCentralGateActionAuditRecorder();
        var adapter = new HikCentralConsumedAuthorizationGateActionAdapter(
            CreateSigner("test-secret"),
            transport,
            audit);

        var result = await adapter.ProcessCommandAsync(CreateCommand(), CreateHandoff(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, handler.SendCount);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(HikCentralRequestSigner.DoorControlPath, handler.LastPathAndQuery);
        var record = Assert.Single(audit.Records);
        Assert.Equal(nameof(HikCentralGateActionOutcome.Succeeded), record.OutcomeCategory);
        Assert.Equal(200, record.HttpStatusCode);
        Assert.Equal("live-fake-request-1", record.VendorRequestId);
    }

    public static IEnumerable<object[]> AuditFailureCases()
    {
        yield return
        [
            Transport(null, null, timedOut: true, transportError: "request timed out"),
            HikCentralGateActionOutcome.Timeout,
            true,
            false,
            true,
            false,
            "TIMEOUT"
        ];
        yield return
        [
            Transport(503, null, vendorUnavailable: true, transportError: "connection refused by host"),
            HikCentralGateActionOutcome.VendorUnavailable,
            true,
            false,
            false,
            true,
            "VENDOR_UNAVAILABLE"
        ];
        yield return
        [
            Transport(401, new HikCentralGateActionEnvelope("SIGNATURE_INVALID", "Signature verification failed.", [])),
            HikCentralGateActionOutcome.Unauthorized,
            false,
            true,
            false,
            false,
            nameof(HikCentralGateActionOutcome.Unauthorized)
        ];
        yield return
        [
            Transport(400, new HikCentralGateActionEnvelope("INVALID_RESOURCE", "Door resource not found.", [])),
            HikCentralGateActionOutcome.InvalidRequest,
            false,
            true,
            false,
            false,
            nameof(HikCentralGateActionOutcome.InvalidRequest)
        ];
        yield return
        [
            Transport(200, null, transportError: "HikCentral response body was empty."),
            HikCentralGateActionOutcome.Unknown,
            true,
            false,
            false,
            false,
            nameof(HikCentralGateActionOutcome.Unknown)
        ];
    }

    private static HikCentralConsumedAuthorizationGateActionAdapter CreateAdapter(
        FakeHikCentralGateActionTransport transport,
        InMemoryHikCentralGateActionAuditRecorder audit,
        string appSecret = "test-secret") =>
        new(CreateSigner(appSecret), transport, audit);

    private static HikCentralRequestSigner CreateSigner(string appSecret) =>
        new(
            new HikCentralGateActionOptions
            {
                AppKey = "test-ak",
                AppSecret = appSecret,
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

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FixedClock(DateTimeOffset utcNow) : IHikCentralClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedNonceProvider(string nonce) : IHikCentralNonceProvider
    {
        public string CreateNonce() => nonce;
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastPathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastMethod = request.Method;
            LastPathAndQuery = request.RequestUri?.PathAndQuery;

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"code\":\"0\",\"msg\":\"Success\",\"data\":[[{\"doorIndexCode\":\"exit-gate-01\",\"controlResultCode\":0,\"controlResultDesc\":\"Success\"}]]}",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.TryAddWithoutValidation("X-Ca-Request-Id", "live-fake-request-1");
            return Task.FromResult(response);
        }
    }
}

#pragma warning restore CS1591
