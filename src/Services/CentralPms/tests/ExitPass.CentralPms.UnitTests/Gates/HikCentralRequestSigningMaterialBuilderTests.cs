using System.Net.Http;
using System.Reflection;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for deterministic, side-effect-free HikCentral AK/SK signing-material construction.
/// </summary>
public sealed class HikCentralRequestSigningMaterialBuilderTests
{
    private const string ExpectedContentMd5 = "DZdZPukMpZAeaS5LD2pZSQ==";
    private const string ExpectedCanonicalSha256 = "16f3b118936f1c693ba26cee5c49b27ca7e45f79540c218724291e8d7fab36f3";

    [Fact]
    public void Build_AccessControlDoorControlGuideSection32And591_ReturnsExpectedCanonicalMaterial()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput();

        var material = builder.Build(input);

        var expectedCanonical = string.Join(
            "\n",
            [
                "POST",
                "*/*",
                ExpectedContentMd5,
                "application/json",
                "x-ca-key:test-client-key",
                "x-ca-nonce:fixed-nonce",
                "x-ca-timestamp:1479968678000",
                "/artemis/api/acs/v1/door/doControl"
            ]);

        Assert.Equal("POST", material.HttpMethod);
        Assert.Equal("*/*", material.Accept);
        Assert.Equal(ExpectedContentMd5, material.ContentMd5);
        Assert.Equal("application/json", material.ContentType);
        Assert.Equal("1479968678000", material.TimestampMilliseconds);
        Assert.Equal("fixed-nonce", material.Nonce);
        Assert.Equal("HmacSHA256", material.SignatureMethod);
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", material.SignedHeaderNames);
        Assert.Equal("/artemis/api/acs/v1/door/doControl", material.ResourcePath);
        Assert.Equal(expectedCanonical, material.CanonicalString);
        Assert.Equal(Encoding.UTF8.GetBytes(expectedCanonical), material.CanonicalUtf8);
        Assert.Equal(ExpectedCanonicalSha256, material.CanonicalSha256);
        Assert.Equal("test-client-key", HeaderValue(material, "X-Ca-Key"));
        Assert.Equal("fixed-nonce", HeaderValue(material, "X-Ca-Nonce"));
        Assert.Equal("1479968678000", HeaderValue(material, "X-Ca-Timestamp"));
        Assert.Equal("x-ca-key,x-ca-nonce,x-ca-timestamp", HeaderValue(material, "X-Ca-Signature-Headers"));
    }

    [Fact]
    public void Build_WhenCalledTwiceWithSameInput_IsDeterministic()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput();

        var first = builder.Build(input);
        var second = builder.Build(input);

        Assert.Equal(first.CanonicalString, second.CanonicalString);
        Assert.Equal(first.CanonicalUtf8, second.CanonicalUtf8);
        Assert.Equal(first.ContentMd5, second.ContentMd5);
        Assert.Equal(first.CanonicalSha256, second.CanonicalSha256);
        Assert.Equal(first.PlannedHeaders, second.PlannedHeaders);
    }

    [Fact]
    public void Build_WhenRequestBodyChanges_ChangesContentMd5AndCanonicalMaterial()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();

        var first = builder.Build(ValidInput(ValidPlan("EXIT-GATE-01")));
        var second = builder.Build(ValidInput(ValidPlan("EXIT-GATE-02")));

        Assert.NotEqual(first.ContentMd5, second.ContentMd5);
        Assert.NotEqual(first.CanonicalString, second.CanonicalString);
        Assert.NotEqual(first.CanonicalSha256, second.CanonicalSha256);
    }

    [Fact]
    public void Build_WhenNonceChanges_ChangesCanonicalMaterial()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();

        var first = builder.Build(ValidInput() with { Nonce = "fixed-nonce" });
        var second = builder.Build(ValidInput() with { Nonce = "fixed-nonce-2" });

        Assert.NotEqual(first.CanonicalString, second.CanonicalString);
        Assert.NotEqual(first.CanonicalSha256, second.CanonicalSha256);
    }

    [Fact]
    public void Build_WhenTimestampChanges_ChangesCanonicalMaterial()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();

        var first = builder.Build(ValidInput() with { TimestampMilliseconds = "1479968678000" });
        var second = builder.Build(ValidInput() with { TimestampMilliseconds = "1479968678001" });

        Assert.NotEqual(first.CanonicalString, second.CanonicalString);
        Assert.NotEqual(first.CanonicalSha256, second.CanonicalSha256);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void Build_WhenInputIsInvalid_RejectsDeterministically(
        HikCentralSigningMaterialInput? input,
        string expectedErrorCode)
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input!));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Theory]
    [InlineData("https://hikcentral.example/artemis/api/acs/v1/door/doControl")]
    [InlineData("//hikcentral.example/artemis/api/acs/v1/door/doControl")]
    [InlineData("/artemis/api/acs/v1/door/doControl?doorIndexCode=EXIT-GATE-01")]
    [InlineData("/artemis/api/acs/v1/door/doControl#fragment")]
    [InlineData("/artemis/api/acs/v1/../door/doControl")]
    [InlineData("/artemis/api/user@example/acs/v1/door/doControl")]
    public void Build_WhenRequestPlanPathIsUnsafe_RejectsDeterministically(string unsafePath)
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput(ValidPlan() with { RelativePath = unsafePath });

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input));

        Assert.Equal("HIKCENTRAL_REQUEST_PLAN_PATH_UNSAFE", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenRequestPlanBodyHashIsInconsistent_RejectsDeterministically()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput(ValidPlan() with { BodySha256 = new string('0', 64) });

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input));

        Assert.Equal("HIKCENTRAL_REQUEST_PLAN_BODY_HASH_MISMATCH", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenSignedHeaderIsUnsupported_RejectsDeterministically()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput() with
        {
            AdditionalSignedHeaders = [new HikCentralSigningHeader("x-ca-signature-method", "HmacSHA256")]
        };

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input));

        Assert.Equal("HIKCENTRAL_SIGNING_HEADER_UNSUPPORTED", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenSignedHeaderIsDuplicate_RejectsDeterministically()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput() with
        {
            AdditionalSignedHeaders = [new HikCentralSigningHeader("x-ca-key", "duplicate-client-key")]
        };

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input));

        Assert.Equal("HIKCENTRAL_SIGNING_HEADER_DUPLICATE", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenHeaderValueContainsCrLf_RejectsDeterministically()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput() with
        {
            AdditionalSignedHeaders = [new HikCentralSigningHeader("x-ca-key", "value\r\nInjected: true")]
        };

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input));

        Assert.Equal("HIKCENTRAL_SIGNING_HEADER_UNSAFE", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenRequestPlanMethodIsOverridden_RejectsDeterministically()
    {
        var builder = new HikCentralRequestSigningMaterialBuilder();
        var input = ValidInput(ValidPlan() with { HttpMethod = "GET" });

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(() => builder.Build(input));

        Assert.Equal("HIKCENTRAL_REQUEST_PLAN_METHOD_UNSUPPORTED", exception.ErrorCode);
    }

    [Fact]
    public void SigningMaterial_DoesNotExposeSecretOrFinalSignatureFields()
    {
        var forbiddenExactNames = new[]
        {
            "AppSecret",
            "Secret",
            "Signature",
            "Authorization",
            "Cookie",
            "Certificate",
            "PrivateKey",
            "ConnectionString",
            "BaseUrl"
        };

        var propertyNames = typeof(HikCentralSigningMaterial)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbiddenName in forbiddenExactNames)
        {
            Assert.DoesNotContain(propertyNames, propertyName => string.Equals(propertyName, forbiddenName, StringComparison.OrdinalIgnoreCase));
        }

        var material = new HikCentralRequestSigningMaterialBuilder().Build(ValidInput());
        Assert.DoesNotContain(material.PlannedHeaders, header => string.Equals(header.Name, "X-Ca-Signature", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(material.PlannedHeaders, header => string.Equals(header.Name, "Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Builder_DoesNotDeclareHttpDatabaseAuditAdapterOrSignerDependencies()
    {
        var constructorParameters = typeof(HikCentralRequestSigningMaterialBuilder)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralRequestSigningMaterialBuilder)
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

    public static IEnumerable<object?[]> InvalidInputs()
    {
        yield return [null, "HIKCENTRAL_SIGNING_INPUT_REQUIRED"];
        yield return [
            new HikCentralSigningMaterialInput(
                null!,
                "test-client-key",
                "1479968678000",
                "fixed-nonce",
                "HmacSHA256"),
            "HIKCENTRAL_REQUEST_PLAN_REQUIRED"
        ];
        yield return [ValidInput(ValidPlan() with { VendorCode = "OTHER" }), "HIKCENTRAL_REQUEST_PLAN_VENDOR_UNSUPPORTED"];
        yield return [ValidInput(ValidPlan() with { VendorOperation = "CLOSE_GATE" }), "HIKCENTRAL_REQUEST_PLAN_OPERATION_UNSUPPORTED"];
        yield return [ValidInput(ValidPlan() with { ContentType = "text/plain" }), "HIKCENTRAL_REQUEST_PLAN_CONTENT_TYPE_UNSUPPORTED"];
        yield return [ValidInput(ValidPlan() with { RelativePath = "/artemis/api/acs/v1/door/other" }), "HIKCENTRAL_REQUEST_PLAN_PATH_UNAPPROVED"];
        yield return [ValidInput(ValidPlan() with { BodyUtf8 = [] }), "HIKCENTRAL_REQUEST_PLAN_BODY_REQUIRED"];
        yield return [ValidInput() with { ClientKeyIdentifier = " " }, "HIKCENTRAL_CLIENT_KEY_IDENTIFIER_REQUIRED"];
        yield return [ValidInput() with { ClientKeyIdentifier = "client\r\nkey" }, "HIKCENTRAL_SIGNING_VALUE_UNSAFE"];
        yield return [ValidInput() with { TimestampMilliseconds = " " }, "HIKCENTRAL_TIMESTAMP_REQUIRED"];
        yield return [ValidInput() with { TimestampMilliseconds = "1479968678" }, "HIKCENTRAL_TIMESTAMP_INVALID"];
        yield return [ValidInput() with { Nonce = " " }, "HIKCENTRAL_NONCE_REQUIRED"];
        yield return [ValidInput() with { Nonce = "nonce/with/slash" }, "HIKCENTRAL_NONCE_INVALID"];
        yield return [ValidInput() with { SignatureMethod = "HmacSHA1" }, "HIKCENTRAL_SIGNATURE_METHOD_UNSUPPORTED"];
        yield return [
            ValidInput() with { AdditionalSignedHeaders = [new HikCentralSigningHeader("x ca key", "value")] },
            "HIKCENTRAL_SIGNING_HEADER_NAME_UNSAFE"
        ];
    }

    private static HikCentralSigningMaterialInput ValidInput(HikCentralGateActionRequestPlan? plan = null) =>
        new(
            plan ?? ValidPlan(),
            "test-client-key",
            "1479968678000",
            "fixed-nonce",
            "HmacSHA256");

    private static HikCentralGateActionRequestPlan ValidPlan(string targetResourceCode = "EXIT-GATE-01")
    {
        var request = new HikCentralGateActionRequest(
            GateCommandId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            GateAuthorizationConsumptionId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            ExitAuthorizationId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            GateDeviceId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            VendorSystemId: Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
            SiteId: Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            LaneId: Guid.Parse("11111111-0000-0000-0000-000000000001"),
            TargetResourceCode: targetResourceCode,
            VendorOperation: HikCentralGateActionConstants.OpenGateOperation,
            CorrelationId: Guid.Parse("22222222-0000-0000-0000-000000000001"),
            RequestedAt: DateTimeOffset.Parse("2026-07-17T08:00:00Z"));

        return new HikCentralGateActionRequestPlanBuilder()
            .Build(request, HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE"));
    }

    private static string HeaderValue(HikCentralSigningMaterial material, string headerName) =>
        material.PlannedHeaders.Single(header => string.Equals(header.Name, headerName, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool IsForbiddenRuntimeDependency(Type type) =>
        type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
        type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Adapter", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Signer", StringComparison.OrdinalIgnoreCase);
}
