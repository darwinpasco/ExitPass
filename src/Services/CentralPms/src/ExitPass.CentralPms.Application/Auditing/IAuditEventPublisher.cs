namespace ExitPass.CentralPms.Application.Auditing;

public interface IAuditEventPublisher
{
    Task AppendAsync(ApplicationAuditEvent auditEvent, CancellationToken cancellationToken);
}

public sealed record ApplicationAuditEvent(
    Guid AuditEventId,
    string EventType,
    string EventCategory,
    string EventResult,
    string? EventReasonCode,
    Guid SiteId,
    Guid? TerminalId,
    string SourceChannel,
    string? Summary,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    Guid? CausationId);
