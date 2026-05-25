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
    /// Records denied privileged access where the audit schema supports it.
    /// </summary>
    Task RecordDeniedAsync(
        string policyName,
        Guid? userId,
        Guid? serviceIdentityId,
        Guid? correlationId,
        string requestPath,
        CancellationToken cancellationToken);
}
