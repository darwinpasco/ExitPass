using ExitPass.VendorPmsAdapter.Contracts.Routing;

namespace ExitPass.VendorPmsAdapter.Contracts.Projection;

/// <summary>Provider-neutral request for a bounded page sequence of passageway records.</summary>
public sealed record VendorPassagewaySyncRequest(
    VendorAdapterRequestContext Context,
    string ParkingLotIndexCode,
    DateTimeOffset BeginTime,
    DateTimeOffset EndTime,
    int PageSize,
    int MaxPages,
    Guid CorrelationId);

/// <summary>Provider-neutral passageway fact normalized inside the Site Integration Adapter.</summary>
public sealed record VendorPassagewayRecordDto(
    string VendorRecordReference,
    string? CardReference,
    string? PlateNumber,
    string ParkingLotReference,
    string? ParkingLotName,
    string? PassagewayReference,
    string? PassagewayName,
    string? LaneReference,
    string? LaneName,
    string? Direction,
    DateTimeOffset? EntryTime,
    DateTimeOffset? ExitTime,
    string? AllowType,
    string? AllowResult,
    string SourceApi,
    string SourcePayloadHash,
    DateTimeOffset SourceTimestamp);

/// <summary>Provider-neutral passageway synchronization response.</summary>
public sealed record VendorPassagewaySyncResponse(
    bool Succeeded,
    int PagesPulled,
    int RecordsSeen,
    int RecordsAccepted,
    int RecordsSkipped,
    IReadOnlyList<VendorPassagewayRecordDto> Records,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId,
    VendorAdapterResponseContext? AdapterContext);
