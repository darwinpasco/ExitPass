namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Options for Central PMS operational RBAC enforcement.
/// </summary>
public sealed class CentralPmsRbacOptions
{
    /// <summary>
    /// Enables metadata-driven RBAC checks for operational and internal eventing endpoints.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Allows test/operator permission headers to satisfy policy checks.
    /// </summary>
    public bool AllowPermissionHeader { get; init; } = true;
}
