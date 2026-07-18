using System.Net.Http;
using System.Reflection;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for production-safe HikCentral runtime-material assembly.
/// </summary>
public sealed class HikCentralGateRuntimeMaterialProviderTests
{
    private static readonly Uri ExpectedBaseAddress = new("https://hikcentral.example.test:8443/");
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.FromUnixTimeMilliseconds(1479968678000);

    [Fact]
    public async Task GetAsync_WithValidRequest_ReturnsDisposableRuntimeMaterial()
    {
        var sourceBytes = Encoding.UTF8.GetBytes("placeholder-secret-not-real");
        var secretSource = new CapturingSecretSource(sourceBytes);
        var timeProvider = new CountingTimeProvider(FixedUtc);
        var nonceGenerator = new CountingNonceGenerator("fixed-nonce");
        var provider = CreateProvider(secretSource, timeProvider, nonceGenerator);

        using var material = await provider.GetAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(ExpectedBaseAddress, material.BaseAddress);
        Assert.Equal("test-client-key-id", material.ClientKeyIdentifier);
        Assert.Equal("door-profile-test", material.ControlProfile.ProfileCode);
        Assert.Equal(HikCentralGateControlMechanism.AccessControlDoorControl, material.ControlProfile.ControlMechanism);
        Assert.Equal(HikCentralGateActionConstants.OpenGateOperation, material.ControlProfile.SupportedVendorOperation);
        Assert.Equal(HikCentralGateActionConstants.RequestMethod, material.ControlProfile.HttpMethod);
        Assert.Equal(HikCentralGateActionRequestPlanConstants.AccessControlDoorControlPath, material.ControlProfile.RelativePath);
        Assert.Equal(HikCentralGateActionRequestPlanConstants.JsonContentType, material.ControlProfile.ContentType);
        Assert.Equal("doorIndexCode", material.ControlProfile.TargetFieldName);
        Assert.Equal("controlType", material.ControlProfile.CommandFieldName);
        Assert.Equal("Open", material.ControlProfile.CommandValue);
        Assert.Equal("1479968678000", material.TimestampMilliseconds);
        Assert.Equal("fixed-nonce", material.Nonce);
        Assert.Equal(HikCentralRequestSigningMaterialConstants.SignatureMethod, material.SignatureMethod);
        Assert.Equal(sourceBytes, material.SecretBytes.ToArray());
        Assert.Equal(1, secretSource.CallCount);
        Assert.Equal(1, timeProvider.CallCount);
        Assert.Equal(1, nonceGenerator.CallCount);
        Assert.True(secretSource.LastMaterial!.IsDisposed);
        AssertCleared(secretSource.LastMaterial, "_secretBytes");
        Assert.Equal(Encoding.UTF8.GetBytes("placeholder-secret-not-real"), sourceBytes);
        Assert.DoesNotContain("placeholder-secret-not-real", material.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-key-id", material.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fixed-nonce", material.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_ReturnedRuntimeMaterialOwnsCopiedSecretAndClearsOnDispose()
    {
        var sourceBytes = Encoding.UTF8.GetBytes("placeholder-secret-not-real");
        var provider = CreateProvider(new CapturingSecretSource(sourceBytes));

        var material = await provider.GetAsync(ValidRequest(), CancellationToken.None);
        sourceBytes[0] = (byte)'X';

        Assert.Equal(Encoding.UTF8.GetBytes("placeholder-secret-not-real"), material.SecretBytes.ToArray());

        material.Dispose();
        material.Dispose();

        Assert.True(material.IsDisposed);
        AssertCleared(material, "_appSecretBytes");
    }

    [Fact]
    public void SecretMaterial_CopiesSourceBytesClearsOnlyOwnedBufferAndRedactsToString()
    {
        var sourceBytes = Encoding.UTF8.GetBytes("placeholder-secret-not-real");
        using var secret = new HikCentralGateSecretMaterial(sourceBytes);

        Assert.Equal(sourceBytes, secret.SecretBytes.ToArray());
        Assert.DoesNotContain("placeholder-secret-not-real", secret.ToString(), StringComparison.Ordinal);

        secret.Dispose();
        secret.Dispose();

        Assert.Equal(Encoding.UTF8.GetBytes("placeholder-secret-not-real"), sourceBytes);
        AssertCleared(secret, "_secretBytes");
    }

    [Fact]
    public void SecretMaterial_WhenEmpty_IsRejectedWithoutSecretInException()
    {
        var exception = Assert.Throws<ArgumentException>(() => new HikCentralGateSecretMaterial(ReadOnlySpan<byte>.Empty));

        Assert.DoesNotContain("placeholder-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_WhenPreCancelled_InvokesNoDependency()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var secretSource = new CapturingSecretSource();
        var timeProvider = new CountingTimeProvider(FixedUtc);
        var nonceGenerator = new CountingNonceGenerator("fixed-nonce");
        var provider = CreateProvider(secretSource, timeProvider, nonceGenerator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAsync(ValidRequest(), cancellation.Token).AsTask());

        Assert.Equal(0, secretSource.CallCount);
        Assert.Equal(0, timeProvider.CallCount);
        Assert.Equal(0, nonceGenerator.CallCount);
    }

    [Fact]
    public async Task GetAsync_WhenSecretRetrievalCancels_PropagatesAndInvokesNoLaterStage()
    {
        var secretSource = new CancellingSecretSource();
        var timeProvider = new CountingTimeProvider(FixedUtc);
        var nonceGenerator = new CountingNonceGenerator("fixed-nonce");
        var provider = CreateProvider(secretSource, timeProvider, nonceGenerator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAsync(ValidRequest(), CancellationToken.None).AsTask());

        Assert.Equal(1, secretSource.CallCount);
        Assert.Equal(0, timeProvider.CallCount);
        Assert.Equal(0, nonceGenerator.CallCount);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task GetAsync_WhenRequestIsInvalid_PreventsSecretRetrieval(
        HikCentralGateActionRequest request,
        string expectedErrorCode)
    {
        var secretSource = new CapturingSecretSource();
        var provider = CreateProvider(secretSource);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(request, CancellationToken.None).AsTask());

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(0, secretSource.CallCount);
    }

    [Theory]
    [InlineData(null, "HIKCENTRAL_BASE_ADDRESS_REQUIRED")]
    [InlineData("", "HIKCENTRAL_BASE_ADDRESS_REQUIRED")]
    [InlineData("http://hikcentral.example.test/", "HIKCENTRAL_BASE_ADDRESS_HTTPS_REQUIRED")]
    [InlineData("/relative", "HIKCENTRAL_BASE_ADDRESS_INVALID")]
    [InlineData("https://user:password@hikcentral.example.test/", "HIKCENTRAL_BASE_ADDRESS_CREDENTIALS_UNSUPPORTED")]
    [InlineData("https://hikcentral.example.test/?q=1", "HIKCENTRAL_BASE_ADDRESS_QUERY_UNSUPPORTED")]
    [InlineData("https://hikcentral.example.test/#fragment", "HIKCENTRAL_BASE_ADDRESS_FRAGMENT_UNSUPPORTED")]
    [InlineData("https://hikcentral.example.test/prefix", "HIKCENTRAL_BASE_ADDRESS_PATH_UNSUPPORTED")]
    public async Task GetAsync_WhenBaseAddressIsInvalid_PreventsSecretRetrieval(
        string? baseAddress,
        string expectedErrorCode)
    {
        var secretSource = new CapturingSecretSource();
        var provider = CreateProvider(secretSource, options: ModifiedOptions(options =>
        {
            options.BaseAddress = baseAddress;
        }));

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(ValidRequest(), CancellationToken.None).AsTask());

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(0, secretSource.CallCount);
    }

    [Theory]
    [InlineData(null, "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_REQUIRED")]
    [InlineData("", "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_REQUIRED")]
    [InlineData("client\rkey", "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_UNSAFE")]
    public async Task GetAsync_WhenClientKeyIdentifierIsInvalid_PreventsSecretRetrieval(
        string? clientKeyIdentifier,
        string expectedErrorCode)
    {
        var secretSource = new CapturingSecretSource();
        var provider = CreateProvider(secretSource, options: ModifiedOptions(options =>
        {
            options.ClientKeyIdentifier = clientKeyIdentifier;
        }));

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(ValidRequest(), CancellationToken.None).AsTask());

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(0, secretSource.CallCount);
    }

    [Theory]
    [InlineData(null, "HIKCENTRAL_PROFILE_CODE_REQUIRED")]
    [InlineData("", "HIKCENTRAL_PROFILE_CODE_REQUIRED")]
    [InlineData("profile\ncode", "HIKCENTRAL_PROFILE_CODE_UNSAFE")]
    public async Task GetAsync_WhenProfileCodeIsInvalid_PreventsSecretRetrieval(
        string? profileCode,
        string expectedErrorCode)
    {
        var secretSource = new CapturingSecretSource();
        var provider = CreateProvider(secretSource, options: ModifiedOptions(options =>
        {
            options.ProfileCode = profileCode;
        }));

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(ValidRequest(), CancellationToken.None).AsTask());

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(0, secretSource.CallCount);
    }

    [Fact]
    public async Task GetAsync_WhenControlMechanismIsUnsupported_PreventsSecretRetrieval()
    {
        var secretSource = new CapturingSecretSource();
        var provider = CreateProvider(secretSource, options: ModifiedOptions(options =>
        {
            options.ControlMechanism = HikCentralGateControlMechanism.AlarmOutputControl;
        }));

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(ValidRequest(), CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_CONTROL_MECHANISM_UNSUPPORTED", exception.ErrorCode);
        Assert.Equal(0, secretSource.CallCount);
    }

    [Fact]
    public async Task GetAsync_WhenConstructionFailsAfterSecretRetrieval_DisposesAndClearsSecret()
    {
        var secretSource = new CapturingSecretSource();
        var nonceGenerator = new CountingNonceGenerator("bad nonce with spaces");
        var provider = CreateProvider(secretSource, nonceGenerator: nonceGenerator);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(ValidRequest(), CancellationToken.None).AsTask());

        Assert.Equal("HIKCENTRAL_NONCE_INVALID", exception.ErrorCode);
        Assert.True(secretSource.LastMaterial!.IsDisposed);
        AssertCleared(secretSource.LastMaterial, "_secretBytes");
    }

    [Fact]
    public void CryptographicNonceGenerator_UsesApprovedFormatAndSigningValidation()
    {
        var generator = new CryptographicHikCentralNonceGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Assert.Equal(CryptographicHikCentralNonceGenerator.NonceTextLength, first.Length);
        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);

        var plan = new HikCentralGateActionRequestPlanBuilder().Build(
            ValidRequest(),
            HikCentralGateControlProfile.AccessControlDoorOpen("door-profile-test"));
        var material = new HikCentralRequestSigningMaterialBuilder().Build(new HikCentralSigningMaterialInput(
            plan,
            "placeholder-client-key",
            "1479968678000",
            first,
            HikCentralRequestSigningMaterialConstants.SignatureMethod));

        Assert.Equal(first, material.Nonce);
    }

    [Fact]
    public void Provider_DoesNotDeclareForbiddenRuntimeDependencies()
    {
        var constructorParameters = typeof(HikCentralGateRuntimeMaterialProvider)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralGateRuntimeMaterialProvider)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.DoesNotContain(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.DoesNotContain(constructorParameters, IsForbiddenRuntimeDependency);
        Assert.DoesNotContain(fieldTypes, IsForbiddenRuntimeDependency);
    }

    [Fact]
    public void RuntimeModels_DoNotExposeAppSecretSignatureOrPhysicalGateClaims()
    {
        var runtimeProperties = typeof(HikCentralGateRuntimeMaterial)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();
        var secretProperties = typeof(HikCentralGateSecretMaterial)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(runtimeProperties, name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(secretProperties, name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(runtimeProperties.Concat(secretProperties), name =>
            name.Contains("SignatureValue", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Signature", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(runtimeProperties.Concat(secretProperties), name => name.Contains("Physical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(runtimeProperties.Concat(secretProperties), name => name.Contains("Opened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAsync_ExceptionTextDoesNotExposeSensitiveValues()
    {
        var provider = CreateProvider(
            new CapturingSecretSource(Encoding.UTF8.GetBytes("placeholder-secret-not-real")),
            nonceGenerator: new CountingNonceGenerator("bad nonce with spaces"));

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => provider.GetAsync(ValidRequest() with
            {
                TargetResourceCode = "DOOR-SECRET-SHOULD-NOT-APPEAR",
                CorrelationId = Guid.Parse("22222222-0000-0000-0000-000000000001")
            }, CancellationToken.None).AsTask());

        Assert.DoesNotContain("placeholder-secret-not-real", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-key-id", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bad nonce", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DOOR-SECRET-SHOULD-NOT-APPEAR", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("22222222-0000-0000-0000-000000000001", exception.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [ValidRequest() with { GateCommandId = Guid.Empty }, "GATE_COMMAND_ID_REQUIRED"];
        yield return [ValidRequest() with { GateAuthorizationConsumptionId = Guid.Empty }, "GATE_AUTHORIZATION_CONSUMPTION_ID_REQUIRED"];
        yield return [ValidRequest() with { ExitAuthorizationId = Guid.Empty }, "EXIT_AUTHORIZATION_ID_REQUIRED"];
        yield return [ValidRequest() with { GateDeviceId = Guid.Empty }, "GATE_DEVICE_ID_REQUIRED"];
        yield return [ValidRequest() with { VendorSystemId = Guid.Empty }, "VENDOR_SYSTEM_ID_REQUIRED"];
        yield return [ValidRequest() with { VendorOperation = "CLOSE_GATE" }, "VENDOR_OPERATION_UNSUPPORTED"];
        yield return [ValidRequest() with { TargetResourceCode = " " }, "TARGET_RESOURCE_CODE_REQUIRED"];
        yield return [ValidRequest() with { CorrelationId = Guid.Empty }, "CORRELATION_ID_REQUIRED"];
    }

    private static HikCentralGateRuntimeMaterialProvider CreateProvider(
        IHikCentralGateSecretSource? secretSource = null,
        CountingTimeProvider? timeProvider = null,
        IHikCentralNonceGenerator? nonceGenerator = null,
        HikCentralGateRuntimeOptions? options = null) =>
        new(
            options ?? ValidOptions(),
            secretSource ?? new CapturingSecretSource(),
            nonceGenerator ?? new CountingNonceGenerator("fixed-nonce"),
            timeProvider ?? new CountingTimeProvider(FixedUtc));

    private static HikCentralGateRuntimeOptions ValidOptions() =>
        new()
        {
            BaseAddress = ExpectedBaseAddress.ToString(),
            ClientKeyIdentifier = "test-client-key-id",
            ProfileCode = "door-profile-test",
            ControlMechanism = HikCentralGateControlMechanism.AccessControlDoorControl
        };

    private static HikCentralGateRuntimeOptions ModifiedOptions(Action<HikCentralGateRuntimeOptions> configure)
    {
        var options = ValidOptions();
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
            RequestedAt: DateTimeOffset.Parse("2026-07-18T08:00:00Z"));

    private static void AssertCleared(object owner, string fieldName)
    {
        var bytes = (byte[])owner
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!;

        Assert.All(bytes, value => Assert.Equal(0, value));
    }

    private static bool IsForbiddenRuntimeDependency(Type type)
    {
        if (type == typeof(HikCentralGateRuntimeOptions) ||
            type == typeof(IHikCentralGateSecretSource) ||
            type == typeof(IHikCentralNonceGenerator) ||
            type == typeof(TimeProvider))
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
               type.Name.Contains("Environment", StringComparison.OrdinalIgnoreCase) ||
               type.Name.Contains("File", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingSecretSource : IHikCentralGateSecretSource
    {
        private readonly byte[] _sourceBytes;

        public CapturingSecretSource(byte[]? sourceBytes = null)
        {
            _sourceBytes = sourceBytes ?? Encoding.UTF8.GetBytes("placeholder-secret-not-real");
        }

        public int CallCount { get; private set; }

        public HikCentralGateSecretMaterial? LastMaterial { get; private set; }

        public ValueTask<HikCentralGateSecretMaterial> GetSecretAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastMaterial = new HikCentralGateSecretMaterial(_sourceBytes);
            return ValueTask.FromResult(LastMaterial);
        }
    }

    private sealed class CancellingSecretSource : IHikCentralGateSecretSource
    {
        public int CallCount { get; private set; }

        public ValueTask<HikCentralGateSecretMaterial> GetSecretAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            throw new OperationCanceledException();
        }
    }

    private sealed class CountingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public CountingTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return _utcNow;
        }
    }

    private sealed class CountingNonceGenerator : IHikCentralNonceGenerator
    {
        private readonly string _nonce;

        public CountingNonceGenerator(string nonce)
        {
            _nonce = nonce;
        }

        public int CallCount { get; private set; }

        public string Generate()
        {
            CallCount++;
            return _nonce;
        }
    }
}
