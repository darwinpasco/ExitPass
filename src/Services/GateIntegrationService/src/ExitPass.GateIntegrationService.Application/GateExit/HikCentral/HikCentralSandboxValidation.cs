namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Operator-supplied request for a controlled HikCentral sandbox validation attempt.
/// </summary>
public sealed record HikCentralSandboxValidationRequest(
    string DoorIndexCode,
    HikCentralDoorControlType ControlType,
    HikCentralDoorControlDirection ControlDirection,
    string ValidationReason,
    string RequestedBy,
    Guid CorrelationId,
    bool ConfirmLiveAction);

/// <summary>
/// Operator-safe report for a HikCentral sandbox validation attempt.
/// </summary>
public sealed record HikCentralSandboxValidationReport(
    Guid ValidationAttemptId,
    Guid CorrelationId,
    DateTimeOffset TimestampUtc,
    string DoorIndexCode,
    HikCentralDoorControlType ControlType,
    HikCentralDoorControlDirection ControlDirection,
    bool Executed,
    bool Succeeded,
    string ResultCode,
    string DiagnosticMessage,
    int? HttpStatusCode,
    string? VendorResponseCode,
    string? VendorResponseMessage,
    HikCentralGateActionOutcome? OutcomeCategory,
    bool Retryable,
    bool TerminalFailure,
    Guid? AuditId,
    int DurationMs);

/// <summary>
/// Creates a validation-only gate command row used only to link sandbox audit metadata.
/// </summary>
public interface IHikCentralSandboxValidationCommandRecorder
{
    /// <summary>
    /// Creates a validation-only command context for a HikCentral sandbox attempt.
    /// </summary>
    Task<GateCommandLifecycleRecord> BeginValidationCommandAsync(
        HikCentralSandboxValidationCommandContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the validation-only command as completed.
    /// </summary>
    Task CompleteValidationCommandAsync(
        Guid commandId,
        bool succeeded,
        string resultCode,
        string diagnosticMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Validation-only command identity used to keep sandbox audit rows linkable.
/// </summary>
public sealed record HikCentralSandboxValidationCommandContext(
    Guid ValidationAttemptId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    string DoorIndexCode,
    Guid CorrelationId,
    DateTimeOffset RequestedAtUtc);

/// <summary>
/// Process-local validation command recorder for tests.
/// </summary>
public sealed class InMemoryHikCentralSandboxValidationCommandRecorder
    : IHikCentralSandboxValidationCommandRecorder
{
    private readonly List<GateCommandLifecycleRecord> _commands = new();

    /// <summary>
    /// Validation-only commands created by the harness.
    /// </summary>
    public IReadOnlyList<GateCommandLifecycleRecord> Commands => _commands;

    /// <inheritdoc />
    public Task<GateCommandLifecycleRecord> BeginValidationCommandAsync(
        HikCentralSandboxValidationCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var command = new GateCommandLifecycleRecord(
            Guid.NewGuid(),
            context.ValidationAttemptId,
            Guid.Empty,
            context.ExitAuthorizationId,
            context.GateAuthorizationConsumptionId,
            context.ParkingSessionId,
            context.PaymentAttemptId,
            context.TariffSnapshotId,
            null,
            context.DoorIndexCode,
            null,
            null,
            null,
            GateCommandStatus.InProgress,
            1,
            1,
            GateCommandRetryPolicy.Default.PolicyCode,
            context.RequestedAtUtc,
            context.RequestedAtUtc,
            context.RequestedAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            context.CorrelationId);

        _commands.Add(command);
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public Task CompleteValidationCommandAsync(
        Guid commandId,
        bool succeeded,
        string resultCode,
        string diagnosticMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Controlled HikCentral sandbox validation harness.
/// </summary>
public interface IHikCentralSandboxValidationHarness
{
    /// <summary>
    /// Executes a gated HikCentral sandbox validation attempt.
    /// </summary>
    Task<HikCentralSandboxValidationReport> ValidateGateActionAsync(
        HikCentralSandboxValidationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Executes explicitly gated HikCentral sandbox validation through the existing adapter path.
/// </summary>
public sealed class HikCentralSandboxValidationHarness : IHikCentralSandboxValidationHarness
{
    private const string DisabledResultCode = "HIKCENTRAL_SANDBOX_VALIDATION_DISABLED";
    private readonly GateActionAdapterMode _adapterMode;
    private readonly HikCentralGateActionOptions _options;
    private readonly IConsumedAuthorizationGateActionAdapter _adapter;
    private readonly IHikCentralSandboxValidationCommandRecorder _commandRecorder;

    /// <summary>
    /// Creates the controlled sandbox validation harness.
    /// </summary>
    public HikCentralSandboxValidationHarness(
        GateActionAdapterMode adapterMode,
        HikCentralGateActionOptions options,
        IConsumedAuthorizationGateActionAdapter adapter,
        IHikCentralSandboxValidationCommandRecorder commandRecorder)
    {
        _adapterMode = adapterMode;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _commandRecorder = commandRecorder ?? throw new ArgumentNullException(nameof(commandRecorder));
    }

    /// <inheritdoc />
    public async Task<HikCentralSandboxValidationReport> ValidateGateActionAsync(
        HikCentralSandboxValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationAttemptId = Guid.NewGuid();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var requestError = ValidateRequest(request);
        if (requestError is not null)
        {
            return Rejected(validationAttemptId, request, "HIKCENTRAL_SANDBOX_VALIDATION_REQUEST_INVALID", requestError);
        }

        var configurationError = ValidateConfiguration();
        if (configurationError is not null)
        {
            return Rejected(
                validationAttemptId,
                request,
                configurationError.Value.Code,
                configurationError.Value.Message);
        }

        if (_adapter is not HikCentralConsumedAuthorizationGateActionAdapter adapter)
        {
            return Rejected(
                validationAttemptId,
                request,
                "HIKCENTRAL_SANDBOX_VALIDATION_ADAPTER_INVALID",
                "HikCentral live adapter is not registered.");
        }

        var context = new HikCentralSandboxValidationCommandContext(
            validationAttemptId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            request.DoorIndexCode.Trim(),
            request.CorrelationId,
            startedAtUtc);
        var command = await _commandRecorder.BeginValidationCommandAsync(context, cancellationToken);
        var handoff = CreateValidationHandoff(request, context);
        var result = await adapter.ProcessCommandAsync(command, handoff, cancellationToken);
        var completedAtUtc = DateTimeOffset.UtcNow;

        await _commandRecorder.CompleteValidationCommandAsync(
            command.CommandId,
            result.Succeeded,
            result.ResultCode,
            result.DiagnosticMessage,
            completedAtUtc,
            cancellationToken);

        return new HikCentralSandboxValidationReport(
            validationAttemptId,
            request.CorrelationId,
            completedAtUtc,
            request.DoorIndexCode.Trim(),
            request.ControlType,
            request.ControlDirection,
            Executed: true,
            result.Succeeded,
            result.ResultCode,
            result.DiagnosticMessage,
            result.TransportResult.HttpStatusCode,
            result.VendorResponse.VendorResponseCode,
            result.VendorResponse.VendorResponseMessage,
            result.VendorResponse.Outcome,
            result.Retryable,
            result.TerminalFailure,
            result.AuditId,
            Math.Max(0, (int)Math.Ceiling((completedAtUtc - startedAtUtc).TotalMilliseconds)));
    }

    private static GateAuthorizationConsumedHandoff CreateValidationHandoff(
        HikCentralSandboxValidationRequest request,
        HikCentralSandboxValidationCommandContext context) =>
        new(
            EventId: Guid.Empty,
            SourceEventRef: $"hikcentral-sandbox-validation://{context.ValidationAttemptId}",
            context.ExitAuthorizationId,
            context.GateAuthorizationConsumptionId,
            context.ParkingSessionId,
            context.PaymentAttemptId,
            context.TariffSnapshotId,
            null,
            request.DoorIndexCode.Trim(),
            null,
            null,
            null,
            context.RequestedAtUtc,
            context.CorrelationId);

    private (string Code, string Message)? ValidateConfiguration()
    {
        if (!_options.SandboxValidationEnabled)
        {
            return (DisabledResultCode, "HikCentral sandbox validation is disabled.");
        }

        if (_adapterMode is not GateActionAdapterMode.HikCentralLive)
        {
            return ("HIKCENTRAL_SANDBOX_VALIDATION_REQUIRES_LIVE_MODE",
                "HikCentral sandbox validation requires GateActionAdapter:Mode=HikCentralLive.");
        }

        var errors = _options.ValidateForLiveTransport();
        return errors.Count > 0
            ? ("HIKCENTRAL_SANDBOX_VALIDATION_CONFIG_INVALID", string.Join(",", errors))
            : null;
    }

    private static string? ValidateRequest(HikCentralSandboxValidationRequest request)
    {
        if (!request.ConfirmLiveAction)
        {
            return "ConfirmLiveAction must be true for sandbox validation.";
        }

        if (string.IsNullOrWhiteSpace(request.DoorIndexCode))
        {
            return "DoorIndexCode is required.";
        }

        if (request.ControlType is not HikCentralDoorControlType.Open)
        {
            return "Only the Open control type is allowed by the sandbox validation harness.";
        }

        if (request.ControlDirection is not HikCentralDoorControlDirection.Exit)
        {
            return "Only the Exit control direction is allowed by the sandbox validation harness.";
        }

        if (string.IsNullOrWhiteSpace(request.ValidationReason))
        {
            return "ValidationReason is required.";
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            return "RequestedBy is required.";
        }

        return request.CorrelationId == Guid.Empty
            ? "CorrelationId is required."
            : null;
    }

    private static HikCentralSandboxValidationReport Rejected(
        Guid validationAttemptId,
        HikCentralSandboxValidationRequest request,
        string resultCode,
        string diagnosticMessage) =>
        new(
            validationAttemptId,
            request.CorrelationId,
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
}
