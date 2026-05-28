namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only repository for loading Operator Console access evaluation inputs.
/// </summary>
public interface IOperatorConsoleAccessEvaluationReadRepository
{
    /// <summary>
    /// Loads the current read model needed by future Operator Console access evaluation rules.
    /// </summary>
    Task<OperatorConsoleAccessEvaluationReadContext> LoadAsync(
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Read request for Operator Console access evaluation context.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationReadRequest(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid? ParkingSessionId,
    string WorkflowCode,
    string ControlledActionCode,
    string? EvidenceAccessIntent,
    DateTimeOffset EvaluatedAt,
    Guid CorrelationId);

/// <summary>
/// Aggregate read model consumed by future Operator Console access evaluation rules.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationReadContext(
    OperatorConsoleAccessEvaluationReadRequest Request,
    OperatorHrIdentityMappingReadModel? HrIdentityMapping,
    OperatorDeviceBindingReadModel? DeviceBinding,
    OperatorDeviceAssignmentReadModel? DeviceAssignment,
    OperatorShiftReadModel? ActiveShift,
    OperatorShiftVersionReadModel? LatestShiftVersion,
    OperatorShiftRevocationReadModel? LatestShiftRevocation,
    OperatorShiftTakeoverReadModel? ActiveShiftTakeover,
    OperatorStatutoryEntitlementFingerprintReadModel? StatutoryEntitlementFingerprint)
{
    /// <summary>
    /// Creates an empty context for missing/not-yet-imported read model rows.
    /// </summary>
    public static OperatorConsoleAccessEvaluationReadContext Empty(OperatorConsoleAccessEvaluationReadRequest request) =>
        new(
            request,
            HrIdentityMapping: null,
            DeviceBinding: null,
            DeviceAssignment: null,
            ActiveShift: null,
            LatestShiftVersion: null,
            LatestShiftRevocation: null,
            ActiveShiftTakeover: null,
            StatutoryEntitlementFingerprint: null);
}

/// <summary>
/// HR/Timekeeping identity mapping read model.
/// </summary>
public sealed record OperatorHrIdentityMappingReadModel(
    Guid HrIdentityMappingId,
    Guid UserId,
    string HrProviderCode,
    string MappingStatus,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? RevokedAt,
    string? RevocationReasonCode);

/// <summary>
/// Operator Console device binding read model.
/// </summary>
public sealed record OperatorDeviceBindingReadModel(
    Guid OperatorDeviceBindingId,
    string DeviceBindingCode,
    string DeviceName,
    Guid SiteGroupId,
    Guid SiteId,
    Guid? ServiceIdentityId,
    string DeviceStatus,
    string TrustLevel,
    string BindingSource,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    string? RevocationReasonCode);

/// <summary>
/// Operator Console device assignment read model.
/// </summary>
public sealed record OperatorDeviceAssignmentReadModel(
    Guid OperatorDeviceAssignmentHistoryId,
    Guid OperatorDeviceBindingId,
    Guid SiteGroupId,
    Guid SiteId,
    string AssignmentStatusCode,
    string AssignmentSourceCode,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? EndedAt);

/// <summary>
/// Active operator shift read model.
/// </summary>
public sealed record OperatorShiftReadModel(
    Guid OperatorShiftId,
    Guid HrIdentityMappingId,
    Guid OperatorUserId,
    Guid SiteGroupId,
    Guid SiteId,
    string HrProviderCode,
    string OperationalStatus,
    DateTimeOffset ScheduledStartAt,
    DateTimeOffset ScheduledEndAt,
    DateTimeOffset? ActiveFrom,
    DateTimeOffset? ActiveTo,
    DateTimeOffset? RevokedAt,
    string? RevocationReasonCode,
    Guid? CurrentTakeoverId);

/// <summary>
/// Latest imported shift version read model.
/// </summary>
public sealed record OperatorShiftVersionReadModel(
    Guid OperatorShiftVersionId,
    Guid OperatorShiftId,
    string HrProviderCode,
    string ImportStatusCode,
    string SourceSystemCode,
    DateTimeOffset ScheduledStartAt,
    DateTimeOffset ScheduledEndAt,
    DateTimeOffset ImportedAt);

/// <summary>
/// Shift revocation read model.
/// </summary>
public sealed record OperatorShiftRevocationReadModel(
    Guid ShiftRevocationId,
    Guid OperatorShiftId,
    string RevocationStatus,
    string ReasonCode,
    Guid RevokedOperatorUserId,
    Guid SiteId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? EffectiveAt);

/// <summary>
/// Shift takeover read model.
/// </summary>
public sealed record OperatorShiftTakeoverReadModel(
    Guid ShiftTakeoverId,
    Guid OperatorShiftId,
    Guid OriginalOperatorUserId,
    Guid TakeoverOperatorUserId,
    string TakeoverStatus,
    string ReasonCode,
    Guid SiteId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ActiveFrom,
    DateTimeOffset? ActiveTo,
    DateTimeOffset? EndedAt);

/// <summary>
/// Statutory entitlement duplicate-detection fingerprint read model.
/// </summary>
public sealed record OperatorStatutoryEntitlementFingerprintReadModel(
    Guid StatutoryEntitlementFingerprintId,
    Guid StatutoryDiscountValidationId,
    string EntitlementType,
    string FingerprintStatus,
    string DuplicateDetectionScope,
    Guid? MatchedExistingFingerprintId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? PurgedAt);
