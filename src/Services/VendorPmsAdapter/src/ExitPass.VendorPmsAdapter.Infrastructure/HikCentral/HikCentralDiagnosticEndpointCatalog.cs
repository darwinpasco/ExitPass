namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Reference-backed safety classification for HikCentral diagnostic endpoints.
/// </summary>
public static class HikCentralDiagnosticEndpointCatalog
{
    public static readonly HikCentralDiagnosticEndpoint Version = new(
        HikCentralTicketDiscoveryClient.VersionPath,
        "POST",
        "Product version lookup.",
        "none",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "Read-only platform metadata.");

    public static readonly HikCentralDiagnosticEndpoint ParkingLotList = new(
        HikCentralTicketDiscoveryClient.ParkingLotListPath,
        "POST",
        "Parking lot list.",
        "none",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "Read-only parking resource metadata.");

    public static readonly HikCentralDiagnosticEndpoint ParkingFeeCalculate = new(
        HikCentralTicketDiscoveryClient.ParkingFeeCalculatePath,
        "POST",
        "Parking fee calculation.",
        "active vehicle lookup key such as cardNum or plateLicense",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "Reference describes calculation only; it does not confirm payment.");

    public static readonly HikCentralDiagnosticEndpoint PassagewayRecord = new(
        HikCentralTicketDiscoveryClient.PassagewayRecordPath,
        "POST",
        "Parking lot entry/exit passageway records.",
        "parkingLotIndexCode, beginTime, endTime",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "Read-only history search.");

    public static readonly HikCentralDiagnosticEndpoint ParkingSpaceRecord = new(
        HikCentralTicketDiscoveryClient.ParkingSpaceRecordPath,
        "POST",
        "Parking space records.",
        "parkingLotIndexCode, beginTime, endTime",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "Read-only history/status-adjacent search.");

    public static readonly HikCentralDiagnosticEndpoint CrossRecordsPage = new(
        HikCentralTicketDiscoveryClient.CrossRecordsPagePath,
        "POST",
        "PMS vehicle passage logs.",
        "cameraIndexCode, startTime, endTime",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "Read-only PMS passage log search; skipped unless cameraIndexCode is configured.");

    public static readonly HikCentralDiagnosticEndpoint FloorParkingSpaceStatus = new(
        HikCentralTicketDiscoveryClient.FloorParkingSpaceStatusPath,
        "POST",
        "Parking space status under a parking floor.",
        "floorIndexCode",
        IsReadOnly: true,
        SafeForLiveDiagnostics: true,
        "PDF reference lists this as a status lookup; skipped unless floorIndexCode is configured.");

    public static readonly HikCentralDiagnosticEndpoint ParkingFeeConfirm = new(
        HikCentralTicketDiscoveryClient.ParkingFeeConfirmPath,
        "POST",
        "Confirm parking fee payment.",
        "payment confirmation details",
        IsReadOnly: false,
        SafeForLiveDiagnostics: false,
        "Mutates payment/exit state and may allow exit.");

    public static IReadOnlyList<HikCentralDiagnosticEndpoint> LiveDiagnosticEndpoints { get; } =
    [
        Version,
        ParkingLotList,
        ParkingFeeCalculate,
        PassagewayRecord,
        ParkingSpaceRecord,
        CrossRecordsPage,
        FloorParkingSpaceStatus
    ];

    public static IReadOnlyList<HikCentralDiagnosticEndpoint> ReferenceInventory { get; } =
    [
        Version,
        ParkingLotList,
        ParkingFeeCalculate,
        PassagewayRecord,
        ParkingSpaceRecord,
        CrossRecordsPage,
        FloorParkingSpaceStatus,
        ParkingFeeConfirm
    ];
}

public sealed record HikCentralDiagnosticEndpoint(
    string Endpoint,
    string Method,
    string Description,
    string Requires,
    bool IsReadOnly,
    bool SafeForLiveDiagnostics,
    string Reason);
