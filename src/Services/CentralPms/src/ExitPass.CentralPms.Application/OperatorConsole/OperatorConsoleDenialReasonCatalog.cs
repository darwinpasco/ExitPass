namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Stable denial reason catalog for Operator Console access readiness.
///
/// Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
/// Invariant: readiness denials must use bounded, operator-safe reason codes.
/// </summary>
public sealed class OperatorConsoleDenialReasonCatalog
{
    /// <summary>Operator identity was not supplied.</summary>
    public const string OperatorIdMissing = "OPERATOR_ID_MISSING";
    /// <summary>Operator identity could not be resolved.</summary>
    public const string OperatorNotFound = "OPERATOR_NOT_FOUND";
    /// <summary>Operator identity is not active for controlled actions.</summary>
    public const string OperatorInactive = "OPERATOR_INACTIVE";
    /// <summary>Operator role does not satisfy the action requirement.</summary>
    public const string RoleNotAllowed = "ROLE_NOT_ALLOWED";
    /// <summary>Operator Console device binding was not supplied.</summary>
    public const string DeviceIdMissing = "DEVICE_ID_MISSING";
    /// <summary>Operator Console device is not enrolled.</summary>
    public const string DeviceNotEnrolled = "DEVICE_NOT_ENROLLED";
    /// <summary>Operator Console device is not active or trusted.</summary>
    public const string DeviceNotActive = "DEVICE_NOT_ACTIVE";
    /// <summary>Operator Console device site assignment does not match the action context.</summary>
    public const string DeviceSiteMismatch = "DEVICE_SITE_MISMATCH";
    /// <summary>Operator shift was not supplied.</summary>
    public const string ShiftIdMissing = "SHIFT_ID_MISSING";
    /// <summary>Operator shift could not be resolved.</summary>
    public const string ShiftNotFound = "SHIFT_NOT_FOUND";
    /// <summary>Operator shift is not active for controlled actions.</summary>
    public const string ShiftNotActive = "SHIFT_NOT_ACTIVE";
    /// <summary>Operator shift site assignment does not match the action context.</summary>
    public const string ShiftSiteMismatch = "SHIFT_SITE_MISMATCH";
    /// <summary>Site context was not supplied.</summary>
    public const string SiteIdMissing = "SITE_ID_MISSING";
    /// <summary>Site group context was not supplied.</summary>
    public const string SiteGroupIdMissing = "SITE_GROUP_ID_MISSING";
    /// <summary>Operator is not allowed at the requested site or site group.</summary>
    public const string OperatorSiteNotAllowed = "OPERATOR_SITE_NOT_ALLOWED";
    /// <summary>Requested action is not allowed for the resolved role.</summary>
    public const string ActionNotAllowedForRole = "ACTION_NOT_ALLOWED_FOR_ROLE";
    /// <summary>Requested action is not allowed for the workflow state.</summary>
    public const string ActionNotAllowedForWorkflowState = "ACTION_NOT_ALLOWED_FOR_WORKFLOW_STATE";
    /// <summary>Local/dev fallback context was used where production trust is required.</summary>
    public const string LocalDevContextNotAllowedInProduction = "LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION";
    /// <summary>Correlation ID was not supplied.</summary>
    public const string CorrelationIdMissing = "CORRELATION_ID_MISSING";
    /// <summary>Required access readiness audit persistence failed.</summary>
    public const string AuditPersistenceFailed = "AUDIT_PERSISTENCE_FAILED";

    private static readonly IReadOnlyList<OperatorConsoleDenialReasonMetadata> Entries =
    [
        Blocking(OperatorIdMissing, retryable: true, "OPERATOR_NOT_READY"),
        Blocking(OperatorNotFound, retryable: false, "OPERATOR_NOT_READY"),
        Blocking(OperatorInactive, retryable: false, "OPERATOR_NOT_READY"),
        Blocking(RoleNotAllowed, retryable: false, "ROLE_BLOCKED"),
        Blocking(DeviceIdMissing, retryable: true, "DEVICE_NOT_READY"),
        Blocking(DeviceNotEnrolled, retryable: true, "DEVICE_NOT_READY"),
        Blocking(DeviceNotActive, retryable: true, "DEVICE_NOT_READY"),
        Blocking(DeviceSiteMismatch, retryable: true, "SITE_BLOCKED"),
        Blocking(ShiftIdMissing, retryable: true, "SHIFT_REQUIRED"),
        Blocking(ShiftNotFound, retryable: true, "SHIFT_REQUIRED"),
        Blocking(ShiftNotActive, retryable: true, "SHIFT_REQUIRED"),
        Blocking(ShiftSiteMismatch, retryable: true, "SITE_BLOCKED"),
        Blocking(SiteIdMissing, retryable: true, "SITE_BLOCKED"),
        Blocking(SiteGroupIdMissing, retryable: true, "SITE_BLOCKED"),
        Blocking(OperatorSiteNotAllowed, retryable: true, "SITE_BLOCKED"),
        Blocking(ActionNotAllowedForRole, retryable: false, "ROLE_BLOCKED"),
        Blocking(ActionNotAllowedForWorkflowState, retryable: true, "WORKFLOW_BLOCKED"),
        Blocking(LocalDevContextNotAllowedInProduction, retryable: false, "PRODUCTION_TRUST_REQUIRED"),
        Blocking(CorrelationIdMissing, retryable: true, "SUPPORT_REQUIRED"),
        Blocking(AuditPersistenceFailed, retryable: true, "SUPPORT_REQUIRED")
    ];

    private static readonly IReadOnlyDictionary<string, OperatorConsoleDenialReasonMetadata> EntriesByCode =
        Entries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);

    /// <summary>Returns all stable denial reason metadata.</summary>
    public IReadOnlyList<OperatorConsoleDenialReasonMetadata> GetAll() => Entries;

    /// <summary>Returns true when the code is part of the stable denial catalog.</summary>
    public bool Contains(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        EntriesByCode.ContainsKey(Normalize(code));

    /// <summary>Returns denial metadata, if present.</summary>
    public OperatorConsoleDenialReasonMetadata? Find(string? code) =>
        !string.IsNullOrWhiteSpace(code) && EntriesByCode.TryGetValue(Normalize(code), out var metadata)
            ? metadata
            : null;

    private static OperatorConsoleDenialReasonMetadata Blocking(
        string code,
        bool retryable,
        string uxMessageCategory) =>
        new(code, Severity: "BLOCKING", ControlCategory: "ACCESS_READINESS", retryable, uxMessageCategory);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

/// <summary>Metadata for a stable Operator Console denial reason.</summary>
public sealed record OperatorConsoleDenialReasonMetadata(
    string Code,
    string Severity,
    string ControlCategory,
    bool Retryable,
    string UxMessageCategory);
