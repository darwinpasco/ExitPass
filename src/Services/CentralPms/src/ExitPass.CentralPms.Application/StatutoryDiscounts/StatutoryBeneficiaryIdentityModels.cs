namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

public static class StatutoryBeneficiaryIdentityCaptureChannels
{
    public const string OperatorConsole = "OPERATOR_CONSOLE";
    public const string ManagementPlatform = "MANAGEMENT_PLATFORM";

    public static bool IsAuthoritative(string? value) =>
        value is OperatorConsole or ManagementPlatform;
}

public sealed class StatutoryBeneficiaryProtectionOptions
{
    public string? ProtectionKeyBase64 { get; init; }
    public string ProtectionKeyReference { get; init; } = string.Empty;
    public string ProtectionKeyVersion { get; init; } = string.Empty;
}

public sealed record StatutoryBeneficiaryIdentityInput(
    string BeneficiaryFullName,
    string StatutoryIdType,
    string StatutoryIdNumber,
    string? BeneficiaryTin);

public sealed record StatutoryBeneficiaryIdentityRecord(
    Guid StatutoryDiscountDecisionCommandId,
    Guid SiteId,
    string EntitlementType,
    string BeneficiaryFullName,
    string StatutoryIdType,
    string StatutoryIdNumber,
    string? BeneficiaryTin,
    Guid CapturedByActorRef,
    string CapturedByChannel,
    DateTimeOffset CapturedAt,
    Guid LastUpdatedByActorRef,
    string LastUpdatedByChannel,
    DateTimeOffset LastUpdatedAt,
    Guid? ReviewedByActorRef,
    DateTimeOffset? ReviewedAt,
    string? ApprovalDecisionReference,
    Guid CorrelationReference,
    bool IsFinal);

public sealed record ProtectedStatutoryBeneficiaryIdentity(
    byte[] Envelope,
    string KeyReference,
    string KeyVersion,
    short EnvelopeFormatVersion);

public interface IStatutoryBeneficiaryIdentityProtector
{
    bool IsConfigured { get; }
    ProtectedStatutoryBeneficiaryIdentity Protect(
        Guid statutoryDiscountDecisionCommandId,
        Guid siteId,
        StatutoryBeneficiaryIdentityInput identity);
    StatutoryBeneficiaryIdentityInput Unprotect(
        Guid statutoryDiscountDecisionCommandId,
        Guid siteId,
        ProtectedStatutoryBeneficiaryIdentity protectedIdentity);
}

public enum StatutoryBeneficiaryIdentityWriteOutcome
{
    Saved,
    NotFound,
    Conflict,
    Invalid,
    ProtectionUnavailable
}

public sealed record StatutoryBeneficiaryIdentityWriteResult(
    StatutoryBeneficiaryIdentityWriteOutcome Outcome,
    string Classification,
    StatutoryBeneficiaryIdentityRecord? Record = null);

public interface IStatutoryBeneficiaryIdentityRepository
{
    Task<StatutoryBeneficiaryIdentityWriteResult> SaveBeforeDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid actorUserId,
        string captureChannel,
        StatutoryBeneficiaryIdentityInput identity,
        Guid correlationReference,
        CancellationToken cancellationToken);

    Task<StatutoryBeneficiaryIdentityWriteResult> FinalizeApprovedAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid reviewedByActorRef,
        string approvalDecisionReference,
        Guid correlationReference,
        CancellationToken cancellationToken);

    Task<StatutoryBeneficiaryIdentityRecord?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);
}

public static class StatutoryBeneficiaryIdentityNormalizer
{
    public static bool TryNormalize(
        StatutoryBeneficiaryIdentityInput? input,
        out StatutoryBeneficiaryIdentityInput? normalized)
    {
        normalized = null;
        if (input is null) return false;

        var name = NormalizeText(input.BeneficiaryFullName, 256, collapseWhitespace: true);
        var idType = NormalizeCode(input.StatutoryIdType, 64);
        var idNumber = NormalizeText(input.StatutoryIdNumber, 128, collapseWhitespace: false);
        var tin = NormalizeText(input.BeneficiaryTin, 32, collapseWhitespace: false);
        if (name is null || idType is null || idNumber is null) return false;

        normalized = new(name, idType, idNumber, tin);
        return true;
    }

    private static string? NormalizeText(string? value, int maximumLength, bool collapseWhitespace)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = collapseWhitespace
            ? string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            : value.Trim();
        return normalized.Length <= maximumLength && !normalized.Contains('\r') && !normalized.Contains('\n')
            ? normalized
            : null;
    }

    private static string? NormalizeCode(string? value, int maximumLength)
    {
        var normalized = NormalizeText(value, maximumLength, collapseWhitespace: false)?.ToUpperInvariant();
        return normalized is not null && normalized.All(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            ? normalized
            : null;
    }
}
