using System.Net.Http;
using System.Reflection;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for the deterministic fake HikCentral gate action adapter boundary.
/// </summary>
public sealed class FakeHikCentralGateActionAdapterTests
{
    private static readonly DateTimeOffset RequestedAt = DateTimeOffset.Parse("2026-07-15T08:00:00Z");

    [Theory]
    [MemberData(nameof(ScenarioMappings))]
    public async Task ExecuteAsync_WhenScenarioIsConfigured_ReturnsDeterministicSafeOutcome(
        FakeHikCentralGateActionScenario scenario,
        string expectedOutcome,
        bool expectedRetryable,
        bool expectedFailureRecorded,
        bool expectedTimedOut,
        bool expectedVendorUnavailable,
        bool expectedTransportFailure,
        int? expectedHttpStatusCode)
    {
        var adapter = new FakeHikCentralGateActionAdapter(scenario);
        var request = ValidRequest();

        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(HikCentralGateActionConstants.VendorCode, result.VendorCode);
        Assert.Equal(HikCentralGateActionConstants.RequestMethod, result.RequestMethod);
        Assert.Equal(HikCentralGateActionConstants.OpenGateOperation, result.VendorOperation);
        Assert.Equal(request.TargetResourceCode, result.TargetResourceCode);
        Assert.Equal(expectedOutcome, result.ActionOutcome);
        Assert.Equal(expectedRetryable, result.Retryable);
        Assert.Equal(expectedFailureRecorded, result.FailureRecorded);
        Assert.Equal(expectedTimedOut, result.TimedOut);
        Assert.Equal(expectedVendorUnavailable, result.VendorUnavailable);
        Assert.Equal(expectedTransportFailure, result.TransportFailure);
        Assert.Equal(expectedHttpStatusCode, result.HttpStatusCode);
        Assert.Equal(request.CorrelationId, result.RequestCorrelationId);
        Assert.Equal($"FAKE-HIKCENTRAL-{request.CorrelationId:N}", result.VendorCorrelationId);
        Assert.Equal(request.RequestedAt, result.RequestedAt);
        Assert.Equal(result.RequestedAt.AddMilliseconds(result.DurationMs), result.RespondedAt);
        Assert.NotEqual("PHYSICAL_GATE_OPENED", result.ActionOutcome);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task ExecuteAsync_WhenRequestIsInvalid_RejectsDeterministically(
        HikCentralGateActionRequest request,
        string expectedErrorCode)
    {
        var adapter = new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success);

        var exception = await Assert.ThrowsAsync<HikCentralGateActionRejectedException>(
            () => adapter.ExecuteAsync(request, CancellationToken.None));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_DoesNotReturnSimulatedSuccess()
    {
        var adapter = new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(ValidRequest(), cancellation.Token));
    }

    [Fact]
    public void HikCentralGateActionResult_DoesNotExposeSecretBearingFields()
    {
        var forbiddenNameFragments = new[]
        {
            "AppKey",
            "AppSecret",
            "Credential",
            "Secret",
            "Signature",
            "Authorization",
            "RequestHeader",
            "ResponseHeader",
            "RequestBody",
            "ResponseBody",
            "PayloadJson"
        };

        var propertyNames = typeof(HikCentralGateActionResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        foreach (var propertyName in propertyNames)
        {
            Assert.DoesNotContain(
                forbiddenNameFragments,
                fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void HikCentralGateActionResult_DoesNotClaimPhysicalGateOpening()
    {
        var propertyNames = typeof(HikCentralGateActionResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Physical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Barrier", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Opened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FakeAdapter_DoesNotDeclareNetworkOrDatabaseDependencies()
    {
        var constructorParameters = typeof(FakeHikCentralGateActionAdapter)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        var fieldTypes = typeof(FakeHikCentralGateActionAdapter)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.DoesNotContain(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.DoesNotContain(constructorParameters, type => IsDatabaseType(type));
        Assert.DoesNotContain(fieldTypes, type => IsDatabaseType(type));
    }

    public static IEnumerable<object[]> ScenarioMappings()
    {
        yield return [
            FakeHikCentralGateActionScenario.Success,
            HikCentralGateActionConstants.OutcomeSucceeded,
            false,
            false,
            false,
            false,
            false,
            200
        ];
        yield return [
            FakeHikCentralGateActionScenario.RetryableFailure,
            HikCentralGateActionConstants.OutcomeRetryableFailure,
            true,
            true,
            false,
            false,
            false,
            503
        ];
        yield return [
            FakeHikCentralGateActionScenario.TerminalFailure,
            HikCentralGateActionConstants.OutcomeTerminalFailure,
            false,
            true,
            false,
            false,
            false,
            400
        ];
        yield return [
            FakeHikCentralGateActionScenario.Timeout,
            HikCentralGateActionConstants.OutcomeTimeout,
            true,
            true,
            true,
            false,
            false,
            null
        ];
        yield return [
            FakeHikCentralGateActionScenario.VendorUnavailable,
            HikCentralGateActionConstants.OutcomeVendorUnavailable,
            true,
            true,
            false,
            true,
            false,
            503
        ];
        yield return [
            FakeHikCentralGateActionScenario.TransportFailure,
            HikCentralGateActionConstants.OutcomeTransportFailure,
            true,
            true,
            false,
            false,
            true,
            null
        ];
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [ValidRequest() with { GateCommandId = Guid.Empty }, "GATE_COMMAND_ID_REQUIRED"];
        yield return [
            ValidRequest() with { GateAuthorizationConsumptionId = Guid.Empty },
            "GATE_AUTHORIZATION_CONSUMPTION_ID_REQUIRED"
        ];
        yield return [ValidRequest() with { ExitAuthorizationId = Guid.Empty }, "EXIT_AUTHORIZATION_ID_REQUIRED"];
        yield return [ValidRequest() with { GateDeviceId = Guid.Empty }, "GATE_DEVICE_ID_REQUIRED"];
        yield return [ValidRequest() with { VendorSystemId = Guid.Empty }, "VENDOR_SYSTEM_ID_REQUIRED"];
        yield return [ValidRequest() with { TargetResourceCode = " " }, "TARGET_RESOURCE_CODE_REQUIRED"];
        yield return [ValidRequest() with { VendorOperation = "DOOR_DO_CONTROL" }, "VENDOR_OPERATION_UNSUPPORTED"];
        yield return [ValidRequest() with { CorrelationId = Guid.Empty }, "CORRELATION_ID_REQUIRED"];
        yield return [ValidRequest() with { RequestedAt = default }, "REQUESTED_AT_REQUIRED"];
    }

    private static HikCentralGateActionRequest ValidRequest()
    {
        return new HikCentralGateActionRequest(
            GateCommandId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            GateAuthorizationConsumptionId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            ExitAuthorizationId: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            GateDeviceId: Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            VendorSystemId: Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
            SiteId: Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            LaneId: Guid.Parse("11111111-0000-0000-0000-000000000001"),
            TargetResourceCode: "EXIT-GATE-01",
            VendorOperation: HikCentralGateActionConstants.OpenGateOperation,
            CorrelationId: Guid.Parse("22222222-0000-0000-0000-000000000001"),
            RequestedAt);
    }

    private static bool IsDatabaseType(Type type) =>
        type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
        type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase);
}
