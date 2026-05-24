using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests the baseline WebPay PayMongo reconciliation diagnostics contract.
/// </summary>
public sealed class WebPayPayMongoReconciliationDiagnosticsTests
{
    [Fact]
    public void DiagnosticsSql_UsesSuppliedTicketReferenceWithoutFallback()
    {
        var sql = ReadDiagnosticsSql();

        Assert.Contains("\\if :{?ticket_reference}", sql, StringComparison.Ordinal);
        Assert.Contains("NULLIF(:'ticket_reference', '')", sql, StringComparison.Ordinal);
        Assert.Contains("REQUESTED_TICKET_NOT_FOUND", sql, StringComparison.Ordinal);
        Assert.Contains("The supplied ticket_reference was not found; no fallback ticket was selected.", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsSql_DefaultsToPayMongoAndDoesNotMentionAub()
    {
        var sql = ReadDiagnosticsSql();

        Assert.Contains("\\set provider_code 'PAYMONGO'", sql, StringComparison.Ordinal);
        Assert.Contains(":'selected_provider_code'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AUB", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportScript_DefaultsToPayMongoRequiresExplicitScopeAndIgnoresLocalExports()
    {
        var script = ReadRepoFile("scripts", "dev-data", "Export-WebPayPayMongoReconciliation.ps1");
        var gitignore = ReadRepoFile(".gitignore");

        Assert.Contains("[string] $ProviderCode = \"PAYMONGO\"", script, StringComparison.Ordinal);
        Assert.Contains("MISSING_RECONCILIATION_SCOPE", script, StringComparison.Ordinal);
        Assert.Contains("reconciliation_classification", script, StringComparison.Ordinal);
        Assert.Contains("webpay-paymongo-reconciliation-{0}.{1}", script, StringComparison.Ordinal);
        Assert.Contains("webpay-paymongo-reconciliation-{0}-{1}.{2}", script, StringComparison.Ordinal);
        Assert.Contains("/scripts/dev-data/.reconciliation-exports/", gitignore, StringComparison.Ordinal);
        Assert.DoesNotContain("AUB", script, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void Classify_WhenGivenPaymentProviderAndExitEvidence_ReturnsExpectedClassification(
        ReconciliationEvidence evidence,
        string expected)
    {
        Assert.Equal(expected, Classify(evidence));
    }

    public static IEnumerable<object[]> ClassificationCases()
    {
        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: true,
                HasPaidProviderSession: true,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 1,
                ProviderCallbackCount: 1,
                ExitAuthorizationCount: 1,
                GateConsumedCount: 1,
                DuplicateProviderEvent: false,
                ConfirmedAmount: 100m,
                ProviderAmount: 100m,
                CurrencyCode: "PHP",
                ProviderCurrency: "PHP",
                HasStalePendingAttempt: false),
            "MATCHED"
        ];

        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: true,
                HasPaidProviderSession: false,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 1,
                ProviderCallbackCount: 0,
                ExitAuthorizationCount: 1,
                GateConsumedCount: 0,
                DuplicateProviderEvent: false,
                ConfirmedAmount: 100m,
                ProviderAmount: null,
                CurrencyCode: "PHP",
                ProviderCurrency: null,
                HasStalePendingAttempt: false),
            "EXITPASS_CONFIRMED_PROVIDER_MISSING"
        ];

        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: false,
                HasPaidProviderSession: true,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 0,
                ProviderCallbackCount: 1,
                ExitAuthorizationCount: 0,
                GateConsumedCount: 0,
                DuplicateProviderEvent: false,
                ConfirmedAmount: null,
                ProviderAmount: 100m,
                CurrencyCode: null,
                ProviderCurrency: "PHP",
                HasStalePendingAttempt: false),
            "PROVIDER_PAID_EXITPASS_MISSING"
        ];

        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: true,
                HasPaidProviderSession: true,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 1,
                ProviderCallbackCount: 1,
                ExitAuthorizationCount: 1,
                GateConsumedCount: 0,
                DuplicateProviderEvent: false,
                ConfirmedAmount: 100m,
                ProviderAmount: 101m,
                CurrencyCode: "PHP",
                ProviderCurrency: "PHP",
                HasStalePendingAttempt: false),
            "AMOUNT_MISMATCH"
        ];

        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: true,
                HasPaidProviderSession: true,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 1,
                ProviderCallbackCount: 1,
                ExitAuthorizationCount: 1,
                GateConsumedCount: 0,
                DuplicateProviderEvent: false,
                ConfirmedAmount: 100m,
                ProviderAmount: 100m,
                CurrencyCode: "PHP",
                ProviderCurrency: "USD",
                HasStalePendingAttempt: false),
            "CURRENCY_MISMATCH"
        ];

        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: true,
                HasPaidProviderSession: true,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 1,
                ProviderCallbackCount: 2,
                ExitAuthorizationCount: 1,
                GateConsumedCount: 0,
                DuplicateProviderEvent: true,
                ConfirmedAmount: 100m,
                ProviderAmount: 100m,
                CurrencyCode: "PHP",
                ProviderCurrency: "PHP",
                HasStalePendingAttempt: false),
            "DUPLICATE_PROVIDER_EVENT"
        ];

        yield return
        [
            new ReconciliationEvidence(
                HasConfirmedAttempt: true,
                HasPaidProviderSession: true,
                HasConfirmedProviderOutcome: false,
                PaymentConfirmationCount: 1,
                ProviderCallbackCount: 1,
                ExitAuthorizationCount: 0,
                GateConsumedCount: 0,
                DuplicateProviderEvent: false,
                ConfirmedAmount: 100m,
                ProviderAmount: 100m,
                CurrencyCode: "PHP",
                ProviderCurrency: "PHP",
                HasStalePendingAttempt: false),
            "CONFIRMED_WITHOUT_EXIT_AUTHORIZATION"
        ];
    }

    private static string Classify(ReconciliationEvidence evidence)
    {
        if (evidence.GateConsumedCount > 0 && evidence.PaymentConfirmationCount == 0)
        {
            return "GATE_CONSUMED_WITHOUT_CONFIRMATION";
        }

        if (evidence.ExitAuthorizationCount > 0 && evidence.PaymentConfirmationCount == 0)
        {
            return "EXIT_AUTHORIZATION_WITHOUT_CONFIRMATION";
        }

        if (evidence.PaymentConfirmationCount > 1)
        {
            return "DUPLICATE_PAYMENT_CONFIRMATION";
        }

        if (evidence.DuplicateProviderEvent)
        {
            return "DUPLICATE_PROVIDER_EVENT";
        }

        if (evidence.ConfirmedAmount is not null &&
            evidence.ProviderAmount is not null &&
            evidence.ConfirmedAmount != evidence.ProviderAmount)
        {
            return "AMOUNT_MISMATCH";
        }

        if (!string.IsNullOrWhiteSpace(evidence.CurrencyCode) &&
            !string.IsNullOrWhiteSpace(evidence.ProviderCurrency) &&
            !string.Equals(evidence.CurrencyCode, evidence.ProviderCurrency, StringComparison.Ordinal))
        {
            return "CURRENCY_MISMATCH";
        }

        if ((evidence.HasPaidProviderSession || evidence.HasConfirmedProviderOutcome) &&
            evidence.PaymentConfirmationCount == 0)
        {
            return "PROVIDER_PAID_EXITPASS_MISSING";
        }

        if (evidence.HasConfirmedAttempt &&
            evidence.PaymentConfirmationCount > 0 &&
            !(evidence.HasPaidProviderSession || evidence.HasConfirmedProviderOutcome || evidence.ProviderCallbackCount > 0))
        {
            return "EXITPASS_CONFIRMED_PROVIDER_MISSING";
        }

        if (evidence.HasConfirmedAttempt &&
            evidence.PaymentConfirmationCount > 0 &&
            evidence.ExitAuthorizationCount == 0)
        {
            return "CONFIRMED_WITHOUT_EXIT_AUTHORIZATION";
        }

        if (evidence.HasStalePendingAttempt)
        {
            return "STALE_PENDING_ATTEMPT";
        }

        if (evidence.HasConfirmedAttempt &&
            evidence.PaymentConfirmationCount == 1 &&
            (evidence.HasPaidProviderSession || evidence.HasConfirmedProviderOutcome) &&
            evidence.ProviderCallbackCount >= 1 &&
            evidence.ExitAuthorizationCount >= 1)
        {
            return "MATCHED";
        }

        return "INCONCLUSIVE";
    }

    private static string ReadDiagnosticsSql()
    {
        return ReadRepoFile("scripts", "dev-data", "webpay-paymongo-reconciliation-diagnostics.sql");
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }

    public sealed record ReconciliationEvidence(
        bool HasConfirmedAttempt,
        bool HasPaidProviderSession,
        bool HasConfirmedProviderOutcome,
        int PaymentConfirmationCount,
        int ProviderCallbackCount,
        int ExitAuthorizationCount,
        int GateConsumedCount,
        bool DuplicateProviderEvent,
        decimal? ConfirmedAmount,
        decimal? ProviderAmount,
        string? CurrencyCode,
        string? ProviderCurrency,
        bool HasStalePendingAttempt);
}
