using ExitPass.CentralPms.Application.Reconciliation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests reconciliation run and item application rules.
/// </summary>
public sealed class ReconciliationRunItemServiceTests
{
    private static readonly Guid RunId = Guid.Parse("66666666-6666-6666-6666-666666666661");
    private static readonly Guid ItemId = Guid.Parse("66666666-6666-6666-6666-666666666662");
    private static readonly Guid CorrelationId = Guid.Parse("66666666-6666-6666-6666-666666666663");

    /// <summary>
    /// Verifies BRD v1.2 9.16 and SDD v1.2 10 run creation delegates only through the reconciliation repository boundary.
    /// </summary>
    [Fact]
    public async Task CreateRun_WhenValid_DelegatesNormalizedCommand()
    {
        var repository = Substitute.For<IReconciliationRunItemRepository>();
        var expected = new ReconciliationRunCreateResult(
            RunId,
            "RUN-001",
            "PAYMENT_PROVIDER_RECONCILIATION",
            "STARTED",
            "TIME_WINDOW",
            0,
            ItemGenerationPerformed: false,
            "deferred",
            CorrelationId);
        repository.CreateRunAsync(Arg.Any<CreateReconciliationRunCommand>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var sut = new ReconciliationRunItemService(repository);

        var result = await sut.CreateRunAsync(
            ValidCommand() with
            {
                RunType = "payment_provider_reconciliation",
                ScopeType = "time_window",
                RunStatus = "started",
                RunCode = " RUN-001 ",
                SourceBatchRef = " BATCH-001 "
            },
            CancellationToken.None);

        result.Should().Be(expected);
        await repository.Received(1).CreateRunAsync(
            Arg.Is<CreateReconciliationRunCommand>(command =>
                command.RunType == "PAYMENT_PROVIDER_RECONCILIATION" &&
                command.ScopeType == "TIME_WINDOW" &&
                command.RunStatus == "STARTED" &&
                command.RunCode == "RUN-001" &&
                command.SourceBatchRef == "BATCH-001" &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies invalid enum values are rejected before persistence.
    /// </summary>
    [Fact]
    public async Task CreateRun_WhenRunTypeUnsupported_Throws()
    {
        var sut = new ReconciliationRunItemService(Substitute.For<IReconciliationRunItemRepository>());

        var act = () => sut.CreateRunAsync(ValidCommand() with { RunType = "NOT_A_RUN_TYPE" }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*RunType must be one of*");
    }

    /// <summary>
    /// Verifies window boundaries are validated before persistence.
    /// </summary>
    [Fact]
    public async Task CreateRun_WhenWindowEndBeforeStart_Throws()
    {
        var sut = new ReconciliationRunItemService(Substitute.For<IReconciliationRunItemRepository>());

        var act = () => sut.CreateRunAsync(
            ValidCommand() with
            {
                WindowStartAt = DateTimeOffset.Parse("2026-05-25T10:00:00Z"),
                WindowEndAt = DateTimeOffset.Parse("2026-05-25T09:00:00Z")
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*WindowEndAt*");
    }

    /// <summary>
    /// Verifies run and item read paths delegate and clamp list limits.
    /// </summary>
    [Fact]
    public async Task Reads_WhenValid_DelegateToRepository()
    {
        var repository = Substitute.For<IReconciliationRunItemRepository>();
        repository.ReadRunAsync(Arg.Any<ReadReconciliationRunQuery>(), Arg.Any<CancellationToken>())
            .Returns(RunRecord());
        repository.ListRunItemsAsync(Arg.Any<ListReconciliationRunItemsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationItemRecord>());
        repository.ReadItemAsync(Arg.Any<ReadReconciliationItemQuery>(), Arg.Any<CancellationToken>())
            .Returns(ItemRecord());
        var sut = new ReconciliationRunItemService(repository);

        await sut.ReadRunAsync(new ReadReconciliationRunQuery(RunId), CancellationToken.None);
        await sut.ListRunItemsAsync(new ListReconciliationRunItemsQuery(RunId, 999), CancellationToken.None);
        await sut.ReadItemAsync(new ReadReconciliationItemQuery(ItemId), CancellationToken.None);

        await repository.Received(1).ReadRunAsync(
            Arg.Is<ReadReconciliationRunQuery>(query => query.ReconciliationRunId == RunId),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ListRunItemsAsync(
            Arg.Is<ListReconciliationRunItemsQuery>(query => query.ReconciliationRunId == RunId && query.Limit == 500),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ReadItemAsync(
            Arg.Is<ReadReconciliationItemQuery>(query => query.ReconciliationItemId == ItemId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies repository SQL does not write payment, provider, exit, or gate truth.
    /// </summary>
    [Fact]
    public void RepositorySql_DoesNotMutatePaymentTruth()
    {
        var repository = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            "ReconciliationRunItemRepository.cs");

        Assert.Contains("INSERT INTO reconciliation.reconciliation_runs", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE payments.provider_outcomes", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO gates.gate_authorization_consumptions", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static CreateReconciliationRunCommand ValidCommand() =>
        new(
            "PAYMENT_PROVIDER_RECONCILIATION",
            "TIME_WINDOW",
            RunCode: null,
            "STARTED",
            SiteGroupId: null,
            SiteId: null,
            IncidentRecordId: null,
            PaymentRailId: null,
            VendorSystemId: null,
            SourceBatchRef: null,
            WindowStartAt: DateTimeOffset.Parse("2026-05-25T00:00:00Z"),
            WindowEndAt: DateTimeOffset.Parse("2026-05-25T23:59:59Z"),
            GenerateItems: false,
            ActorUserId: null,
            ServiceIdentityId: null,
            CorrelationId);

    private static ReconciliationRunDetailRecord RunRecord() =>
        new(
            RunId,
            "RUN-001",
            "PAYMENT_PROVIDER_RECONCILIATION",
            "STARTED",
            "TIME_WINDOW",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            CorrelationId);

    private static ReconciliationItemRecord ItemRecord() =>
        new(
            ItemId,
            RunId,
            null,
            null,
            null,
            null,
            null,
            "DEV_TEST",
            null,
            "PROVIDER_TO_CORE",
            "PENDING",
            "NOT_EVALUATED",
            null,
            null,
            "PHP",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
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
