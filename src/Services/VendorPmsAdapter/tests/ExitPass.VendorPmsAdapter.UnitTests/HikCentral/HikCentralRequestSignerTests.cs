using System.Net.Http.Json;
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
    /// Verifies that the request body digest participates in the signature.
    /// </summary>
    [Fact]
    public async Task HikCentralRequestSigner_WhenBodyChanges_ChangesSignature()
    {
        using var first = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        using var second = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "XYZ789");
        var signer = CreateSigner();

        await signer.SignAsync(first, CancellationToken.None);
        await signer.SignAsync(second, CancellationToken.None);

        Assert.NotEqual(
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
    /// Verifies the canonical string shape from HikCentral Professional OpenAPI V3.1.0 section 3.2.
    /// </summary>
    [Fact]
    public async Task HikCentralRequestSigner_BuildsOfficialV310CanonicalStringShape()
    {
        using var request = CreateCalculateRequest("/artemis/api/vehicle/v1/parkingfee/calculate", "ABC123");
        var signer = CreateSigner();

        await signer.SignAsync(request, CancellationToken.None);

        var expected = string.Join(
            "\n",
            [
                "POST",
                "*/*",
                request.Content!.Headers.GetValues("Content-MD5").Single(),
                "application/json; charset=utf-8",
                "x-ca-key:test-ak",
                "x-ca-timestamp:1479968678000",
                "/artemis/api/vehicle/v1/parkingfee/calculate"
            ]);
        Assert.Equal(expected, HikCentralRequestSigner.BuildStringToSign(request));
        Assert.Equal("test-ak", request.Headers.GetValues("X-Ca-Key").Single());
        Assert.Equal("1479968678000", request.Headers.GetValues("X-Ca-Timestamp").Single());
        Assert.Equal("x-ca-key,x-ca-timestamp", request.Headers.GetValues("X-Ca-Signature-Headers").Single());
        Assert.True(request.Headers.Contains("X-Ca-Signature"));
    }

    private static HikCentralRequestSigner CreateSigner()
    {
        return new HikCentralRequestSigner(
            new HikCentralCredentialOptions("test-ak", "test-secret"),
            () => FixedTimestamp);
    }

    private static HttpRequestMessage CreateCalculateRequest(string path, string plateLicense)
    {
        return new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { plateLicense })
        };
    }
}
