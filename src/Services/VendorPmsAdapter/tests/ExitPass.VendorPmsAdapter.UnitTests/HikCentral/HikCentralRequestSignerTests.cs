using System.Net.Http.Headers;
using System.Text;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;

namespace ExitPass.VendorPmsAdapter.UnitTests.HikCentral;

/// <summary>
/// Unit tests for HikCentral Professional AK/SK request signing.
/// </summary>
public sealed class HikCentralRequestSignerTests
{
    private static readonly DateTimeOffset FixedTimestamp =
        DateTimeOffset.FromUnixTimeMilliseconds(1479968678000);

    /// <summary>
    /// Verifies that identical requests signed with the same timestamp produce stable signatures.
    /// </summary>
    [Fact]
    public async Task HikCentralRequestSigner_WhenSameRequestAndTimestamp_ProducesDeterministicSignature()
    {
        using var first = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        using var second = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        var signer = CreateSigner();

        await signer.SignAsync(first, CancellationToken.None);
        await signer.SignAsync(second, CancellationToken.None);

        Assert.Equal(
            first.Headers.GetValues("X-Ca-Signature").Single(),
            second.Headers.GetValues("X-Ca-Signature").Single());
        Assert.Equal(
            HikCentralRequestSigner.BuildStringToSign(first),
            HikCentralRequestSigner.BuildStringToSign(second));
    }

    /// <summary>
    /// Verifies that the official URI path and query participate in the signature.
    /// </summary>
    [Fact]
    public async Task HikCentralRequestSigner_WhenPathChanges_ChangesSignature()
    {
        using var first = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        using var second = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate?qa=value", "ABC123");
        var signer = CreateSigner();

        await signer.SignAsync(first, CancellationToken.None);
        await signer.SignAsync(second, CancellationToken.None);

        Assert.NotEqual(
            first.Headers.GetValues("X-Ca-Signature").Single(),
            second.Headers.GetValues("X-Ca-Signature").Single());
    }

    /// <summary>
    /// Verifies that the local OpenDataServer profile does not include Content-MD5 in the signature by default.
    /// </summary>
    [Fact]
    public async Task HikCentralRequestSigner_DefaultLocalProfile_DoesNotAddContentMd5()
    {
        using var first = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        using var second = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "XYZ789");
        var signer = CreateSigner();

        await signer.SignAsync(first, CancellationToken.None);
        await signer.SignAsync(second, CancellationToken.None);

        Assert.False(first.Content!.Headers.Contains("Content-MD5"));
        Assert.Equal(
            first.Headers.GetValues("X-Ca-Signature").Single(),
            second.Headers.GetValues("X-Ca-Signature").Single());
    }

    /// <summary>
    /// Verifies that missing test-safe AK/SK credentials fail during signer construction.
    /// </summary>
    [Fact]
    public void HikCentralRequestSigner_WhenCredentialsMissing_ThrowsConfigurationException()
    {
        Assert.Throws<InvalidOperationException>(
            () => new HikCentralRequestSigner(new HikCentralCredentialOptions(string.Empty, "test-secret")));
        Assert.Throws<InvalidOperationException>(
            () => new HikCentralRequestSigner(new HikCentralCredentialOptions("test-ak", string.Empty)));
    }

    /// <summary>
    /// Verifies the canonical string shape confirmed against local HikCentral OpenDataServer V3.1.0.
    /// </summary>
    [Fact]
    public async Task HikCentralRequestSigner_BuildsLocalOpenDataServerCanonicalStringShape()
    {
        using var request = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        var signer = CreateSigner();

        await signer.SignAsync(request, CancellationToken.None);

        var expected = string.Join(
            "\n",
            [
                "POST",
                "*/*",
                "application/json",
                "x-ca-key:test-ak",
                "x-ca-timestamp:1479968678000",
                "/artemis/api/vehicle/v1/parkingfee/calculate"
            ]);
        Assert.Equal(expected, HikCentralRequestSigner.BuildStringToSign(request));
        Assert.Equal("rkpWqx7qKJj95FSwyQhUDgIGT30BCs6AjwFxQwYizmk=", request.Headers.GetValues("X-Ca-Signature").Single());
        Assert.Equal("test-ak", request.Headers.GetValues("X-Ca-Key").Single());
        Assert.Equal("1479968678000", request.Headers.GetValues("X-Ca-Timestamp").Single());
        Assert.Equal("x-ca-key,x-ca-timestamp", request.Headers.GetValues("X-Ca-Signature-Headers").Single());
        Assert.True(request.Headers.Contains("X-Ca-Signature"));
        Assert.False(request.Content!.Headers.Contains("Content-MD5"));
    }

    [Fact]
    public void HikCentralLogSanitizer_RedactsAppSecretFromDiagnostics()
    {
        var message = "HikCentral failure appSecret=local-secret-value signature=abc123";

        var sanitized = HikCentralLogSanitizer.Redact(
            message,
            "local-secret-value",
            ["abc123"]);

        Assert.DoesNotContain("local-secret-value", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", sanitized, StringComparison.Ordinal);
        Assert.Contains(HikCentralLogSanitizer.Redacted, sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void HikCentralLogSanitizer_RedactsSignatureUnlessLocalDebugModeEnabled()
    {
        const string signature = "signature-to-redact";

        Assert.Equal(
            HikCentralLogSanitizer.Redacted,
            HikCentralLogSanitizer.SignatureForDiagnostics(signature, allowSignatureDebug: false));
        Assert.Equal(
            signature,
            HikCentralLogSanitizer.SignatureForDiagnostics(signature, allowSignatureDebug: true));
    }

    private static HikCentralRequestSigner CreateSigner()
    {
        return new HikCentralRequestSigner(
            new HikCentralCredentialOptions("test-ak", "test-secret"),
            () => FixedTimestamp);
    }

    private static HttpRequestMessage CreateCalculateRequest(string path, string plateLicense)
    {
        var content = new StringContent($"{{ \"plateLicense\": \"{plateLicense}\" }}", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = content
        };
    }
}
