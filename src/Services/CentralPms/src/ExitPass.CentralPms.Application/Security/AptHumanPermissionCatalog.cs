namespace ExitPass.CentralPms.Application.Security;

/// <summary>
/// Canonical Central PMS permission literals for APT human authorization.
/// </summary>
public static class AptHumanPermissionCatalog
{
    /// <summary>Allows a scoped, device-bound human to enter and use APT.</summary>
    public const string Access = "apt.access";

    /// <summary>Allows the authenticated cashier to operate their own shift.</summary>
    public const string CashierShiftOperate = "cashier-shifts.operate";

    /// <summary>Allows the authenticated cashier to operate their own cash custody.</summary>
    public const string CashCustodyOperate = "cash-custody.operate";

    /// <summary>Provides the human-permission dimension for cash receipt.</summary>
    public const string TerminalCashReceive = "terminal-cash.receive";

    /// <summary>Allows read-only payable-basis resolution and revalidation.</summary>
    public const string PayableBasisRead = "terminal-cash.payable-basis.read";

    /// <summary>
    /// Operation-specific APT permissions. Every use also requires a current APT human session,
    /// device binding, and Site or Site Group scope. None permits GLOBAL APT operation or handover.
    /// </summary>
    public static IReadOnlyList<string> OperationalPermissions { get; } =
    [
        Access,
        CashierShiftOperate,
        CashCustodyOperate,
        TerminalCashReceive
    ];

    /// <summary>
    /// Read-only APT permissions that do not authorize application access or cash operations.
    /// </summary>
    public static IReadOnlyList<string> ReadOnlyPermissions { get; } = [PayableBasisRead];
}
