using ExitPass.CentralPms.Application.ManagementPlatform;

namespace ExitPass.CentralPms.Application.HumanAuthentication;

public static class AptCashCustodyMovementTypes
{
    public const string SafeDrop = "SAFE_DROP";
    public const string DirectRemittance = "DIRECT_REMITTANCE";
    public const string CashReturn = "CASH_RETURN";
}

public sealed record AptCashCustodyCounterpartyQuery(
    Guid InitiatingUserReference,
    Guid SiteReference,
    string MovementType,
    string? OriginalMovementType,
    string? RequiredCounterpartyReference,
    DateTimeOffset AsOf);

public sealed record AptCashCustodyCounterparty(
    Guid UserReference,
    string LoginIdentifier,
    string DisplayName,
    string RoleCode,
    string RoleLabel);

public interface IAptCashCustodyCounterpartyDirectory
{
    Task<IReadOnlyList<AptCashCustodyCounterparty>> ListEligibleAsync(
        AptCashCustodyCounterpartyQuery query,
        CancellationToken cancellationToken);
}

public static class AptCashCustodyCounterpartyPolicy
{
    public static bool TryResolveEligibleRoleCodes(
        string? movementType,
        string? originalMovementType,
        out IReadOnlyList<string> roleCodes)
    {
        var normalized = Normalize(movementType);
        var original = Normalize(originalMovementType);
        roleCodes = normalized switch
        {
            AptCashCustodyMovementTypes.SafeDrop =>
            [
                ApprovedIdentityRoleCatalog.OperationsSupervisor,
                ApprovedIdentityRoleCatalog.AptCashCustodySupervisor,
                ApprovedIdentityRoleCatalog.AptSiteCustodian
            ],
            AptCashCustodyMovementTypes.DirectRemittance =>
                [ApprovedIdentityRoleCatalog.AptCashCollector],
            AptCashCustodyMovementTypes.CashReturn when original == AptCashCustodyMovementTypes.SafeDrop =>
            [
                ApprovedIdentityRoleCatalog.OperationsSupervisor,
                ApprovedIdentityRoleCatalog.AptCashCustodySupervisor,
                ApprovedIdentityRoleCatalog.AptSiteCustodian
            ],
            AptCashCustodyMovementTypes.CashReturn when original == AptCashCustodyMovementTypes.DirectRemittance =>
                [ApprovedIdentityRoleCatalog.AptCashCollector],
            _ => Array.Empty<string>()
        };
        return roleCodes.Count > 0;
    }

    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Replace('-', '_').ToUpperInvariant() switch
        {
            "SAFEDROP" => AptCashCustodyMovementTypes.SafeDrop,
            "DIRECTREMITTANCE" => AptCashCustodyMovementTypes.DirectRemittance,
            "CASHRETURN" => AptCashCustodyMovementTypes.CashReturn,
            var normalized => normalized
        };
}
