namespace ExitPass.CentralPms.Application.Security;

/// <summary>
/// Repository boundary for Central PMS RBAC checks.
/// </summary>
public interface ICentralPmsRbacRepository
{
    /// <summary>
    /// Checks whether an active user has one of the requested permissions through active role assignments.
    /// </summary>
    Task<bool> UserHasAnyPermissionAsync(
        Guid userId,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether an internal service identity is active.
    /// </summary>
    Task<bool> ServiceIdentityIsActiveAsync(
        Guid serviceIdentityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the canonical lifecycle, application ownership, and Site assignment for a service identity.
    /// </summary>
    Task<CentralPmsServiceIdentityAuthorizationRecord?> GetServiceIdentityAuthorizationAsync(
        Guid serviceIdentityId,
        Guid siteId,
        CancellationToken cancellationToken) =>
        Task.FromResult<CentralPmsServiceIdentityAuthorizationRecord?>(null);

    /// <summary>
    /// Records denied privileged access where the audit schema supports it.
    /// </summary>
    Task RecordDeniedAsync(
        string policyName,
        Guid? userId,
        Guid? serviceIdentityId,
        Guid? correlationId,
        string requestPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an operational audit event where the audit schema supports it.
    /// </summary>
    Task RecordAuditEventAsync(
        string eventType,
        string eventResult,
        string eventReasonCode,
        string targetEntityType,
        Guid? targetEntityId,
        Guid? actorUserId,
        Guid? actorServiceIdentityId,
        Guid? correlationId,
        string summary,
        CancellationToken cancellationToken);
}

/// <summary>
/// Server-owned service-identity facts used by application-layer authorization policies.
/// </summary>
public sealed record CentralPmsServiceIdentityAuthorizationRecord(
    Guid ServiceIdentityId,
    string IdentityType,
    string OwningServiceName,
    bool Active,
    bool SiteAssigned);
