using ExitPass.CentralPms.Application.FiscalIssuance;

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
        if (existing is not null)
        {
            return ToResult(existing, false, null);
        }

        var context = await _contextReader.ReadAsync(
            command.PaymentAttemptId,
            command.PaymentConfirmationId,
            command.ParkingSessionId,
            cancellationToken);
        var upstreamReference = $"PAYMENT_CONFIRMATION:{command.PaymentConfirmationId:D}";
        var prepared = await _orchestration.PreparePendingAsync(
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

        var amount = context.AmountMinorUnits;
        var attemptRef = command.PaymentAttemptId.ToString("D");
        var confirmationRef = command.PaymentConfirmationId.ToString("D");
        var mapping = new CentralPmsFiscalDocumentMappingContext(
            prepared.SitePosServerId,
            prepared.SitePosServerRef,
            prepared.FiscalDocumentTypeCodeId,
            prepared.FiscalDocumentTypeCodeKey,
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
            prepared.FiscalIssuanceReferenceId,
            mapping,
            new PosServerCreateResultRecordingContext(
                upstreamReference,
                prepared.SitePosServerId,
                prepared.FiscalDocumentTypeCodeId,
                command.CorrelationId,
                DateTimeOffset.UtcNow,
                command.ServiceIdentityId),
            cancellationToken);

        return ToResult(
            issue.FiscalIssuanceReference ?? prepared,
            issue.MappedRequest is not null && issue.PosServerResult is not null,
            issue.PosServerResult?.Succeeded == false ? issue.PosServerResult.Code : null);
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
