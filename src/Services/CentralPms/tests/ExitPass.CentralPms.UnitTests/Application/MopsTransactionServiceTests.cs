using ExitPass.CentralPms.Application.Reconciliation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests MoPS continuity import application rules.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
///
/// ExitPass v1.2 Invariants Enforced:
/// - MoPS imports are reconciliation evidence only and never become payment truth.
/// </summary>
public sealed class MopsTransactionServiceTests
{
    private static readonly Guid SiteId = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid CorrelationId = Guid.Parse("44444444-4444-4444-4444-444444444442");

    /// <summary>
    /// Verifies valid MoPS imports are normalized and delegated to the reconciliation repository boundary.
    /// </summary>
    [Fact]
    public async Task Import_WhenValid_DelegatesNormalizedCommand()
    {
        var repository = Substitute.For<IMopsTransactionRepository>();
        var expected = new MopsImportResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IMPORTED",
            "MOPS-TEST",
            WasDuplicate: false,
            CorrelationId);
        repository.ImportAsync(Arg.Any<ImportMopsTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var sut = new MopsTransactionService(repository);

        var result = await sut.ImportAsync(
            ValidCommand() with
            {
                SourceSystemCode = " mops ",
                SourceTransactionRef = "  tx-001 ",
                CurrencyCode = " php ",
                ContinuityReasonCode = " manual_gate "
            },
            CancellationToken.None);

        result.Should().Be(expected);
        await repository.Received(1).ImportAsync(
            Arg.Is<ImportMopsTransactionCommand>(command =>
                command.SourceSystemCode == "MOPS" &&
                command.SourceTransactionRef == "tx-001" &&
                command.CurrencyCode == "PHP" &&
                command.ContinuityReasonCode == "MANUAL_GATE" &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies v1.2 idempotency inputs are required before persistence.
    /// </summary>
    [Fact]
    public async Task Import_WhenNaturalKeyMissing_Throws()
    {
        var sut = new MopsTransactionService(Substitute.For<IMopsTransactionRepository>());

        var act = () => sut.ImportAsync(
            ValidCommand() with
            {
                SourceTransactionRef = null,
                SourceBatchRef = "batch",
                CollectionReference = null
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*SourceTransactionRef*SourceBatchRef*CollectionReference*");
    }

    /// <summary>
    /// Verifies imported continuity evidence cannot carry invalid financial values.
    /// </summary>
    [Fact]
    public async Task Import_WhenAmountNegative_Throws()
    {
        var sut = new MopsTransactionService(Substitute.For<IMopsTransactionRepository>());

        var act = () => sut.ImportAsync(ValidCommand() with { Amount = -1m }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Amount must be non-negative*");
    }

    /// <summary>
    /// Verifies read/list queries remain bounded and delegated.
    /// </summary>
    [Fact]
    public async Task Reads_WhenValid_DelegateToRepository()
    {
        var repository = Substitute.For<IMopsTransactionRepository>();
        repository.ListAsync(Arg.Any<ListMopsTransactionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MopsTransactionRecord>());
        repository.ReadAsync(Arg.Any<ReadMopsTransactionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new MopsTransactionRecord(
                Guid.NewGuid(),
                null,
                null,
                SiteId,
                null,
                null,
                null,
                null,
                null,
                "MOPS",
                "TX-001",
                null,
                null,
                "PHP",
                100m,
                "QRPH",
                "MANUAL_GATE",
                "IMPORTED",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                CorrelationId));
        var sut = new MopsTransactionService(repository);

        await sut.ListAsync(new ListMopsTransactionsQuery(500, SiteId, "mops"), CancellationToken.None);
        await sut.ReadAsync(new ReadMopsTransactionQuery(Guid.NewGuid()), CancellationToken.None);

        await repository.Received(1).ListAsync(
            Arg.Is<ListMopsTransactionsQuery>(query => query.Limit == 100 && query.SiteId == SiteId),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ReadAsync(
            Arg.Any<ReadMopsTransactionQuery>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the MoPS repository SQL does not mutate payment, provider, exit, or gate state.
    /// </summary>
    [Fact]
    public void RepositorySql_StaysWithinReconciliationEvidenceBoundary()
    {
        var repository = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            "MopsTransactionRepository.cs");

        repository.Should().Contain("reconciliation.mops_transaction_records");
        repository.Should().Contain("reconciliation.reconciliation_items");
        repository.Should().Contain("reconciliation.reconciliation_runs");
        Assert.DoesNotContain("INSERT INTO core.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO payments.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO payments.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO gates.gate_authorization_consumptions", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static ImportMopsTransactionCommand ValidCommand() =>
        new(
            SiteId,
            SiteGroupId: null,
            PaymentRailId: null,
            VendorSystemId: null,
            ParkingSessionId: null,
            LaneId: null,
            SourceSystemCode: "MOPS",
            SourceTransactionRef: "TX-001",
            SourceBatchRef: null,
            CollectionReference: null,
            CurrencyCode: "PHP",
            Amount: 100m,
            PaymentMethodLabel: "QRPH",
            ContinuityReasonCode: "MANUAL_GATE",
            CapturedAt: DateTimeOffset.UtcNow,
            EvidenceRef: null,
            EvidenceHash: null,
            ActorUserId: null,
            ImportedByServiceIdentityId: null,
            CorrelationId);

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
