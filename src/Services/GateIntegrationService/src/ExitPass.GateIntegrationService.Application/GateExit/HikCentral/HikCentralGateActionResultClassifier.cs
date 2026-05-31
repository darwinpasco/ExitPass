namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Deterministically classifies HikCentral transport and response envelopes.
/// </summary>
public static class HikCentralGateActionResultClassifier
{
    /// <summary>
    /// Classifies a raw HikCentral transport result into gate-command semantics.
    /// </summary>
    public static HikCentralGateActionResponse Classify(HikCentralGateActionTransportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var outcome = ResolveOutcome(result);
        var vendorCode = result.Envelope?.Code;
        var vendorMessage = result.Envelope?.Message;

        return new HikCentralGateActionResponse(
            outcome,
            result.VendorRequestId,
            result.VendorCorrelationId,
            vendorCode,
            vendorMessage,
            ResolveRawStatusCategory(result, outcome),
            IsRetryable(outcome),
            IsTerminal(outcome),
            ResolveDiagnostic(result, outcome),
            result.ObservedAtUtc,
            result.Envelope?.DoorResults ?? Array.Empty<HikCentralDoorControlResult>());
    }

    private static HikCentralGateActionOutcome ResolveOutcome(HikCentralGateActionTransportResult result)
    {
        if (result.TimedOut)
        {
            return HikCentralGateActionOutcome.Timeout;
        }

        if (result.VendorUnavailable || result.HttpStatusCode is 408 or 429 or 500 or 502 or 503 or 504)
        {
            return HikCentralGateActionOutcome.VendorUnavailable;
        }

        if (result.HttpStatusCode is 401 or 403)
        {
            return HikCentralGateActionOutcome.Unauthorized;
        }

        if (result.HttpStatusCode is 400 or 404 or 409 or 422)
        {
            return HikCentralGateActionOutcome.InvalidRequest;
        }

        if (result.Envelope is null)
        {
            return HikCentralGateActionOutcome.Unknown;
        }

        if (string.Equals(result.Envelope.Code, "0", StringComparison.OrdinalIgnoreCase)
            && AllDoorResultsSucceeded(result.Envelope.DoorResults))
        {
            return HikCentralGateActionOutcome.Succeeded;
        }

        if (IsUnauthorizedCode(result.Envelope.Code, result.Envelope.Message))
        {
            return HikCentralGateActionOutcome.Unauthorized;
        }

        if (IsInvalidRequestCode(result.Envelope.Code, result.Envelope.Message)
            || HasTerminalDoorResult(result.Envelope.DoorResults))
        {
            return HikCentralGateActionOutcome.InvalidRequest;
        }

        return HikCentralGateActionOutcome.Unknown;
    }

    private static bool AllDoorResultsSucceeded(IReadOnlyList<HikCentralDoorControlResult> results) =>
        results.Count == 0 || results.All(result => result.ControlResultCode == 0);

    private static bool HasTerminalDoorResult(IReadOnlyList<HikCentralDoorControlResult> results) =>
        results.Any(result => result.ControlResultCode is 400 or 401 or 403 or 404);

    private static bool IsUnauthorizedCode(string? code, string? message)
    {
        var value = $"{code} {message}".ToUpperInvariant();
        return value.Contains("TOKEN", StringComparison.Ordinal)
            || value.Contains("SIGNATURE", StringComparison.Ordinal)
            || value.Contains("AUTH", StringComparison.Ordinal)
            || value.Contains("UNAUTHORIZED", StringComparison.Ordinal)
            || value.Contains("FORBIDDEN", StringComparison.Ordinal)
            || value.Contains("0X02401006", StringComparison.Ordinal);
    }

    private static bool IsInvalidRequestCode(string? code, string? message)
    {
        var value = $"{code} {message}".ToUpperInvariant();
        return value.Contains("INVALID", StringComparison.Ordinal)
            || value.Contains("PARAM", StringComparison.Ordinal)
            || value.Contains("RESOURCE", StringComparison.Ordinal)
            || value.Contains("DOOR", StringComparison.Ordinal)
            || value.Contains("NOT FOUND", StringComparison.Ordinal);
    }

    private static bool IsRetryable(HikCentralGateActionOutcome outcome) =>
        outcome is HikCentralGateActionOutcome.Timeout
            or HikCentralGateActionOutcome.VendorUnavailable
            or HikCentralGateActionOutcome.RetryableFailure
            or HikCentralGateActionOutcome.Unknown;

    private static bool IsTerminal(HikCentralGateActionOutcome outcome) =>
        outcome is HikCentralGateActionOutcome.Unauthorized
            or HikCentralGateActionOutcome.Misconfigured
            or HikCentralGateActionOutcome.InvalidRequest
            or HikCentralGateActionOutcome.TerminalFailure;

    private static string ResolveRawStatusCategory(
        HikCentralGateActionTransportResult result,
        HikCentralGateActionOutcome outcome)
    {
        if (result.TimedOut)
        {
            return "TIMEOUT";
        }

        if (result.HttpStatusCode.HasValue)
        {
            return $"HTTP_{result.HttpStatusCode.Value}";
        }

        return outcome.ToString().ToUpperInvariant();
    }

    private static string ResolveDiagnostic(
        HikCentralGateActionTransportResult result,
        HikCentralGateActionOutcome outcome)
    {
        if (!string.IsNullOrWhiteSpace(result.TransportError))
        {
            return result.TransportError;
        }

        if (!string.IsNullOrWhiteSpace(result.Envelope?.Message))
        {
            return result.Envelope.Message;
        }

        return outcome switch
        {
            HikCentralGateActionOutcome.Succeeded => "HikCentral gate action succeeded.",
            HikCentralGateActionOutcome.Timeout => "HikCentral gate action timed out before a definitive response.",
            HikCentralGateActionOutcome.VendorUnavailable => "HikCentral gateway or service was unavailable.",
            HikCentralGateActionOutcome.Unauthorized => "HikCentral authentication or authorization failed.",
            HikCentralGateActionOutcome.InvalidRequest => "HikCentral rejected the request or target resource.",
            _ => "HikCentral gate action outcome was not recognized."
        };
    }
}
