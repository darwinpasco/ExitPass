using ExitPass.AuditEventService.Api.Configuration;
using ExitPass.AuditEventService.Api.Security;
using ExitPass.AuditEventService.Application.AuditEvents;
using ExitPass.AuditEventService.Contracts;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ExitPass.AuditEventService.Api.Controllers;

[ApiController]
[Route("v1/audit/events")]
public sealed class AuditEventsController(
    IAuditEventRepository repository,
    AuditEventServiceOptions options,
    ILogger<AuditEventsController> logger) : ControllerBase
{
    private static readonly HashSet<string> Categories =
    [
        "DOMAIN_STATE_CHANGE", "ACCESS", "CONFIGURATION_CHANGE", "POLICY_CHANGE",
        "SECURITY_RELEVANT", "INTEGRATION", "RECONCILIATION", "MANUAL_OPERATION",
        "EVIDENCE_ACCESS", "EVENTING", "SYSTEM"
    ];

    private static readonly HashSet<string> Results =
    [
        "SUCCESS", "FAILED", "DENIED", "REJECTED", "EXPIRED", "CANCELLED",
        "DUPLICATE", "NO_OP", "UNKNOWN"
    ];

    [HttpPost]
    public async Task<IActionResult> Append(AppendAuditEventRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null) return Problem(StatusCodes.Status400BadRequest,
            "AUDIT_EVENT_REQUEST_INVALID", validationError);

        var identity = (Guid)HttpContext.Items[AuditServiceAuthenticationMiddleware.AuthenticatedIdentityItem]!;
        var record = new AuditEventRecord(
            request.AuditEventId, request.EventType.Trim(), request.EventCategory, request.EventResult,
            NullIfWhiteSpace(request.EventReasonCode), request.SiteId, request.TerminalId,
            options.SourceServiceName, request.SourceChannel.Trim(), identity,
            NullIfWhiteSpace(request.Summary), request.OccurredAt.ToUniversalTime(), default,
            request.CorrelationId, request.CausationId);
        try
        {
            var (persisted, created) = await repository.AppendAsync(record, cancellationToken);
            Response.Headers.Location = $"/v1/audit/events?correlationId={persisted.CorrelationId:D}";
            return StatusCode(created ? StatusCodes.Status201Created : StatusCodes.Status200OK, ToResponse(persisted));
        }
        catch (AuditEventIdentityConflictException)
        {
            return Problem(StatusCodes.Status409Conflict, "AUDIT_EVENT_IDENTITY_CONFLICT",
                "The audit event identity is already bound to different content.");
        }
        catch (NpgsqlException)
        {
            logger.LogError("Audit event persistence failed with correlation {CorrelationId}.",
                request.CorrelationId);
            return Problem(StatusCodes.Status503ServiceUnavailable, "AUDIT_PERSISTENCE_UNAVAILABLE",
                "Audit persistence is temporarily unavailable.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] Guid correlationId,
        [FromQuery] Guid? siteId,
        CancellationToken cancellationToken)
    {
        if (correlationId == Guid.Empty)
            return Problem(StatusCodes.Status400BadRequest, "AUDIT_QUERY_INVALID",
                "A non-empty correlationId is required.");
        try
        {
            var records = await repository.QueryAsync(correlationId, siteId, cancellationToken);
            return Ok(new AuditEventQueryResponse(records.Select(ToResponse).ToArray()));
        }
        catch (NpgsqlException)
        {
            logger.LogError("Audit event query failed with correlation {CorrelationId}.", correlationId);
            return Problem(StatusCodes.Status503ServiceUnavailable, "AUDIT_PERSISTENCE_UNAVAILABLE",
                "Audit persistence is temporarily unavailable.");
        }
    }

    private ObjectResult Problem(int status, string code, string message)
    {
        var correlation = Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? HttpContext.TraceIdentifier;
        return StatusCode(status, new AuditProblem(code, message, correlation));
    }

    private static string? Validate(AppendAuditEventRequest request)
    {
        if (request.AuditEventId == Guid.Empty || request.SiteId == Guid.Empty ||
            request.CorrelationId == Guid.Empty) return "Required identifiers must be non-empty UUIDs.";
        if (string.IsNullOrWhiteSpace(request.EventType) || request.EventType.Trim().Length > 96 ||
            !Categories.Contains(request.EventCategory) || !Results.Contains(request.EventResult))
            return "Event type, category, or result is invalid.";
        if (string.IsNullOrWhiteSpace(request.SourceChannel) || request.SourceChannel.Trim().Length > 64 ||
            request.EventReasonCode?.Length > 64 || request.Summary?.Length > 256)
            return "Event metadata exceeds the controlled contract.";
        if (request.OccurredAt == default || request.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return "OccurredAt must identify a current or past event.";
        return null;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEventResponse ToResponse(AuditEventRecord record) => new(
        record.AuditEventId, record.EventType, record.EventCategory, record.EventResult,
        record.EventReasonCode, record.SiteId, record.TerminalId, record.SourceServiceName,
        record.SourceChannel, record.ActorServiceIdentityId, record.Summary, record.OccurredAt,
        record.RecordedAt, record.CorrelationId, record.CausationId);
}
