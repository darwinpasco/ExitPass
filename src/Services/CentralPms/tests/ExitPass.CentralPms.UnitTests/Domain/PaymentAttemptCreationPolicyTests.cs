using ExitPass.CentralPms.Domain.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Domain;

public sealed class PaymentAttemptCreationPolicyTests
{
    [Fact]
    public void Accepts_valid_idempotent_replay()
    {
        var policy = new PaymentAttemptCreationPolicy();

        var result = policy.IsValidIdempotentReplay(new IdempotentReplayComparison
        {
            RequestedParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RequestedTariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            RequestedProviderCode = PaymentProvider.GCash.Code,
            PersistedIdempotencyKey = "idem-001",
            PersistedParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PersistedTariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            PersistedProviderCode = PaymentProvider.GCash.Code
        });

        result.Should().BeTrue();
    }
}