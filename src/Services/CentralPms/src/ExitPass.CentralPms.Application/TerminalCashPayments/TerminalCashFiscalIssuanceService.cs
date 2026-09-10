using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Starts fiscal issuance for confirmed terminal cash payments by reusing the existing Central PMS fiscal path.
/// </summary>
public sealed class TerminalCashFiscalIssuanceService : ITerminalCashFiscalIssuanceService
{
    private const string ConfirmedCanonicalPaymentStatus = "CONFIRMED";
    private const string RecordedPaymentConfirmationStatus = "RECORDED";
    private const string FiscalDocumentTypeCodeKey = "sales_invoice";
    private const string SalesInvoiceHeaderProfileNotFound = "sales_invoice_header_profile_not_found";
    private static readonly HashSet<string> ApprovedRetryableConfigurationErrorCodes = new(StringComparer.Ordinal)
    {
        "fiscal_reporting_period_unavailable",
        "fiscal_reporting_period_assignment_mismatch",
        SalesInvoiceHeaderProfileNotFound
    };
    private static readonly string CanonicalDeliveryRequestHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes("{}"))).ToLowerInvariant();
    private static readonly Guid StatutoryDiscountPrivilegeTypeCodeId =
        Guid.Parse("10000000-0000-0000-0000-000000000501");

    private readonly ITerminalCashPaymentService _terminalCashPayments;
    private readonly IFiscalIssuanceReferenceRepository _fiscalReferences;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _posServerIntegration;
    private readonly ITerminalCashStatutoryFiscalLinkageReader _statutoryFiscalLinkageReader;
    private readonly ISitePosServerBindingResolver _sitePosServerBindingResolver;
    private readonly IIssueExitAuthorizationUseCase _issueExitAuthorizationUseCase;
    private readonly IVendorPaymentAcknowledgmentWorkflow? _vendorPaymentAcknowledgmentWorkflow;
    private readonly FiscalIssuancePosServerIntegrationOptions _posServerOptions;
    private readonly IPosServerFiscalDocumentRequestMapper _requestMapper;
    private readonly IFiscalSemanticRequestHashCalculator _semanticRequestHashCalculator;
    private readonly IFiscalExceptionControlledRetryExecutionAuditRepository _recoveryAuditRepository;
    private readonly ITerminalCashFiscalConflictRecoveryGuardRepository _recoveryGuardRepository;
    private readonly ITerminalCashFiscalConflictRecoveryLock _recoveryLock;
    private readonly ILogger<TerminalCashFiscalIssuanceService> _logger;

    public TerminalCashFiscalIssuanceService(
        ITerminalCashPaymentService terminalCashPayments,
        IFiscalIssuanceReferenceRepository fiscalReferences,
        IFiscalIssuanceOrchestrationService orchestrationService,
        IFiscalIssuancePosServerLiveIntegrationService posServerIntegration,
        ITerminalCashStatutoryFiscalLinkageReader statutoryFiscalLinkageReader,
        ISitePosServerBindingResolver sitePosServerBindingResolver,
        IIssueExitAuthorizationUseCase issueExitAuthorizationUseCase,
        ILogger<TerminalCashFiscalIssuanceService> logger,
        FiscalIssuancePosServerIntegrationOptions posServerOptions,
        IPosServerFiscalDocumentRequestMapper requestMapper,
        IFiscalSemanticRequestHashCalculator semanticRequestHashCalculator,
        IFiscalExceptionControlledRetryExecutionAuditRepository recoveryAuditRepository,
        ITerminalCashFiscalConflictRecoveryGuardRepository recoveryGuardRepository,
        ITerminalCashFiscalConflictRecoveryLock recoveryLock,
        IVendorPaymentAcknowledgmentWorkflow? vendorPaymentAcknowledgmentWorkflow = null)
    {
        _terminalCashPayments = terminalCashPayments;
        _fiscalReferences = fiscalReferences;
        _orchestrationService = orchestrationService;
        _posServerIntegration = posServerIntegration;
        _statutoryFiscalLinkageReader = statutoryFiscalLinkageReader;
        _sitePosServerBindingResolver = sitePosServerBindingResolver;
        _issueExitAuthorizationUseCase = issueExitAuthorizationUseCase;
        _vendorPaymentAcknowledgmentWorkflow = vendorPaymentAcknowledgmentWorkflow;
        _posServerOptions = posServerOptions;
        _requestMapper = requestMapper;
        _semanticRequestHashCalculator = semanticRequestHashCalculator;
        _recoveryAuditRepository = recoveryAuditRepository;
        _recoveryGuardRepository = recoveryGuardRepository;
        _recoveryLock = recoveryLock;
        _logger = logger;
    }

    public async Task<TerminalCashFiscalIssuanceResult> IssueOrReadAsync(
        TerminalCashFiscalIssuanceCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var cashPayment = await ReadConfirmedCashPaymentAsync(command.TerminalCashTenderId, cancellationToken)
            .ConfigureAwait(false);
        var upstreamFinalityReference = BuildUpstreamFinalityReference(cashPayment);

        var existingByConfirmation = await _fiscalReferences.FindByPaymentConfirmationIdAsync(
                cashPayment.PaymentConfirmationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingByConfirmation is not null)
        {
            EnsureExistingReferenceMatchesTerminalCashPayment(existingByConfirmation, cashPayment, upstreamFinalityReference);
            var existingExitAuthorizationIssued = await ContinueAfterVerifiedFiscalEvidenceAsync(
                    cashPayment,
                    existingByConfirmation,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(
                cashPayment,
                existingByConfirmation,
                command.CorrelationId,
                posServerCallAttempted: false,
                exitAuthorizationIssued: existingExitAuthorizationIssued);
        }

        var existingByUpstream = await _fiscalReferences.FindByUpstreamFinalityReferenceAsync(
                upstreamFinalityReference,
                sitePosServerId: null,
                fiscalDocumentTypeCodeId: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingByUpstream is not null)
        {
            EnsureExistingReferenceMatchesTerminalCashPayment(existingByUpstream, cashPayment, upstreamFinalityReference);
            var existingExitAuthorizationIssued = await ContinueAfterVerifiedFiscalEvidenceAsync(
                    cashPayment,
                    existingByUpstream,
                    command.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(
                cashPayment,
                existingByUpstream,
                command.CorrelationId,
                posServerCallAttempted: false,
                exitAuthorizationIssued: existingExitAuthorizationIssued);
        }

        var statutoryFiscalLinkage = await _statutoryFiscalLinkageReader
            .ReadByAppliedTariffSnapshotAsync(cashPayment, cancellationToken)
            .ConfigureAwait(false);
        EnsureStatutoryFiscalLinkageCanBeFiscalized(statutoryFiscalLinkage);
        var posServerEndpoint = ResolvePosServerEndpoint(cashPayment);

        var prepared = await _orchestrationService.PreparePendingAsync(
                BuildPrepareCommand(cashPayment, posServerEndpoint, upstreamFinalityReference, command.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);

        var fiscalContext = BuildFiscalContext(cashPayment, prepared, statutoryFiscalLinkage.Context);
        var recordingContext = new PosServerCreateResultRecordingContext(
            UpstreamFinalityReference: upstreamFinalityReference,
            SitePosServerId: prepared.SitePosServerId,
            FiscalDocumentTypeCodeId: prepared.FiscalDocumentTypeCodeId,
            CorrelationId: command.CorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow,
            ServiceIdentityId: null);

        var issueResult = await _posServerIntegration.TryIssueFiscalDocumentViaPosServerAsync(
                prepared.FiscalIssuanceReferenceId,
                fiscalContext,
                recordingContext,
                cancellationToken)
            .ConfigureAwait(false);

        var issuedReference = issueResult.FiscalIssuanceReference ?? prepared;
        var exitAuthorizationIssued = await ContinueAfterVerifiedFiscalEvidenceAsync(
                cashPayment,
                issuedReference,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return ToResult(
            cashPayment,
            issuedReference,
            command.CorrelationId,
            posServerCallAttempted: issueResult.MappedRequest is not null && issueResult.PosServerResult is not null,
            safeErrorCode: issueResult.PosServerResult?.Succeeded == false
                ? issueResult.PosServerResult.Code
                : issueResult.Status is FiscalIssuancePosServerLiveIntegrationStatus.Applied ? null : issueResult.Code,
            safeErrorPosture: issueResult.PosServerResult?.ErrorPosture?.ToString(),
            exitAuthorizationIssued: exitAuthorizationIssued);
    }

    public async Task<TerminalCashFiscalConflictRecoveryResult> RecoverConfigurationFailureAsync(
        TerminalCashFiscalConflictRecoveryCommand command,
        CancellationToken cancellationToken)
    {
        ValidateRecovery(command);

        await using var recoveryLease = await _recoveryLock
            .TryAcquireAsync(command.FiscalIssuanceReferenceId, cancellationToken)
            .ConfigureAwait(false);
        if (recoveryLease is null)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_IN_PROGRESS",
                "A governed recovery is already in progress for this fiscal obligation.");
        }

        var cashPayment = await ReadConfirmedCashPaymentAsync(command.TerminalCashTenderId, cancellationToken)
            .ConfigureAwait(false);
        var reference = await _fiscalReferences.FindByFiscalIssuanceReferenceIdAsync(
                command.FiscalIssuanceReferenceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (reference is null)
        {
            throw new TerminalCashFiscalIssuanceRejectedException(
                "TERMINAL_CASH_FISCAL_ISSUANCE_NOT_FOUND",
                "The governed fiscal obligation was not found.",
                isNotFound: true);
        }

        var upstreamFinalityReference = BuildUpstreamFinalityReference(cashPayment);
        EnsureExistingReferenceMatchesTerminalCashPayment(reference, cashPayment, upstreamFinalityReference);
        EnsureRecoveryExpectedFacts(command, cashPayment, reference, upstreamFinalityReference);

        if (FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(reference))
        {
            var existingExitAuthorizationIssued = await ContinueAfterVerifiedFiscalEvidenceAsync(
                    cashPayment,
                    reference,
                    command.RecoveryCorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
            var replayAuditId = await RecordRecoveryAuditAsync(
                    command,
                    reference,
                    FiscalExceptionControlledRetryExecutionStatus.ReplayMatched,
                    "terminal_cash_fiscal_recovery_already_completed",
                    posServerResult: null,
                    cancellationToken)
                .ConfigureAwait(false);
            return new TerminalCashFiscalConflictRecoveryResult(
                replayAuditId,
                "ALREADY_COMPLETED",
                RecoveryExecuted: false,
                IdempotentReadback: true,
                ToResult(
                    cashPayment,
                    reference,
                    command.RecoveryCorrelationId,
                    posServerCallAttempted: false,
                    exitAuthorizationIssued: existingExitAuthorizationIssued));
        }

        EnsureApprovedRecoveryState(command, reference);
        EnsureReferenceHasNoFiscalEvidence(reference);

        var durableFacts = await _recoveryGuardRepository.ReadAsync(
                command.ExpectedPaymentAttemptId,
                command.ExpectedPaymentConfirmationId,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureDurableRecoveryFacts(command, cashPayment, durableFacts);

        var statutoryFiscalLinkage = await _statutoryFiscalLinkageReader
            .ReadByAppliedTariffSnapshotAsync(cashPayment, cancellationToken)
            .ConfigureAwait(false);
        EnsureStatutoryFiscalLinkageCanBeFiscalized(statutoryFiscalLinkage);

        var resolvedEndpoint = ResolvePosServerEndpoint(cashPayment);
        if (reference.SitePosServerId != resolvedEndpoint.SitePosServerId ||
            !string.Equals(
                reference.SitePosServerRef,
                resolvedEndpoint.SitePosServerRef?.Trim(),
                StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_POS_BINDING_MISMATCH",
                "The current Site POS Server binding differs from the original fiscal obligation.");
        }

        if (!_posServerOptions.EvaluateReadiness().IsReady)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_POS_NOT_READY",
                "POS Server fiscal integration readiness is not confirmed.");
        }

        var fiscalContext = BuildFiscalContext(cashPayment, reference, statutoryFiscalLinkage.Context);
        PosServerFiscalDocumentCreateRequest mappedRequest;
        try
        {
            mappedRequest = _requestMapper.Map(
                FiscalIssuancePosServerLiveIntegrationService.ApplyConfiguredFiscalProfile(
                    _posServerOptions,
                    fiscalContext));
        }
        catch (ArgumentException)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_REQUEST_FACTS_INVALID",
                "The unchanged fiscal request cannot be reconstructed from durable facts.");
        }

        var recalculatedHash = _semanticRequestHashCalculator.Calculate(mappedRequest);
        EnsureSemanticRequestUnchanged(command, reference, mappedRequest, recalculatedHash);

        await RecordRecoveryAuditAsync(
                command,
                reference,
                FiscalExceptionControlledRetryExecutionStatus.DryRunReady,
                "terminal_cash_configuration_failure_recovery_authorized",
                posServerResult: null,
                cancellationToken)
            .ConfigureAwait(false);

        var recordingContext = new PosServerCreateResultRecordingContext(
            UpstreamFinalityReference: reference.UpstreamFinalityReference,
            SitePosServerId: reference.SitePosServerId,
            FiscalDocumentTypeCodeId: reference.FiscalDocumentTypeCodeId,
            CorrelationId: command.RecoveryCorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow,
            ServiceIdentityId: command.ActorServiceIdentityId);
        var liveResult = await _posServerIntegration.TryIssueFiscalDocumentViaPosServerAsync(
                reference.FiscalIssuanceReferenceId,
                fiscalContext,
                recordingContext,
                cancellationToken)
            .ConfigureAwait(false);
        var recoveredReference = liveResult.FiscalIssuanceReference ?? reference;
        var executionStatus = liveResult.PosServerResult switch
        {
            { Succeeded: true, ResultClassification: FiscalIssuanceResultClassification.IdempotentReplay } =>
                FiscalExceptionControlledRetryExecutionStatus.ReplayMatched,
            { Succeeded: true } => FiscalExceptionControlledRetryExecutionStatus.Executed,
            { Outcome: PosServerFiscalDocumentOutcome.Conflict } =>
                FiscalExceptionControlledRetryExecutionStatus.Conflict,
            null => FiscalExceptionControlledRetryExecutionStatus.Failed,
            _ => FiscalExceptionControlledRetryExecutionStatus.Failed
        };
        var finalAuditId = await RecordRecoveryAuditAsync(
                command,
                recoveredReference,
                executionStatus,
                liveResult.PosServerResult?.Code ?? liveResult.Code,
                liveResult.PosServerResult,
                cancellationToken)
            .ConfigureAwait(false);

        var exitAuthorizationIssued = await ContinueAfterVerifiedFiscalEvidenceAsync(
                cashPayment,
                recoveredReference,
                command.RecoveryCorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return new TerminalCashFiscalConflictRecoveryResult(
            finalAuditId,
            executionStatus.ToString().ToUpperInvariant(),
            RecoveryExecuted: liveResult.MappedRequest is not null && liveResult.PosServerResult is not null,
            IdempotentReadback: executionStatus == FiscalExceptionControlledRetryExecutionStatus.ReplayMatched,
            ToResult(
                cashPayment,
                recoveredReference,
                command.RecoveryCorrelationId,
                posServerCallAttempted: liveResult.MappedRequest is not null && liveResult.PosServerResult is not null,
                safeErrorCode: liveResult.PosServerResult?.Succeeded == false
                    ? liveResult.PosServerResult.Code
                    : null,
                safeErrorPosture: liveResult.PosServerResult?.ErrorPosture?.ToString(),
                exitAuthorizationIssued: exitAuthorizationIssued));
    }

    private async Task<bool> ContinueAfterVerifiedFiscalEvidenceAsync(
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var gate = FiscalIssuanceExitAuthorizationGateEvaluator.Evaluate(
            reference,
            new FiscalIssuanceGatingEvaluationContext(IsPaymentFinalityVerified: true));
        if (!gate.IsReadyForNormalExitAuthorization)
        {
            return false;
        }

        if (!Guid.TryParse(cashPayment.CashierId, out var cashierUserId) || cashierUserId == Guid.Empty)
        {
            throw Rejected(
                "TERMINAL_CASH_CASHIER_ID_INVALID",
                "Terminal cash payment does not contain a canonical cashier identity.");
        }

        await _issueExitAuthorizationUseCase.ExecuteAsync(
                new IssueExitAuthorizationCommand(
                    cashPayment.ParkingSessionId,
                    cashPayment.PaymentAttemptId,
                    cashierUserId,
                    correlationId),
                cancellationToken)
            .ConfigureAwait(false);

        if (_vendorPaymentAcknowledgmentWorkflow is not null)
        {
            try
            {
                await _vendorPaymentAcknowledgmentWorkflow.ProcessAsync(
                        new VendorPaymentAcknowledgmentWorkflowCommand(
                            cashPayment.PaymentAttemptId,
                            cashPayment.PaymentConfirmationId,
                            cashPayment.ParkingSessionId,
                            correlationId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Vendor PMS payment acknowledgment failed after terminal cash fiscal evidence and will not roll back payment or fiscal finality. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} correlation_id={CorrelationId}",
                    cashPayment.PaymentAttemptId,
                    cashPayment.PaymentConfirmationId,
                    correlationId);
            }
        }

        return true;
    }

    public async Task<TerminalCashFiscalIssuanceResult?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (terminalCashTenderId == Guid.Empty)
        {
            throw Rejected("TERMINAL_CASH_TENDER_ID_REQUIRED", "Terminal cash tender reference is required.");
        }

        var cashPayment = await _terminalCashPayments.GetByTerminalCashTenderIdAsync(
                terminalCashTenderId,
                cancellationToken)
            .ConfigureAwait(false);
        if (cashPayment is null)
        {
            return null;
        }

        var reference = await _fiscalReferences.FindByPaymentConfirmationIdAsync(
                cashPayment.PaymentConfirmationId,
                cancellationToken)
            .ConfigureAwait(false);

        return reference is null
            ? null
            : ToResult(cashPayment, reference, correlationId, posServerCallAttempted: false);
    }

    private async Task<TerminalCashPaymentReadback> ReadConfirmedCashPaymentAsync(
        Guid terminalCashTenderId,
        CancellationToken cancellationToken)
    {
        var cashPayment = await _terminalCashPayments.GetByTerminalCashTenderIdAsync(
                terminalCashTenderId,
                cancellationToken)
            .ConfigureAwait(false);
        if (cashPayment is null)
        {
            throw new TerminalCashFiscalIssuanceRejectedException(
                "TERMINAL_CASH_PAYMENT_NOT_FOUND",
                "Terminal cash payment was not found.",
                isNotFound: true);
        }

        if (!string.Equals(
                cashPayment.CanonicalPaymentStatus,
                ConfirmedCanonicalPaymentStatus,
                StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_NOT_CONFIRMED",
                "Terminal cash payment is not canonically confirmed.");
        }

        if (cashPayment.PaymentAttemptId == Guid.Empty || cashPayment.PaymentConfirmationId == Guid.Empty)
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_CONFIRMATION_MISSING",
                "Terminal cash payment confirmation is missing.");
        }

        if (string.IsNullOrWhiteSpace(cashPayment.PosServerId))
        {
            throw Rejected(
                "SITE_POS_SERVER_BINDING_MISSING",
                "Terminal cash payment does not have a POS Server reference.");
        }

        return cashPayment;
    }

    private static PrepareFiscalIssuanceCommand BuildPrepareCommand(
        TerminalCashPaymentReadback cashPayment,
        SitePosServerEndpointOptions posServerEndpoint,
        string upstreamFinalityReference,
        Guid correlationId) =>
        new(
            PaymentConfirmationId: cashPayment.PaymentConfirmationId,
            PaymentAttemptId: cashPayment.PaymentAttemptId,
            ParkingSessionId: cashPayment.ParkingSessionId,
            TariffSnapshotId: cashPayment.TariffSnapshotId,
            SiteId: cashPayment.SiteId,
            SitePosServerId: posServerEndpoint.SitePosServerId,
            SitePosServerRef: posServerEndpoint.SitePosServerRef!.Trim(),
            FiscalDocumentTypeCodeId: null,
            FiscalDocumentTypeCodeKey: FiscalDocumentTypeCodeKey,
            PayableBasisRef: cashPayment.TariffSnapshotId.ToString("D"),
            UpstreamFinalityReference: upstreamFinalityReference,
            CorrelationId: correlationId,
            ServiceIdentityId: null);

    private SitePosServerEndpointOptions ResolvePosServerEndpoint(TerminalCashPaymentReadback cashPayment)
    {
        if (!Guid.TryParse(cashPayment.PosServerId.Trim(), out var persistedPosServerId) ||
            persistedPosServerId == Guid.Empty)
        {
            throw Rejected(
                "SITE_POS_SERVER_ID_INVALID",
                "The terminal cash payment does not contain a canonical POS Server identity.");
        }

        var resolution = _sitePosServerBindingResolver.Resolve(
            new SitePosServerBindingRequest(cashPayment.SiteId, persistedPosServerId));
        if (!resolution.IsSuccess || resolution.Endpoint is null)
        {
            throw Rejected(
                MapBindingErrorCode(resolution.Code),
                "The terminal cash payment POS Server binding is not valid for its Site and canonical identity.");
        }

        return resolution.Endpoint;
    }

    private static string MapBindingErrorCode(string code) => code switch
    {
        SitePosServerBindingResolutionCodes.Missing => "SITE_POS_SERVER_BINDING_MISSING",
        SitePosServerBindingResolutionCodes.Ambiguous => "SITE_POS_SERVER_BINDING_AMBIGUOUS",
        SitePosServerBindingResolutionCodes.Inactive => "SITE_POS_SERVER_BINDING_INACTIVE",
        SitePosServerBindingResolutionCodes.SiteMismatch => "SITE_POS_SERVER_SITE_MISMATCH",
        SitePosServerBindingResolutionCodes.IdentityMissing => "SITE_POS_SERVER_IDENTITY_MISSING",
        SitePosServerBindingResolutionCodes.IdentityMismatch => "SITE_POS_SERVER_IDENTITY_MISMATCH",
        SitePosServerBindingResolutionCodes.ReferenceMissing => "SITE_POS_SERVER_ENDPOINT_REFERENCE_MISSING",
        SitePosServerBindingResolutionCodes.ReferenceMismatch => "SITE_POS_SERVER_ENDPOINT_REFERENCE_MISMATCH",
        _ => "SITE_POS_SERVER_BINDING_INCONSISTENT"
    };

    private static CentralPmsFiscalDocumentMappingContext BuildFiscalContext(
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference,
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext)
    {
        var currency = cashPayment.Currency.Trim().ToUpperInvariant();
        var amount = statutoryContext?.FinalPayableAmountMinorUnits ?? cashPayment.AmountDueMinorUnits;
        var lineGrossAmount = statutoryContext?.VatExclusiveBasisAmountMinorUnits ?? amount;
        var discountAmount = statutoryContext?.StatutoryDiscountAmountMinorUnits ?? 0;
        var taxAmount = 0;
        var paymentAttemptRef = cashPayment.PaymentAttemptId.ToString("D");
        var paymentConfirmationRef = cashPayment.PaymentConfirmationId.ToString("D");
        var payableBasisContext = BuildPayableBasisReferenceContext(cashPayment, statutoryContext);
        var documentLineContext = BuildDocumentLineContext(cashPayment, statutoryContext);
        var tenderContext = BuildTenderContext(cashPayment);
        var totalContext = statutoryContext is null
            ? new Dictionary<string, string> { ["kind"] = "grand_total" }
            : new Dictionary<string, string> { ["kind"] = "final_statutory_payable" };
        var referenceContext = BuildFiscalReferenceContext(cashPayment, reference, statutoryContext);

        return new CentralPmsFiscalDocumentMappingContext(
            SitePosServerId: reference.SitePosServerId,
            SitePosServerRef: reference.SitePosServerRef,
            FiscalDocumentTypeCodeId: reference.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: reference.FiscalDocumentTypeCodeKey,
            FiscalDocumentStatusCodeId: null,
            BusinessDayDate: DateOnly.FromDateTime(cashPayment.ConfirmedAt.UtcDateTime),
            CentralPmsParkingSessionRef: cashPayment.ParkingSessionId.ToString("D"),
            CentralPmsPaymentAttemptRef: paymentAttemptRef,
            CentralPmsPaymentConfirmationRef: paymentConfirmationRef,
            PayableBasis: new CentralPmsPayableBasisContext(
                PayableBasisRef: cashPayment.TariffSnapshotId.ToString("D"),
                UpstreamFinalityRef: reference.UpstreamFinalityReference,
                CurrencyCode: currency,
                PayableAmountMinorUnits: amount,
                DiscountReferences: BuildDiscountReferences(statutoryContext),
                ReferenceContext: payableBasisContext),
            DocumentLines:
            [
                new CentralPmsFiscalDocumentLineContext(
                    LineSequence: 1,
                    LineTypeCodeId: null,
                    Description: statutoryContext is null
                        ? "Parking fee - cash"
                        : "Parking fee - statutory discount applied",
                    Quantity: 1m,
                    UnitAmountMinorUnits: lineGrossAmount,
                    GrossAmountMinorUnits: lineGrossAmount,
                    DiscountAmountMinorUnits: discountAmount,
                    TaxAmountMinorUnits: taxAmount,
                    NetAmountMinorUnits: amount,
                    CurrencyCode: currency,
                    LineStatusCodeId: null,
                    SourceRef: cashPayment.TariffSnapshotId.ToString("D"),
                    LineContext: documentLineContext)
            ],
            Tenders:
            [
                new CentralPmsFiscalTenderContext(
                    TenderTypeCodeId: null,
                    AmountMinorUnits: amount,
                    CurrencyCode: currency,
                    CentralPmsPaymentAttemptRef: paymentAttemptRef,
                    CentralPmsPaymentConfirmationRef: paymentConfirmationRef,
                    PaymentFinalityRef: reference.UpstreamFinalityReference,
                    ProviderRef: "CASH",
                    TenderContext: tenderContext)
            ],
            TaxDetails: Array.Empty<CentralPmsFiscalTaxDetailContext>(),
            DiscountPrivilegeDetails: BuildDiscountPrivilegeDetails(statutoryContext, currency),
            Totals:
            [
                new CentralPmsFiscalTotalContext(
                    TotalTypeCodeId: null,
                    AmountMinorUnits: amount,
                    CurrencyCode: currency,
                    TotalContext: totalContext)
            ],
            ReferenceContext: referenceContext,
            PaymentFinalityRef: reference.UpstreamFinalityReference,
            VendorAckRef: null,
            AppliedStatutoryFiscalFacts: BuildAppliedStatutoryFiscalFacts(cashPayment, statutoryContext),
            SiteId: cashPayment.SiteId);
    }

    private static void EnsureStatutoryFiscalLinkageCanBeFiscalized(
        TerminalCashStatutoryFiscalLinkageResult linkage)
    {
        if (linkage.Status is TerminalCashStatutoryFiscalLinkageStatus.NotApplicable
            or TerminalCashStatutoryFiscalLinkageStatus.CompleteApprovedContext)
        {
            return;
        }

        throw Rejected(
            linkage.SafeErrorCode ?? "STATUTORY_FISCAL_LINKAGE_NOT_READY",
            linkage.Status == TerminalCashStatutoryFiscalLinkageStatus.RetryableUnavailable
                ? "Statutory discount fiscal context is temporarily unavailable."
                : "Statutory discount fiscal context is incomplete or inconsistent.");
    }

    private static IReadOnlyList<CentralPmsFiscalDiscountReferenceContext> BuildDiscountReferences(
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext)
    {
        if (statutoryContext is null)
        {
            return Array.Empty<CentralPmsFiscalDiscountReferenceContext>();
        }

        return
        [
            new CentralPmsFiscalDiscountReferenceContext(
                DiscountValidationRef: statutoryContext.StatutoryDiscountValidationId.ToString("D"),
                Status: "approved",
                AppliesStatutoryDiscountTreatment: true,
                ReferenceContext: new Dictionary<string, string>
                {
                    ["statutoryDiscountPayableBasisApplicationCommandId"] =
                        statutoryContext.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D"),
                    ["entitlementType"] = statutoryContext.EntitlementType,
                    ["source"] = "central-pms-canonical-statutory-discount"
                })
            {
                StatutoryDiscountDecisionCommandRef = statutoryContext.StatutoryDiscountDecisionCommandId.ToString("D"),
                EntitlementType = statutoryContext.EntitlementType,
                AppliedPolicyReferenceRef = statutoryContext.AppliedPolicyReferenceId?.ToString("D"),
                OriginalTariffSnapshotRef = statutoryContext.OriginalTariffSnapshotId.ToString("D"),
                AppliedTariffSnapshotRef = statutoryContext.AppliedTariffSnapshotId.ToString("D"),
                OriginalAmountMinorUnits = statutoryContext.OriginalAmountMinorUnits,
                VatExclusiveBasisAmountMinorUnits = statutoryContext.VatExclusiveBasisAmountMinorUnits,
                VatTreatment = statutoryContext.VatTreatment,
                DiscountAmountMinorUnits = statutoryContext.StatutoryDiscountAmountMinorUnits,
                FinalPayableAmountMinorUnits = statutoryContext.FinalPayableAmountMinorUnits,
                DecisionTimestamp = statutoryContext.DecisionTimestamp,
                SourceChannel = statutoryContext.SourceChannel
            }
        ];
    }

    private static IReadOnlyList<CentralPmsFiscalDiscountPrivilegeDetailContext> BuildDiscountPrivilegeDetails(
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext,
        string currency)
    {
        if (statutoryContext is null)
        {
            return Array.Empty<CentralPmsFiscalDiscountPrivilegeDetailContext>();
        }

        var context = new Dictionary<string, string>
        {
            ["entitlementType"] = statutoryContext.EntitlementType,
            ["discountBaseScope"] = statutoryContext.VatTreatment,
            ["statutoryDiscountDecisionCommandId"] = statutoryContext.StatutoryDiscountDecisionCommandId.ToString("D"),
            ["statutoryDiscountPayableBasisApplicationCommandId"] =
                statutoryContext.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D")
        };
        AddIfPresent(context, "policyResolutionBasis", statutoryContext.PolicyResolutionBasis);
        AddIfPresent(context, "statutoryDiscountPayableBasisApplicationId",
            statutoryContext.StatutoryDiscountPayableBasisApplicationId?.ToString("D"));

        return
        [
            new CentralPmsFiscalDiscountPrivilegeDetailContext(
                DiscountPrivilegeTypeCodeId: StatutoryDiscountPrivilegeTypeCodeId,
                BasisAmountMinorUnits: statutoryContext.VatExclusiveBasisAmountMinorUnits,
                DiscountAmountMinorUnits: statutoryContext.StatutoryDiscountAmountMinorUnits,
                VatPrivilegeAmountMinorUnits: statutoryContext.VatAmountMinorUnits,
                CurrencyCode: currency,
                LineSequence: 1,
                BeneficiaryRef: null,
                EvidenceRef: null,
                ApprovalRef: statutoryContext.StatutoryDiscountValidationId.ToString("D"),
                DiscountPrivilegeContext: context)
        ];
    }

    private static CentralPmsAppliedStatutoryFiscalFactsContext? BuildAppliedStatutoryFiscalFacts(
        TerminalCashPaymentReadback cashPayment,
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext)
    {
        if (statutoryContext is null)
        {
            return null;
        }

        if (cashPayment.SiteId == Guid.Empty ||
            cashPayment.SiteGroupId == Guid.Empty)
        {
            throw Rejected(
                "STATUTORY_FISCAL_SITE_SCOPE_MISSING",
                "Statutory discount fiscal context requires canonical Site and Site Group scope.");
        }

        return new CentralPmsAppliedStatutoryFiscalFactsContext(
            StatutoryDiscountDecisionCommandId: statutoryContext.StatutoryDiscountDecisionCommandId,
            StatutoryRequestReference: statutoryContext.StatutoryDiscountValidationId,
            StatutoryPayableBasisApplicationCommandId: statutoryContext.StatutoryDiscountPayableBasisApplicationCommandId,
            StatutoryValidationId: statutoryContext.StatutoryDiscountValidationId,
            ParkingSessionId: statutoryContext.ParkingSessionId,
            SiteId: cashPayment.SiteId,
            SiteGroupId: cashPayment.SiteGroupId,
            EntitlementType: statutoryContext.EntitlementType,
            BenefitClassification: ResolveBenefitClassification(statutoryContext),
            PolicyReference: new CentralPmsAppliedStatutoryPolicyReferenceContext(
                ResolutionBasis: NormalizePolicyResolutionBasis(statutoryContext.PolicyResolutionBasis),
                AppliedPolicyReferenceId: statutoryContext.AppliedPolicyReferenceId),
            OriginalTariffSnapshotId: statutoryContext.OriginalTariffSnapshotId,
            AppliedTariffSnapshotId: statutoryContext.AppliedTariffSnapshotId,
            OriginalAmountMinorUnits: statutoryContext.OriginalAmountMinorUnits,
            VatExclusiveBasisAmountMinorUnits: statutoryContext.VatExclusiveBasisAmountMinorUnits,
            VatAmountMinorUnits: statutoryContext.VatAmountMinorUnits,
            VatTreatment: statutoryContext.VatTreatment,
            StatutoryDiscountAmountMinorUnits: statutoryContext.StatutoryDiscountAmountMinorUnits,
            FinalPayableAmountMinorUnits: statutoryContext.FinalPayableAmountMinorUnits,
            Currency: statutoryContext.Currency,
            AppliedAt: statutoryContext.AppliedAt ?? DateTimeOffset.UtcNow,
            SourcePaymentChannel: "ASSISTED_PAYMENT_TERMINAL",
            TerminalCashTenderId: cashPayment.TerminalCashTenderId);
    }

    private static string ResolveBenefitClassification(TerminalCashStatutoryFiscalLinkageContext statutoryContext)
    {
        if (statutoryContext.FinalPayableAmountMinorUnits == 0)
        {
            return "FREE_PARKING";
        }

        if (statutoryContext.VatAmountMinorUnits > 0 && statutoryContext.StatutoryDiscountAmountMinorUnits > 0)
        {
            return "VAT_EXEMPTION_AND_STATUTORY_DISCOUNT";
        }

        if (statutoryContext.VatAmountMinorUnits > 0)
        {
            return "VAT_EXEMPTION_ONLY";
        }

        return statutoryContext.StatutoryDiscountAmountMinorUnits > 0
            ? "STATUTORY_DISCOUNT_ONLY"
            : "REDUCED_PARKING_RATE";
    }

    private static string NormalizePolicyResolutionBasis(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "INTERNAL_POLICY_REFERENCE";
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "NATIONAL_LAW" or "LOCAL_ORDINANCE" or "MIXED" or "INTERNAL_POLICY_REFERENCE" => normalized,
            "NATIONAL_LAW_FALLBACK" => "NATIONAL_LAW",
            _ => "INTERNAL_POLICY_REFERENCE"
        };
    }

    private static Dictionary<string, string> BuildPayableBasisReferenceContext(
        TerminalCashPaymentReadback cashPayment,
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext)
    {
        var context = new Dictionary<string, string>
        {
            ["tariffSnapshotId"] = cashPayment.TariffSnapshotId.ToString("D"),
            ["paymentMethod"] = "CASH"
        };

        if (statutoryContext is null)
        {
            return context;
        }

        context["appliedTariffSnapshotId"] = statutoryContext.AppliedTariffSnapshotId.ToString("D");
        context["originalTariffSnapshotId"] = statutoryContext.OriginalTariffSnapshotId.ToString("D");
        context["statutoryDiscountValidationId"] = statutoryContext.StatutoryDiscountValidationId.ToString("D");
        context["statutoryDiscountDecisionCommandId"] = statutoryContext.StatutoryDiscountDecisionCommandId.ToString("D");
        context["statutoryDiscountPayableBasisApplicationCommandId"] =
            statutoryContext.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D");
        context["entitlementType"] = statutoryContext.EntitlementType;
        return context;
    }

    private static Dictionary<string, string> BuildDocumentLineContext(
        TerminalCashPaymentReadback cashPayment,
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext)
    {
        var context = new Dictionary<string, string>
        {
            ["source"] = statutoryContext is null ? "terminal-cash-payment" : "central-pms-applied-statutory-payable-basis",
            ["terminalCashTenderId"] = cashPayment.TerminalCashTenderId.ToString("D")
        };

        if (statutoryContext is null)
        {
            return context;
        }

        context["entitlementType"] = statutoryContext.EntitlementType;
        context["originalAmountMinorUnits"] = statutoryContext.OriginalAmountMinorUnits.ToString();
        context["vatExclusiveBasisAmountMinorUnits"] = statutoryContext.VatExclusiveBasisAmountMinorUnits.ToString();
        context["vatAmountMinorUnits"] = statutoryContext.VatAmountMinorUnits.ToString();
        context["statutoryDiscountAmountMinorUnits"] = statutoryContext.StatutoryDiscountAmountMinorUnits.ToString();
        context["finalPayableAmountMinorUnits"] = statutoryContext.FinalPayableAmountMinorUnits.ToString();
        return context;
    }

    private static Dictionary<string, string> BuildTenderContext(TerminalCashPaymentReadback cashPayment) =>
        new()
        {
            ["paymentMethod"] = "CASH",
            ["terminalCashTenderId"] = cashPayment.TerminalCashTenderId.ToString("D")
        };

    private static Dictionary<string, string> BuildFiscalReferenceContext(
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference,
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext)
    {
        var context = new Dictionary<string, string>
        {
            ["terminalCashTenderId"] = cashPayment.TerminalCashTenderId.ToString("D"),
            ["cashCustodySessionId"] = cashPayment.CashCustodySessionId.ToString("D"),
            ["fiscalIssuanceReferenceId"] = reference.FiscalIssuanceReferenceId.ToString("D")
        };

        if (statutoryContext is null)
        {
            return context;
        }

        context["statutoryDiscountValidationId"] = statutoryContext.StatutoryDiscountValidationId.ToString("D");
        context["statutoryDiscountDecisionCommandId"] = statutoryContext.StatutoryDiscountDecisionCommandId.ToString("D");
        context["statutoryDiscountPayableBasisApplicationCommandId"] =
            statutoryContext.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D");
        AddIfPresent(context, "statutoryDiscountPayableBasisApplicationId",
            statutoryContext.StatutoryDiscountPayableBasisApplicationId?.ToString("D"));
        context["appliedTariffSnapshotId"] = statutoryContext.AppliedTariffSnapshotId.ToString("D");
        context["originalTariffSnapshotId"] = statutoryContext.OriginalTariffSnapshotId.ToString("D");
        return context;
    }

    private static void AddIfPresent(Dictionary<string, string> context, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            context[key] = value.Trim();
        }
    }

    private static void EnsureExistingReferenceMatchesTerminalCashPayment(
        FiscalIssuanceReferenceRecord reference,
        TerminalCashPaymentReadback cashPayment,
        string upstreamFinalityReference)
    {
        if (reference.PaymentConfirmationId != cashPayment.PaymentConfirmationId ||
            reference.PaymentAttemptId != cashPayment.PaymentAttemptId ||
            reference.ParkingSessionId != cashPayment.ParkingSessionId ||
            !string.Equals(reference.UpstreamFinalityReference, upstreamFinalityReference, StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_SEMANTIC_CONFLICT",
                "Terminal cash payment is already linked to a conflicting fiscal issuance request.");
        }
    }

    private static TerminalCashFiscalIssuanceResult ToResult(
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference,
        Guid? correlationId,
        bool posServerCallAttempted,
        string? safeErrorCode = null,
        string? safeErrorPosture = null,
        bool exitAuthorizationIssued = false) =>
        new(
            TerminalCashTenderId: cashPayment.TerminalCashTenderId,
            PaymentAttemptId: reference.PaymentAttemptId!.Value,
            PaymentConfirmationId: reference.PaymentConfirmationId!.Value,
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            FiscalIssuanceState: reference.FiscalIssuanceState,
            ResultClassification: reference.ResultClassification,
            PosFiscalDocumentId: reference.PosServerFiscalDocumentId,
            FiscalDocumentNumber: reference.FiscalDocumentNumber,
            FiscalNumberAssignedAt: reference.FiscalNumberAssignedAt,
            SemanticHashSourceVersion: reference.SemanticRequestHashSourceVersion,
            CreatedAt: reference.FirstRecordedAt,
            UpdatedAt: reference.LastUpdatedAt,
            CorrelationId: correlationId ?? reference.CorrelationId,
            SafeErrorCode: safeErrorCode ?? reference.LatestErrorCode,
            SafeErrorPosture: safeErrorPosture ?? reference.LatestErrorPosture?.ToString(),
            PosServerCallAttempted: posServerCallAttempted,
            ExitAuthorizationIssued: exitAuthorizationIssued,
            GateBehaviorTriggered: false);

    private static void EnsureRecoveryExpectedFacts(
        TerminalCashFiscalConflictRecoveryCommand command,
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference,
        string upstreamFinalityReference)
    {
        var currency = command.ExpectedCurrency.Trim().ToUpperInvariant();
        if (cashPayment.PaymentAttemptId != command.ExpectedPaymentAttemptId ||
            cashPayment.PaymentConfirmationId != command.ExpectedPaymentConfirmationId ||
            cashPayment.ParkingSessionId != command.ExpectedParkingSessionId ||
            cashPayment.TariffSnapshotId != command.ExpectedTariffSnapshotId ||
            cashPayment.AmountDueMinorUnits != command.ExpectedAmountMinorUnits ||
            !string.Equals(cashPayment.Currency.Trim(), currency, StringComparison.OrdinalIgnoreCase) ||
            cashPayment.CorrelationId != command.ExpectedTransactionCorrelationId ||
            reference.PaymentAttemptId != command.ExpectedPaymentAttemptId ||
            reference.PaymentConfirmationId != command.ExpectedPaymentConfirmationId ||
            reference.ParkingSessionId != command.ExpectedParkingSessionId ||
            reference.TariffSnapshotId != command.ExpectedTariffSnapshotId ||
            reference.SiteId != cashPayment.SiteId ||
            !string.Equals(reference.PayableBasisRef, command.ExpectedTariffSnapshotId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(reference.FiscalDocumentTypeCodeKey, FiscalDocumentTypeCodeKey, StringComparison.Ordinal) ||
            !string.Equals(reference.CompletionBasis, FiscalCompletionBasisCodes.PaymentFinality, StringComparison.Ordinal) ||
            reference.CompletionAuthorityReferenceId != command.ExpectedPaymentConfirmationId ||
            !string.Equals(reference.UpstreamFinalityReference, upstreamFinalityReference, StringComparison.Ordinal) ||
            !string.Equals(reference.UpstreamFinalityReference, command.ExpectedUpstreamFinalityReference, StringComparison.Ordinal) ||
            reference.CorrelationId != command.ExpectedFiscalCorrelationId ||
            command.RecoveryCorrelationId != command.ExpectedFiscalCorrelationId)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_ANCESTRY_MISMATCH",
                "The recovery request does not match the unchanged durable payment and fiscal ancestry.");
        }

        var canonicalDeliveryKey = $"terminal-cash-fiscal-{command.TerminalCashTenderId:N}";
        if (!string.Equals(command.DeliveryIdempotencyKey, canonicalDeliveryKey, StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_IDEMPOTENCY_KEY_MISMATCH",
                "The recovery request does not preserve the terminal-cash fiscal delivery idempotency key.");
        }
    }

    private static void EnsureApprovedRecoveryState(
        TerminalCashFiscalConflictRecoveryCommand command,
        FiscalIssuanceReferenceRecord reference)
    {
        if (reference.FiscalIssuanceState is not (
                FiscalIssuanceIntegrationState.FiscalIssuanceConflict or
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration) ||
            reference.LatestErrorCode is null ||
            !ApprovedRetryableConfigurationErrorCodes.Contains(reference.LatestErrorCode) ||
            !ApprovedRetryableConfigurationErrorCodes.Contains(command.ReasonCode) ||
            !string.Equals(reference.LatestErrorCode, command.ReasonCode, StringComparison.Ordinal) ||
            string.Equals(reference.LatestErrorCode, SalesInvoiceHeaderProfileNotFound, StringComparison.Ordinal) &&
            reference.LatestErrorPosture != FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_REASON_NOT_APPROVED",
                "Only an unchanged explicitly approved retryable configuration failure is eligible for guarded recovery.");
        }
    }

    private static void EnsureReferenceHasNoFiscalEvidence(FiscalIssuanceReferenceRecord reference)
    {
        if (reference.PosServerFiscalDocumentId is not null ||
            reference.FiscalIdentityId is not null ||
            reference.FiscalSequencePolicyId is not null ||
            reference.FiscalSequenceValue is not null ||
            !string.IsNullOrWhiteSpace(reference.FiscalDocumentNumber) ||
            !string.IsNullOrWhiteSpace(reference.ElectronicJournalEventReference) ||
            reference.FiscalIssuanceEvidenceStatus is not null ||
            reference.ResultClassification is not null ||
            reference.FiscalNumberAssignmentState != FiscalNumberAssignmentState.NotAssigned)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_EXISTING_EVIDENCE_AMBIGUOUS",
                "The fiscal obligation already contains evidence that requires readback or manual reconciliation.");
        }
    }

    private static void EnsureDurableRecoveryFacts(
        TerminalCashFiscalConflictRecoveryCommand command,
        TerminalCashPaymentReadback cashPayment,
        TerminalCashFiscalConflictRecoveryFacts? facts)
    {
        var currency = command.ExpectedCurrency.Trim().ToUpperInvariant();
        if (facts is null ||
            facts.PaymentAttemptId != command.ExpectedPaymentAttemptId ||
            facts.PaymentConfirmationId != command.ExpectedPaymentConfirmationId ||
            facts.ParkingSessionId != command.ExpectedParkingSessionId ||
            facts.TariffSnapshotId != command.ExpectedTariffSnapshotId ||
            !string.Equals(facts.PaymentAttemptStatus, ConfirmedCanonicalPaymentStatus, StringComparison.Ordinal) ||
            !string.Equals(facts.PaymentConfirmationStatus, RecordedPaymentConfirmationStatus, StringComparison.Ordinal) ||
            facts.PaymentAttemptAmountMinorUnits != command.ExpectedAmountMinorUnits ||
            facts.PaymentConfirmationAmountMinorUnits != command.ExpectedAmountMinorUnits ||
            !string.Equals(facts.PaymentAttemptCurrency, currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(facts.PaymentConfirmationCurrency, currency, StringComparison.OrdinalIgnoreCase) ||
            facts.PaymentAttemptId != cashPayment.PaymentAttemptId ||
            facts.PaymentConfirmationId != cashPayment.PaymentConfirmationId)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_PAYMENT_FACTS_MISMATCH",
                "The canonical payment facts are not eligible for unchanged-request recovery.");
        }

        if (facts.ExitAuthorizationCount != 0)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_EXIT_ALREADY_AUTHORIZED",
                "An ExitAuthorization already exists; fiscal recovery requires reconciliation instead of another POST.");
        }
    }

    private static void EnsureSemanticRequestUnchanged(
        TerminalCashFiscalConflictRecoveryCommand command,
        FiscalIssuanceReferenceRecord reference,
        PosServerFiscalDocumentCreateRequest mappedRequest,
        FiscalSemanticRequestHashResult recalculatedHash)
    {
        if (recalculatedHash.Status != FiscalSemanticRequestHashSourceStatus.Available ||
            string.IsNullOrWhiteSpace(recalculatedHash.HashValue) ||
            reference.SemanticRequestHashStatus != FiscalSemanticRequestHashSourceStatus.Available ||
            !string.Equals(reference.SemanticRequestHashValue, command.ExpectedFiscalSemanticRequestHash, StringComparison.Ordinal) ||
            !string.Equals(reference.SemanticRequestHashValue, recalculatedHash.HashValue, StringComparison.Ordinal) ||
            !string.Equals(reference.SemanticRequestHashAlgorithm, recalculatedHash.HashAlgorithm, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reference.SemanticRequestHashSourceVersion, recalculatedHash.HashSourceVersion, StringComparison.Ordinal) ||
            !string.Equals(mappedRequest.UpstreamFinalityRef, command.ExpectedUpstreamFinalityReference, StringComparison.Ordinal) ||
            !string.Equals(mappedRequest.PayableBasis.UpstreamFinalityRef, command.ExpectedUpstreamFinalityReference, StringComparison.Ordinal) ||
            mappedRequest.PayableBasis.PayableAmountMinorUnits != command.ExpectedAmountMinorUnits ||
            !string.Equals(mappedRequest.PayableBasis.CurrencyCode, command.ExpectedCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_SEMANTIC_HASH_MISMATCH",
                "The reconstructed fiscal request differs from the original semantic request.");
        }
    }

    private async Task<Guid> RecordRecoveryAuditAsync(
        TerminalCashFiscalConflictRecoveryCommand command,
        FiscalIssuanceReferenceRecord reference,
        FiscalExceptionControlledRetryExecutionStatus status,
        string? resultCode,
        PosServerFiscalDocumentCreateResult? posServerResult,
        CancellationToken cancellationToken)
    {
        var failed = status is FiscalExceptionControlledRetryExecutionStatus.Conflict or
            FiscalExceptionControlledRetryExecutionStatus.Blocked or
            FiscalExceptionControlledRetryExecutionStatus.Unavailable or
            FiscalExceptionControlledRetryExecutionStatus.Unknown or
            FiscalExceptionControlledRetryExecutionStatus.Failed;
        var safeResultCode = string.IsNullOrWhiteSpace(resultCode) ? "none" : resultCode.Trim();
        var audit = await _recoveryAuditRepository.RecordAsync(
            new FiscalExceptionControlledRetryExecutionAttemptWrite(
                FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
                RetryCommandPreparationAttemptId: null,
                RetrySchedulePreparationAttemptId: null,
                ReadbackClassificationBasis: null,
                SemanticRequestHashValue: reference.SemanticRequestHashValue,
                SemanticRequestHashAlgorithm: reference.SemanticRequestHashAlgorithm,
                SemanticRequestHashSourceVersion: reference.SemanticRequestHashSourceVersion,
                UpstreamFinalityReference: reference.UpstreamFinalityReference,
                ExecutionStatus: status,
                BlockReasonCode: failed ? safeResultCode : null,
                PosServerOutcome: posServerResult?.Outcome,
                PosServerResultClassification: posServerResult?.ResultClassification,
                PosServerFiscalDocumentId: posServerResult?.FiscalDocumentId,
                FiscalDocumentNumber: posServerResult?.FiscalDocumentNumber,
                FiscalIdentityId: posServerResult?.FiscalIdentityId,
                FiscalSequencePolicyId: posServerResult?.FiscalSequencePolicyId,
                FiscalSequenceValue: posServerResult?.FiscalSequenceValue,
                FiscalSeries: posServerResult?.FiscalSeries,
                FiscalNumberPrefixText: posServerResult?.FiscalNumberPrefixText,
                FiscalNumberSuffixText: posServerResult?.FiscalNumberSuffixText,
                FiscalNumberAssignedAt: posServerResult?.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef: posServerResult?.FiscalNumberAssignedByRef,
                AttemptedAt: DateTimeOffset.UtcNow,
                CompletedAt: status == FiscalExceptionControlledRetryExecutionStatus.DryRunReady
                    ? null
                    : DateTimeOffset.UtcNow,
                ServiceIdentityId: command.ActorServiceIdentityId,
                CorrelationId: command.RecoveryCorrelationId,
                SafeSummary: BuildRecoveryAuditSummary(command, safeResultCode)),
            cancellationToken).ConfigureAwait(false);
        return audit.RetryExecutionAttemptId;
    }

    private static string BuildRecoveryAuditSummary(
        TerminalCashFiscalConflictRecoveryCommand command,
        string safeResultCode)
    {
        var summary = string.Join(
            ';',
            "terminal_cash_configuration_failure_recovery",
            $"reason={command.ReasonCode.Trim()}",
            $"approval={command.ApprovalReference.Trim()}",
            $"result={safeResultCode}",
            $"justification={command.SafeJustification.Trim().Replace('\r', ' ').Replace('\n', ' ')}");
        return summary.Length <= 240 ? summary : summary[..240];
    }

    private static void Validate(TerminalCashFiscalIssuanceCommand command)
    {
        if (command.TerminalCashTenderId == Guid.Empty)
        {
            throw Rejected("TERMINAL_CASH_TENDER_ID_REQUIRED", "Terminal cash tender reference is required.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw Rejected("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
        }

        if (command.CorrelationId == Guid.Empty)
        {
            throw Rejected("CORRELATION_ID_REQUIRED", "X-Correlation-Id header is required.");
        }
    }

    private static void ValidateRecovery(TerminalCashFiscalConflictRecoveryCommand command)
    {
        if (command.TerminalCashTenderId == Guid.Empty ||
            command.FiscalIssuanceReferenceId == Guid.Empty ||
            command.ExpectedPaymentAttemptId == Guid.Empty ||
            command.ExpectedPaymentConfirmationId == Guid.Empty ||
            command.ExpectedParkingSessionId == Guid.Empty ||
            command.ExpectedTariffSnapshotId == Guid.Empty ||
            command.ActorServiceIdentityId == Guid.Empty ||
            command.ExpectedTransactionCorrelationId == Guid.Empty ||
            command.ExpectedFiscalCorrelationId == Guid.Empty ||
            command.RecoveryCorrelationId == Guid.Empty ||
            command.ExpectedAmountMinorUnits <= 0 ||
            string.IsNullOrWhiteSpace(command.ExpectedCurrency) ||
            string.IsNullOrWhiteSpace(command.ExpectedDeliveryRequestHash) ||
            string.IsNullOrWhiteSpace(command.ExpectedFiscalSemanticRequestHash) ||
            string.IsNullOrWhiteSpace(command.ExpectedUpstreamFinalityReference) ||
            string.IsNullOrWhiteSpace(command.DeliveryIdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.ApprovalReference) ||
            string.IsNullOrWhiteSpace(command.ReasonCode) ||
            string.IsNullOrWhiteSpace(command.SafeJustification) ||
            command.ApprovalReference.Length > 160 ||
            command.ReasonCode.Length > 80 ||
            command.SafeJustification.Length > 512)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_REQUEST_INVALID",
                "The governed fiscal recovery request is incomplete or invalid.");
        }

        if (!string.Equals(command.ExpectedDeliveryRequestHash, CanonicalDeliveryRequestHash, StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_RECOVERY_DELIVERY_HASH_MISMATCH",
                "The recovery request does not preserve the canonical terminal-cash fiscal delivery payload hash.");
        }
    }

    private static string BuildUpstreamFinalityReference(TerminalCashPaymentReadback cashPayment) =>
        $"terminal-cash-payment-confirmation:{cashPayment.PaymentConfirmationId:D}:sales_invoice";

    private static TerminalCashFiscalIssuanceRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
