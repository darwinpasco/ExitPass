namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Vendor-neutral result returned by the non-live HikCentral adapter preparation layer.
/// </summary>
public sealed record HikCentralConsumedAuthorizationGateActionAdapterResult(
    bool Succeeded,
    bool Retryable,
    bool TerminalFailure,
    string ResultCode,
    string DiagnosticMessage,
    HikCentralGateActionRequest VendorRequest,
    HikCentralSignedRequest SignedRequest,
    HikCentralGateActionResponse VendorResponse);

/// <summary>
/// Exception form usable by the current command lifecycle, which records adapter exceptions as failures.
/// </summary>
public sealed class ConsumedAuthorizationGateActionAdapterException : Exception
{
    /// <summary>
    /// Creates an adapter failure with deterministic retry semantics.
    /// </summary>
    public ConsumedAuthorizationGateActionAdapterException(
        string resultCode,
        string message,
        bool retryable)
        : base(message)
    {
        ResultCode = resultCode;
        Retryable = retryable;
    }

    /// <summary>
    /// Deterministic failure code.
    /// </summary>
    public string ResultCode { get; }

    /// <summary>
    /// Whether the current command lifecycle may retry the failure.
    /// </summary>
    public bool Retryable { get; }
}

/// <summary>
/// Composes HikCentral request creation, signing, fake transport, and response classification.
/// </summary>
public sealed class HikCentralConsumedAuthorizationGateActionAdapter : IConsumedAuthorizationGateCommandActionAdapter
{
    private readonly HikCentralRequestSigner _signer;
    private readonly IHikCentralGateActionTransport _transport;

    /// <summary>
    /// Creates the non-live HikCentral gate action adapter.
    /// </summary>
    public HikCentralConsumedAuthorizationGateActionAdapter(
        HikCentralRequestSigner signer,
        IHikCentralGateActionTransport transport)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Executes a non-live HikCentral door-control preparation flow for the supplied gate command.
    /// </summary>
    public async Task<HikCentralConsumedAuthorizationGateActionAdapterResult> ProcessCommandAsync(
        GateCommandLifecycleRecord command,
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handoff);

        var vendorRequest = HikCentralGateActionRequestFactory.CreateOpenExitBarrierRequest(command, handoff);
        var signedRequest = _signer.SignDoorControlRequest(vendorRequest);
        var transportResult = await _transport.SendAsync(signedRequest, cancellationToken);
        var response = HikCentralGateActionResultClassifier.Classify(transportResult);

        return new HikCentralConsumedAuthorizationGateActionAdapterResult(
            response.Outcome is HikCentralGateActionOutcome.Succeeded,
            response.Retryable,
            response.TerminalFailure,
            ResolveResultCode(response),
            response.DiagnosticMessage,
            vendorRequest,
            signedRequest,
            response);
    }

    /// <summary>
    /// Executes the non-live flow and throws a deterministic adapter exception for failed outcomes.
    /// </summary>
    public async Task ProcessCommandOrThrowAsync(
        GateCommandLifecycleRecord command,
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        var result = await ProcessCommandAsync(command, handoff, cancellationToken);
        if (result.Succeeded)
        {
            return;
        }

        throw new ConsumedAuthorizationGateActionAdapterException(
            result.ResultCode,
            result.DiagnosticMessage,
            result.Retryable);
    }

    /// <inheritdoc />
    public Task ProcessConsumedAuthorizationAsync(
        GateCommandLifecycleRecord command,
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken) =>
        ProcessCommandOrThrowAsync(command, handoff, cancellationToken);

    /// <inheritdoc />
    public Task ProcessConsumedAuthorizationAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        throw new InvalidOperationException(
            "HikCentral gate action adapter requires the active gate command lifecycle record.");
    }

    private static string ResolveResultCode(HikCentralGateActionResponse response) =>
        response.Outcome switch
        {
            HikCentralGateActionOutcome.Succeeded => "HIKCENTRAL_GATE_ACTION_SUCCEEDED",
            HikCentralGateActionOutcome.Timeout => "HIKCENTRAL_GATE_ACTION_TIMEOUT",
            HikCentralGateActionOutcome.VendorUnavailable => "HIKCENTRAL_GATE_ACTION_VENDOR_UNAVAILABLE",
            HikCentralGateActionOutcome.Unauthorized => "HIKCENTRAL_GATE_ACTION_UNAUTHORIZED",
            HikCentralGateActionOutcome.InvalidRequest => "HIKCENTRAL_GATE_ACTION_INVALID_REQUEST",
            HikCentralGateActionOutcome.Misconfigured => "HIKCENTRAL_GATE_ACTION_MISCONFIGURED",
            _ => "HIKCENTRAL_GATE_ACTION_UNKNOWN_FAILURE"
        };
}
