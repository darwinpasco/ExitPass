namespace ExitPass.AuditEventService.Contracts;

public sealed record AppendAuditEventRequest(
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

public sealed record AuditEventResponse(
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

public sealed record AuditEventQueryResponse(IReadOnlyList<AuditEventResponse> Items);

public sealed record AuditProblem(string Code, string Message, string CorrelationId);
