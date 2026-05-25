using ExitPass.CentralPms.Application.Reconciliation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests conservative reconciliation item evaluation rules.
/// </summary>
public sealed class ReconciliationEvaluationServiceTests
{
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid SourceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
    private static readonly Guid TargetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4");
    private static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5");

    /// <summary>
    /// Verifies schema-supported evidence combinations are classified conservatively.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvaluationCases))]
    public async Task Evaluate_WhenGivenSupportedEvidence_ClassifiesExpectedItem(
        ReconciliationItemRecord item,
        string expectedStatus,
        string expectedMatchStatus)
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>()).Returns(item);
        repository.SaveEvaluationAsync(
                Arg.Any<EvaluateReconciliationItemCommand>(),
                Arg.Any<ReconciliationEvaluationDecision>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var decision = call.ArgAt<ReconciliationEvaluationDecision>(1);
                return Evaluation(item, decision);
            });
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.EvaluateAsync(
            new EvaluateReconciliationItemCommand(ItemId, null, null, CorrelationId),
            CancellationToken.None);

        result.ItemStatus.Should().Be(expectedStatus);
        result.MatchStatus.Should().Be(expectedMatchStatus);
        await repository.Received(1).SaveEvaluationAsync(
            Arg.Any<EvaluateReconciliationItemCommand>(),
            Arg.Is<ReconciliationEvaluationDecision>(decision =>
                decision.ItemStatus == expectedStatus &&
                decision.MatchStatus == expectedMatchStatus),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies current evaluation readback does not mutate the item.
    /// </summary>
    [Fact]
    public async Task ReadEvaluation_WhenKnown_ReturnsCurrentStateWithoutSaving()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns(Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 100m) with
            {
                ItemStatus = "MATCHED",
                MatchStatus = "MATCH",
                VarianceAmount = 0m
            });
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.ReadEvaluationAsync(new ReadReconciliationItemEvaluationQuery(ItemId), CancellationToken.None);

        result.MatchStatus.Should().Be("MATCH");
        await repository.DidNotReceive().SaveEvaluationAsync(
            Arg.Any<EvaluateReconciliationItemCommand>(),
            Arg.Any<ReconciliationEvaluationDecision>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies unknown items flow through the repository as deterministic not-found errors.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenItemMissing_PropagatesNotFound()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns<Task<ReconciliationItemRecord>>(_ => throw new ReconciliationItemNotFoundException(ItemId));
        var sut = new ReconciliationEvaluationService(repository);

        var act = () => sut.EvaluateAsync(
            new EvaluateReconciliationItemCommand(ItemId, null, null, CorrelationId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ReconciliationItemNotFoundException>();
    }

    /// <summary>
    /// Verifies evaluation SQL remains bounded to reconciliation item evidence fields.
    /// </summary>
    [Fact]
    public void RepositorySql_DoesNotMutatePaymentProviderOrExitTruth()
    {
        var repository = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            "ReconciliationEvaluationRepository.cs");

        Assert.Contains("UPDATE reconciliation.reconciliation_items", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE payments.provider_outcomes", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE gates.gate_authorization_consumptions", repository, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Evaluation test cases for conservative matching.
    /// </summary>
    public static IEnumerable<object[]> EvaluationCases()
    {
        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: null, paymentConfirmationId: TargetId, expected: 100m, actual: 100m),
            "EXCEPTION",
            "MISSING_SOURCE"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: null, expected: 100m, actual: 100m),
            "EXCEPTION",
            "MISSING_TARGET"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 100m),
            "MATCHED",
            "MATCH"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 110m),
            "MISMATCHED",
            "AMOUNT_MISMATCH"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: null, actual: 110m),
            "PENDING",
            "INCONCLUSIVE"
        ];
    }

    private static ReconciliationItemEvaluationRecord Evaluation(
        ReconciliationItemRecord item,
        ReconciliationEvaluationDecision decision) =>
        new(
            item.ReconciliationItemId,
            item.ReconciliationRunId,
            item.ComparisonBasis,
            decision.ItemStatus,
            decision.MatchStatus,
            decision.EvaluationClassification,
            decision.EvaluationReason,
            item.ExpectedAmount,
            item.ActualAmount,
            decision.VarianceAmount,
            decision.ExceptionReasonCode,
            ExceptionCreatedOrUpdated: false,
            "deferred",
            DateTimeOffset.UtcNow,
            CorrelationId);

    private static ReconciliationItemRecord Item(
        string comparisonBasis,
        Guid? providerOutcomeId = null,
        Guid? paymentConfirmationId = null,
        decimal? expected = null,
        decimal? actual = null) =>
        new(
            ItemId,
            RunId,
            MopsTransactionRecordId: null,
            ManualGateLogId: null,
            PaymentAttemptId: null,
            PaymentConfirmationId: paymentConfirmationId,
            ProviderOutcomeId: providerOutcomeId,
            TargetEntityType: null,
            TargetEntityId: null,
            ComparisonBasis: comparisonBasis,
            ItemStatus: "PENDING",
            MatchStatus: "NOT_EVALUATED",
            ExpectedAmount: expected,
            ActualAmount: actual,
            CurrencyCode: "PHP",
            VarianceAmount: null,
            ExceptionReasonCode: null,
            ResolvedAt: null,
            ResolvedByUserId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            CorrelationId: null);

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
}
