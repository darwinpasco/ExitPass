using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using Xunit;
using Xunit.Abstractions;

namespace ExitPass.VendorPmsAdapter.IntegrationTests.HikCentral;

/// <summary>
/// Gated local read-only HikCentral ticket discovery tests.
/// </summary>
public sealed class HikCentralTicketOnlyReadonlyDiscoveryLiveTests
{
    private static readonly string[] PhysicalTicketNumbers =
    [
        "3518855073102",
        "3518855085105"
    ];

    private readonly ITestOutputHelper _output;

    public HikCentralTicketOnlyReadonlyDiscoveryLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [HikCentralTicketDiscoveryFact]
    public async Task HikCentralTicketOnlyDiscovery_LocalVersionParkingLotsAndTickets_ReturnSanitizedDiagnostics()
    {
        var settings = HikCentralTicketDiscoveryEnvironment.GetRequired();
        using var httpClient = new HttpClient
        {
            BaseAddress = settings.BaseUri,
            Timeout = TimeSpan.FromSeconds(20)
        };
        var client = new HikCentralTicketDiscoveryClient(
            httpClient,
            new HikCentralRequestSigner(new HikCentralCredentialOptions(settings.AppKey, settings.AppSecret)));

        _output.WriteLine(
            string.Join(
                " | ",
                "label=timeWindow",
                $"source={settings.TimeWindowSource}",
                $"beginTimeSent={HikCentralParkingTimeFormatter.Format(settings.BeginTime)}",
                $"endTimeSent={HikCentralParkingTimeFormatter.Format(settings.EndTime)}",
                $"cameraIndexCode={Safe(settings.CameraIndexCode)}",
                $"floorIndexCode={Safe(settings.FloorIndexCode)}"));

        var version = await client.GetVersionAsync(CancellationToken.None);
        _output.WriteLine(FormatEndpoint("version", version.ToSummary()));

        var parkingLots = await client.ListParkingLotsAsync(CancellationToken.None);
        _output.WriteLine(FormatEndpoint("parkingLots", parkingLots.ToSummary()));

        foreach (var ticketNumber in PhysicalTicketNumbers)
        {
            var result = await client.DiscoverTicketAsync(
                new HikCentralTicketDiscoveryRequest(
                    ticketNumber,
                    settings.ParkingLotIndexCode,
                    settings.BeginTime,
                    settings.EndTime,
                    settings.CameraIndexCode,
                    settings.FloorIndexCode,
                    PhysicalTicketNumbers.Append("3518835144105").ToArray()),
                CancellationToken.None);

            _output.WriteLine(FormatTicketResult(result));
            foreach (var summary in result.EndpointSummaries)
            {
                _output.WriteLine(FormatEndpoint($"ticket:{ticketNumber}", summary));
                foreach (var sample in summary.SanitizedRecordSamples.Select((value, index) => (value, index)))
                {
                    _output.WriteLine(
                        string.Join(
                            " | ",
                            $"label=ticket:{ticketNumber}",
                            $"endpoint={summary.EndpointPath}",
                            $"sampleIndex={sample.index + 1}",
                            $"sample={sample.value}"));
                }
            }

            Assert.DoesNotContain(
                "/artemis/api/vehicle/v1/parkingfee/confirm",
                result.EndpointSummaries.Select(summary => summary.EndpointPath),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string FormatEndpoint(string label, HikCentralEndpointSummary summary) =>
        string.Join(
            " | ",
            $"label={label}",
            $"endpoint={summary.EndpointPath}",
            $"httpStatus={summary.HttpStatusCode}",
            $"hikCentralCode={Safe(summary.HikCentralCode)}",
            $"hikCentralMsg={Safe(summary.HikCentralMessage)}",
            $"itemCount={summary.ItemCount}",
            $"outcome={summary.Outcome}",
            $"requestShape={Safe(summary.RequestShape)}",
            $"ticketMatched={summary.TicketMatched}",
            $"matchedTicketValue={Safe(summary.MatchedTicketValue)}",
            $"matchedTicketField={Safe(summary.MatchedTicketField)}",
            $"observedOtherLookupValues={FormatList(summary.ObservedOtherLookupValues)}");

    private static string FormatTicketResult(HikCentralTicketDiscoveryResult result) =>
        string.Join(
            " | ",
            $"ticket={result.TicketNumber}",
            $"cardNumAccepted={result.CardNumAccepted}",
            $"hikCentralCode={Safe(result.HikCentralCode)}",
            $"hikCentralMsg={Safe(result.HikCentralMessage)}",
            $"identifierType={Safe(result.DiscoveredIdentifierType)}",
            $"identifierValue={Safe(result.DiscoveredIdentifierValue)}",
            $"endpointSource={Safe(result.EndpointSource)}",
            $"parkingLotIndexCode={result.ParkingLotIndexCode}",
            $"passagewayIndexCode={Safe(result.PassagewayIndexCode)}",
            $"laneIndexCode={Safe(result.LaneIndexCode)}",
            $"plateLicense={Safe(result.PlateLicense)}",
            $"fee={Safe(result.Fee)}",
            $"parkingInTime={Safe(result.ParkingInTime)}",
            $"parkingDuration={Safe(result.ParkingDuration)}",
            $"conclusion={result.Conclusion}");

    private static string Safe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "n/a" : value;

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "n/a" : string.Join(",", values);
}

internal sealed class HikCentralTicketDiscoveryFactAttribute : FactAttribute
{
    public HikCentralTicketDiscoveryFactAttribute()
    {
        Skip = HikCentralTicketDiscoveryEnvironment.Evaluate(Environment.GetEnvironmentVariable).SkipReason;
    }
}

internal static class HikCentralTicketDiscoveryEnvironment
{
    public const string RunFlagName = "EXITPASS_RUN_HIKCENTRAL_TICKET_DISCOVERY";
    public const string BaseUrlName = "HIKCENTRAL_BASE_URL";
    public const string AppKeyName = "HIKCENTRAL_APP_KEY";
    public const string AppSecretName = "HIKCENTRAL_APP_SECRET";
    public const string ParkingLotIndexCodeName = "HIKCENTRAL_TEST_PARKING_LOT_INDEX_CODE";
    public const string ConfirmPaymentEnabledName = "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED";
    public const string GateOpenAllowedName = "HIKCENTRAL_GATE_OPEN_ALLOWED";
    public const string BeginTimeName = "HIKCENTRAL_TICKET_DISCOVERY_BEGIN_TIME";
    public const string EndTimeName = "HIKCENTRAL_TICKET_DISCOVERY_END_TIME";
    public const string CameraIndexCodeName = "HIKCENTRAL_TEST_CAMERA_INDEX_CODE";
    public const string FloorIndexCodeName = "HIKCENTRAL_TEST_FLOOR_INDEX_CODE";

    public static HikCentralTicketDiscoveryGateResult Evaluate(Func<string, string?> getEnvironmentVariable)
    {
        if (!string.Equals(getEnvironmentVariable(RunFlagName), "true", StringComparison.OrdinalIgnoreCase))
        {
            return HikCentralTicketDiscoveryGateResult.Skipped(
                $"Set {RunFlagName}=true to run local read-only HikCentral ticket discovery.");
        }

        var missing = new[]
            {
                BaseUrlName,
                AppKeyName,
                AppSecretName,
                ParkingLotIndexCodeName
            }
            .Where(name => string.IsNullOrWhiteSpace(getEnvironmentVariable(name)))
            .ToArray();

        if (missing.Length > 0)
        {
            return HikCentralTicketDiscoveryGateResult.Skipped(
                $"Missing required HikCentral ticket discovery environment variables: {string.Join(", ", missing)}.");
        }

        if (!string.Equals(getEnvironmentVariable(ConfirmPaymentEnabledName), "false", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(getEnvironmentVariable(GateOpenAllowedName), "false", StringComparison.OrdinalIgnoreCase))
        {
            return HikCentralTicketDiscoveryGateResult.Skipped(
                $"{ConfirmPaymentEnabledName} and {GateOpenAllowedName} must both be false for read-only discovery.");
        }

        if (!Uri.TryCreate(getEnvironmentVariable(BaseUrlName), UriKind.Absolute, out var baseUri) ||
            !baseUri.IsLoopback)
        {
            return HikCentralTicketDiscoveryGateResult.Skipped($"{BaseUrlName} must be an absolute loopback URL.");
        }

        return HikCentralTicketDiscoveryGateResult.Runnable();
    }

    public static HikCentralTicketDiscoverySettings GetRequired()
    {
        var gate = Evaluate(Environment.GetEnvironmentVariable);
        if (!gate.CanRun)
        {
            throw new InvalidOperationException(gate.SkipReason);
        }

        var beginTimeValue = Environment.GetEnvironmentVariable(BeginTimeName);
        var endTimeValue = Environment.GetEnvironmentVariable(EndTimeName);
        var now = DateTimeOffset.Now;
        var defaultBeginTime = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var defaultEndTime = defaultBeginTime.AddDays(1).AddSeconds(-1);
        var beginTime = ParseOptionalDate(beginTimeValue) ?? defaultBeginTime;
        var endTime = ParseOptionalDate(endTimeValue) ?? defaultEndTime;
        var timeWindowSource = string.IsNullOrWhiteSpace(beginTimeValue) && string.IsNullOrWhiteSpace(endTimeValue)
            ? "default-local-day"
            : "environment";

        return new HikCentralTicketDiscoverySettings(
            new Uri(Environment.GetEnvironmentVariable(BaseUrlName)!, UriKind.Absolute),
            Environment.GetEnvironmentVariable(AppKeyName)!,
            Environment.GetEnvironmentVariable(AppSecretName)!,
            Environment.GetEnvironmentVariable(ParkingLotIndexCodeName)!,
            beginTime,
            endTime,
            timeWindowSource,
            Environment.GetEnvironmentVariable(CameraIndexCodeName),
            Environment.GetEnvironmentVariable(FloorIndexCodeName));
    }

    private static DateTimeOffset? ParseOptionalDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}

internal sealed record HikCentralTicketDiscoveryGateResult(bool CanRun, string? SkipReason)
{
    public static HikCentralTicketDiscoveryGateResult Runnable() => new(true, null);

    public static HikCentralTicketDiscoveryGateResult Skipped(string reason) => new(false, reason);
}

internal sealed record HikCentralTicketDiscoverySettings(
    Uri BaseUri,
    string AppKey,
    string AppSecret,
    string ParkingLotIndexCode,
    DateTimeOffset BeginTime,
    DateTimeOffset EndTime,
    string TimeWindowSource,
    string? CameraIndexCode,
    string? FloorIndexCode);
