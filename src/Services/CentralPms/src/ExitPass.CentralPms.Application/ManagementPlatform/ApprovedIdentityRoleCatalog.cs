namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed record ApprovedIdentityRolePolicy(
    string Code,
    string DisplayName,
    IReadOnlySet<string> AllowedApplicationAudiences,
    IReadOnlySet<string> AllowedAssignmentScopes,
    string? DefaultAssignmentScope = null);

/// <summary>
/// Authoritative assignable-human-role, application-audience, and assignment-scope policy for v1.3.
/// Authentication/session issuance consumes this policy; it must not maintain a second role catalog.
/// </summary>
public static class ApprovedIdentityRoleCatalog
{
    public const string SystemAdministrator = "SYSTEM_ADMINISTRATOR";
    public const string OperationsSupervisor = "OPERATIONS_SUPERVISOR";
    public const string SiteOperator = "SITE_OPERATOR";
    public const string ParkingAttendant = "PARKING_ATTENDANT";
    public const string AptCashierOperator = "APT_CASHIER_OPERATOR";
    public const string FinanceReconciliationAnalyst = "FINANCE_RECONCILIATION_ANALYST";
    public const string CompliancePolicyAdministrator = "COMPLIANCE_POLICY_ADMINISTRATOR";
    public const string ExecutiveManagement = "EXECUTIVE_MANAGEMENT";

    public const string ManagementPlatformAudience = "MANAGEMENT_PLATFORM";
    public const string OperatorConsoleAudience = "OPERATOR_CONSOLE";
    public const string NativeParkingAppAudience = "NATIVE_PARKING_APP";
    public const string AptAudience = "APT";

    public const string GlobalScope = "GLOBAL";
    public const string SiteGroupScope = "SITE_GROUP";
    public const string SiteScope = "SITE";

    private static readonly StringComparer CodeComparer = StringComparer.Ordinal;

    private static readonly IReadOnlyDictionary<string, ApprovedIdentityRolePolicy> PolicyByCode =
        new Dictionary<string, ApprovedIdentityRolePolicy>(CodeComparer)
        {
            [SystemAdministrator] = Policy(SystemAdministrator, "System Administrator", [ManagementPlatformAudience], [GlobalScope], GlobalScope),
            [OperationsSupervisor] = Policy(OperationsSupervisor, "Operations Supervisor", [OperatorConsoleAudience, ManagementPlatformAudience], [SiteScope], SiteScope),
            [SiteOperator] = Policy(SiteOperator, "Site Operator", [OperatorConsoleAudience], [SiteScope], SiteScope),
            [ParkingAttendant] = Policy(ParkingAttendant, "Parking Attendant", [NativeParkingAppAudience], [SiteScope], SiteScope),
            [AptCashierOperator] = Policy(AptCashierOperator, "APT / Cashier Operator", [AptAudience], [SiteScope], SiteScope),
            [FinanceReconciliationAnalyst] = Policy(FinanceReconciliationAnalyst, "Finance / Reconciliation Analyst", [ManagementPlatformAudience], [SiteScope, SiteGroupScope, GlobalScope]),
            [CompliancePolicyAdministrator] = Policy(CompliancePolicyAdministrator, "Compliance / Policy Administrator", [ManagementPlatformAudience], [SiteScope, SiteGroupScope, GlobalScope], GlobalScope),
            [ExecutiveManagement] = Policy(ExecutiveManagement, "Executive / Management", [ManagementPlatformAudience], [GlobalScope], GlobalScope)
        };

    private static readonly HashSet<string> ManagementPlatformOnlyStatutorySupervisorPermissions = new(CodeComparer)
    {
        "statutory-discounts.review.queue.read",
        "statutory-discounts.review.detail.read",
        "statutory-discounts.evidence.review.view",
        "statutory-discounts.decision.review",
        "statutory-discounts.decision.approve",
        "statutory-discounts.decision.reject",
        "statutory-discounts.payable-basis.apply"
    };

    private static readonly HashSet<string> ManagementPlatformStatutoryReviewPermissions = new(CodeComparer)
    {
        "statutory-discounts.review.queue.read",
        "statutory-discounts.review.detail.read",
        "statutory-discounts.evidence.review.view",
        "statutory-discounts.decision.approve",
        "statutory-discounts.decision.reject"
    };

    public static IReadOnlyList<ApprovedIdentityRolePolicy> Policies { get; } =
        PolicyByCode.Values.ToArray();

    public static IReadOnlyList<string> AssignableCodes { get; } =
        PolicyByCode.Keys.ToArray();

    public static bool TryGetPolicy(string? roleCode, out ApprovedIdentityRolePolicy? policy)
    {
        if (roleCode is not null && PolicyByCode.TryGetValue(roleCode, out var found))
        {
            policy = found;
            return true;
        }

        policy = null;
        return false;
    }

    public static bool IsAssignable(string? roleCode) => TryGetPolicy(roleCode, out _);

    public static bool IsApplicationEligible(string? roleCode, string? audience) =>
        TryGetPolicy(roleCode, out var policy) &&
        audience is not null &&
        policy!.AllowedApplicationAudiences.Contains(audience);

    public static bool IsUserEligibleForApplication(IEnumerable<string>? roleCodes, string? audience) =>
        roleCodes is not null && roleCodes.Any(roleCode => IsApplicationEligible(roleCode, audience));

    public static bool IsScopeAllowed(string? roleCode, string? scopeType) =>
        TryGetPolicy(roleCode, out var policy) &&
        scopeType is not null &&
        policy!.AllowedAssignmentScopes.Contains(scopeType);

    /// <summary>
    /// Returns false for statutory supervisor permissions on every audience except Management Platform.
    /// Other permissions are outside this narrow surface-boundary rule and are left to their policy mapping.
    /// </summary>
    public static bool IsPermissionEligibleForApplication(string? permissionCode, string? audience) =>
        permissionCode is not null &&
        (!ManagementPlatformOnlyStatutorySupervisorPermissions.Contains(permissionCode) ||
         string.Equals(audience, ManagementPlatformAudience, StringComparison.Ordinal));

    public static bool IsManagementPlatformStatutoryReviewPermission(string? permissionCode) =>
        permissionCode is not null && ManagementPlatformStatutoryReviewPermissions.Contains(permissionCode);

    private static ApprovedIdentityRolePolicy Policy(
        string code,
        string displayName,
        string[] applications,
        string[] scopes,
        string? defaultScope = null) =>
        new(code, displayName, applications.ToHashSet(CodeComparer), scopes.ToHashSet(CodeComparer), defaultScope);
}
