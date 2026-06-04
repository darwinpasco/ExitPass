namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated read-only Operator Console parking session lookup service.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Access evaluation is persisted before any session details are returned.
/// - Denied access does not read or return parking session details.
/// - This service never creates or mutates payment, provider, gate, coupon, statutory discount, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleSessionLookupService : IOperatorConsoleSessionLookupService
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string ControlledActionCode = OperatorConsoleActionCodes.SessionLookup;
    private const string LookupModeParkingSessionId = "PARKING_SESSION_ID";
    private const string LookupModeTicketReference = "TICKET_REFERENCE";

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

        var eligible = string.Equals(session.SessionStatus, "ACTIVE", StringComparison.Ordinal);
        var alerts = new List<string>();
        if (!eligible)
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
            eligible ? null : "SESSION_NOT_ACTIVE",
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

        if (!command.ParkingSessionId.HasValue && string.IsNullOrWhiteSpace(command.TicketReference))
        {
            throw new ArgumentException("Either ParkingSessionId or TicketReference is required.", nameof(command));
        }

        var normalizedLookupMode = NormalizeIdentifier(command.LookupMode);
        if (string.IsNullOrWhiteSpace(normalizedLookupMode))
        {
            return command.ParkingSessionId.HasValue ? LookupModeParkingSessionId : LookupModeTicketReference;
        }

        if (normalizedLookupMode is not LookupModeParkingSessionId and not LookupModeTicketReference)
        {
            throw new ArgumentException("LookupMode must be PARKING_SESSION_ID or TICKET_REFERENCE.", nameof(command.LookupMode));
        }

        if (normalizedLookupMode == LookupModeParkingSessionId && !command.ParkingSessionId.HasValue)
        {
            throw new ArgumentException("ParkingSessionId is required when LookupMode is PARKING_SESSION_ID.", nameof(command.ParkingSessionId));
        }

        if (normalizedLookupMode == LookupModeTicketReference && string.IsNullOrWhiteSpace(command.TicketReference))
        {
            throw new ArgumentException("TicketReference is required when LookupMode is TICKET_REFERENCE.", nameof(command.TicketReference));
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
}
