namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Controlled Operator Console action codes used for access evaluation and audit evidence.
/// </summary>
public static class OperatorConsoleActionCodes
{
    /// <summary>Parking session lookup for Operator Console workflows.</summary>
    public const string SessionLookup = "SESSION_LOOKUP";

    /// <summary>Create a statutory discount validation draft.</summary>
    public const string CreateStatutoryDiscountDraft = "CREATE_STATUTORY_DISCOUNT_DRAFT";

    /// <summary>View statutory discount validation draft queue or detail.</summary>
    public const string ViewStatutoryDiscountDraft = "VIEW_STATUTORY_DISCOUNT_DRAFT";

    /// <summary>Approve or reject a statutory discount validation draft.</summary>
    public const string DecideStatutoryDiscount = "DECIDE_STATUTORY_DISCOUNT";

    /// <summary>Capture statutory discount evidence metadata.</summary>
    public const string CaptureEvidence = "CAPTURE_EVIDENCE";

    /// <summary>View statutory discount evidence metadata.</summary>
    public const string ViewEvidence = "VIEW_EVIDENCE";

    /// <summary>Apply an approved statutory discount validation to payable basis.</summary>
    public const string ApplyStatutoryDiscountPayableBasis = "APPLY_STATUTORY_DISCOUNT_PAYABLE_BASIS";

    /// <summary>Resolve statutory discount policy context.</summary>
    public const string ViewPolicyResolution = "VIEW_POLICY_RESOLUTION";
}
