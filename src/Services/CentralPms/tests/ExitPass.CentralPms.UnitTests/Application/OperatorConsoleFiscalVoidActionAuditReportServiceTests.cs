using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests read-only fiscal void action audit review service behavior.
/// </summary>
public sealed class OperatorConsoleFiscalVoidActionAuditReportServiceTests
{
    private static readonly Guid CorrelationId = Guid.Parse("6d000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task ListAsync_DefaultsPaginationAndNormalizesFilters()
    {
        var repository = new CapturingRepository();
        var sut = new OperatorConsoleFiscalVoidActionAuditReportService(repository);

        await sut.ListAsync(Query(limit: 0, offset: -10, resultClass: " conflict ", fiscalDocumentNumber: " SI-1 "), CancellationToken.None);

        repository.LastQuery.Should().NotBeNull();
        repository.LastQuery!.Limit.Should().Be(25);
        repository.LastQuery.Offset.Should().Be(0);
        repository.LastQuery.ResultClass.Should().Be("CONFLICT");
        repository.LastQuery.FiscalDocumentNumber.Should().Be("SI-1");
    }

    [Fact]
    public async Task ListAsync_CapsLimitAtTwoHundred()
    {
        var repository = new CapturingRepository();
        var sut = new OperatorConsoleFiscalVoidActionAuditReportService(repository);

        await sut.ListAsync(Query(limit: 500, offset: 5), CancellationToken.None);

        repository.LastQuery.Should().NotBeNull();
        repository.LastQuery!.Limit.Should().Be(200);
        repository.LastQuery.Offset.Should().Be(5);
    }

    [Fact]
    public async Task ListAsync_WhenDateRangeInvalid_Throws()
    {
        var repository = new CapturingRepository();
        var sut = new OperatorConsoleFiscalVoidActionAuditReportService(repository);

        var query = Query(
            from: DateTimeOffset.Parse("2026-07-08T10:00:00Z"),
            to: DateTimeOffset.Parse("2026-07-08T09:00:00Z"));

        await Assert.ThrowsAsync<ArgumentException>(() => sut.ListAsync(query, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_WhenResultClassUnsupported_Throws()
    {
        var repository = new CapturingRepository();
        var sut = new OperatorConsoleFiscalVoidActionAuditReportService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ListAsync(Query(resultClass: "RAW_PAYLOAD"), CancellationToken.None));
    }

    [Fact]
    public void RepositorySource_FiltersOnlyFiscalVoidActionAndAvoidsUnsafePayloadNames()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "OperatorConsole",
            "OperatorConsoleFiscalVoidActionAuditReportRepository.cs");

        source.Should().Contain("OperatorConsoleActionCodes.VoidFiscalDocument");
        source.Should().Contain("FISCAL_ISSUANCE_REFERENCE");
        source.Should().Contain("action_reason_code = @action_reason_code");
        source.Should().Contain("target_entity_type = @target_entity_type");
        source.Should().Contain("COUNT(*) OVER()");
        source.Should().Contain("ORDER BY performed_at DESC, operator_action_log_id DESC");
        Assert.DoesNotContain("raw_payload", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment_provider_payload", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("statutory_evidence", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack_trace", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer_pii", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FiscalVoidActionAuditService_DoesNotWireMutationDependencies()
    {
        var parameterTypes = typeof(OperatorConsoleFiscalVoidActionAuditReportService)
            .GetConstructors()
            .Should()
            .ContainSingle()
            .Which
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        parameterTypes.Should().BeEquivalentTo([nameof(IOperatorConsoleFiscalVoidActionAuditReportRepository)]);
        parameterTypes.Should().NotContain(typeName =>
            typeName.Contains("PosServer", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("VoidCommand", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("PaymentConfirmation", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Gate", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Refund", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Rendering", StringComparison.OrdinalIgnoreCase));
    }

    private static OperatorConsoleFiscalVoidActionAuditReportQuery Query(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 25,
        int offset = 0,
        string? resultClass = null,
        string? fiscalDocumentNumber = null) =>
        new(
            from,
            to,
            SiteId: null,
            SiteGroupId: null,
            OperatorUserId: null,
            FiscalIssuanceReferenceId: null,
            fiscalDocumentNumber,
            resultClass,
            CorrelationIdFilter: null,
            limit,
            offset,
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

    private sealed class CapturingRepository : IOperatorConsoleFiscalVoidActionAuditReportRepository
    {
        public OperatorConsoleFiscalVoidActionAuditReportQuery? LastQuery { get; private set; }

        public Task<OperatorConsoleFiscalVoidActionAuditReportResult> ListAsync(
            OperatorConsoleFiscalVoidActionAuditReportQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new OperatorConsoleFiscalVoidActionAuditReportResult(
                Array.Empty<OperatorConsoleFiscalVoidActionAuditReportItemResult>(),
                TotalCount: 0,
                query.Limit,
                query.Offset,
                query.CorrelationId));
        }
    }
}
