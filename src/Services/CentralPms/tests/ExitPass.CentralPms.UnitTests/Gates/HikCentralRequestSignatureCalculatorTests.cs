using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for guide-confirmed HikCentral HMAC-SHA256 signature calculation.
/// </summary>
public sealed class HikCentralRequestSignatureCalculatorTests
{
    private const string PlaceholderSecret = "test-app-secret";
    private const string ExpectedSignature = "Q6wkj//UcTv00A6MGXfIVCvIhgT9DnDAJeyMFRUG410=";

    [Fact]
    public void Calculate_GuideSection32HmacSha256Vector_ReturnsExpectedBase64Signature()
    {
        var material = ValidSigningMaterial();
        var secretBytes = Encoding.UTF8.GetBytes(PlaceholderSecret);
        var calculator = new HikCentralRequestSignatureCalculator();

        var signature = calculator.Calculate(material, secretBytes);

        Assert.Equal("HmacSHA256", signature.SignatureAlgorithmIdentifier);
        Assert.Equal("X-Ca-Signature", signature.HeaderName);
        Assert.Equal(ExpectedSignature, signature.EncodedSignatureValue);
        Assert.EndsWith("=", signature.EncodedSignatureValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WhenCalledTwiceWithSameInput_IsDeterministic()
    {
        var material = ValidSigningMaterial();
        var secretBytes = Encoding.UTF8.GetBytes(PlaceholderSecret);
        var calculator = new HikCentralRequestSignatureCalculator();

        var first = calculator.Calculate(material, secretBytes);
        var second = calculator.Calculate(material, secretBytes);

        Assert.Equal(first.SignatureAlgorithmIdentifier, second.SignatureAlgorithmIdentifier);
        Assert.Equal(first.HeaderName, second.HeaderName);
        Assert.Equal(first.EncodedSignatureValue, second.EncodedSignatureValue);
    }

    [Fact]
    public void Calculate_WhenCanonicalBytesChange_ChangesSignature()
    {
        var material = ValidSigningMaterial();
        var changedCanonical = material.CanonicalString.Replace("fixed-nonce", "fixed-nonce-2", StringComparison.Ordinal);
        var changedBytes = Encoding.UTF8.GetBytes(changedCanonical);
        var changedMaterial = material with
        {
            CanonicalString = changedCanonical,
            CanonicalUtf8 = changedBytes,
            CanonicalSha256 = Sha256Hex(changedBytes)
        };
        var secretBytes = Encoding.UTF8.GetBytes(PlaceholderSecret);
        var calculator = new HikCentralRequestSignatureCalculator();

        var first = calculator.Calculate(material, secretBytes);
        var second = calculator.Calculate(changedMaterial, secretBytes);

        Assert.NotEqual(first.EncodedSignatureValue, second.EncodedSignatureValue);
    }

    [Fact]
    public void Calculate_WhenSecretBytesChange_ChangesSignature()
    {
        var material = ValidSigningMaterial();
        var calculator = new HikCentralRequestSignatureCalculator();

        var first = calculator.Calculate(material, Encoding.UTF8.GetBytes(PlaceholderSecret));
        var second = calculator.Calculate(material, Encoding.UTF8.GetBytes("another-test-app-secret"));

        Assert.NotEqual(first.EncodedSignatureValue, second.EncodedSignatureValue);
    }

    [Fact]
    public void Calculate_UsesCanonicalBytesFromSigningMaterialWithoutReconstruction()
    {
        var canonical = "CUSTOM-CANONICAL-BYTES";
        var canonicalBytes = Encoding.UTF8.GetBytes(canonical);
        var material = ValidSigningMaterial() with
        {
            CanonicalString = canonical,
            CanonicalUtf8 = canonicalBytes,
            CanonicalSha256 = Sha256Hex(canonicalBytes)
        };
        var secretBytes = Encoding.UTF8.GetBytes(PlaceholderSecret);
        using var hmac = new HMACSHA256(secretBytes);
        var expected = Convert.ToBase64String(hmac.ComputeHash(canonicalBytes));
        var calculator = new HikCentralRequestSignatureCalculator();

        var signature = calculator.Calculate(material, secretBytes);

        Assert.Equal(expected, signature.EncodedSignatureValue);
    }

    [Fact]
    public void Calculate_DoesNotMutateCallerSecretBuffer()
    {
        var material = ValidSigningMaterial();
        var secretBytes = Encoding.UTF8.GetBytes(PlaceholderSecret);
        var originalSecretBytes = secretBytes.ToArray();
        var calculator = new HikCentralRequestSignatureCalculator();

        calculator.Calculate(material, secretBytes);

        Assert.Equal(originalSecretBytes, secretBytes);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void Calculate_WhenInputIsInvalid_RejectsDeterministically(
        HikCentralSigningMaterial? material,
        byte[] secretBytes,
        string expectedErrorCode)
    {
        var calculator = new HikCentralRequestSignatureCalculator();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => calculator.Calculate(material!, secretBytes));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Fact]
    public void Calculate_WhenRejected_DoesNotExposeSecretCanonicalTextOrSignatureInException()
    {
        var secretBytes = Encoding.UTF8.GetBytes(PlaceholderSecret);
        var material = ValidSigningMaterial() with { SignatureMethod = "HmacSHA1" };
        var calculator = new HikCentralRequestSignatureCalculator();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => calculator.Calculate(material, secretBytes));

        Assert.DoesNotContain(PlaceholderSecret, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ExpectedSignature, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(material.CanonicalString, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureResult_RedactsSensitiveValueFromToString()
    {
        var signature = new HikCentralRequestSignatureCalculator()
            .Calculate(ValidSigningMaterial(), Encoding.UTF8.GetBytes(PlaceholderSecret));

        var text = signature.ToString();

        Assert.DoesNotContain(signature.EncodedSignatureValue, text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureResult_DoesNotExposeSecretCredentialOrAuthorizationFields()
    {
        var forbiddenNameFragments = new[]
        {
            "AppSecret",
            "SecretBuffer",
            "SecretBytes",
            "Credential",
            "Authorization",
            "Cookie",
            "Certificate",
            "PrivateKey",
            "BaseUrl",
            "ConnectionString"
        };

        var propertyNames = typeof(HikCentralRequestSignature)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        foreach (var propertyName in propertyNames)
        {
            Assert.DoesNotContain(
                forbiddenNameFragments,
                fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Calculator_DoesNotDeclareConfigurationLoggingHttpDatabaseAuditOrAdapterDependencies()
    {
        var constructorParameters = typeof(HikCentralRequestSignatureCalculator)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralRequestSignatureCalculator)
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
        yield return [null, Encoding.UTF8.GetBytes(PlaceholderSecret), "HIKCENTRAL_SIGNING_MATERIAL_REQUIRED"];
        yield return [ValidSigningMaterial(), Array.Empty<byte>(), "HIKCENTRAL_APP_SECRET_REQUIRED"];
        yield return [
            ValidSigningMaterial() with { CanonicalUtf8 = [] },
            Encoding.UTF8.GetBytes(PlaceholderSecret),
            "HIKCENTRAL_CANONICAL_BYTES_REQUIRED"
        ];
        yield return [
            ValidSigningMaterial() with { SignatureMethod = "HmacSHA1" },
            Encoding.UTF8.GetBytes(PlaceholderSecret),
            "HIKCENTRAL_SIGNATURE_METHOD_UNSUPPORTED"
        ];
        yield return [
            ValidSigningMaterial() with { CanonicalString = " " },
            Encoding.UTF8.GetBytes(PlaceholderSecret),
            "HIKCENTRAL_CANONICAL_STRING_REQUIRED"
        ];
        yield return [
            ValidSigningMaterial() with { CanonicalUtf8 = Encoding.UTF8.GetBytes("different canonical bytes") },
            Encoding.UTF8.GetBytes(PlaceholderSecret),
            "HIKCENTRAL_CANONICAL_BYTES_MISMATCH"
        ];
        yield return [
            ValidSigningMaterial() with { CanonicalSha256 = new string('0', 64) },
            Encoding.UTF8.GetBytes(PlaceholderSecret),
            "HIKCENTRAL_CANONICAL_HASH_MISMATCH"
        ];
    }

    private static HikCentralSigningMaterial ValidSigningMaterial()
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

        return new HikCentralRequestSigningMaterialBuilder()
            .Build(new HikCentralSigningMaterialInput(
                plan,
                "test-client-key",
                "1479968678000",
                "fixed-nonce",
                "HmacSHA256"));
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsForbiddenRuntimeDependency(Type type) =>
        type.Namespace?.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
        type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Adapter", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Options", StringComparison.OrdinalIgnoreCase);
}
