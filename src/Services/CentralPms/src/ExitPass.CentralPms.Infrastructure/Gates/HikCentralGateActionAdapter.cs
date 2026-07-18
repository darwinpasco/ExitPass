using System.Net.Http;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Composes existing HikCentral request planning, signing, request construction, and transport for one gate action.
/// </summary>
public sealed class HikCentralGateActionAdapter : IHikCentralGateActionAdapter
{
    private readonly IHikCentralGateRuntimeMaterialProvider _runtimeMaterialProvider;
    private readonly IHikCentralGateActionRequestPlanBuilder _requestPlanBuilder;
    private readonly IHikCentralRequestSigningMaterialBuilder _signingMaterialBuilder;
    private readonly IHikCentralRequestSignatureCalculator _signatureCalculator;
    private readonly IHikCentralSignedHttpRequestBuilder _signedRequestBuilder;
    private readonly IHikCentralHttpTransport _transport;

    /// <summary>
    /// Creates a composed HikCentral gate-action adapter.
    /// </summary>
    public HikCentralGateActionAdapter(
        IHikCentralGateRuntimeMaterialProvider runtimeMaterialProvider,
        IHikCentralGateActionRequestPlanBuilder requestPlanBuilder,
        IHikCentralRequestSigningMaterialBuilder signingMaterialBuilder,
        IHikCentralRequestSignatureCalculator signatureCalculator,
        IHikCentralSignedHttpRequestBuilder signedRequestBuilder,
        IHikCentralHttpTransport transport)
    {
        _runtimeMaterialProvider = runtimeMaterialProvider ?? throw new ArgumentNullException(nameof(runtimeMaterialProvider));
        _requestPlanBuilder = requestPlanBuilder ?? throw new ArgumentNullException(nameof(requestPlanBuilder));
        _signingMaterialBuilder = signingMaterialBuilder ?? throw new ArgumentNullException(nameof(signingMaterialBuilder));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _signedRequestBuilder = signedRequestBuilder ?? throw new ArgumentNullException(nameof(signedRequestBuilder));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <inheritdoc />
    public async Task<HikCentralGateActionResult> ExecuteAsync(
        HikCentralGateActionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null)
        {
            throw Rejected("HIKCENTRAL_GATE_ACTION_REQUEST_REQUIRED", "HikCentral gate action request is required.");
        }

        HikCentralGateRuntimeMaterial? runtimeMaterial = null;
        HttpRequestMessage? signedRequest = null;
        try
        {
            runtimeMaterial = await _runtimeMaterialProvider.GetAsync(request, cancellationToken).ConfigureAwait(false);
            if (runtimeMaterial is null)
            {
                throw Rejected("HIKCENTRAL_RUNTIME_MATERIAL_REQUIRED", "HikCentral runtime material is required.");
            }

            var requestPlan = _requestPlanBuilder.Build(request, runtimeMaterial.ControlProfile);
            var signingMaterial = _signingMaterialBuilder.Build(new HikCentralSigningMaterialInput(
                requestPlan,
                runtimeMaterial.ClientKeyIdentifier,
                runtimeMaterial.TimestampMilliseconds,
                runtimeMaterial.Nonce,
                runtimeMaterial.SignatureMethod));
            var signature = _signatureCalculator.Calculate(signingMaterial, runtimeMaterial.SecretBytes);

            signedRequest = _signedRequestBuilder.Build(
                runtimeMaterial.BaseAddress,
                requestPlan,
                signingMaterial,
                signature);

            var transportResult = await _transport.SendAsync(signedRequest, cancellationToken).ConfigureAwait(false);
            return MapTransportResult(request, transportResult);
        }
        finally
        {
            signedRequest?.Dispose();
            runtimeMaterial?.Dispose();
        }
    }

    private static HikCentralGateActionResult MapTransportResult(
        HikCentralGateActionRequest request,
        HikCentralHttpTransportResult transportResult)
    {
        if (transportResult is null)
        {
            throw Rejected("HIKCENTRAL_TRANSPORT_RESULT_REQUIRED", "HikCentral transport result is required.");
        }

        var mapping = MapOutcome(transportResult);
        return new HikCentralGateActionResult(
            HikCentralGateActionConstants.VendorCode,
            HikCentralGateActionConstants.RequestMethod,
            request.VendorOperation.Trim().ToUpperInvariant(),
            request.TargetResourceCode.Trim(),
            mapping.ActionOutcome,
            mapping.Retryable,
            mapping.FailureRecorded,
            ToDurationMilliseconds(transportResult.DurationMs),
            transportResult.TimedOut,
            transportResult.VendorUnavailable,
            transportResult.TransportFailure,
            transportResult.HttpStatusCode,
            transportResult.VendorResultCode,
            transportResult.VendorResultMessage,
            request.CorrelationId,
            transportResult.VendorCorrelationId,
            request.RequestedAt,
            transportResult.RespondedAt);
    }

    private static AdapterOutcomeMapping MapOutcome(HikCentralHttpTransportResult transportResult)
    {
        return transportResult.Outcome switch
        {
            HikCentralHttpTransportOutcome.Succeeded => new(
                HikCentralGateActionConstants.OutcomeSucceeded,
                Retryable: false,
                FailureRecorded: false),

            HikCentralHttpTransportOutcome.Throttled => new(
                HikCentralGateActionConstants.OutcomeRetryableFailure,
                Retryable: true,
                FailureRecorded: true),

            HikCentralHttpTransportOutcome.RequestTimeout or
            HikCentralHttpTransportOutcome.TimedOut => new(
                HikCentralGateActionConstants.OutcomeTimeout,
                Retryable: true,
                FailureRecorded: true),

            HikCentralHttpTransportOutcome.VendorFailure when transportResult.VendorUnavailable => new(
                HikCentralGateActionConstants.OutcomeVendorUnavailable,
                Retryable: true,
                FailureRecorded: true),

            HikCentralHttpTransportOutcome.VendorFailure => new(
                HikCentralGateActionConstants.OutcomeRetryableFailure,
                Retryable: true,
                FailureRecorded: true),

            HikCentralHttpTransportOutcome.TransportFailure => new(
                HikCentralGateActionConstants.OutcomeTransportFailure,
                Retryable: true,
                FailureRecorded: true),

            HikCentralHttpTransportOutcome.ClientError or
            HikCentralHttpTransportOutcome.Unauthorized or
            HikCentralHttpTransportOutcome.Forbidden or
            HikCentralHttpTransportOutcome.MalformedResponse or
            HikCentralHttpTransportOutcome.ResponseBodyTooLarge => new(
                HikCentralGateActionConstants.OutcomeTerminalFailure,
                Retryable: false,
                FailureRecorded: true),

            _ => new(
                HikCentralGateActionConstants.OutcomeTerminalFailure,
                Retryable: false,
                FailureRecorded: true)
        };
    }

    private static int ToDurationMilliseconds(long durationMs)
    {
        if (durationMs <= 0)
        {
            return 0;
        }

        return durationMs > int.MaxValue
            ? int.MaxValue
            : (int)durationMs;
    }

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);

    private sealed record AdapterOutcomeMapping(
        string ActionOutcome,
        bool Retryable,
        bool FailureRecorded);
}
