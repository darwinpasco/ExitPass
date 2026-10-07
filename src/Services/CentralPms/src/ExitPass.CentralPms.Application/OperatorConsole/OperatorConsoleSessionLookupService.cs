namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated Operator Console parking session lookup service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Access evaluation is persisted before any session details are returned.
/// - Denied access does not read or return parking session details.
/// - Session lookup reads an existing core session first and otherwise returns the projection without financial side effects.
/// </summary>
public sealed class OperatorConsoleSessionLookupService : IOperatorConsoleSessionLookupService
{
    private const string CoreParkingSessionSource = "CORE_PARKING_SESSION";
    private const string VendorSessionProjectionSource = "VENDOR_SESSION_PROJECTION";
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.SessionLookup;
    private const string LookupModeParkingSessionId = "PARKING_SESSION_ID";
    private const string LookupModeTicketReference = "TICKET_REFERENCE";
    private const string LookupModePlateLicense = "PLATE_LICENSE";

    private readonly IOperatorConsoleAccessEvaluationService _accessEvaluationService;
    private readonly IOperatorConsoleAccessEvaluationWriter _accessEvaluationWriter;
    private readonly IOperatorConsoleSessionLookupReadRepository _sessionRepository;

    /// <summary>
    /// Creates an Operator Console session lookup service.
    /// </summary>
    public OperatorConsoleSessionLookupService(
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        IOperatorConsoleSessionLookupReadRepository sessionRepository)
    {
        _accessEvaluationService = accessEvaluationService ?? throw new ArgumentNullException(nameof(accessEvaluationService));
        _accessEvaluationWriter = accessEvaluationWriter ?? throw new ArgumentNullException(nameof(accessEvaluationWriter));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleSessionLookupResult> LookupAsync(
        OperatorConsoleSessionLookupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var lookupMode = ValidateAndResolveLookupMode(command);

        var evaluation = await _accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                command.UserId,
                command.OperatorDeviceBindingId,
                command.SiteId,
                command.SiteGroupId,
                command.OperatorShiftId,
                WorkflowCode,
                ControlledActionCode,
                command.ParkingSessionId,
                EvidenceAccessIntent: null,
                command.IdempotencyKey,
                command.CorrelationId),
            cancellationToken);

        var persistedEvaluation = await _accessEvaluationWriter.PersistAsync(evaluation, cancellationToken);

        if (!persistedEvaluation.Allowed)
        {
            return new OperatorConsoleSessionLookupResult(
                persistedEvaluation.EvaluationId,
                AccessAllowed: false,
                persistedEvaluation.Decision,
                persistedEvaluation.DenialReasons,
                persistedEvaluation.Persisted,
                Session: null,
                SessionEligible: false,
                IneligibilityReason: "ACCESS_DENIED",
                Alerts: Array.Empty<string>(),
                persistedEvaluation.CorrelationId);
        }

        var session = await _sessionRepository.FindAsync(
            new OperatorConsoleSessionLookupReadRequest(
                command.ParkingSessionId,
                NormalizeIdentifier(command.TicketReference),
                NormalizePlateNumber(command.PlateNumber),
                command.SiteId,
                command.SiteGroupId,
                lookupMode),
            cancellationToken);

        if (session is null)
        {
            return new OperatorConsoleSessionLookupResult(
                persistedEvaluation.EvaluationId,
                AccessAllowed: true,
                persistedEvaluation.Decision,
                persistedEvaluation.DenialReasons,
                persistedEvaluation.Persisted,
                Session: null,
                SessionEligible: false,
                IneligibilityReason: "SESSION_NOT_FOUND",
                Alerts: Array.Empty<string>(),
                persistedEvaluation.CorrelationId);
        }

        var isTransactionalSession = session.ParkingSessionId.HasValue &&
            string.Equals(session.SessionSource, CoreParkingSessionSource, StringComparison.Ordinal);
        var eligible = isTransactionalSession &&
            string.Equals(session.SessionStatus, "ACTIVE", StringComparison.Ordinal);
        var alerts = new List<string>();
        if (string.Equals(session.SessionSource, VendorSessionProjectionSource, StringComparison.Ordinal))
        {
            alerts.Add("VENDOR_PROJECTION_ONLY");
        }
        else if (!eligible)
        {
            alerts.Add("SESSION_NOT_ELIGIBLE_FOR_OPERATOR_WORKFLOW");
        }

        return new OperatorConsoleSessionLookupResult(
            persistedEvaluation.EvaluationId,
            AccessAllowed: true,
            persistedEvaluation.Decision,
            persistedEvaluation.DenialReasons,
            persistedEvaluation.Persisted,
            session,
            eligible,
            eligible
                ? null
                : isTransactionalSession
                    ? "SESSION_NOT_ACTIVE"
                    : "TRANSACTIONAL_SESSION_NOT_STARTED",
            alerts,
            persistedEvaluation.CorrelationId);
    }

    private static string ValidateAndResolveLookupMode(OperatorConsoleSessionLookupCommand command)
    {
        ValidateGuid(command.UserId, nameof(command.UserId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(command.IdempotencyKey));
        }

        if (!command.ParkingSessionId.HasValue &&
            string.IsNullOrWhiteSpace(command.TicketReference) &&
            string.IsNullOrWhiteSpace(command.PlateNumber))
        {
            throw new ArgumentException("ParkingSessionId, TicketReference, or PlateNumber is required.", nameof(command));
        }

        var normalizedLookupMode = NormalizeIdentifier(command.LookupMode);
        if (string.IsNullOrWhiteSpace(normalizedLookupMode))
        {
            return command.ParkingSessionId.HasValue ? LookupModeParkingSessionId : LookupModeTicketReference;
        }

        if (normalizedLookupMode is not LookupModeParkingSessionId and
            not LookupModeTicketReference and
            not LookupModePlateLicense)
        {
            throw new ArgumentException("LookupMode must be PARKING_SESSION_ID, TICKET_REFERENCE, or PLATE_LICENSE.", nameof(command.LookupMode));
        }

        if (normalizedLookupMode == LookupModeParkingSessionId && !command.ParkingSessionId.HasValue)
        {
            throw new ArgumentException("ParkingSessionId is required when LookupMode is PARKING_SESSION_ID.", nameof(command.ParkingSessionId));
        }

        if (normalizedLookupMode == LookupModeTicketReference && string.IsNullOrWhiteSpace(command.TicketReference))
        {
            throw new ArgumentException("TicketReference is required when LookupMode is TICKET_REFERENCE.", nameof(command.TicketReference));
        }

        if (normalizedLookupMode == LookupModePlateLicense && string.IsNullOrWhiteSpace(command.PlateNumber))
        {
            throw new ArgumentException("PlateNumber is required when LookupMode is PLATE_LICENSE.", nameof(command.PlateNumber));
        }

        return normalizedLookupMode;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static string? NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePlateNumber(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
