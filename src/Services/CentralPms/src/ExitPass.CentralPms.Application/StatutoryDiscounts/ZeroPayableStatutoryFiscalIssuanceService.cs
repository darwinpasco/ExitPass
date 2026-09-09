using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

public interface IZeroPayableStatutoryFiscalIssuanceService
{
    Task<ZeroPayableStatutoryFiscalIssuanceResult?> ReadAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken);

    Task<ZeroPayableStatutoryFiscalIssuanceResult> IssueOrReadAsync(
        ZeroPayableStatutoryFiscalIssuanceCommand command,
        CancellationToken cancellationToken);
}

public sealed class ZeroPayableStatutoryFiscalIssuanceService : IZeroPayableStatutoryFiscalIssuanceService
{
    private const string FiscalDocumentTypeCodeKey = "sales_invoice";
    private readonly IFiscalIssuanceReferenceRepository _references;
    private readonly IFiscalIssuanceOrchestrationService _orchestration;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _posServer;
    private readonly FiscalIssuancePosServerIntegrationOptions _options;

    public ZeroPayableStatutoryFiscalIssuanceService(
        IFiscalIssuanceReferenceRepository references,
        IFiscalIssuanceOrchestrationService orchestration,
        IFiscalIssuancePosServerLiveIntegrationService posServer,
        FiscalIssuancePosServerIntegrationOptions options)
    {
        _references = references;
        _orchestration = orchestration;
        _posServer = posServer;
        _options = options;
    }

    public async Task<ZeroPayableStatutoryFiscalIssuanceResult?> ReadAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken)
    {
        if (statutoryDiscountPayableBasisApplicationCommandId == Guid.Empty)
        {
            throw new ArgumentException("Statutory payable-basis application command id is required.", nameof(statutoryDiscountPayableBasisApplicationCommandId));
        }

        var reference = await _references.FindByStatutoryApplicationCommandIdAsync(
            statutoryDiscountPayableBasisApplicationCommandId,
            cancellationToken).ConfigureAwait(false);
        return reference is null ? null : ToResult(reference, false, reference.LatestErrorCode);
    }

    public async Task<ZeroPayableStatutoryFiscalIssuanceResult> IssueOrReadAsync(
        ZeroPayableStatutoryFiscalIssuanceCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        var finality = command.Finality;
        var authority = command.CompletionAuthority;
        var upstreamReference = $"ZERO_PAYABLE_STATUTORY_FINALITY:{finality.StatutoryDiscountPayableBasisApplicationCommandId:D}";

        var existing = await _references.FindByStatutoryApplicationCommandIdAsync(
            finality.StatutoryDiscountPayableBasisApplicationCommandId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureExistingMatches(existing, command, upstreamReference);
            if (IsFiscalPrerequisiteSatisfied(existing) || !CanRetry(existing))
            {
                return ToResult(existing, false, existing.LatestErrorCode);
            }
        }

        var endpoint = ResolveEndpoint(finality.SiteId);
        var reference = existing ?? await _orchestration.PreparePendingAsync(
            new PrepareFiscalIssuanceCommand(
                PaymentConfirmationId: null,
                PaymentAttemptId: null,
                ParkingSessionId: finality.ParkingSessionId,
                TariffSnapshotId: finality.AppliedTariffSnapshotId,
                SiteId: finality.SiteId,
                SitePosServerId: endpoint.SitePosServerId,
                SitePosServerRef: endpoint.SitePosServerRef,
                FiscalDocumentTypeCodeId: endpoint.FiscalDocumentTypeCodeId,
                FiscalDocumentTypeCodeKey: FiscalDocumentTypeCodeKey,
                PayableBasisRef: finality.AppliedTariffSnapshotId.ToString("D"),
                UpstreamFinalityReference: upstreamReference,
                CorrelationId: finality.CorrelationId,
                ServiceIdentityId: null,
                CompletionBasis: FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality,
                CompletionAuthorityReferenceId: authority.DurableSourceReferenceId,
                StatutoryDiscountDecisionCommandId: finality.StatutoryDiscountDecisionCommandId,
                StatutoryDiscountPayableBasisApplicationCommandId: finality.StatutoryDiscountPayableBasisApplicationCommandId,
                StatutoryDiscountValidationId: finality.StatutoryDiscountValidationId,
                AppliedPolicyReferenceId: finality.AppliedPolicyReferenceId),
            cancellationToken).ConfigureAwait(false);

        var issue = await _posServer.TryIssueFiscalDocumentViaPosServerAsync(
            reference.FiscalIssuanceReferenceId,
            BuildMapping(reference, command, upstreamReference),
            new PosServerCreateResultRecordingContext(
                upstreamReference,
                reference.SitePosServerId,
                reference.FiscalDocumentTypeCodeId,
                finality.CorrelationId,
                DateTimeOffset.UtcNow,
                null),
            cancellationToken).ConfigureAwait(false);

        var resolved = issue.FiscalIssuanceReference ?? reference;
        return ToResult(
            resolved,
            issue.MappedRequest is not null && issue.PosServerResult is not null,
            issue.PosServerResult?.Succeeded == false ? issue.PosServerResult.Code : resolved.LatestErrorCode);
    }

    private SitePosServerEndpointOptions ResolveEndpoint(Guid siteId)
    {
        var matches = _options.Endpoints.Where(candidate => candidate.SiteId == siteId).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(matches.Length == 0
                ? "site_pos_server_site_binding_not_found"
                : "site_pos_server_site_binding_ambiguous");
        }

        var endpoint = matches[0];
        if (!endpoint.Enabled || endpoint.SitePosServerId == Guid.Empty ||
            string.IsNullOrWhiteSpace(endpoint.SitePosServerRef) ||
            !string.Equals(endpoint.Environment, _options.RuntimeEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("site_pos_server_site_binding_inactive");
        }

        return endpoint;
    }

    private static CentralPmsFiscalDocumentMappingContext BuildMapping(
        FiscalIssuanceReferenceRecord reference,
        ZeroPayableStatutoryFiscalIssuanceCommand command,
        string upstreamReference)
    {
        var finality = command.Finality;
        var vatExclusiveBasis = command.VatExclusiveBasisAmountMinorUnits;
        var currency = finality.Currency.Trim().ToUpperInvariant();
        var applicationRef = finality.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D");
        var validationRef = finality.StatutoryDiscountValidationId.ToString("D");
        var appliedTariffRef = finality.AppliedTariffSnapshotId.ToString("D");
        var originalTariffRef = finality.OriginalTariffSnapshotId.ToString("D");

        var statutoryContext = new Dictionary<string, string>
        {
            ["completionBasis"] = FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality,
            ["statutoryDiscountDecisionCommandId"] = finality.StatutoryDiscountDecisionCommandId.ToString("D"),
            ["statutoryDiscountPayableBasisApplicationCommandId"] = applicationRef,
            ["statutoryDiscountValidationId"] = validationRef,
            ["originalTariffSnapshotId"] = originalTariffRef,
            ["appliedTariffSnapshotId"] = appliedTariffRef,
            ["entitlementType"] = finality.EntitlementType
        };

        return new CentralPmsFiscalDocumentMappingContext(
            SitePosServerId: reference.SitePosServerId,
            SitePosServerRef: reference.SitePosServerRef,
            FiscalDocumentTypeCodeId: reference.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: reference.FiscalDocumentTypeCodeKey,
            FiscalDocumentStatusCodeId: null,
            BusinessDayDate: null,
            CentralPmsParkingSessionRef: finality.ParkingSessionId.ToString("D"),
            CentralPmsPaymentAttemptRef: null,
            CentralPmsPaymentConfirmationRef: null,
            PayableBasis: new CentralPmsPayableBasisContext(
                appliedTariffRef,
                upstreamReference,
                currency,
                0,
                [new CentralPmsFiscalDiscountReferenceContext(
                    validationRef,
                    "approved",
                    true,
                    statutoryContext)
                {
                    StatutoryDiscountDecisionCommandRef = finality.StatutoryDiscountDecisionCommandId.ToString("D"),
                    EntitlementType = finality.EntitlementType,
                    AppliedPolicyReferenceRef = finality.AppliedPolicyReferenceId.ToString("D"),
                    OriginalTariffSnapshotRef = originalTariffRef,
                    AppliedTariffSnapshotRef = appliedTariffRef,
                    OriginalAmountMinorUnits = finality.OriginalAmountMinorUnits,
                    VatExclusiveBasisAmountMinorUnits = vatExclusiveBasis,
                    VatTreatment = command.VatTreatment,
                    DiscountAmountMinorUnits = vatExclusiveBasis,
                    FinalPayableAmountMinorUnits = 0,
                    DecisionTimestamp = finality.DecidedAt,
                    SourceChannel = finality.SourceChannel
                }],
                statutoryContext),
            DocumentLines: [new CentralPmsFiscalDocumentLineContext(
                1,
                null,
                "Parking fee - statutory full-fee exemption",
                1m,
                vatExclusiveBasis,
                vatExclusiveBasis,
                vatExclusiveBasis,
                0,
                0,
                currency,
                null,
                appliedTariffRef,
                statutoryContext)],
            Tenders: [],
            TaxDetails: [new CentralPmsFiscalTaxDetailContext(
                null,
                null,
                vatExclusiveBasis,
                finality.VatAmountMinorUnits,
                currency,
                1,
                12m,
                new Dictionary<string, string> { ["basis"] = command.VatTreatment })],
            DiscountPrivilegeDetails: [new CentralPmsFiscalDiscountPrivilegeDetailContext(
                null,
                vatExclusiveBasis,
                vatExclusiveBasis,
                finality.VatAmountMinorUnits,
                currency,
                1,
                null,
                null,
                validationRef,
                statutoryContext)],
            Totals: [new CentralPmsFiscalTotalContext(
                null,
                0,
                currency,
                new Dictionary<string, string> { ["kind"] = "final_statutory_payable" })],
            ReferenceContext: new Dictionary<string, string>(statutoryContext)
            {
                ["site_id"] = finality.SiteId.ToString("D"),
                ["site_group_id"] = finality.SiteGroupId.ToString("D"),
                ["fiscal_issuance_reference_id"] = reference.FiscalIssuanceReferenceId.ToString("D"),
                ["monetaryPaymentReceived"] = "false"
            },
            PaymentFinalityRef: null,
            VendorAckRef: null,
            AppliedStatutoryFiscalFacts: new CentralPmsAppliedStatutoryFiscalFactsContext(
                finality.StatutoryDiscountDecisionCommandId,
                command.StatutoryRequestReference,
                finality.StatutoryDiscountPayableBasisApplicationCommandId,
                finality.StatutoryDiscountValidationId,
                finality.ParkingSessionId,
                finality.SiteId,
                finality.SiteGroupId,
                finality.EntitlementType,
                "FREE_PARKING",
                new CentralPmsAppliedStatutoryPolicyReferenceContext(
                    NormalizePolicyResolutionBasis(command.PolicyResolutionBasis),
                    AppliedPolicyReferenceId: finality.AppliedPolicyReferenceId),
                finality.OriginalTariffSnapshotId,
                finality.AppliedTariffSnapshotId,
                finality.OriginalAmountMinorUnits,
                vatExclusiveBasis,
                finality.VatAmountMinorUnits,
                command.VatTreatment,
                vatExclusiveBasis,
                0,
                currency,
                finality.AppliedAt,
                finality.SourceChannel),
            SiteId: finality.SiteId,
            CompletionBasis: FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality,
            CompletionAuthorityRef: applicationRef);
    }

    private static void Validate(ZeroPayableStatutoryFiscalIssuanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Finality);
        ArgumentNullException.ThrowIfNull(command.CompletionAuthority);
        var finality = command.Finality;
        var authority = command.CompletionAuthority;

        if (finality.FinalityState != StatutoryDiscountFinalityStates.ZeroPayableStatutoryFinality ||
            authority.CompletionBasis != CompletionBasisCodes.ZeroPayableStatutoryFinality ||
            authority.DurableSourceReferenceId != finality.StatutoryDiscountPayableBasisApplicationCommandId ||
            authority.ParkingSessionId != finality.ParkingSessionId ||
            authority.TariffSnapshotId != finality.AppliedTariffSnapshotId ||
            authority.SiteId != finality.SiteId || authority.SiteGroupId != finality.SiteGroupId ||
            authority.FinalPayableAmountMinorUnits != 0 || finality.FinalPayableAmountMinorUnits != 0 ||
            finality.OriginalAmountMinorUnits <= 0 ||
            finality.StatutoryWaiverAmountMinorUnits != finality.OriginalAmountMinorUnits ||
            command.VatExclusiveBasisAmountMinorUnits < 0 || finality.VatAmountMinorUnits < 0 ||
            command.VatExclusiveBasisAmountMinorUnits + finality.VatAmountMinorUnits != finality.OriginalAmountMinorUnits ||
            string.IsNullOrWhiteSpace(command.VatTreatment) ||
            string.IsNullOrWhiteSpace(command.PolicyResolutionBasis) ||
            command.StatutoryRequestReference == Guid.Empty ||
            authority.PaymentAttemptId is not null || authority.PaymentConfirmationId is not null)
        {
            throw new InvalidOperationException("ZERO_PAYABLE_STATUTORY_FISCAL_CONTEXT_INVALID");
        }
    }

    private static string NormalizePolicyResolutionBasis(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "NATIONAL_LAW" or "LOCAL_ORDINANCE" or "MIXED" or "INTERNAL_POLICY_REFERENCE" =>
                value.Trim().ToUpperInvariant(),
            "LOCAL_ORDINANCE_APPLIED" => "LOCAL_ORDINANCE",
            "NATIONAL_LAW_FALLBACK" => "NATIONAL_LAW",
            _ => "INTERNAL_POLICY_REFERENCE"
        };

    private static void EnsureExistingMatches(
        FiscalIssuanceReferenceRecord reference,
        ZeroPayableStatutoryFiscalIssuanceCommand command,
        string upstreamReference)
    {
        var finality = command.Finality;
        if (reference.CompletionBasis != FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality ||
            reference.CompletionAuthorityReferenceId != finality.StatutoryDiscountPayableBasisApplicationCommandId ||
            reference.StatutoryDiscountDecisionCommandId != finality.StatutoryDiscountDecisionCommandId ||
            reference.StatutoryDiscountPayableBasisApplicationCommandId != finality.StatutoryDiscountPayableBasisApplicationCommandId ||
            reference.StatutoryDiscountValidationId != finality.StatutoryDiscountValidationId ||
            reference.AppliedPolicyReferenceId != finality.AppliedPolicyReferenceId ||
            reference.ParkingSessionId != finality.ParkingSessionId ||
            reference.TariffSnapshotId != finality.AppliedTariffSnapshotId ||
            reference.SiteId != finality.SiteId ||
            reference.PaymentAttemptId is not null || reference.PaymentConfirmationId is not null ||
            !string.Equals(reference.UpstreamFinalityReference, upstreamReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ZERO_PAYABLE_STATUTORY_FISCAL_REFERENCE_CONFLICT");
        }
    }

    private static bool IsFiscalPrerequisiteSatisfied(FiscalIssuanceReferenceRecord reference) =>
        reference.CompletionBasis == FiscalCompletionBasisCodes.ZeroPayableStatutoryFinality &&
        FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(reference) &&
        !string.IsNullOrWhiteSpace(reference.ElectronicJournalEventReference);

    private static bool CanRetry(FiscalIssuanceReferenceRecord reference) =>
        reference.FiscalIssuanceState is FiscalIssuanceIntegrationState.PendingFiscalIssuance or
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested or
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown ||
        reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedService &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterServiceRecovery ||
        reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection;

    private static ZeroPayableStatutoryFiscalIssuanceResult ToResult(
        FiscalIssuanceReferenceRecord reference,
        bool posServerCallAttempted,
        string? safeErrorCode) =>
        new(
            reference.FiscalIssuanceReferenceId,
            IsFiscalPrerequisiteSatisfied(reference),
            posServerCallAttempted,
            reference.FiscalIssuanceState,
            reference.PosServerFiscalDocumentId,
            reference.FiscalDocumentNumber,
            reference.ElectronicJournalEventReference,
            reference.CompletionBasis,
            reference.CompletionAuthorityReferenceId,
            safeErrorCode);
}

public sealed record ZeroPayableStatutoryFiscalIssuanceCommand(
    StatutoryDiscountZeroPayableFinality Finality,
    CompletionAuthority CompletionAuthority,
    Guid StatutoryRequestReference,
    long VatExclusiveBasisAmountMinorUnits,
    string VatTreatment,
    string PolicyResolutionBasis);

public sealed record ZeroPayableStatutoryFiscalIssuanceResult(
    Guid FiscalIssuanceReferenceId,
    bool FiscalPrerequisiteSatisfied,
    bool PosServerCallAttempted,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    string? ElectronicJournalEventReference,
    string CompletionBasis,
    Guid? CompletionAuthorityReferenceId,
    string? SafeErrorCode);
