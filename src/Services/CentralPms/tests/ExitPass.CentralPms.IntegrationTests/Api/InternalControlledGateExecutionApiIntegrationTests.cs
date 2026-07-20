using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Gates;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the disabled-by-default internal controlled gate execution endpoint.
/// </summary>
public sealed class InternalControlledGateExecutionApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string RouteTemplate = "/v1/internal/gates/commands/{0}/execute";
    private const string CertificateSelectorHeader = "X-Test-Client-Certificate";
    private readonly CustomWebApplicationFactory _factory;

    public InternalControlledGateExecutionApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ControlledExecution_WhenDisabled_IsNotMappedAndDoesNotInvokeService()
    {
        var executionService = RecordingExecutionService.Success();
        using var factory = _factory.WithServiceOverrides(services => ReplaceExecutionService(services, executionService));
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Guid.NewGuid(), new { confirmation = "OPEN_GATE" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        executionService.ExecuteCalls.Should().Be(0);
        executionService.RetryCalls.Should().Be(0);
    }

    [Fact]
    public void ControlledExecutionOptions_DefaultsToDisabled()
    {
        var options = new ExitPass.CentralPms.Api.Services.HikCentralControlledGateExecutionOptions();

        options.Enabled.Should().BeFalse();
        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void ControlledExecution_WhenEnabledButLiveIntegrationDisabled_FailsClosed()
    {
        using var factory = _factory.WithConfigurationOverrides(new Dictionary<string, string?>
        {
            ["CentralPms:HikCentralControlledGateExecution:Enabled"] = "true",
            ["CentralPms:HikCentralGateIntegration:Enabled"] = "false"
        });

        Action act = () =>
        {
            using var _ = factory.CreateClient();
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HIKCENTRAL_GATE_INTEGRATION_REQUIRED*");
    }

    [Fact]
    public async Task ControlledExecution_WhenLiveIntegrationEnabledAlone_RemainsDisabled()
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = RecordingExecutionService.Success();
        using var factory = CreateFactory(
            executionService,
            secretFile.Path,
            controlledExecutionEnabled: false,
            liveIntegrationEnabled: true);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Guid.NewGuid(), new { confirmation = "OPEN_GATE" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        executionService.ExecuteCalls.Should().Be(0);
    }

    [Fact]
    public async Task ControlledExecution_WhenMtlsEnabled_RequiresTrustedInternalCertificate()
    {
        using var secretFile = TemporarySecretFile.Create();
        using var trustedCertificate = CreateCertificate("trusted-central-pms-controlled-gate-client");
        using var untrustedCertificate = CreateCertificate("untrusted-central-pms-controlled-gate-client");
        var executionService = RecordingExecutionService.Success();
        using var factory = CreateMtlsFactory(executionService, secretFile.Path, trustedCertificate, untrustedCertificate);
        using var client = factory.CreateClient();
        var gateCommandId = Guid.Parse("87000000-0000-0000-0000-000000000001");

        using var noCertificateResponse = await SendAsync(client, gateCommandId, new { confirmation = "OPEN_GATE" });
        using var untrustedResponse = await SendAsync(client, gateCommandId, new { confirmation = "OPEN_GATE" }, certificateName: "untrusted");
        using var trustedResponse = await SendAsync(client, gateCommandId, new { confirmation = "OPEN_GATE" }, certificateName: "trusted");

        noCertificateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        untrustedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        trustedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        executionService.ExecuteCalls.Should().Be(1);
        executionService.RetryCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("open_gate")]
    [InlineData("OPEN_GATE ")]
    [InlineData("CLOSE_GATE")]
    public async Task ControlledExecution_RequiresExactOpenGateConfirmation(string? confirmation)
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = RecordingExecutionService.Success();
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Guid.NewGuid(), new { confirmation });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Confirmation must exactly match OPEN_GATE.");
        executionService.ExecuteCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ControlledExecution_RequiresSafeCorrelationHeader(string? correlationHeader)
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = RecordingExecutionService.Success();
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            Guid.NewGuid(),
            new { confirmation = "OPEN_GATE" },
            correlationHeader: correlationHeader,
            addCorrelationHeader: correlationHeader is not null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        executionService.ExecuteCalls.Should().Be(0);
    }

    [Fact]
    public async Task ControlledExecution_RejectsMalformedGateCommandId()
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = RecordingExecutionService.Success();
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/internal/gates/commands/not-a-guid/execute")
        {
            Content = JsonContent.Create(new { confirmation = "OPEN_GATE" })
        };
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        executionService.ExecuteCalls.Should().Be(0);
    }

    [Fact]
    public async Task ControlledExecution_RejectsOverridesAndCredentialFields()
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = RecordingExecutionService.Success();
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            Guid.NewGuid(),
            new
            {
                confirmation = "OPEN_GATE",
                targetResourceCode = "door-overridden",
                retryMode = true,
                appSecret = "placeholder-secret",
                requestSignature = "placeholder-signature"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Request contains unsupported fields.");
        content.Should().NotContain("placeholder-secret");
        content.Should().NotContain("placeholder-signature");
        executionService.ExecuteCalls.Should().Be(0);
    }

    [Fact]
    public async Task ControlledExecution_ValidRequest_InvokesInitialExecutionOnceAndReturnsSafeMetadata()
    {
        using var secretFile = TemporarySecretFile.Create();
        var gateCommandId = Guid.Parse("88000000-0000-0000-0000-000000000001");
        var correlationId = Guid.Parse("88000000-0000-0000-0000-000000000002");
        var auditId = Guid.Parse("88000000-0000-0000-0000-000000000003");
        var executionService = new RecordingExecutionService((id, _) => Task.FromResult(
            new GateCommandExecutionResult(
                id,
                GateCommandExecutionOutcome.Executed,
                "SUCCEEDED",
                auditId,
                AdapterInvoked: true,
                ErrorCode: null,
                Message: null)));
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            gateCommandId,
            new { confirmation = "OPEN_GATE" },
            correlationHeader: correlationId.ToString());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionService.ExecuteCalls.Should().Be(1);
        executionService.RetryCalls.Should().Be(0);
        executionService.ExecutedCommandIds.Should().ContainSingle().Which.Should().Be(gateCommandId);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Executed");
        content.Should().Contain("SUCCEEDED");
        content.Should().Contain(correlationId.ToString());
        content.Should().Contain(auditId.ToString());
        content.Contains("physical gate opened", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        content.Contains("AppSecret", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        content.Contains("clientKey", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        content.Contains("signature", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        content.Contains("headers", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        content.Contains("raw", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Theory]
    [InlineData(GateCommandExecutionOutcome.AlreadyCompleted, "SUCCEEDED", null, HttpStatusCode.Conflict)]
    [InlineData(GateCommandExecutionOutcome.Rejected, "RETRYABLE", "GATE_COMMAND_STATUS_NOT_REQUESTED", HttpStatusCode.Conflict)]
    [InlineData(GateCommandExecutionOutcome.Rejected, "IN_PROGRESS", "GATE_COMMAND_STATUS_NOT_REQUESTED", HttpStatusCode.Conflict)]
    [InlineData(GateCommandExecutionOutcome.Rejected, "TERMINAL_FAILURE", "GATE_COMMAND_STATUS_NOT_REQUESTED", HttpStatusCode.Conflict)]
    [InlineData(GateCommandExecutionOutcome.Rejected, "", "GATE_COMMAND_NOT_FOUND", HttpStatusCode.NotFound)]
    [InlineData(GateCommandExecutionOutcome.Rejected, "REQUESTED", "GATE_COMMAND_TYPE_UNSUPPORTED", HttpStatusCode.Conflict)]
    [InlineData(GateCommandExecutionOutcome.Rejected, "REQUESTED", "GATE_COMMAND_TARGET_RESOURCE_MISSING", HttpStatusCode.Conflict)]
    public async Task ControlledExecution_NonInitialOrRejectedServiceOutcomes_MapSafely(
        GateCommandExecutionOutcome outcome,
        string commandStatus,
        string? errorCode,
        HttpStatusCode expectedStatus)
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = new RecordingExecutionService((id, _) => Task.FromResult(
            new GateCommandExecutionResult(
                id,
                outcome,
                commandStatus,
                HikCentralGateActionAuditId: null,
                AdapterInvoked: false,
                errorCode,
                "Gate command was not executed.")));
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Guid.NewGuid(), new { confirmation = "OPEN_GATE" });

        response.StatusCode.Should().Be(expectedStatus);
        executionService.ExecuteCalls.Should().Be(1);
        executionService.RetryCalls.Should().Be(0);
        var content = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(commandStatus))
        {
            content.Should().Contain(commandStatus);
        }

        if (errorCode is not null)
        {
            content.Should().Contain(errorCode);
        }
    }

    [Fact]
    public async Task ControlledExecution_RetryableAdapterOutcome_ReturnsSafeRetryableMetadataWithoutRetryExecution()
    {
        using var secretFile = TemporarySecretFile.Create();
        var executionService = new RecordingExecutionService((id, _) => Task.FromResult(
            new GateCommandExecutionResult(
                id,
                GateCommandExecutionOutcome.Executed,
                "RETRYABLE",
                Guid.Parse("89000000-0000-0000-0000-000000000001"),
                AdapterInvoked: true,
                ErrorCode: null,
                Message: null)));
        using var factory = CreateFactory(executionService, secretFile.Path);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Guid.NewGuid(), new { confirmation = "OPEN_GATE" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionService.ExecuteCalls.Should().Be(1);
        executionService.RetryCalls.Should().Be(0);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("commandStatus").GetString().Should().Be("RETRYABLE");
    }

    private CustomWebApplicationFactory CreateFactory(
        RecordingExecutionService executionService,
        string secretFilePath,
        bool controlledExecutionEnabled = true,
        bool liveIntegrationEnabled = true) =>
        _factory
            .WithConfigurationOverrides(EnabledConfiguration(secretFilePath, controlledExecutionEnabled, liveIntegrationEnabled))
            .WithServiceOverrides(services => ReplaceExecutionService(services, executionService));

    private static CustomWebApplicationFactory CreateMtlsFactory(
        RecordingExecutionService executionService,
        string secretFilePath,
        X509Certificate2 trustedCertificate,
        X509Certificate2 untrustedCertificate)
    {
        var accessor = new HeaderBackedCertificateAccessor(new Dictionary<string, X509Certificate2>
        {
            ["trusted"] = trustedCertificate,
            ["untrusted"] = untrustedCertificate
        });

        return new CustomWebApplicationFactory()
            .WithInternalMtls(new[] { trustedCertificate.Thumbprint }, accessor)
            .WithConfigurationOverrides(EnabledConfiguration(secretFilePath))
            .WithServiceOverrides(services => ReplaceExecutionService(services, executionService));
    }

    private static IReadOnlyDictionary<string, string?> EnabledConfiguration(
        string secretFilePath,
        bool controlledExecutionEnabled = true,
        bool liveIntegrationEnabled = true) =>
        new Dictionary<string, string?>
        {
            ["CentralPms:HikCentralControlledGateExecution:Enabled"] = controlledExecutionEnabled.ToString(),
            ["CentralPms:HikCentralGateIntegration:Enabled"] = liveIntegrationEnabled.ToString(),
            ["CentralPms:HikCentralGateIntegration:BaseAddress"] = "https://hikcentral-controlled-execution.example.invalid",
            ["CentralPms:HikCentralGateIntegration:ClientKeyIdentifier"] = "placeholder-client-key",
            ["CentralPms:HikCentralGateIntegration:ProfileCode"] = "access-control-door-open-v1",
            ["CentralPms:HikCentralGateIntegration:ControlMechanism"] = "AccessControlDoorControl",
            ["CentralPms:HikCentralGateIntegration:SecretFilePath"] = secretFilePath,
            ["CentralPms:HikCentralGateIntegration:MaxSecretBytes"] = "4096",
            ["CentralPms:HikCentralGateIntegration:HttpTimeoutSeconds"] = "10",
            ["CentralPms:HikCentralGateIntegration:MaxResponseBodyBytes"] = "16384",
            ["CentralPms:GateCommandDispatchWorker:Enabled"] = "false",
            ["CentralPms:GateCommandRecoveryWorker:Enabled"] = "false"
        };

    private static void ReplaceExecutionService(IServiceCollection services, RecordingExecutionService executionService)
    {
        services.RemoveAll<IGateCommandExecutionService>();
        services.AddSingleton<IGateCommandExecutionService>(executionService);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Guid gateCommandId,
        object body,
        string? correlationHeader = null,
        string? certificateName = null,
        bool addCorrelationHeader = true) =>
        await SendAsync(client, gateCommandId.ToString(), body, correlationHeader, certificateName, addCorrelationHeader);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string gateCommandId,
        object body,
        string? correlationHeader = null,
        string? certificateName = null,
        bool addCorrelationHeader = true)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            string.Format(RouteTemplate, gateCommandId))
        {
            Content = JsonContent.Create(body)
        };

        if (correlationHeader is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationHeader);
        }
        else if (addCorrelationHeader)
        {
            request.Headers.Add("X-Correlation-Id", Guid.Parse("86000000-0000-0000-0000-000000000001").ToString());
        }

        if (!string.IsNullOrWhiteSpace(certificateName))
        {
            request.Headers.Add(CertificateSelectorHeader, certificateName);
        }

        return await client.SendAsync(request);
    }

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class RecordingExecutionService : IGateCommandExecutionService
    {
        private readonly Func<Guid, CancellationToken, Task<GateCommandExecutionResult>> _execute;
        private readonly List<Guid> _executedCommandIds = [];

        public RecordingExecutionService(Func<Guid, CancellationToken, Task<GateCommandExecutionResult>> execute)
        {
            _execute = execute;
        }

        public int ExecuteCalls { get; private set; }

        public int RetryCalls { get; private set; }

        public IReadOnlyList<Guid> ExecutedCommandIds => _executedCommandIds;

        public static RecordingExecutionService Success() =>
            new((id, _) => Task.FromResult(
                new GateCommandExecutionResult(
                    id,
                    GateCommandExecutionOutcome.Executed,
                    "SUCCEEDED",
                    Guid.Parse("85000000-0000-0000-0000-000000000001"),
                    AdapterInvoked: true,
                    ErrorCode: null,
                    Message: null)));

        public async Task<GateCommandExecutionResult> ExecuteAsync(
            Guid gateCommandId,
            CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            _executedCommandIds.Add(gateCommandId);
            return await _execute(gateCommandId, cancellationToken);
        }

        public Task<GateCommandExecutionResult> RetryAsync(
            Guid gateCommandId,
            CancellationToken cancellationToken)
        {
            RetryCalls++;
            return Task.FromResult(
                new GateCommandExecutionResult(
                    gateCommandId,
                    GateCommandExecutionOutcome.Rejected,
                    "RETRYABLE",
                    HikCentralGateActionAuditId: null,
                    AdapterInvoked: false,
                    "RETRY_NOT_SUPPORTED_BY_ENDPOINT",
                    "Retry execution is not supported by this endpoint."));
        }
    }

    private sealed class HeaderBackedCertificateAccessor : IInternalClientCertificateAccessor
    {
        private readonly IReadOnlyDictionary<string, X509Certificate2> _certificates;

        public HeaderBackedCertificateAccessor(IReadOnlyDictionary<string, X509Certificate2> certificates)
        {
            _certificates = certificates;
        }

        public Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue(CertificateSelectorHeader, out var certificateName) ||
                !_certificates.TryGetValue(certificateName.ToString(), out var certificate))
            {
                return Task.FromResult<X509Certificate2?>(null);
            }

            return Task.FromResult<X509Certificate2?>(certificate);
        }
    }

    private sealed class TemporarySecretFile : IDisposable
    {
        private TemporarySecretFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporarySecretFile Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"exitpass-controlled-gate-secret-{Guid.NewGuid():N}.txt");
            File.WriteAllBytes(path, "placeholder-mounted-secret"u8.ToArray());
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
