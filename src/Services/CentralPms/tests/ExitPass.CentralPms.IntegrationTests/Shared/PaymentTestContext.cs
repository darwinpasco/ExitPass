namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Per-test data context for DB-backed payment integration tests.
///
/// BRD:
/// - 10.7.13 End-to-End Traceability Invariant
///
/// SDD:
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - Each test owns its own canonical records
/// - Integration tests must not share transactional identities
/// </summary>
public sealed record PaymentTestContext(
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid CorrelationId,
    Guid RequestedByUserId,
    string SiteGroupId,
    string SiteId,
    string VendorSystemCode)
{
    /// <summary>
    /// Creates a unique per-test data context for the supplied scenario.
    /// </summary>
    public static PaymentTestContext Create(string scenarioName)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return new PaymentTestContext(
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            RequestedByUserId: Guid.NewGuid(),
            SiteGroupId: $"SG-TEST-{suffix}",
            SiteId: $"SITE-TEST-{suffix}",
            VendorSystemCode: $"VENDOR-TEST-{suffix}");
    }
}
