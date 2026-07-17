using System.Net.Http;
using System.Reflection;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for side-effect-free HikCentral signed HTTP request construction.
/// </summary>
public sealed class HikCentralSignedHttpRequestBuilderTests
{
    private static readonly Uri BaseAddress = new("https://hikcentral.test:8443/");

    [Fact]
    public async Task Build_WithValidInputs_ReturnsSignedPostRequest()
    {
        var fixture = ValidFixture();
        var builder = new HikCentralSignedHttpRequestBuilder();

        using var request = builder.Build(BaseAddress, fixture.Plan, fixture.Material, fixture.Signature);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hikcentral.test:8443/artemis/api/acs/v1/door/doControl", request.RequestUri!.AbsoluteUri);
        Assert.Equal("/artemis/api/acs/v1/door/doControl", request.RequestUri.PathAndQuery);
        Assert.Equal(fixture.Plan.BodyUtf8, await request.Content!.ReadAsByteArrayAsync());
        Assert.Equal("application/json", request.Content.Headers.ContentType!.ToString());
        Assert.Equal(fixture.Material.ContentMd5, request.Content.Headers.GetValues("Content-MD5").Single());
        Assert.Equal("*/*", request.Headers.Accept.Single().ToString());
        Assert.Equal("test-client-key", HeaderValue(request, "X-Ca-Key"));
        Assert.Equal("fixed-nonce", HeaderValue(request, "X-Ca-Nonce"));
        Assert.Equal("1479968678000", HeaderValue(request, "X-Ca-Timestamp"));
        Assert.Equal("HmacSHA256", HeaderValue(request, "X-Ca-Signature-Method"));
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", HeaderValue(request, "X-Ca-Signature-Headers"));
        Assert.Equal(fixture.Signature.EncodedSignatureValue, HeaderValue(request, "X-Ca-Signature"));
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("AppSecret"));
    }

    [Fact]
    public async Task Build_WhenCalledTwiceWithIdenticalInputs_IsDeterministic()
    {
        var fixture = ValidFixture();
        var builder = new HikCentralSignedHttpRequestBuilder();

        using var first = builder.Build(BaseAddress, fixture.Plan, fixture.Material, fixture.Signature);
        using var second = builder.Build(BaseAddress, fixture.Plan, fixture.Material, fixture.Signature);

        Assert.Equal(first.Method, second.Method);
        Assert.Equal(first.RequestUri, second.RequestUri);
        Assert.Equal(await first.Content!.ReadAsByteArrayAsync(), await second.Content!.ReadAsByteArrayAsync());
        Assert.Equal(HeaderValue(first, "X-Ca-Signature"), HeaderValue(second, "X-Ca-Signature"));
        Assert.Equal(first.Content.Headers.GetValues("Content-MD5").Single(), second.Content!.Headers.GetValues("Content-MD5").Single());
    }

    [Fact]
    public async Task Build_UsesRequestPlanBodyBytesWithoutReserializing()
    {
        var fixture = ValidFixture();
        var customBody = Encoding.UTF8.GetBytes("{\"doorIndexCode\":\"EXIT-GATE-01\",\"controlType\":\"Open\",\"evidence\":\"spacing stays exact\"}");
        var plan = fixture.Plan with
        {
            BodyUtf8 = customBody,
            BodySha256 = Sha256Hex(customBody)
        };
        var material = new HikCentralRequestSigningMaterialBuilder()
            .Build(new HikCentralSigningMaterialInput(
                plan,
                "test-client-key",
                "1479968678000",
                "fixed-nonce",
                "HmacSHA256"));
        var signature = new HikCentralRequestSignatureCalculator()
            .Calculate(material, Encoding.UTF8.GetBytes("test-app-secret"));
        var builder = new HikCentralSignedHttpRequestBuilder();

        using var request = builder.Build(BaseAddress, plan, material, signature);

        Assert.Equal(customBody, await request.Content!.ReadAsByteArrayAsync());
    }

    [Fact]
    public void Build_ReturnedRequestIsCallerDisposable()
    {
        var fixture = ValidFixture();
        var builder = new HikCentralSignedHttpRequestBuilder();

        var request = builder.Build(BaseAddress, fixture.Plan, fixture.Material, fixture.Signature);
        request.Dispose();

        Assert.NotNull(request);
    }

    [Theory]
    [MemberData(nameof(InvalidBaseAddresses))]
    public void Build_WhenBaseAddressIsInvalid_RejectsDeterministically(
        Uri? baseAddress,
        string expectedErrorCode)
    {
        var fixture = ValidFixture();
        var builder = new HikCentralSignedHttpRequestBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(baseAddress!, fixture.Plan, fixture.Material, fixture.Signature));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(InconsistentInputs))]
    public void Build_WhenPreparedInputsAreInconsistent_RejectsDeterministically(
        HikCentralGateActionRequestPlan plan,
        HikCentralSigningMaterial material,
        HikCentralRequestSignature signature,
        string expectedErrorCode)
    {
        var builder = new HikCentralSignedHttpRequestBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(BaseAddress, plan, material, signature));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenPlannedHeaderContainsCrLf_RejectsDeterministically()
    {
        var fixture = ValidFixture();
        var material = fixture.Material with
        {
            PlannedHeaders = ReplaceHeader(fixture.Material, "X-Ca-Key", "test-client-key\r\nInjected: true")
        };
        var builder = new HikCentralSignedHttpRequestBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(BaseAddress, fixture.Plan, material, fixture.Signature));

        Assert.Equal("HIKCENTRAL_SIGNING_MATERIAL_HEADER_UNSAFE", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenPlannedHeaderIsUnsupported_RejectsDeterministically()
    {
        var fixture = ValidFixture();
        var material = fixture.Material with
        {
            PlannedHeaders = fixture.Material.PlannedHeaders
                .Concat([new HikCentralSigningHeader("X-Ca-Extra", "value")])
                .ToArray()
        };
        var builder = new HikCentralSignedHttpRequestBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(BaseAddress, fixture.Plan, material, fixture.Signature));

        Assert.Equal("HIKCENTRAL_SIGNING_MATERIAL_HEADER_UNSUPPORTED", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenRejected_DoesNotExposeSensitiveValuesInException()
    {
        var fixture = ValidFixture();
        var signature = new HikCentralRequestSignature(
            fixture.Signature.SignatureAlgorithmIdentifier,
            "X-Other-Signature",
            fixture.Signature.EncodedSignatureValue);
        var builder = new HikCentralSignedHttpRequestBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(BaseAddress, fixture.Plan, fixture.Material, signature));

        Assert.DoesNotContain(fixture.Signature.EncodedSignatureValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fixed-nonce", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Plan.BodySha256, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_DoesNotDeclareConfigurationLoggingHttpClientDatabaseAuditWorkerOrAdapterDependencies()
    {
        var constructorParameters = typeof(HikCentralSignedHttpRequestBuilder)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralSignedHttpRequestBuilder)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.Empty(constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.DoesNotContain(typeof(IHikCentralGateActionAdapter), constructorParameters);
        Assert.DoesNotContain(typeof(IHikCentralGateActionAdapter), fieldTypes);
        Assert.DoesNotContain(constructorParameters, IsForbiddenRuntimeDependency);
        Assert.DoesNotContain(fieldTypes, IsForbiddenRuntimeDependency);
    }

    public static IEnumerable<object?[]> InvalidBaseAddresses()
    {
        yield return [null, "HIKCENTRAL_BASE_ADDRESS_REQUIRED"];
        yield return [new Uri("/relative", UriKind.Relative), "HIKCENTRAL_BASE_ADDRESS_ABSOLUTE_REQUIRED"];
        yield return [new Uri("http://hikcentral.test", UriKind.Absolute), "HIKCENTRAL_BASE_ADDRESS_HTTPS_REQUIRED"];
        yield return [new Uri("file:///tmp/hikcentral", UriKind.Absolute), "HIKCENTRAL_BASE_ADDRESS_HTTPS_REQUIRED"];
        yield return [new Uri("https://user:pass@hikcentral.test", UriKind.Absolute), "HIKCENTRAL_BASE_ADDRESS_CREDENTIALS_UNSUPPORTED"];
        yield return [new Uri("https://hikcentral.test?x=1", UriKind.Absolute), "HIKCENTRAL_BASE_ADDRESS_QUERY_UNSUPPORTED"];
        yield return [new Uri("https://hikcentral.test#fragment", UriKind.Absolute), "HIKCENTRAL_BASE_ADDRESS_FRAGMENT_UNSUPPORTED"];
        yield return [new Uri("https://hikcentral.test/base", UriKind.Absolute), "HIKCENTRAL_BASE_ADDRESS_PATH_UNSUPPORTED"];
    }

    public static IEnumerable<object[]> InconsistentInputs()
    {
        var fixture = ValidFixture();
        yield return [fixture.Plan with { HttpMethod = "GET" }, fixture.Material, fixture.Signature, "HIKCENTRAL_REQUEST_PLAN_METHOD_UNSUPPORTED"];
        yield return [fixture.Plan, fixture.Material with { HttpMethod = "GET" }, fixture.Signature, "HIKCENTRAL_SIGNING_MATERIAL_METHOD_MISMATCH"];
        yield return [fixture.Plan, fixture.Material with { ResourcePath = "/artemis/api/acs/v1/door/other" }, fixture.Signature, "HIKCENTRAL_SIGNING_MATERIAL_PATH_MISMATCH"];
        yield return [fixture.Plan, fixture.Material with { ContentType = "text/plain" }, fixture.Signature, "HIKCENTRAL_SIGNING_MATERIAL_CONTENT_TYPE_MISMATCH"];
        yield return [fixture.Plan, fixture.Material with { ContentMd5 = "bad-md5" }, fixture.Signature, "HIKCENTRAL_SIGNING_MATERIAL_CONTENT_MD5_MISMATCH"];
        yield return [fixture.Plan with { BodySha256 = new string('0', 64) }, fixture.Material, fixture.Signature, "HIKCENTRAL_REQUEST_PLAN_BODY_HASH_MISMATCH"];
        yield return [fixture.Plan, fixture.Material with { SignatureMethod = "HmacSHA1" }, fixture.Signature, "HIKCENTRAL_SIGNING_MATERIAL_SIGNATURE_METHOD_UNSUPPORTED"];
        yield return [
            fixture.Plan,
            fixture.Material,
            new HikCentralRequestSignature("HmacSHA1", "X-Ca-Signature", fixture.Signature.EncodedSignatureValue),
            "HIKCENTRAL_SIGNATURE_METHOD_MISMATCH"
        ];
        yield return [
            fixture.Plan,
            fixture.Material,
            new HikCentralRequestSignature("HmacSHA256", "X-Other-Signature", fixture.Signature.EncodedSignatureValue),
            "HIKCENTRAL_SIGNATURE_HEADER_UNSUPPORTED"
        ];
        yield return [
            fixture.Plan,
            fixture.Material,
            new HikCentralRequestSignature("HmacSHA256", "X-Ca-Signature", " "),
            "HIKCENTRAL_SIGNATURE_VALUE_INVALID"
        ];
        yield return [
            fixture.Plan,
            fixture.Material with { SignedHeaderNames = "x-ca-key,x-ca-timestamp" },
            fixture.Signature,
            "HIKCENTRAL_SIGNING_MATERIAL_SIGNED_HEADERS_UNSUPPORTED"
        ];
    }

    private static SignedRequestFixture ValidFixture()
    {
        var request = new HikCentralGateActionRequest(
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
        var plan = new HikCentralGateActionRequestPlanBuilder()
            .Build(request, HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE"));
        var material = new HikCentralRequestSigningMaterialBuilder()
            .Build(new HikCentralSigningMaterialInput(
                plan,
                "test-client-key",
                "1479968678000",
                "fixed-nonce",
                "HmacSHA256"));
        var signature = new HikCentralRequestSignatureCalculator()
            .Calculate(material, Encoding.UTF8.GetBytes("test-app-secret"));

        return new SignedRequestFixture(plan, material, signature);
    }

    private static IReadOnlyList<HikCentralSigningHeader> ReplaceHeader(
        HikCentralSigningMaterial material,
        string headerName,
        string replacementValue) =>
        material.PlannedHeaders
            .Select(header => string.Equals(header.Name, headerName, StringComparison.OrdinalIgnoreCase)
                ? header with { Value = replacementValue }
                : header)
            .ToArray();

    private static string HeaderValue(HttpRequestMessage request, string headerName) =>
        request.Headers.GetValues(headerName).Single();

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsForbiddenRuntimeDependency(Type type) =>
        type.Namespace?.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
        type.Name.Contains("HttpClient", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Adapter", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Worker", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Options", StringComparison.OrdinalIgnoreCase);

    private sealed record SignedRequestFixture(
        HikCentralGateActionRequestPlan Plan,
        HikCentralSigningMaterial Material,
        HikCentralRequestSignature Signature);
}
