using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.Payments;

public interface IDigitalPaymentFiscalContextReader
{
    Task<DigitalPaymentFiscalContext> ReadAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid parkingSessionId,
        CancellationToken cancellationToken);
}

public interface IDigitalPaymentFiscalIssuanceService
{
    Task<DigitalPaymentFiscalIssuanceResult> IssueOrReadAsync(
        DigitalPaymentFiscalIssuanceCommand command,
        CancellationToken cancellationToken);
}

public sealed class DigitalPaymentFiscalIssuanceService : IDigitalPaymentFiscalIssuanceService
{
    private const string FiscalDocumentTypeCodeKey = "sales_invoice";
    private readonly IDigitalPaymentFiscalContextReader _contextReader;
    private readonly IFiscalIssuanceReferenceRepository _references;
    private readonly IFiscalIssuanceOrchestrationService _orchestration;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _posServer;

    public DigitalPaymentFiscalIssuanceService(
        IDigitalPaymentFiscalContextReader contextReader,
        IFiscalIssuanceReferenceRepository references,
        IFiscalIssuanceOrchestrationService orchestration,
        IFiscalIssuancePosServerLiveIntegrationService posServer)
    {
        _contextReader = contextReader;
        _references = references;
        _orchestration = orchestration;
        _posServer = posServer;
    }

    public async Task<DigitalPaymentFiscalIssuanceResult> IssueOrReadAsync(
        DigitalPaymentFiscalIssuanceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _references.FindByPaymentConfirmationIdAsync(
            command.PaymentConfirmationId,
            cancellationToken);
        if (existing is not null &&
            FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(existing))
        {
            return ToResult(existing, false, null);
        }

        if (existing is not null && !CanRetry(existing))
        {
            return ToResult(existing, false, existing.LatestErrorCode);
        }

        var context = await _contextReader.ReadAsync(
            command.PaymentAttemptId,
            command.PaymentConfirmationId,
            command.ParkingSessionId,
            cancellationToken);
        var upstreamReference = $"PAYMENT_CONFIRMATION:{command.PaymentConfirmationId:D}";
        FiscalIssuanceReferenceRecord reference;
        if (existing is null)
        {
            reference = await _orchestration.PreparePendingAsync(
                new PrepareFiscalIssuanceCommand(
                    command.PaymentConfirmationId,
                    command.PaymentAttemptId,
                    command.ParkingSessionId,
                    context.TariffSnapshotId,
                    context.SiteId,
                    context.SitePosServerId,
                    context.SitePosServerRef,
                    null,
                    FiscalDocumentTypeCodeKey,
                    context.TariffSnapshotId.ToString("D"),
                    upstreamReference,
                    command.CorrelationId,
                    command.ServiceIdentityId),
                cancellationToken);
        }
        else
        {
            EnsureExistingReferenceMatches(existing, command, context, upstreamReference);
            reference = existing;
        }

        var amount = context.AmountMinorUnits;
        var attemptRef = command.PaymentAttemptId.ToString("D");
        var confirmationRef = command.PaymentConfirmationId.ToString("D");
        var mapping = new CentralPmsFiscalDocumentMappingContext(
            reference.SitePosServerId,
            reference.SitePosServerRef,
            reference.FiscalDocumentTypeCodeId,
            reference.FiscalDocumentTypeCodeKey,
            null,
            DateOnly.FromDateTime(context.ConfirmedAt.UtcDateTime),
            command.ParkingSessionId.ToString("D"),
            attemptRef,
            confirmationRef,
            new CentralPmsPayableBasisContext(
                context.TariffSnapshotId.ToString("D"),
                upstreamReference,
                context.Currency,
                amount,
                [],
                new Dictionary<string, string> { ["source"] = "central-pms-authoritative-tariff" }),
            [new CentralPmsFiscalDocumentLineContext(1, null, "Parking fee - digital payment", 1m, amount, amount, 0, 0, amount, context.Currency, null, context.TariffSnapshotId.ToString("D"), new Dictionary<string, string>())],
            [new CentralPmsFiscalTenderContext(null, amount, context.Currency, attemptRef, confirmationRef, upstreamReference, command.ProviderReference, new Dictionary<string, string> { ["channel"] = "DIGITAL" })],
            [],
            [],
            [new CentralPmsFiscalTotalContext(null, amount, context.Currency, new Dictionary<string, string> { ["kind"] = "grand_total" })],
            new Dictionary<string, string> { ["site_id"] = context.SiteId.ToString("D"), ["payment_channel"] = "DIGITAL" },
            upstreamReference,
            null);

        var issue = await _posServer.TryIssueFiscalDocumentViaPosServerAsync(
            reference.FiscalIssuanceReferenceId,
            mapping,
            new PosServerCreateResultRecordingContext(
                upstreamReference,
                reference.SitePosServerId,
                reference.FiscalDocumentTypeCodeId,
                command.CorrelationId,
                DateTimeOffset.UtcNow,
                command.ServiceIdentityId),
            cancellationToken);

        return ToResult(
            issue.FiscalIssuanceReference ?? reference,
            issue.MappedRequest is not null && issue.PosServerResult is not null,
            issue.PosServerResult?.Succeeded == false ? issue.PosServerResult.Code : null);
    }

    private static bool CanRetry(FiscalIssuanceReferenceRecord reference) =>
        reference.FiscalIssuanceState is
            FiscalIssuanceIntegrationState.PendingFiscalIssuance or
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested or
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown ||
        reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedService &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterServiceRecovery ||
        reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection;

    private static void EnsureExistingReferenceMatches(
        FiscalIssuanceReferenceRecord reference,
        DigitalPaymentFiscalIssuanceCommand command,
        DigitalPaymentFiscalContext context,
        string upstreamReference)
    {
        if (reference.PaymentConfirmationId != command.PaymentConfirmationId ||
            reference.PaymentAttemptId != command.PaymentAttemptId ||
            reference.ParkingSessionId != command.ParkingSessionId ||
            reference.TariffSnapshotId != context.TariffSnapshotId ||
            reference.SiteId != context.SiteId ||
            reference.SitePosServerId != context.SitePosServerId ||
            !string.Equals(reference.SitePosServerRef, context.SitePosServerRef, StringComparison.Ordinal) ||
            !string.Equals(reference.PayableBasisRef, context.TariffSnapshotId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(reference.UpstreamFinalityReference, upstreamReference, StringComparison.Ordinal) ||
            !string.Equals(reference.FiscalDocumentTypeCodeKey, FiscalDocumentTypeCodeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("digital_payment_fiscal_routing_context_mismatch");
        }
    }

    private static DigitalPaymentFiscalIssuanceResult ToResult(
        FiscalIssuanceReferenceRecord reference,
        bool posServerCallAttempted,
        string? safeErrorCode) =>
        new(
            reference.FiscalIssuanceReferenceId,
            FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(reference),
            posServerCallAttempted,
            safeErrorCode,
            reference.SitePosServerId,
            reference.SitePosServerRef);
}

public sealed record DigitalPaymentFiscalIssuanceCommand(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid ParkingSessionId,
    string ProviderReference,
    Guid CorrelationId,
    Guid? ServiceIdentityId);

public sealed record DigitalPaymentFiscalContext(
    Guid SiteId,
    Guid TariffSnapshotId,
    long AmountMinorUnits,
    string Currency,
    DateTimeOffset ConfirmedAt,
    Guid SitePosServerId,
    string SitePosServerRef);

public sealed record DigitalPaymentFiscalIssuanceResult(
    Guid FiscalIssuanceReferenceId,
    bool ReadyForExitAuthorization,
    bool PosServerCallAttempted,
    string? SafeErrorCode,
    Guid? SitePosServerId,
    string? SitePosServerRef);
