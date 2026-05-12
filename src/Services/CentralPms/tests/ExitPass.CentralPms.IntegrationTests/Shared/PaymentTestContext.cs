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
/// - Each test owns its own canonical records.
/// - Integration tests must not share transactional identities.
/// - Database identity fields must use UUID values.
/// - Business-readable reference fields must use codes, not database IDs.
/// </summary>
public sealed record PaymentTestContext(
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid CorrelationId,
    Guid RequestedByUserId,
    Guid SiteGroupId,
    Guid SiteId,
    string SiteGroupCode,
    string SiteCode,
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
            SiteGroupId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SiteGroupCode: $"SG-TEST-{suffix}",
            SiteCode: $"SITE-TEST-{suffix}",
            VendorSystemCode: $"VENDOR-TEST-{suffix}");
    }
}
