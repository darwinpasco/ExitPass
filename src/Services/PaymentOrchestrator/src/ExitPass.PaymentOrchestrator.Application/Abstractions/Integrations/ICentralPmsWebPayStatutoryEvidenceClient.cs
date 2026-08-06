namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

public interface ICentralPmsWebPayStatutoryEvidenceClient
{
    Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> BootstrapAsync(
        CentralPmsStatutoryEvidenceBootstrapRequest request,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> GetStatusAsync(
        Guid? statutoryDiscountDecisionCommandId,
        Guid? evidenceSetReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> CreateUploadSessionAsync(
        CentralPmsStatutoryEvidenceUploadSessionRequest request,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> UploadAsync(
        Guid opaqueUploadSessionReference,
        string contentType,
        long contentLength,
        Stream content,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> FinalizeAsync(
        Guid opaqueUploadSessionReference,
        string? clientOperationKey,
        Guid correlationId,
        CancellationToken cancellationToken);
}
