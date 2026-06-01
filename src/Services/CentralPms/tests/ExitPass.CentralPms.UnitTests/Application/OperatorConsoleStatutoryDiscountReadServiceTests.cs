using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleStatutoryDiscountReadServiceTests
{
    [Fact]
    public async Task ListDraftsAsync_NormalizesPagingAndFilters()
    {
        var repository = Substitute.For<IOperatorConsoleStatutoryDiscountReadRepository>();
        repository.ListDraftsAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftQueueQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftQueueResult(
                Array.Empty<OperatorConsoleStatutoryDiscountDraftQueueItemResult>(),
                Page: 1,
                PageSize: 25,
                HasMore: false,
                Guid.Parse("8b000000-0000-0000-0000-000000000001")));
        var sut = new OperatorConsoleStatutoryDiscountReadService(repository);
        var correlationId = Guid.Parse("8b000000-0000-0000-0000-000000000001");

        await sut.ListDraftsAsync(
            new OperatorConsoleStatutoryDiscountDraftQueueQuery(
                " requested ",
                " pwd ",
                SiteId: null,
                CreatedFrom: null,
                CreatedTo: null,
                Page: -1,
                PageSize: 500,
                correlationId),
            CancellationToken.None);

        await repository.Received(1).ListDraftsAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDraftQueueQuery>(query =>
                query.Status == "REQUESTED" &&
                query.EntitlementType == "PWD" &&
                query.Page == 1 &&
                query.PageSize == 100 &&
                query.CorrelationId == correlationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListDraftsAsync_WhenCreatedRangeInvalid_ThrowsValidationError()
    {
        var sut = new OperatorConsoleStatutoryDiscountReadService(
            Substitute.For<IOperatorConsoleStatutoryDiscountReadRepository>());

        var act = () => sut.ListDraftsAsync(
            new OperatorConsoleStatutoryDiscountDraftQueueQuery(
                Status: null,
                EntitlementType: null,
                SiteId: null,
                CreatedFrom: DateTimeOffset.Parse("2026-06-02T00:00:00+08:00"),
                CreatedTo: DateTimeOffset.Parse("2026-06-01T00:00:00+08:00"),
                Page: 1,
                PageSize: 25,
                Guid.Parse("8b000000-0000-0000-0000-000000000002")),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*createdFrom*");
    }

    [Fact]
    public async Task GetDraftAsync_WhenDraftIdMissing_ThrowsValidationError()
    {
        var sut = new OperatorConsoleStatutoryDiscountReadService(
            Substitute.For<IOperatorConsoleStatutoryDiscountReadRepository>());

        var act = () => sut.GetDraftAsync(
            new OperatorConsoleStatutoryDiscountDraftDetailQuery(
                Guid.Empty,
                Guid.Parse("8b000000-0000-0000-0000-000000000003")),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*DraftId*");
    }
}
