namespace ExitPass.CentralPms.Domain.Common;

/// <summary>
/// Provides the current UTC time for application services and tests.
///
/// BRD:
/// - 10.8 Reliability and Fault Tolerance
///
/// SDD:
/// - Cross-cutting application support
///
/// Invariants Enforced:
/// - Time-dependent behavior must use an injectable clock source.
/// </summary>
public interface ISystemClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
