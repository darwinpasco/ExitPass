using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Infrastructure.Common;

/// <summary>
/// Default system clock implementation for Central PMS.
///
/// BRD:
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 11 Cross-Cutting Concerns
///
/// Invariants Enforced:
/// - Application services obtain time through an injectable clock abstraction
/// - Time-dependent workflows remain testable and deterministic at the application boundary
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
