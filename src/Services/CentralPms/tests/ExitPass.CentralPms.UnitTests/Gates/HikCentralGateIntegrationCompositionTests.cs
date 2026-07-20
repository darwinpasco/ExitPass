using System.Net;
using System.Reflection;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for disabled-by-default, fail-closed HikCentral live gate integration composition.
/// </summary>
public sealed class HikCentralGateIntegrationCompositionTests
{
    private static readonly DateTimeOffset RequestedAt = DateTimeOffset.Parse("2026-07-20T08:00:00Z");

    [Fact]
    public void Options_DefaultToDisabledAndPermitEmptyDeploymentValues()
    {
        var options = new HikCentralGateIntegrationOptions();

        Assert.False(options.Enabled);
        Assert.Empty(options.Validate());
        Assert.Equal(HikCentralGateSecretFileOptions.DefaultMaxSecretBytes, options.MaxSecretBytes);
        Assert.Equal(HikCentralGateIntegrationOptions.DefaultHttpTimeoutSeconds, options.HttpTimeoutSeconds);
        Assert.Equal(HikCentralHttpTransportOptions.DefaultMaxResponseBodyBytes, options.MaxResponseBodyBytes);
    }

    [Fact]
    public void DisabledComposition_RegistersNoLiveAdapterChainHttpClientOrWorker()
    {
        var services = new ServiceCollection();
        services.AddHikCentralGateIntegration(new HikCentralGateIntegrationOptions
        {
            Enabled = false,
            SecretFilePath = @"C:\not-read\placeholder-secret.txt"
        });

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IHikCentralGateActionAdapter>());
        Assert.Null(provider.GetService<IHikCentralGateSecretSource>());
        Assert.Null(provider.GetService<IHikCentralGateRuntimeMaterialProvider>());
        Assert.Null(provider.GetService<IHikCentralHttpTransport>());
        Assert.Null(provider.GetService<IHttpClientFactory>());
        Assert.DoesNotContain(services, IsHostedServiceDescriptor);
    }

    [Fact]
    public void DisabledComposition_FromEmptyConfiguration_StartsWithoutSecretFileAccess()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddHikCentralGateIntegration(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IHikCentralGateActionAdapter>());
    }

    [Theory]
    [MemberData(nameof(InvalidEnabledOptions))]
    public void EnabledComposition_WithInvalidConfiguration_FailsClosedWithoutSensitiveValues(
        HikCentralGateIntegrationOptions options,
        string expectedCode)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddHikCentralGateIntegration(options));

        Assert.Contains(expectedCode, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-client-key-sensitive", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hikcentral-secret-placeholder.txt", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-secret-not-real", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledComposition_WithValidConfiguration_ResolvesOnlyTheLiveChain()
    {
        using var secretFile = TemporarySecretFile.Create("placeholder-secret-not-real");
        var services = new ServiceCollection();

        services.AddHikCentralGateIntegration(ValidEnabledOptions(secretFile.Path));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IHikCentralGateActionAdapter>();
        var adapters = provider.GetServices<IHikCentralGateActionAdapter>().ToArray();

        Assert.IsType<HikCentralGateActionAdapter>(adapter);
        Assert.Single(adapters);
        Assert.DoesNotContain(adapters, candidate => candidate is FakeHikCentralGateActionAdapter);
        Assert.IsType<MountedFileHikCentralGateSecretSource>(provider.GetRequiredService<IHikCentralGateSecretSource>());
        Assert.IsType<CryptographicHikCentralNonceGenerator>(provider.GetRequiredService<IHikCentralNonceGenerator>());
        Assert.IsType<HikCentralGateRuntimeMaterialProvider>(provider.GetRequiredService<IHikCentralGateRuntimeMaterialProvider>());
        Assert.IsType<HikCentralGateActionRequestPlanBuilder>(provider.GetRequiredService<IHikCentralGateActionRequestPlanBuilder>());
        Assert.IsType<HikCentralRequestSigningMaterialBuilder>(provider.GetRequiredService<IHikCentralRequestSigningMaterialBuilder>());
        Assert.IsType<HikCentralRequestSignatureCalculator>(provider.GetRequiredService<IHikCentralRequestSignatureCalculator>());
        Assert.IsType<HikCentralSignedHttpRequestBuilder>(provider.GetRequiredService<IHikCentralSignedHttpRequestBuilder>());
        Assert.IsType<HikCentralHttpTransport>(provider.GetRequiredService<IHikCentralHttpTransport>());
        Assert.DoesNotContain(services, IsHostedServiceDescriptor);
    }

    [Fact]
    public void EnabledComposition_ConfiguresHikCentralHttpClientTimeoutWithoutBaseAddressOrDefaultHeaders()
    {
        using var secretFile = TemporarySecretFile.Create("placeholder-secret-not-real");
        var services = new ServiceCollection();

        services.AddHikCentralGateIntegration(ModifiedOptions(secretFile.Path, options =>
        {
            options.HttpTimeoutSeconds = 7;
        }));

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IHikCentralHttpTransport>();
        var httpClient = (HttpClient)typeof(HikCentralHttpTransport)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(transport)!;

        Assert.Equal(TimeSpan.FromSeconds(7), httpClient.Timeout);
        Assert.Null(httpClient.BaseAddress);
        Assert.Empty(httpClient.DefaultRequestHeaders);
    }

    [Fact]
    public async Task EnabledComposition_RunsOneControlledOpenGateRequestThroughTheCompleteChain()
    {
        using var secretFile = TemporarySecretFile.Create("placeholder-secret-not-real");
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0","msg":"Success"}"""));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1479968678000)));
        services.AddHikCentralGateIntegration(
            ValidEnabledOptions(secretFile.Path),
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IHikCentralGateActionAdapter>();

        var result = await adapter.ExecuteAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://hikcentral.example.test:8443/artemis/api/acs/v1/door/doControl", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"doorIndexCode\":\"EXIT-GATE-01\"", Encoding.UTF8.GetString(handler.LastRequestBody!), StringComparison.Ordinal);
        Assert.True(handler.LastRequest.Headers.Contains(HikCentralRequestSigningMaterialConstants.HeaderSignature));
        Assert.Equal(HikCentralGateActionConstants.OutcomeSucceeded, result.ActionOutcome);
        Assert.Equal(HikCentralGateActionConstants.VendorCode, result.VendorCode);
        Assert.Equal("0", result.VendorResultCode);
        Assert.Equal("Success", result.VendorResultMessage);
        Assert.DoesNotContain("PHYSICAL_GATE_OPENED", result.ActionOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Signature", string.Join(",", PublicPropertyNames(result)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSecret", string.Join(",", PublicPropertyNames(result)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnabledComposition_WhenSecretFileIsMissing_FailsClosedAndDoesNotCallHandler()
    {
        var missingSecretPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hikcentral-missing-{Guid.NewGuid():N}.txt");
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0","msg":"Success"}"""));
        var services = new ServiceCollection();
        services.AddHikCentralGateIntegration(
            ValidEnabledOptions(missingSecretPath),
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IHikCentralGateActionAdapter>();

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal("HIKCENTRAL_SECRET_FILE_MISSING", exception.ErrorCode);
        Assert.Equal(0, handler.CallCount);
        Assert.DoesNotContain(missingSecretPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnabledComposition_DoesNotRetryTransportFailures()
    {
        using var secretFile = TemporarySecretFile.Create("placeholder-secret-not-real");
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, """{"code":"500","msg":"Vendor failure"}"""));
        var services = new ServiceCollection();
        services.AddHikCentralGateIntegration(
            ValidEnabledOptions(secretFile.Path),
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IHikCentralGateActionAdapter>();

        var result = await adapter.ExecuteAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HikCentralGateActionConstants.OutcomeVendorUnavailable, result.ActionOutcome);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task EnabledComposition_WhenCallerCancels_PreventsHandlerCall()
    {
        using var secretFile = TemporarySecretFile.Create("placeholder-secret-not-real");
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"code":"0","msg":"Success"}"""));
        var services = new ServiceCollection();
        services.AddHikCentralGateIntegration(
            ValidEnabledOptions(secretFile.Path),
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var adapter = provider.GetRequiredService<IHikCentralGateActionAdapter>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(ValidRequest(), cancellation.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void CompositionShape_ContainsNoAppSecretFallbackRetryCommandAuditWorkerOrAptDependencies()
    {
        var optionProperties = typeof(HikCentralGateIntegrationOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();
        var extensionAssemblyTypes = typeof(HikCentralGateIntegrationServiceCollectionExtensions)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(HikCentralGateIntegrationServiceCollectionExtensions).Namespace)
            .ToArray();

        Assert.DoesNotContain(optionProperties, name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(optionProperties, name => name.Contains("SecretValue", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(optionProperties, name => name.Contains("Environment", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(optionProperties, name => name.Contains("Vault", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(optionProperties, name => name.Contains("Certificate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(optionProperties, name => name.Contains("RequestPath", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(optionProperties, name => name.Contains("RequestBody", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionAssemblyTypes, type => type.FullName?.Contains("TerminalCashPayments", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(extensionAssemblyTypes, type => type.FullName?.Contains(".Apt.", StringComparison.OrdinalIgnoreCase) == true);
    }

    public static IEnumerable<object[]> InvalidEnabledOptions()
    {
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.BaseAddress = null), "HIKCENTRAL_BASE_ADDRESS_REQUIRED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.BaseAddress = "http://hikcentral.example.test/"), "HIKCENTRAL_BASE_ADDRESS_HTTPS_REQUIRED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.BaseAddress = "https://user:password@hikcentral.example.test/"), "HIKCENTRAL_BASE_ADDRESS_CREDENTIALS_UNSUPPORTED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.BaseAddress = "https://hikcentral.example.test/?query=1"), "HIKCENTRAL_BASE_ADDRESS_QUERY_UNSUPPORTED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.BaseAddress = "https://hikcentral.example.test/#fragment"), "HIKCENTRAL_BASE_ADDRESS_FRAGMENT_UNSUPPORTED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.ClientKeyIdentifier = null), "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_REQUIRED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.ClientKeyIdentifier = "placeholder-client-key-sensitive\r\nInjected"), "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_UNSAFE"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.ProfileCode = null), "HIKCENTRAL_PROFILE_CODE_REQUIRED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.ControlMechanism = HikCentralGateControlMechanism.AlarmOutputControl), "HIKCENTRAL_CONTROL_MECHANISM_UNSUPPORTED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.SecretFilePath = null), "HIKCENTRAL_SECRET_FILE_PATH_REQUIRED"];
        yield return [ValidEnabledOptions(@"relative\hikcentral-secret-placeholder.txt"), "HIKCENTRAL_SECRET_FILE_PATH_ABSOLUTE_REQUIRED"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.MaxSecretBytes = 0), "HIKCENTRAL_SECRET_FILE_MAX_BYTES_INVALID"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.MaxSecretBytes = HikCentralGateSecretFileOptions.MaximumAllowedSecretBytes + 1), "HIKCENTRAL_SECRET_FILE_MAX_BYTES_UNREASONABLE"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.HttpTimeoutSeconds = 0), "HIKCENTRAL_HTTP_TIMEOUT_SECONDS_INVALID"];
        yield return [ModifiedOptions(@"C:\safe\hikcentral-secret-placeholder.txt", options => options.MaxResponseBodyBytes = 0), "HIKCENTRAL_MAX_RESPONSE_BODY_BYTES_INVALID"];
    }

    private static HikCentralGateIntegrationOptions ValidEnabledOptions(string secretFilePath) =>
        new()
        {
            Enabled = true,
            BaseAddress = "https://hikcentral.example.test:8443/",
            ClientKeyIdentifier = "placeholder-client-key-sensitive",
            ProfileCode = "door-profile-placeholder",
            ControlMechanism = HikCentralGateControlMechanism.AccessControlDoorControl,
            SecretFilePath = secretFilePath,
            MaxSecretBytes = 4096,
            HttpTimeoutSeconds = 10,
            MaxResponseBodyBytes = 16 * 1024
        };

    private static HikCentralGateIntegrationOptions ModifiedOptions(
        string secretFilePath,
        Action<HikCentralGateIntegrationOptions> configure)
    {
        var options = ValidEnabledOptions(secretFilePath);
        configure(options);
        return options;
    }

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
            RequestedAt: RequestedAt);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string[] PublicPropertyNames<T>(T instance) =>
        instance!
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

    private static bool IsHostedServiceDescriptor(ServiceDescriptor descriptor) =>
        string.Equals(
            descriptor.ServiceType.FullName,
            "Microsoft.Extensions.Hosting.IHostedService",
            StringComparison.Ordinal);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public byte[]? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return _handler(request);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TemporarySecretFile : IDisposable
    {
        private TemporarySecretFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporarySecretFile Create(string content)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hikcentral-secret-placeholder-{Guid.NewGuid():N}.txt");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
            return new TemporarySecretFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
