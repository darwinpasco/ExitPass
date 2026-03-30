using ExitPass.CentralPms.Domain.PaymentAttempts;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Domain;

public sealed class PaymentAttemptTests
{
    [Fact]
    public void Create_sets_initial_state_to_initiated()
    {
        var attempt = PaymentAttempt.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentProvider.GCash,
            "idem-001",
            100m,
            0m,
            0m,
            100m,
            100m,
            "PHP",
            "TVR-001",
            DateTimeOffset.UtcNow,
            "test");

        attempt.AttemptStatus.Should().Be(PaymentAttemptStatus.Initiated);
    }

    [Fact]
    public void IsActive_returns_true_for_initiated()
    {
        var attempt = PaymentAttempt.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentProvider.GCash,
            "idem-001",
            100m,
            0m,
            0m,
            100m,
            100m,
            "PHP",
            "TVR-001",
            DateTimeOffset.UtcNow,
            "test");

        attempt.IsActive().Should().BeTrue();
    }

    [Fact]
    public void Cannot_transition_from_confirmed_to_failed()
    {
        var attempt = PaymentAttempt.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentProvider.GCash,
            "idem-001",
            100m,
            0m,
            0m,
            100m,
            100m,
            "PHP",
            "TVR-001",
            DateTimeOffset.UtcNow,
            "test");

        attempt.MarkConfirmed(DateTimeOffset.UtcNow, "test");

        var act = () => attempt.MarkFailed(DateTimeOffset.UtcNow, "test");

        act.Should().Throw<InvalidOperationException>();
    }
}