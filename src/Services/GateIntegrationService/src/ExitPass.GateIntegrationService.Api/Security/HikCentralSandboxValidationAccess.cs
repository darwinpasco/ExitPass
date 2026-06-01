using System.Security.Cryptography;
using System.Text;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

namespace ExitPass.GateIntegrationService.Api.Security;

/// <summary>
/// Configuration for the operational access gate on HikCentral sandbox validation.
/// </summary>
public sealed class HikCentralSandboxValidationAccessOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "GateIntegrations:HikCentral:SandboxValidationAccess";

    /// <summary>
    /// Gets or sets a value indicating whether endpoint access control is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets allowed internal service identity identifiers.
    /// </summary>
    public List<Guid> AllowedServiceIdentityIds { get; } = new();

    /// <summary>
    /// Gets or sets the required sandbox validation key. Supply through secrets/environment only.
    /// </summary>
    public string? RequiredApiKey { get; set; }
}

/// <summary>
/// Validates operational caller headers before the HikCentral sandbox harness can run.
/// </summary>
public sealed class HikCentralSandboxValidationAccessValidator
{
    private const string CorrelationIdHeaderName = "X-Correlation-Id";
    private const string ServiceIdentityHeaderName = "X-Service-Identity-Id";
    private const string ValidationKeyHeaderName = "X-HikCentral-Sandbox-Validation-Key";
    private readonly HikCentralSandboxValidationAccessOptions _options;

    /// <summary>
    /// Creates the access validator.
    /// </summary>
    public HikCentralSandboxValidationAccessValidator(HikCentralSandboxValidationAccessOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Validates access-control headers and returns a denial report when access is not allowed.
    /// </summary>
    public HikCentralSandboxValidationAccessDecision Validate(
        HttpRequest httpRequest,
        HikCentralSandboxValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(httpRequest);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadGuid(httpRequest, CorrelationIdHeaderName, out var correlationId))
        {
            return Denied(
                request,
                Guid.Empty,
                StatusCodes.Status400BadRequest,
                "CORRELATION_ID_REQUIRED",
                "X-Correlation-Id header is required.");
        }

        if (!_options.Enabled ||
            _options.AllowedServiceIdentityIds.Count == 0 ||
            string.IsNullOrWhiteSpace(_options.RequiredApiKey))
        {
            return Denied(
                request,
                correlationId,
                StatusCodes.Status401Unauthorized,
                "HIKCENTRAL_SANDBOX_ACCESS_DISABLED",
                "HikCentral sandbox validation access control is disabled or incomplete.");
        }

        if (!TryReadGuid(httpRequest, ServiceIdentityHeaderName, out var serviceIdentityId))
        {
            return Denied(
                request,
                correlationId,
                StatusCodes.Status401Unauthorized,
                "SERVICE_IDENTITY_REQUIRED",
                "X-Service-Identity-Id header is required.");
        }

        if (!_options.AllowedServiceIdentityIds.Contains(serviceIdentityId))
        {
            return Denied(
                request,
                correlationId,
                StatusCodes.Status403Forbidden,
                "SERVICE_IDENTITY_NOT_ALLOWED",
                "The service identity is not allowed to run HikCentral sandbox validation.");
        }

        if (!httpRequest.Headers.TryGetValue(ValidationKeyHeaderName, out var validationKeyHeader) ||
            string.IsNullOrWhiteSpace(validationKeyHeader.ToString()))
        {
            return Denied(
                request,
                correlationId,
                StatusCodes.Status401Unauthorized,
                "SANDBOX_VALIDATION_KEY_REQUIRED",
                "HikCentral sandbox validation key is required.");
        }

        if (!ApiKeysEqual(validationKeyHeader.ToString(), _options.RequiredApiKey))
        {
            return Denied(
                request,
                correlationId,
                StatusCodes.Status401Unauthorized,
                "SANDBOX_VALIDATION_KEY_INVALID",
                "HikCentral sandbox validation key is invalid.");
        }

        return HikCentralSandboxValidationAccessDecision.Allowed();
    }

    private static bool TryReadGuid(HttpRequest request, string headerName, out Guid value)
    {
        value = Guid.Empty;
        return request.Headers.TryGetValue(headerName, out var headerValue) &&
               Guid.TryParse(headerValue.ToString(), out value) &&
               value != Guid.Empty;
    }

    private static bool ApiKeysEqual(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static HikCentralSandboxValidationAccessDecision Denied(
        HikCentralSandboxValidationRequest request,
        Guid correlationId,
        int statusCode,
        string resultCode,
        string diagnosticMessage)
    {
        var report = new HikCentralSandboxValidationReport(
            Guid.NewGuid(),
            correlationId,
            DateTimeOffset.UtcNow,
            request.DoorIndexCode,
            request.ControlType,
            request.ControlDirection,
            Executed: false,
            Succeeded: false,
            resultCode,
            diagnosticMessage,
            HttpStatusCode: null,
            VendorResponseCode: null,
            VendorResponseMessage: null,
            OutcomeCategory: null,
            Retryable: false,
            TerminalFailure: true,
            AuditId: null,
            DurationMs: 0);

        return HikCentralSandboxValidationAccessDecision.Denied(statusCode, report);
    }
}

/// <summary>
/// Access decision for HikCentral sandbox validation endpoint calls.
/// </summary>
public sealed record HikCentralSandboxValidationAccessDecision(
    bool IsAllowed,
    int StatusCode,
    HikCentralSandboxValidationReport? DenialReport)
{
    /// <summary>
    /// Creates an allowed decision.
    /// </summary>
    public static HikCentralSandboxValidationAccessDecision Allowed() =>
        new(IsAllowed: true, StatusCodes.Status200OK, DenialReport: null);

    /// <summary>
    /// Creates a denied decision.
    /// </summary>
    public static HikCentralSandboxValidationAccessDecision Denied(
        int statusCode,
        HikCentralSandboxValidationReport report) =>
        new(IsAllowed: false, statusCode, report);
}
