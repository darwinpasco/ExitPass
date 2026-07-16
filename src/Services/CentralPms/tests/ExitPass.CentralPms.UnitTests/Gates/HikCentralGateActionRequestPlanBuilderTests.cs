using System.Net.Http;
using System.Reflection;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Infrastructure.Gates;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Gates;

/// <summary>
/// Tests for deterministic, side-effect-free HikCentral gate action request planning.
/// </summary>
public sealed class HikCentralGateActionRequestPlanBuilderTests
{
    private const string ExpectedDoorControlPath = "/artemis/api/acs/v1/door/doControl";
    private const string ExpectedDoorControlBody = "{\"doorIndexCode\":\"EXIT-GATE-01\",\"controlType\":\"Open\"}";
    private const string ExpectedDoorControlSha256 = "390274ee3f8dcbafa2e5acb15bad512ad58ebd39462b472d49f2736da3c2028f";

    [Fact]
    public void Build_AccessControlDoorControlGuideSection591_ReturnsExpectedPlan()
    {
        var request = ValidRequest();
        var profile = HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE");
        var builder = new HikCentralGateActionRequestPlanBuilder();

        var plan = builder.Build(request, profile);

        Assert.Equal(HikCentralGateActionConstants.VendorCode, plan.VendorCode);
        Assert.Equal(HikCentralGateActionConstants.OpenGateOperation, plan.VendorOperation);
        Assert.Equal(HikCentralGateControlMechanism.AccessControlDoorControl, plan.ControlMechanism);
        Assert.Equal("POST", plan.HttpMethod);
        Assert.Equal(ExpectedDoorControlPath, plan.RelativePath);
        Assert.Equal("application/json", plan.ContentType);
        Assert.Equal(ExpectedDoorControlBody, Encoding.UTF8.GetString(plan.BodyUtf8));
        Assert.Equal(ExpectedDoorControlSha256, plan.BodySha256);
        Assert.Equal("EXIT-GATE-01", plan.TargetResourceCode);
        Assert.Equal(request.CorrelationId, plan.RequestCorrelationId);
        Assert.Equal("DOOR-CONTROL-PROFILE", plan.ProfileCode);
    }

    [Fact]
    public void Build_WhenCalledTwiceWithSameRequestAndProfile_IsDeterministic()
    {
        var request = ValidRequest();
        var profile = HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE");
        var builder = new HikCentralGateActionRequestPlanBuilder();

        var first = builder.Build(request, profile);
        var second = builder.Build(request, profile);

        Assert.Equal(first.HttpMethod, second.HttpMethod);
        Assert.Equal(first.RelativePath, second.RelativePath);
        Assert.Equal(first.BodyUtf8, second.BodyUtf8);
        Assert.Equal(first.BodySha256, second.BodySha256);
    }

    [Fact]
    public void Build_WhenTargetDiffers_ChangesBodyAndHash()
    {
        var profile = HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE");
        var builder = new HikCentralGateActionRequestPlanBuilder();

        var first = builder.Build(ValidRequest() with { TargetResourceCode = "EXIT-GATE-01" }, profile);
        var second = builder.Build(ValidRequest() with { TargetResourceCode = "EXIT-GATE-02" }, profile);

        Assert.NotEqual(first.BodyUtf8, second.BodyUtf8);
        Assert.NotEqual(first.BodySha256, second.BodySha256);
        Assert.Contains("\"doorIndexCode\":\"EXIT-GATE-02\"", Encoding.UTF8.GetString(second.BodyUtf8), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Build_WhenRequestIsInvalid_RejectsDeterministically(
        HikCentralGateActionRequest? request,
        string expectedErrorCode)
    {
        var builder = new HikCentralGateActionRequestPlanBuilder();
        var profile = HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE");

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(request!, profile));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void Build_WhenProfileIsInvalid_RejectsDeterministically(
        HikCentralGateControlProfile? profile,
        string expectedErrorCode)
    {
        var builder = new HikCentralGateActionRequestPlanBuilder();

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(ValidRequest(), profile!));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Theory]
    [InlineData("https://hikcentral.example/artemis/api/acs/v1/door/doControl")]
    [InlineData("//hikcentral.example/artemis/api/acs/v1/door/doControl")]
    [InlineData("/artemis/api/acs/v1/door/doControl?doorIndexCode=EXIT-GATE-01")]
    [InlineData("/artemis/api/acs/v1/door/doControl#fragment")]
    [InlineData("/artemis/api/acs/v1/../door/doControl")]
    [InlineData("/artemis/api/user@example/acs/v1/door/doControl")]
    public void Build_WhenProfilePathIsUnsafe_RejectsDeterministically(string unsafePath)
    {
        var builder = new HikCentralGateActionRequestPlanBuilder();
        var profile = HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
        {
            RelativePath = unsafePath
        };

        var exception = Assert.Throws<HikCentralGateActionRejectedException>(
            () => builder.Build(ValidRequest(), profile));

        Assert.Equal("HIKCENTRAL_PROFILE_PATH_UNSAFE", exception.ErrorCode);
    }

    [Fact]
    public void Build_WhenTargetRequiresJsonEscaping_ProducesDeterministicUtf8Json()
    {
        var builder = new HikCentralGateActionRequestPlanBuilder();
        var profile = HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE");

        var plan = builder.Build(ValidRequest() with { TargetResourceCode = "EXIT-\"GATE\\01" }, profile);

        Assert.Equal(
            "{\"doorIndexCode\":\"EXIT-\\\"GATE\\\\01\",\"controlType\":\"Open\"}",
            Encoding.UTF8.GetString(plan.BodyUtf8));
    }

    [Fact]
    public void RequestPlan_DoesNotExposeSecretSignatureAuthorizationOrPhysicalOpenFields()
    {
        var forbiddenNameFragments = new[]
        {
            "AppKey",
            "AppSecret",
            "Credential",
            "Secret",
            "Signature",
            "Authorization",
            "Cookie",
            "Header",
            "Host",
            "BaseAddress",
            "ConnectionString",
            "Physical",
            "Opened",
            "PayloadJson"
        };

        var propertyNames = typeof(HikCentralGateActionRequestPlan)
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
    public void Builder_DoesNotDeclareHttpDatabaseAuditOrAdapterDependencies()
    {
        var constructorParameters = typeof(HikCentralGateActionRequestPlanBuilder)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        var fieldTypes = typeof(HikCentralGateActionRequestPlanBuilder)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.DoesNotContain(typeof(HttpClient), constructorParameters);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.DoesNotContain(typeof(IHikCentralGateActionAdapter), constructorParameters);
        Assert.DoesNotContain(typeof(IHikCentralGateActionAdapter), fieldTypes);
        Assert.DoesNotContain(constructorParameters, IsDatabaseOrAuditType);
        Assert.DoesNotContain(fieldTypes, IsDatabaseOrAuditType);
    }

    public static IEnumerable<object?[]> InvalidRequests()
    {
        yield return [null, "HIKCENTRAL_REQUEST_REQUIRED"];
        yield return [ValidRequest() with { VendorOperation = "CLOSE_GATE" }, "VENDOR_OPERATION_UNSUPPORTED"];
        yield return [ValidRequest() with { TargetResourceCode = " " }, "TARGET_RESOURCE_CODE_REQUIRED"];
        yield return [ValidRequest() with { CorrelationId = Guid.Empty }, "CORRELATION_ID_REQUIRED"];
    }

    public static IEnumerable<object?[]> InvalidProfiles()
    {
        yield return [null, "HIKCENTRAL_GATE_CONTROL_PROFILE_REQUIRED"];
        yield return [HikCentralGateControlProfile.AccessControlDoorOpen(" "), "HIKCENTRAL_PROFILE_CODE_REQUIRED"];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("ALARM-OUTPUT-PROFILE") with
            {
                ControlMechanism = HikCentralGateControlMechanism.AlarmOutputControl
            },
            "HIKCENTRAL_CONTROL_MECHANISM_UNSUPPORTED"
        ];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
            {
                SupportedVendorOperation = "CLOSE_GATE"
            },
            "HIKCENTRAL_PROFILE_OPERATION_UNSUPPORTED"
        ];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
            {
                HttpMethod = "GET"
            },
            "HIKCENTRAL_PROFILE_METHOD_UNSUPPORTED"
        ];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
            {
                RelativePath = "/artemis/api/acs/v1/door/other"
            },
            "HIKCENTRAL_PROFILE_PATH_UNAPPROVED"
        ];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
            {
                ContentType = "text/plain"
            },
            "HIKCENTRAL_PROFILE_CONTENT_TYPE_UNSUPPORTED"
        ];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
            {
                TargetFieldName = "payload_json"
            },
            "HIKCENTRAL_PROFILE_BODY_MAPPING_UNAPPROVED"
        ];
        yield return [
            HikCentralGateControlProfile.AccessControlDoorOpen("DOOR-CONTROL-PROFILE") with
            {
                CommandValue = "Close"
            },
            "HIKCENTRAL_PROFILE_BODY_MAPPING_UNAPPROVED"
        ];
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
            RequestedAt: DateTimeOffset.Parse("2026-07-17T08:00:00Z"));
    }

    private static bool IsDatabaseOrAuditType(Type type) =>
        type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
        type.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase);
}
