using System.Globalization;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Assembles one disposable HikCentral runtime-material snapshot without resolving production secrets directly.
/// </summary>
public sealed class HikCentralGateRuntimeMaterialProvider : IHikCentralGateRuntimeMaterialProvider
{
    private readonly HikCentralGateRuntimeOptions _options;
    private readonly IHikCentralGateSecretSource _secretSource;
    private readonly IHikCentralNonceGenerator _nonceGenerator;
    private readonly TimeProvider _timeProvider;

    public HikCentralGateRuntimeMaterialProvider(
        HikCentralGateRuntimeOptions options,
        IHikCentralGateSecretSource secretSource,
        IHikCentralNonceGenerator nonceGenerator,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _secretSource = secretSource ?? throw new ArgumentNullException(nameof(secretSource));
        _nonceGenerator = nonceGenerator ?? throw new ArgumentNullException(nameof(nonceGenerator));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<HikCentralGateRuntimeMaterial> GetAsync(
        HikCentralGateActionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        ValidateOptions(_options);

        HikCentralGateSecretMaterial? secretMaterial = null;
        try
        {
            secretMaterial = await _secretSource.GetSecretAsync(cancellationToken).ConfigureAwait(false);
            if (secretMaterial is null)
            {
                throw Rejected("HIKCENTRAL_SECRET_MATERIAL_REQUIRED", "HikCentral secret material is required.");
            }

            if (secretMaterial.SecretBytes.IsEmpty)
            {
                throw Rejected("HIKCENTRAL_SECRET_MATERIAL_EMPTY", "HikCentral secret material is required.");
            }

            var timestampMilliseconds = _timeProvider
                .GetUtcNow()
                .ToUnixTimeMilliseconds()
                .ToString(CultureInfo.InvariantCulture);
            var nonce = _nonceGenerator.Generate();
            ValidateNonce(nonce);

            var profile = HikCentralGateControlProfile.AccessControlDoorOpen(_options.ProfileCode!.Trim());
            return new HikCentralGateRuntimeMaterial(
                ParseBaseAddress(_options.BaseAddress),
                profile,
                _options.ClientKeyIdentifier!.Trim(),
                secretMaterial.SecretBytes,
                timestampMilliseconds,
                nonce.Trim(),
                HikCentralRequestSigningMaterialConstants.SignatureMethod);
        }
        finally
        {
            secretMaterial?.Dispose();
        }
    }

    private static void ValidateRequest(HikCentralGateActionRequest request)
    {
        if (request is null)
        {
            throw Rejected("HIKCENTRAL_GATE_ACTION_REQUEST_REQUIRED", "HikCentral gate action request is required.");
        }

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

        if (!string.Equals(
                request.VendorOperation?.Trim(),
                HikCentralGateActionConstants.OpenGateOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected("VENDOR_OPERATION_UNSUPPORTED", "Only OPEN_GATE HikCentral runtime material is supported.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetResourceCode))
        {
            throw Rejected("TARGET_RESOURCE_CODE_REQUIRED", "Target resource code is required.");
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw Rejected("CORRELATION_ID_REQUIRED", "Correlation id is required.");
        }
    }

    private static void ValidateOptions(HikCentralGateRuntimeOptions options)
    {
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw Rejected(errors[0], "HikCentral gate runtime options are invalid.");
        }
    }

    private static Uri ParseBaseAddress(string? baseAddress) =>
        new(baseAddress!.Trim(), UriKind.Absolute);

    private static void ValidateNonce(string? nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce))
        {
            throw Rejected("HIKCENTRAL_NONCE_REQUIRED", "HikCentral nonce is required.");
        }

        var trimmed = nonce.Trim();
        if (trimmed.Length > 64 ||
            trimmed.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.'))
        {
            throw Rejected("HIKCENTRAL_NONCE_INVALID", "HikCentral nonce contains unsupported characters.");
        }
    }

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
