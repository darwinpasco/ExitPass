namespace ExitPass.AuditEventService.Application.AuditEvents;

public sealed record AuditEventRecord(
    Guid AuditEventId,
    string EventType,
    string EventCategory,
    string EventResult,
    string? EventReasonCode,
    Guid SiteId,
    Guid? TerminalId,
    string SourceServiceName,
    string SourceChannel,
    Guid ActorServiceIdentityId,
    string? Summary,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid CorrelationId,
    Guid? CausationId);

public interface IAuditEventRepository
{
    Task<(AuditEventRecord Record, bool Created)> AppendAsync(
        AuditEventRecord record,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEventRecord>> QueryAsync(
        Guid correlationId,
        Guid? siteId,
        CancellationToken cancellationToken);
}

public sealed class AuditEventIdentityConflictException : Exception
{
    public AuditEventIdentityConflictException()
        : base("The audit event identity is already bound to different immutable content.")
    {
    }
}
