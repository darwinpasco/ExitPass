namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Non-live transport boundary for HikCentral gate action requests.
/// </summary>
public interface IHikCentralGateActionTransport
{
    /// <summary>
    /// Sends a prepared HikCentral request through the configured non-live transport.
    /// </summary>
    Task<HikCentralGateActionTransportResult> SendAsync(
        HikCentralSignedRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fixture-backed HikCentral transport that never performs network I/O.
/// </summary>
public sealed class FakeHikCentralGateActionTransport : IHikCentralGateActionTransport
{
    private readonly Queue<HikCentralGateActionTransportResult> _queuedResults = new();
    private readonly List<HikCentralSignedRequest> _requests = new();

    /// <summary>
    /// Captured signed requests sent through the fake transport.
    /// </summary>
    public IReadOnlyList<HikCentralSignedRequest> Requests => _requests;

    /// <summary>
    /// Adds a fixture result returned by the next send operation.
    /// </summary>
    public void Enqueue(HikCentralGateActionTransportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _queuedResults.Enqueue(result);
    }

    /// <inheritdoc />
    public Task<HikCentralGateActionTransportResult> SendAsync(
        HikCentralSignedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _requests.Add(request);

        var result = _queuedResults.Count > 0
            ? _queuedResults.Dequeue()
            : Success(request);

        return Task.FromResult(result);
    }

    private static HikCentralGateActionTransportResult Success(HikCentralSignedRequest request) =>
        new(
            200,
            new HikCentralGateActionEnvelope(
                "0",
                "Success",
                [new HikCentralDoorControlResult(
                    ResolveFirstDoorIndexCode(request.Body),
                    0,
                    "Success")]),
            VendorRequestId: "fake-hikcentral-request",
            VendorCorrelationId: null,
            TimedOut: false,
            VendorUnavailable: false,
            TransportError: null,
            ObservedAtUtc: DateTimeOffset.UtcNow);

    private static string ResolveFirstDoorIndexCode(string body)
    {
        const string marker = "\"doorIndexCodes\":[\"";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += marker.Length;
        var end = body.IndexOf('"', start);
        return end > start
            ? body[start..end]
            : string.Empty;
    }
}
