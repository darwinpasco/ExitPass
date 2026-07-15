using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Deterministic fake HikCentral gate action adapter for tests and local executor proofs.
/// </summary>
public sealed class FakeHikCentralGateActionAdapter : IHikCentralGateActionAdapter
{
    /// <summary>
    /// Creates a fake HikCentral gate action adapter with an explicit scenario.
    /// </summary>
    public FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario scenario)
    {
        Scenario = scenario;
    }

    /// <summary>
    /// Scenario returned by this fake adapter.
    /// </summary>
    public FakeHikCentralGateActionScenario Scenario { get; }

    /// <inheritdoc />
    public Task<HikCentralGateActionResult> ExecuteAsync(
        HikCentralGateActionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);

        var outcome = Scenario switch
        {
            FakeHikCentralGateActionScenario.Success => CreateOutcome(
                HikCentralGateActionConstants.OutcomeSucceeded,
                retryable: false,
                failureRecorded: false,
                durationMs: 25,
                httpStatusCode: 200,
                vendorResultCode: "0",
                vendorResultMessage: "Simulated HikCentral request accepted."),

            FakeHikCentralGateActionScenario.RetryableFailure => CreateOutcome(
                HikCentralGateActionConstants.OutcomeRetryableFailure,
                retryable: true,
                failureRecorded: true,
                durationMs: 40,
                httpStatusCode: 503,
                vendorResultCode: "SIM_RETRYABLE_FAILURE",
                vendorResultMessage: "Simulated retryable HikCentral failure."),

            FakeHikCentralGateActionScenario.TerminalFailure => CreateOutcome(
                HikCentralGateActionConstants.OutcomeTerminalFailure,
                retryable: false,
                failureRecorded: true,
                durationMs: 35,
                httpStatusCode: 400,
                vendorResultCode: "SIM_TERMINAL_FAILURE",
                vendorResultMessage: "Simulated terminal HikCentral failure."),

            FakeHikCentralGateActionScenario.Timeout => CreateOutcome(
                HikCentralGateActionConstants.OutcomeTimeout,
                retryable: true,
                failureRecorded: true,
                durationMs: 30000,
                httpStatusCode: null,
                vendorResultCode: "SIM_TIMEOUT",
                vendorResultMessage: "Simulated HikCentral timeout.",
                timedOut: true),

            FakeHikCentralGateActionScenario.VendorUnavailable => CreateOutcome(
                HikCentralGateActionConstants.OutcomeVendorUnavailable,
                retryable: true,
                failureRecorded: true,
                durationMs: 10,
                httpStatusCode: 503,
                vendorResultCode: "SIM_VENDOR_UNAVAILABLE",
                vendorResultMessage: "Simulated HikCentral unavailable response.",
                vendorUnavailable: true),

            FakeHikCentralGateActionScenario.TransportFailure => CreateOutcome(
                HikCentralGateActionConstants.OutcomeTransportFailure,
                retryable: true,
                failureRecorded: true,
                durationMs: 5,
                httpStatusCode: null,
                vendorResultCode: "SIM_TRANSPORT_FAILURE",
                vendorResultMessage: "Simulated transport failure.",
                transportFailure: true),

            _ => throw new HikCentralGateActionRejectedException(
                "HIKCENTRAL_FAKE_SCENARIO_UNSUPPORTED",
                "Unsupported fake HikCentral gate action scenario.")
        };

        return Task.FromResult(new HikCentralGateActionResult(
            HikCentralGateActionConstants.VendorCode,
            HikCentralGateActionConstants.RequestMethod,
            request.VendorOperation.Trim().ToUpperInvariant(),
            request.TargetResourceCode.Trim(),
            outcome.ActionOutcome,
            outcome.Retryable,
            outcome.FailureRecorded,
            outcome.DurationMs,
            outcome.TimedOut,
            outcome.VendorUnavailable,
            outcome.TransportFailure,
            outcome.HttpStatusCode,
            outcome.VendorResultCode,
            outcome.VendorResultMessage,
            request.CorrelationId,
            BuildVendorCorrelationId(request),
            request.RequestedAt,
            request.RequestedAt.AddMilliseconds(outcome.DurationMs)));
    }

    private static void Validate(HikCentralGateActionRequest request)
    {
        if (request.GateCommandId == Guid.Empty)
        {
            throw Rejected("GATE_COMMAND_ID_REQUIRED", "Gate command id is required.");
        }

        if (request.GateAuthorizationConsumptionId == Guid.Empty)
        {
            throw Rejected("GATE_AUTHORIZATION_CONSUMPTION_ID_REQUIRED", "Gate authorization consumption id is required.");
        }

        if (request.ExitAuthorizationId == Guid.Empty)
        {
            throw Rejected("EXIT_AUTHORIZATION_ID_REQUIRED", "Exit authorization id is required.");
        }

        if (request.GateDeviceId == Guid.Empty)
        {
            throw Rejected("GATE_DEVICE_ID_REQUIRED", "Gate device id is required.");
        }

        if (request.VendorSystemId == Guid.Empty)
        {
            throw Rejected("VENDOR_SYSTEM_ID_REQUIRED", "Vendor system id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetResourceCode))
        {
            throw Rejected("TARGET_RESOURCE_CODE_REQUIRED", "Target resource code is required.");
        }

        if (!string.Equals(
                request.VendorOperation?.Trim(),
                HikCentralGateActionConstants.OpenGateOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected("VENDOR_OPERATION_UNSUPPORTED", "Vendor operation is not supported by the fake adapter.");
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw Rejected("CORRELATION_ID_REQUIRED", "Correlation id is required.");
        }

        if (request.RequestedAt == default)
        {
            throw Rejected("REQUESTED_AT_REQUIRED", "Requested-at timestamp is required.");
        }
    }

    private static FakeOutcome CreateOutcome(
        string actionOutcome,
        bool retryable,
        bool failureRecorded,
        int durationMs,
        int? httpStatusCode,
        string vendorResultCode,
        string vendorResultMessage,
        bool timedOut = false,
        bool vendorUnavailable = false,
        bool transportFailure = false)
    {
        return new FakeOutcome(
            actionOutcome,
            retryable,
            failureRecorded,
            durationMs,
            timedOut,
            vendorUnavailable,
            transportFailure,
            httpStatusCode,
            vendorResultCode,
            vendorResultMessage);
    }

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);

    private static string BuildVendorCorrelationId(HikCentralGateActionRequest request) =>
        $"FAKE-HIKCENTRAL-{request.CorrelationId:N}";

    private sealed record FakeOutcome(
        string ActionOutcome,
        bool Retryable,
        bool FailureRecorded,
        int DurationMs,
        bool TimedOut,
        bool VendorUnavailable,
        bool TransportFailure,
        int? HttpStatusCode,
        string VendorResultCode,
        string VendorResultMessage);
}

/// <summary>
/// Deterministic fake HikCentral gate action scenarios.
/// </summary>
public enum FakeHikCentralGateActionScenario
{
    /// <summary>
    /// Simulates a successful accepted request.
    /// </summary>
    Success,

    /// <summary>
    /// Simulates a retryable vendor failure.
    /// </summary>
    RetryableFailure,

    /// <summary>
    /// Simulates a terminal vendor failure.
    /// </summary>
    TerminalFailure,

    /// <summary>
    /// Simulates a request timeout.
    /// </summary>
    Timeout,

    /// <summary>
    /// Simulates a vendor unavailable response.
    /// </summary>
    VendorUnavailable,

    /// <summary>
    /// Simulates a transport-level failure.
    /// </summary>
    TransportFailure
}
