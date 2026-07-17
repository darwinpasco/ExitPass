using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for the composed HikCentral gate-action adapter orchestration.
/// </summary>
public sealed class HikCentralGateActionAdapterTests
{
    private static readonly Uri BaseAddress = new("https://hikcentral.test:8443/");

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_RunsEachStageOnceAndMapsAcceptedResult()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0","msg":"Success"}"""));
        using var client = new HttpClient(handler);
        var provider = new TestRuntimeMaterialProvider();
        var planBuilder = new SpyRequestPlanBuilder(new HikCentralGateActionRequestPlanBuilder());
        var signingMaterialBuilder = new SpySigningMaterialBuilder(new HikCentralRequestSigningMaterialBuilder());
        var signatureCalculator = new SpySignatureCalculator(new HikCentralRequestSignatureCalculator());
        var signedRequestBuilder = new SpySignedRequestBuilder(new HikCentralSignedHttpRequestBuilder());
        var transport = new HikCentralHttpTransport(client);
        var adapter = new HikCentralGateActionAdapter(
            provider,
            planBuilder,
            signingMaterialBuilder,
            signatureCalculator,
            signedRequestBuilder,
            transport);
        var request = ValidRequest();

        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, planBuilder.CallCount);
        Assert.Equal(1, signingMaterialBuilder.CallCount);
        Assert.Equal(1, signatureCalculator.CallCount);
        Assert.Equal(1, signedRequestBuilder.CallCount);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HikCentralGateActionConstants.VendorCode, result.VendorCode);
        Assert.Equal(HikCentralGateActionConstants.RequestMethod, result.RequestMethod);
        Assert.Equal(HikCentralGateActionConstants.OpenGateOperation, result.VendorOperation);
        Assert.Equal(HikCentralGateActionConstants.OutcomeSucceeded, result.ActionOutcome);
        Assert.False(result.Retryable);
        Assert.False(result.FailureRecorded);
        Assert.False(result.TimedOut);
        Assert.False(result.VendorUnavailable);
        Assert.False(result.TransportFailure);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("0", result.VendorResultCode);
        Assert.Equal("Success", result.VendorResultMessage);
        Assert.Equal(request.CorrelationId, result.RequestCorrelationId);
        Assert.Equal(request.TargetResourceCode, result.TargetResourceCode);
        Assert.Equal(request.RequestedAt, result.RequestedAt);
        Assert.DoesNotContain("PHYSICAL_GATE_OPENED", result.ActionOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.Same(provider.LastMaterial!.ControlProfile, planBuilder.LastProfile);
        Assert.Equal("EXIT-GATE-01", planBuilder.LastPlan!.TargetResourceCode);
        Assert.Contains("\"doorIndexCode\":\"EXIT-GATE-01\"", Encoding.UTF8.GetString(planBuilder.LastPlan.BodyUtf8), StringComparison.Ordinal);
        Assert.Equal("1479968678000", signingMaterialBuilder.LastInput!.TimestampMilliseconds);
        Assert.Equal("fixed-nonce", signingMaterialBuilder.LastInput.Nonce);
        Assert.Equal("test-client-key", signingMaterialBuilder.LastInput.ClientKeyIdentifier);
        Assert.Equal("test-app-secret", Encoding.UTF8.GetString(signatureCalculator.LastSecretBytes!));
        Assert.Equal<byte>(planBuilder.LastPlan.BodyUtf8, handler.LastRequestBody!);
        Assert.True(provider.LastMaterial.IsDisposed);
        AssertCleared(provider.LastMaterial);
        Assert.Equal<byte>(provider.OriginalSecretBytes, Encoding.UTF8.GetBytes("test-app-secret"));
    }

    [Theory]
    [MemberData(nameof(TransportMappings))]
    public async Task ExecuteAsync_MapsTransportOutcomesToSafeAdapterResults(
        HikCentralHttpTransportResult transportResult,
        string expectedActionOutcome,
        bool expectedRetryable,
        bool expectedFailureRecorded)
    {
        var provider = new TestRuntimeMaterialProvider();
        var transport = new StubTransport(transportResult);
        var adapter = CreateAdapter(provider, transport: transport);

        var result = await adapter.ExecuteAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(1, transport.CallCount);
        Assert.Equal(expectedActionOutcome, result.ActionOutcome);
        Assert.Equal(expectedRetryable, result.Retryable);
        Assert.Equal(expectedFailureRecorded, result.FailureRecorded);
        Assert.Equal(transportResult.TimedOut, result.TimedOut);
        Assert.Equal(transportResult.VendorUnavailable, result.VendorUnavailable);
        Assert.Equal(transportResult.TransportFailure, result.TransportFailure);
        Assert.Equal(transportResult.HttpStatusCode, result.HttpStatusCode);
        Assert.Equal(transportResult.VendorResultCode, result.VendorResultCode);
        Assert.Equal(transportResult.VendorResultMessage, result.VendorResultMessage);
        Assert.Equal(transportResult.VendorCorrelationId, result.VendorCorrelationId);
        Assert.True(provider.LastMaterial!.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPreCancelled_InvokesNoStage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new TestRuntimeMaterialProvider();
        var adapter = CreateAdapter(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(ValidRequest(), cancellation.Token));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProviderRetrievalIsCancelled_SendsNothing()
    {
        var provider = new CancellingRuntimeMaterialProvider();
        var transport = new StubTransport(TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200));
        var adapter = CreateAdapter(provider, transport: transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransportCancels_DisposesRequestAndRuntimeMaterial()
    {
        var provider = new TestRuntimeMaterialProvider();
        var signedRequestBuilder = new TrackingSignedRequestBuilder();
        var transport = new CancellingTransport();
        var adapter = CreateAdapter(provider, signedRequestBuilder: signedRequestBuilder, transport: transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal(1, transport.CallCount);
        Assert.True(signedRequestBuilder.LastRequest!.Disposed);
        Assert.True(provider.LastMaterial!.IsDisposed);
        AssertCleared(provider.LastMaterial);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestPlanningFails_PreventsLaterStagesAndClearsMaterial()
    {
        var provider = new TestRuntimeMaterialProvider(controlProfile: HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
        {
            ControlMechanism = HikCentralGateControlMechanism.AlarmOutputControl
        });
        var signingMaterialBuilder = new SpySigningMaterialBuilder(new HikCentralRequestSigningMaterialBuilder());
        var signatureCalculator = new SpySignatureCalculator(new HikCentralRequestSignatureCalculator());
        var signedRequestBuilder = new TrackingSignedRequestBuilder();
        var transport = new StubTransport(TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200));
        var adapter = CreateAdapter(
            provider,
            signingMaterialBuilder: signingMaterialBuilder,
            signatureCalculator: signatureCalculator,
            signedRequestBuilder: signedRequestBuilder,
            transport: transport);

        await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal(0, signingMaterialBuilder.CallCount);
        Assert.Equal(0, signatureCalculator.CallCount);
        Assert.Equal(0, signedRequestBuilder.CallCount);
        Assert.Equal(0, transport.CallCount);
        Assert.True(provider.LastMaterial!.IsDisposed);
        AssertCleared(provider.LastMaterial);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSigningMaterialFails_PreventsSignatureRequestAndTransport()
    {
        var provider = new TestRuntimeMaterialProvider(nonce: "bad nonce with spaces");
        var signatureCalculator = new SpySignatureCalculator(new HikCentralRequestSignatureCalculator());
        var signedRequestBuilder = new TrackingSignedRequestBuilder();
        var transport = new StubTransport(TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200));
        var adapter = CreateAdapter(
            provider,
            signatureCalculator: signatureCalculator,
            signedRequestBuilder: signedRequestBuilder,
            transport: transport);

        await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal(0, signatureCalculator.CallCount);
        Assert.Equal(0, signedRequestBuilder.CallCount);
        Assert.Equal(0, transport.CallCount);
        Assert.True(provider.LastMaterial!.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSignatureCalculationFails_PreventsRequestConstructionAndTransport()
    {
        var provider = new TestRuntimeMaterialProvider(signatureMethod: "HmacSHA1");
        var signedRequestBuilder = new TrackingSignedRequestBuilder();
        var transport = new StubTransport(TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200));
        var adapter = CreateAdapter(provider, signedRequestBuilder: signedRequestBuilder, transport: transport);

        await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal(0, signedRequestBuilder.CallCount);
        Assert.Equal(0, transport.CallCount);
        Assert.True(provider.LastMaterial!.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestBuilderFails_PreventsTransport()
    {
        var provider = new TestRuntimeMaterialProvider(baseAddress: new Uri("http://hikcentral.test"));
        var transport = new StubTransport(TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200));
        var adapter = CreateAdapter(provider, transport: transport);

        await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal(0, transport.CallCount);
        Assert.True(provider.LastMaterial!.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransportFails_DoesNotRetry()
    {
        var transport = new StubTransport(TransportResult(
            HikCentralHttpTransportOutcome.TransportFailure,
            transportFailure: true,
            vendorUnavailable: true));
        var adapter = CreateAdapter(new TestRuntimeMaterialProvider(), transport: transport);

        var result = await adapter.ExecuteAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(1, transport.CallCount);
        Assert.Equal(HikCentralGateActionConstants.OutcomeTransportFailure, result.ActionOutcome);
        Assert.True(result.Retryable);
        Assert.True(result.FailureRecorded);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRejected_DoesNotExposeSensitiveValues()
    {
        var provider = new TestRuntimeMaterialProvider(baseAddress: new Uri("http://hikcentral.test"));
        var adapter = CreateAdapter(provider);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.DoesNotContain("test-app-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fixed-nonce", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Ca-Signature", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeMaterial_ToStringAndResultShape_DoNotExposeSecretsOrPhysicalGateClaim()
    {
        using var material = new TestRuntimeMaterialProvider().CreateMaterial();

        Assert.DoesNotContain("test-app-secret", material.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-key", material.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fixed-nonce", material.ToString(), StringComparison.Ordinal);

        var propertyNames = typeof(HikCentralGateActionResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Signature", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Physical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Opened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adapter_DoesNotDeclareConfigurationLoggingHttpClientDatabaseCommandAuditWorkerDependencies()
    {
        var constructorParameters = typeof(HikCentralGateActionAdapter)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralGateActionAdapter)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.DoesNotContain(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.DoesNotContain(constructorParameters, IsForbiddenRuntimeDependency);
        Assert.DoesNotContain(fieldTypes, IsForbiddenRuntimeDependency);
    }

    public static IEnumerable<object[]> TransportMappings()
    {
        yield return [TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200, vendorCode: "0"), HikCentralGateActionConstants.OutcomeSucceeded, false, false];
        yield return [TransportResult(HikCentralHttpTransportOutcome.ClientError, httpStatusCode: 400, vendorCode: "400"), HikCentralGateActionConstants.OutcomeTerminalFailure, false, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.Unauthorized, httpStatusCode: 401, vendorCode: "401"), HikCentralGateActionConstants.OutcomeTerminalFailure, false, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.Forbidden, httpStatusCode: 403, vendorCode: "403"), HikCentralGateActionConstants.OutcomeTerminalFailure, false, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.Throttled, httpStatusCode: 429, vendorCode: "429"), HikCentralGateActionConstants.OutcomeRetryableFailure, true, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.RequestTimeout, httpStatusCode: 408, timedOut: true, vendorCode: "408"), HikCentralGateActionConstants.OutcomeTimeout, true, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.TimedOut, timedOut: true, vendorCode: "TIMEOUT"), HikCentralGateActionConstants.OutcomeTimeout, true, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.VendorFailure, httpStatusCode: 500, vendorUnavailable: true, vendorCode: "500"), HikCentralGateActionConstants.OutcomeVendorUnavailable, true, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.VendorFailure, httpStatusCode: 500, vendorCode: "500"), HikCentralGateActionConstants.OutcomeRetryableFailure, true, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.TransportFailure, transportFailure: true, vendorUnavailable: true, vendorCode: "TRANSPORT"), HikCentralGateActionConstants.OutcomeTransportFailure, true, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.MalformedResponse, httpStatusCode: 200), HikCentralGateActionConstants.OutcomeTerminalFailure, false, true];
        yield return [TransportResult(HikCentralHttpTransportOutcome.ResponseBodyTooLarge, httpStatusCode: 200), HikCentralGateActionConstants.OutcomeTerminalFailure, false, true];
    }

    private static HikCentralGateActionAdapter CreateAdapter(
        IHikCentralGateRuntimeMaterialProvider? provider = null,
        IHikCentralGateActionRequestPlanBuilder? requestPlanBuilder = null,
        IHikCentralRequestSigningMaterialBuilder? signingMaterialBuilder = null,
        IHikCentralRequestSignatureCalculator? signatureCalculator = null,
        IHikCentralSignedHttpRequestBuilder? signedRequestBuilder = null,
        IHikCentralHttpTransport? transport = null) =>
        new(
            provider ?? new TestRuntimeMaterialProvider(),
            requestPlanBuilder ?? new HikCentralGateActionRequestPlanBuilder(),
            signingMaterialBuilder ?? new HikCentralRequestSigningMaterialBuilder(),
            signatureCalculator ?? new HikCentralRequestSignatureCalculator(),
            signedRequestBuilder ?? new HikCentralSignedHttpRequestBuilder(),
            transport ?? new StubTransport(TransportResult(HikCentralHttpTransportOutcome.Succeeded, httpStatusCode: 200, vendorCode: "0")));

    private static HikCentralGateActionRequest ValidRequest() =>
        new(
            GateCommandId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            GateAuthorizationConsumptionId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            ExitAuthorizationId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            GateDeviceId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            VendorSystemId: Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
            SiteId: Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            LaneId: Guid.Parse("11111111-0000-0000-0000-000000000001"),
            TargetResourceCode: "EXIT-GATE-01",
            VendorOperation: HikCentralGateActionConstants.OpenGateOperation,
            CorrelationId: Guid.Parse("22222222-0000-0000-0000-000000000001"),
            RequestedAt: DateTimeOffset.Parse("2026-07-17T08:00:00Z"));

    private static HikCentralHttpTransportResult TransportResult(
        HikCentralHttpTransportOutcome outcome,
        int? httpStatusCode = null,
        bool timedOut = false,
        bool transportFailure = false,
        bool vendorUnavailable = false,
        string? vendorCode = null) =>
        new(
            httpStatusCode,
            httpStatusCode is >= 200 and <= 299,
            outcome,
            timedOut,
            transportFailure,
            vendorUnavailable,
            outcome == HikCentralHttpTransportOutcome.ResponseBodyTooLarge,
            ResponseBodyByteCount: 18,
            ResponseBodySha256: Sha256Hex(Encoding.UTF8.GetBytes("""{"code":"0"}""")),
            VendorResultCode: vendorCode,
            VendorResultMessage: vendorCode is null ? null : "Vendor message",
            VendorCorrelationId: "vendor-correlation-id",
            DurationMs: 25,
            RespondedAt: DateTimeOffset.Parse("2026-07-17T08:00:00.025Z"));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertCleared(HikCentralGateRuntimeMaterial material)
    {
        var secretBytes = (byte[])typeof(HikCentralGateRuntimeMaterial)
            .GetField("_appSecretBytes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(material)!;

        Assert.All(secretBytes, value => Assert.Equal(0, value));
    }

    private static bool IsForbiddenRuntimeDependency(Type type)
    {
        if (type == typeof(IHikCentralGateRuntimeMaterialProvider) ||
            type == typeof(IHikCentralGateActionRequestPlanBuilder) ||
            type == typeof(IHikCentralRequestSigningMaterialBuilder) ||
            type == typeof(IHikCentralRequestSignatureCalculator) ||
            type == typeof(IHikCentralSignedHttpRequestBuilder) ||
            type == typeof(IHikCentralHttpTransport))
        {
            return false;
        }

        return type.Namespace?.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true ||
               type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
               type == typeof(HttpClient) ||
               type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Worker", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestRuntimeMaterialProvider : IHikCentralGateRuntimeMaterialProvider
    {
        private readonly Uri _baseAddress;
        private readonly HikCentralGateControlProfile _controlProfile;
        private readonly string _clientKeyIdentifier;
        private readonly byte[] _secretBytes;
        private readonly string _timestampMilliseconds;
        private readonly string _nonce;
        private readonly string _signatureMethod;

        public TestRuntimeMaterialProvider(
            Uri? baseAddress = null,
            HikCentralGateControlProfile? controlProfile = null,
            string clientKeyIdentifier = "test-client-key",
            string appSecret = "test-app-secret",
            string timestampMilliseconds = "1479968678000",
            string nonce = "fixed-nonce",
            string signatureMethod = "HmacSHA256")
        {
            _baseAddress = baseAddress ?? BaseAddress;
            _controlProfile = controlProfile ?? HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE");
            _clientKeyIdentifier = clientKeyIdentifier;
            _secretBytes = Encoding.UTF8.GetBytes(appSecret);
            _timestampMilliseconds = timestampMilliseconds;
            _nonce = nonce;
            _signatureMethod = signatureMethod;
        }

        public int CallCount { get; private set; }

        public HikCentralGateRuntimeMaterial? LastMaterial { get; private set; }

        public byte[] OriginalSecretBytes => _secretBytes;

        public ValueTask<HikCentralGateRuntimeMaterial> GetAsync(
            HikCentralGateActionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastMaterial = CreateMaterial();
            return ValueTask.FromResult(LastMaterial);
        }

        public HikCentralGateRuntimeMaterial CreateMaterial() =>
            new(
                _baseAddress,
                _controlProfile,
                _clientKeyIdentifier,
                _secretBytes,
                _timestampMilliseconds,
                _nonce,
                _signatureMethod);
    }

    private sealed class CancellingRuntimeMaterialProvider : IHikCentralGateRuntimeMaterialProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<HikCentralGateRuntimeMaterial> GetAsync(
            HikCentralGateActionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new OperationCanceledException();
        }
    }

    private sealed class SpyRequestPlanBuilder : IHikCentralGateActionRequestPlanBuilder
    {
        private readonly IHikCentralGateActionRequestPlanBuilder _inner;

        public SpyRequestPlanBuilder(IHikCentralGateActionRequestPlanBuilder inner)
        {
            _inner = inner;
        }

        public int CallCount { get; private set; }

        public HikCentralGateControlProfile? LastProfile { get; private set; }

        public HikCentralGateActionRequestPlan? LastPlan { get; private set; }

        public HikCentralGateActionRequestPlan Build(
            HikCentralGateActionRequest request,
            HikCentralGateControlProfile profile)
        {
            CallCount++;
            LastProfile = profile;
            LastPlan = _inner.Build(request, profile);
            return LastPlan;
        }
    }

    private sealed class SpySigningMaterialBuilder : IHikCentralRequestSigningMaterialBuilder
    {
        private readonly IHikCentralRequestSigningMaterialBuilder _inner;

        public SpySigningMaterialBuilder(IHikCentralRequestSigningMaterialBuilder inner)
        {
            _inner = inner;
        }

        public int CallCount { get; private set; }

        public HikCentralSigningMaterialInput? LastInput { get; private set; }

        public HikCentralSigningMaterial Build(HikCentralSigningMaterialInput input)
        {
            CallCount++;
            LastInput = input;
            return _inner.Build(input);
        }
    }

    private sealed class SpySignatureCalculator : IHikCentralRequestSignatureCalculator
    {
        private readonly IHikCentralRequestSignatureCalculator _inner;

        public SpySignatureCalculator(IHikCentralRequestSignatureCalculator inner)
        {
            _inner = inner;
        }

        public int CallCount { get; private set; }

        public byte[]? LastSecretBytes { get; private set; }

        public HikCentralRequestSignature Calculate(
            HikCentralSigningMaterial signingMaterial,
            ReadOnlySpan<byte> appSecretBytes)
        {
            CallCount++;
            LastSecretBytes = appSecretBytes.ToArray();
            return _inner.Calculate(signingMaterial, appSecretBytes);
        }
    }

    private sealed class SpySignedRequestBuilder : IHikCentralSignedHttpRequestBuilder
    {
        private readonly IHikCentralSignedHttpRequestBuilder _inner;

        public SpySignedRequestBuilder(IHikCentralSignedHttpRequestBuilder inner)
        {
            _inner = inner;
        }

        public int CallCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public HttpRequestMessage Build(
            Uri baseAddress,
            HikCentralGateActionRequestPlan requestPlan,
            HikCentralSigningMaterial signingMaterial,
            HikCentralRequestSignature signature)
        {
            CallCount++;
            LastRequest = _inner.Build(baseAddress, requestPlan, signingMaterial, signature);
            return LastRequest;
        }
    }

    private sealed class TrackingSignedRequestBuilder : IHikCentralSignedHttpRequestBuilder
    {
        public int CallCount { get; private set; }

        public TrackingHttpRequestMessage? LastRequest { get; private set; }

        public HttpRequestMessage Build(
            Uri baseAddress,
            HikCentralGateActionRequestPlan requestPlan,
            HikCentralSigningMaterial signingMaterial,
            HikCentralRequestSignature signature)
        {
            CallCount++;
            LastRequest = new TrackingHttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, requestPlan.RelativePath.TrimStart('/')))
            {
                Content = new ByteArrayContent(requestPlan.BodyUtf8)
            };
            return LastRequest;
        }
    }

    private sealed class TrackingHttpRequestMessage : HttpRequestMessage
    {
        public TrackingHttpRequestMessage(HttpMethod method, Uri requestUri)
            : base(method, requestUri)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StubTransport : IHikCentralHttpTransport
    {
        private readonly HikCentralHttpTransportResult _result;

        public StubTransport(HikCentralHttpTransportResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<HikCentralHttpTransportResult> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class CancellingTransport : IHikCentralHttpTransport
    {
        public int CallCount { get; private set; }

        public Task<HikCentralHttpTransportResult> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new OperationCanceledException();
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public byte[]? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return _handler(request);
        }
    }
}
