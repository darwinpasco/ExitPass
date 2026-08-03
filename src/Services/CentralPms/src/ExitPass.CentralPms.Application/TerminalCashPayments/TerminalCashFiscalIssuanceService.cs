using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Starts fiscal issuance for confirmed terminal cash payments by reusing the existing Central PMS fiscal path.
/// </summary>
public sealed class TerminalCashFiscalIssuanceService : ITerminalCashFiscalIssuanceService
{
    private const string ConfirmedCanonicalPaymentStatus = "CONFIRMED";
    private const string FiscalDocumentTypeCodeKey = "sales_invoice";
    private static readonly Guid StatutoryDiscountPrivilegeTypeCodeId =
        Guid.Parse("10000000-0000-0000-0000-000000000501");

    private readonly ITerminalCashPaymentService _terminalCashPayments;
    private readonly IFiscalIssuanceReferenceRepository _fiscalReferences;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _posServerIntegration;
    private readonly ITerminalCashStatutoryFiscalLinkageReader _statutoryFiscalLinkageReader;

    public TerminalCashFiscalIssuanceService(
        ITerminalCashPaymentService terminalCashPayments,
        IFiscalIssuanceReferenceRepository fiscalReferences,
        IFiscalIssuanceOrchestrationService orchestrationService,
        IFiscalIssuancePosServerLiveIntegrationService posServerIntegration,
        ITerminalCashStatutoryFiscalLinkageReader statutoryFiscalLinkageReader)
    {
        _terminalCashPayments = terminalCashPayments;
        _fiscalReferences = fiscalReferences;
        _orchestrationService = orchestrationService;
        _posServerIntegration = posServerIntegration;
        _statutoryFiscalLinkageReader = statutoryFiscalLinkageReader;
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
            return ToResult(
                cashPayment,
                existingByConfirmation,
                command.CorrelationId,
                posServerCallAttempted: false);
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
            return ToResult(
                cashPayment,
                existingByUpstream,
                command.CorrelationId,
                posServerCallAttempted: false);
        }

        var statutoryFiscalLinkage = await _statutoryFiscalLinkageReader
            .ReadByAppliedTariffSnapshotAsync(cashPayment, cancellationToken)
            .ConfigureAwait(false);
        EnsureStatutoryFiscalLinkageCanBeFiscalized(statutoryFiscalLinkage);

        var prepared = await _orchestrationService.PreparePendingAsync(
                BuildPrepareCommand(cashPayment, upstreamFinalityReference, command.CorrelationId),
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

        return ToResult(
            cashPayment,
            issueResult.FiscalIssuanceReference ?? prepared,
            command.CorrelationId,
            posServerCallAttempted: issueResult.MappedRequest is not null && issueResult.PosServerResult is not null,
            safeErrorCode: issueResult.PosServerResult?.Succeeded == false
                ? issueResult.PosServerResult.Code
                : issueResult.Status is FiscalIssuancePosServerLiveIntegrationStatus.Applied ? null : issueResult.Code,
            safeErrorPosture: issueResult.PosServerResult?.ErrorPosture?.ToString());
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
        string upstreamFinalityReference,
        Guid correlationId) =>
        new(
            PaymentConfirmationId: cashPayment.PaymentConfirmationId,
            PaymentAttemptId: cashPayment.PaymentAttemptId,
            ParkingSessionId: cashPayment.ParkingSessionId,
            TariffSnapshotId: cashPayment.TariffSnapshotId,
            SiteId: cashPayment.SiteId,
            SitePosServerId: null,
            SitePosServerRef: cashPayment.PosServerId.Trim(),
            FiscalDocumentTypeCodeId: null,
            FiscalDocumentTypeCodeKey: FiscalDocumentTypeCodeKey,
            PayableBasisRef: cashPayment.TariffSnapshotId.ToString("D"),
            UpstreamFinalityReference: upstreamFinalityReference,
            CorrelationId: correlationId,
            ServiceIdentityId: null);

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
            AppliedStatutoryFiscalFacts: BuildAppliedStatutoryFiscalFacts(cashPayment, statutoryContext));
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
        string? safeErrorPosture = null) =>
        new(
            TerminalCashTenderId: cashPayment.TerminalCashTenderId,
            PaymentAttemptId: reference.PaymentAttemptId,
            PaymentConfirmationId: reference.PaymentConfirmationId,
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
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false);

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

    private static string BuildUpstreamFinalityReference(TerminalCashPaymentReadback cashPayment) =>
        $"terminal-cash-payment-confirmation:{cashPayment.PaymentConfirmationId:D}:sales_invoice";

    private static TerminalCashFiscalIssuanceRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
