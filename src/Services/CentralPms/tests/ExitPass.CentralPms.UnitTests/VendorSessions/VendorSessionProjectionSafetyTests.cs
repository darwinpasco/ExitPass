using System.Reflection;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.VendorSessions;

public sealed class VendorSessionProjectionSafetyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T02:00:00Z");

    [Fact]
    public void Options_DefaultToDisabledSixtySecondFailClosedFreshness()
    {
        var options = new VendorSessionProjectionOptions();

        options.SchedulerEnabled.Should().BeFalse();
        options.RequiredForEnvironment.Should().BeFalse();
        options.ActivationMode.Should().Be(VendorSessionProjectionOptions.LocalProfileActivationMode);
        options.ManagedDeploymentApproved.Should().BeFalse();
        options.DefaultPollIntervalSeconds.Should().Be(60);
        options.NormalFreshnessTargetSeconds.Should().Be(60);
        options.MaxProjectionAgeMinutes.Should().Be(1);
        options.DegradedResolveFallbackEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnabledLocalConfiguration_WithExactTarget_IsValid()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);
        var options = ValidLocalOptions(target);

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            "HikCentralLocal",
            "SITE_ADAPTER",
            "https://hikcentral.nonproduction.invalid",
            "127.0.0.1",
            "exitpass_hikcentral_local_dev",
            [target],
            []);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ManagedDeployment_WithExplicitApprovalAndMultipleTargets_IsValid()
    {
        var first = Target(enabled: true, lastSuccessAt: Now);
        var second = first with
        {
            ProjectionSyncTargetId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            SiteId = Guid.Parse("30000000-0000-0000-0000-000000000002"),
            ParkingLotIndexCode = "SECOND-LOT"
        };
        var options = ValidManagedOptions();

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            "Development",
            "SITE_ADAPTER",
            "https://hikcentral-uat.example",
            "127.0.0.1",
            "exitpass_hikcentral_local_uat",
            [first, second],
            []);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ManagedDeployment_WithoutApprovalOrExactEnvironment_FailsClosed()
    {
        var options = ValidManagedOptions();
        options.ManagedDeploymentApproved = false;
        options.ActivationEnvironment = "Uat";

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            "Development",
            "SITE_ADAPTER",
            "https://hikcentral-uat.example",
            "127.0.0.1",
            "exitpass_hikcentral_local_uat",
            [Target(enabled: true, lastSuccessAt: Now)],
            []);

        errors.Should().Contain("PROJECTION_MANAGED_DEPLOYMENT_APPROVAL_REQUIRED");
        errors.Should().Contain("PROJECTION_ACTIVATION_ENVIRONMENT_MISMATCH");
    }

    [Fact]
    public void ManagedDeployment_RequiresExplicitDatabaseInfrastructureApproval()
    {
        var options = ValidManagedOptions();

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            "Development",
            "SITE_ADAPTER",
            "https://production.hikcentral.example",
            "postgres.internal",
            "exitpass_hikcentral_local_uat",
            [Target(enabled: true, lastSuccessAt: Now)],
            []);

        errors.Should().Contain("PROJECTION_NON_LOOPBACK_DATABASE_NOT_APPROVED");
        errors.Should().NotContain("PROJECTION_ENDPOINT_IDENTITY_MISMATCH");
    }

    [Fact]
    public void ManagedProcessActivation_DoesNotRequireLocalProfileMarkerOrInteractiveAcknowledgement()
    {
        var values = ValidManagedProcessActivationValues();

        var errors = VendorSessionProjectionStartupValidationHostedService
            .ValidateManagedProcessScopedActivationConfiguration(name => values.GetValueOrDefault(name));

        errors.Should().BeEmpty();
        values.Keys.Should().NotContain(VendorSessionProjectionStartupValidationHostedService.LaunchProfileMarkerVariable);
        values.Keys.Should().NotContain("CentralPms__VendorSessionProjections__LocalNonProductionEndpointAcknowledged");
    }

    [Fact]
    public void ManagedProcessActivation_RequiresRoutingEnvironmentIdentityAndSecretMount()
    {
        var values = ValidManagedProcessActivationValues();
        values.Remove("CentralPms__VendorPms__Environment");
        values.Remove("CentralPms__VendorPms__CentralPmsServiceIdentityId");
        values.Remove("CentralPms__VendorPms__AdapterSecretMountRoot");

        var errors = VendorSessionProjectionStartupValidationHostedService
            .ValidateManagedProcessScopedActivationConfiguration(name => values.GetValueOrDefault(name));

        errors.Should().Contain("PROJECTION_PROCESS_CONFIGURATION_MISSING_CENTRALPMS__VENDORPMS__ENVIRONMENT");
        errors.Should().Contain("PROJECTION_PROCESS_CONFIGURATION_MISSING_CENTRALPMS__VENDORPMS__CENTRALPMSSERVICEIDENTITYID");
        errors.Should().Contain("PROJECTION_PROCESS_CONFIGURATION_MISSING_CENTRALPMS__VENDORPMS__ADAPTERSECRETMOUNTROOT");
    }

    [Fact]
    public void EnabledLocalConfiguration_WithWrongProviderOrTarget_FailsClosed()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);
        var options = WithExpectedSite(ValidLocalOptions(target), Guid.NewGuid());

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            "HikCentralLocal",
            "MOCK",
            "https://hikcentral.nonproduction.invalid",
            "127.0.0.1",
            "exitpass_hikcentral_local_dev",
            [target],
            []);

        errors.Should().Contain("PROJECTION_PROVIDER_MUST_BE_SITE_ADAPTER");
        errors.Should().Contain("PROJECTION_LOCAL_TARGET_IDENTITY_MISMATCH");
    }

    [Fact]
    public void EnabledConfiguration_WithNoEnabledTarget_FailsClosed()
    {
        var target = Target(enabled: false, lastSuccessAt: null);
        var options = ValidLocalOptions(target);

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            "HikCentralLocal",
            "SITE_ADAPTER",
            "https://hikcentral.nonproduction.invalid",
            "127.0.0.1",
            "exitpass_hikcentral_local_dev",
            [target],
            []);

        errors.Should().Contain("PROJECTION_ENABLED_TARGET_REQUIRED");
        errors.Should().Contain("PROJECTION_LOCAL_SINGLE_TARGET_REQUIRED");
    }

    [Fact]
    public void EnabledLocalConfiguration_DoesNotInspectSiteLocalHikCentralEndpoint()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);

        var errors = VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            ValidLocalOptions(target),
            "HikCentralLocal",
            "SITE_ADAPTER",
            "https://hcp.production.example",
            "127.0.0.1",
            "exitpass_hikcentral_local_dev",
            [target],
            []);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void EnabledLocalConfiguration_WithoutAcknowledgement_FailsClosed()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);
        var options = ValidLocalOptions(target);
        options.LocalNonProductionEndpointAcknowledged = false;

        var errors = Validate(options, "HikCentralLocal", target);

        errors.Should().Contain("PROJECTION_LOCAL_ACTIVATION_ACKNOWLEDGEMENT_REQUIRED");
    }

    [Fact]
    public void EnabledConfiguration_InWrongEnvironment_FailsClosed()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);

        var errors = Validate(ValidLocalOptions(target), "Development", target);

        errors.Should().Contain("PROJECTION_HIKCENTRAL_LOCAL_ENVIRONMENT_REQUIRED");
    }

    [Fact]
    public void EnabledConfiguration_WithMatchingAlternateActivationEnvironment_StillFailsClosed()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);
        var options = ValidLocalOptions(target);
        options.ActivationEnvironment = "Development";

        var errors = Validate(options, "Development", target);

        errors.Should().Contain("PROJECTION_HIKCENTRAL_LOCAL_ENVIRONMENT_REQUIRED");
        errors.Should().Contain("PROJECTION_ACTIVATION_ENVIRONMENT_MISMATCH");
    }

    [Fact]
    public void SchedulerEnabledAlternateEnvironment_CannotBypassAcknowledgement()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);
        var options = ValidLocalOptions(target);
        options.ActivationEnvironment = "Test";
        options.LocalNonProductionEndpointAcknowledged = false;

        var errors = Validate(options, "Test", target);

        errors.Should().Contain("PROJECTION_HIKCENTRAL_LOCAL_ENVIRONMENT_REQUIRED");
        errors.Should().Contain("PROJECTION_LOCAL_ACTIVATION_ACKNOWLEDGEMENT_REQUIRED");
    }

    [Fact]
    public void SchedulerDisabledLiveAdapter_CannotEnableManualSynchronization()
    {
        var target = Target(enabled: true, lastSuccessAt: Now);
        var options = ValidLocalOptions(target);
        options.SchedulerEnabled = false;

        var errors = Validate(options, "HikCentralLocal", target);

        errors.Should().Contain("PROJECTION_LIVE_ADAPTER_REQUIRES_SCHEDULER_ENABLED");
    }

    [Fact]
    public void ProcessActivation_RequiresProfileMarkerAndExplicitAcknowledgement()
    {
        var values = ValidProcessActivationValues();
        values.Remove(VendorSessionProjectionStartupValidationHostedService.LaunchProfileMarkerVariable);
        values["CentralPms__VendorSessionProjections__LocalNonProductionEndpointAcknowledged"] = "false";

        var errors = VendorSessionProjectionStartupValidationHostedService
            .ValidateProcessScopedActivationConfiguration(name => values.GetValueOrDefault(name));

        errors.Should().Contain("PROJECTION_HIKCENTRAL_LOCAL_LAUNCH_PROFILE_REQUIRED");
        errors.Should().Contain("PROJECTION_PROCESS_OPERATOR_ACKNOWLEDGEMENT_REQUIRED");
    }

    [Fact]
    public void ProcessActivation_WithHikCentralLocalProfileAndCompleteAcknowledgement_Passes()
    {
        var values = ValidProcessActivationValues();

        var errors = VendorSessionProjectionStartupValidationHostedService
            .ValidateProcessScopedActivationConfiguration(name => values.GetValueOrDefault(name));

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Launcher_UsesOnlyHikCentralLocalLaunchProfile()
    {
        var launchSettings = File.ReadAllText(FindRepoFile(Path.Combine(
            "src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Api",
            "Properties", "launchSettings.json")));
        var launcher = File.ReadAllText(FindRepoFile(Path.Combine(
            "scripts", "v1.3", "hikcentral", "Start-HikCentralLocalProjection.ps1")));

        launchSettings.Should().Contain("\"HikCentralLocal\"");
        launchSettings.Should().Contain(VendorSessionProjectionStartupValidationHostedService.LaunchProfileMarkerVariable);
        launcher.Should().Contain("--launch-profile HikCentralLocal");
        launcher.Should().NotContain("--no-launch-profile");
    }

    [Fact]
    public void LocalUatTargetConfiguration_IsDisabledByDefaultAndContainsNoEndpointOrSecret()
    {
        var sql = File.ReadAllText(FindRepoFile(Path.Combine(
            "docs", "sql", "HikCentralProjectionTestSiteLocalUat.sql")));
        var normalizedSql = sql.Replace("\r\n", "\n", StringComparison.Ordinal);

        sql.Should().Contain("'TEST SITE'");
        normalizedSql.Should().Contain("false,\n    60");
        sql.Should().Contain("exitpass_hikcentral_local_uat");
        sql.Should().NotContain("127.0.0.1:9019");
        sql.Should().NotContainEquivalentOf("AppSecret");
        sql.Should().NotContainEquivalentOf("Password=");
    }

    [Fact]
    public void LegacyAndCurrentConfiguration_AreAmbiguous()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HIKCENTRAL:BASEURL"] = "legacy",
                ["CentralPms:VendorPms:HikCentral:BaseUrl"] = "current"
            })
            .Build();

        VendorSessionProjectionStartupValidationHostedService
            .HasAmbiguousLegacyConfiguration(configuration)
            .Should().BeTrue();
    }

    [Fact]
    public void ManualSyncPolicy_UsesOnlyNamedLeastPrivilegePermission()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions("VendorSessionProjectionSyncOperator")
            .Should()
            .Equal("ops.vendor-session-projection-sync.execute");
    }

    [Fact]
    public async Task ManualSyncPolicy_WithNamedPermission_IsAuthorized()
    {
        var nextCalled = false;
        var middleware = new CentralPmsRbacMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ProjectionSyncContext("ops.vendor-session-projection-sync.execute");

        await middleware.InvokeAsync(
            context,
            Options.Create(new CentralPmsRbacOptions
            {
                Enabled = true,
                AllowPermissionHeader = true,
                AllowFixtureIdentityHeaders = true
            }),
            Substitute.For<ICentralPmsRbacRepository>(),
            TestEnvironment(),
            NullLogger<CentralPmsRbacMiddleware>.Instance);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ManualSyncPolicy_WithUnrelatedPermission_IsDenied()
    {
        var middleware = new CentralPmsRbacMiddleware(_ => Task.CompletedTask);
        var context = ProjectionSyncContext("reconciliation.view");

        await middleware.InvokeAsync(
            context,
            Options.Create(new CentralPmsRbacOptions
            {
                Enabled = true,
                AllowPermissionHeader = true,
                AllowFixtureIdentityHeaders = true
            }),
            Substitute.For<ICentralPmsRbacRepository>(),
            TestEnvironment(),
            NullLogger<CentralPmsRbacMiddleware>.Instance);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void AdvisoryLockKey_IsStableAndTargetScoped()
    {
        var method = typeof(PostgresVendorSessionProjectionExecutionLock).GetMethod(
            "DeriveLockKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var firstTarget = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondTarget = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var first = (long)method!.Invoke(null, [firstTarget])!;
        var replay = (long)method.Invoke(null, [firstTarget])!;
        var second = (long)method.Invoke(null, [secondTarget])!;

        first.Should().Be(replay);
        first.Should().NotBe(second);
    }

    [Fact]
    public void WebPayStatutoryWalkthrough_RemainsMockedAndDoesNotActivateProjection()
    {
        var script = File.ReadAllText(FindRepoFile(
            Path.Combine("scripts", "v1.3", "webpay", "Start-WebPayStatutoryDiscountWalkthrough.ps1")));

        script.Should().Contain("ASPNETCORE_ENVIRONMENT");
        script.Should().NotContain("CentralPms__VendorSessionProjections__SchedulerEnabled");
        script.Should().NotContain("CentralPms__VendorPms__HikCentral");
        script.Should().NotContain("HIKCENTRAL__");
    }

    [Fact]
    public async Task Readiness_WhenProjectionIsExplicitlyDisabledAndOptional_ReportsDisabledHealthy()
    {
        var result = await CheckReadinessAsync(new VendorSessionProjectionOptions());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("explicitly disabled");
        result.Data["scheduler_enabled"].Should().Be(false);
    }

    [Theory]
    [InlineData("CURRENT", HealthStatus.Healthy)]
    [InlineData("DELAYED", HealthStatus.Degraded)]
    [InlineData("LOCK_CONTENDED_DEFERRED", HealthStatus.Degraded)]
    [InlineData("STALE", HealthStatus.Unhealthy)]
    [InlineData("FAILED", HealthStatus.Unhealthy)]
    [InlineData("NEVER_SYNCHRONIZED", HealthStatus.Unhealthy)]
    public async Task Readiness_WhenProjectionIsRequired_UsesFreshnessClassification(
        string classification,
        HealthStatus expected)
    {
        var result = await CheckReadinessAsync(
            new VendorSessionProjectionOptions
            {
                SchedulerEnabled = true,
                RequiredForEnvironment = true
            },
            HealthTarget(classification));

        result.Status.Should().Be(expected);
        result.Data.Keys.Should().NotContain(key =>
            key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Readiness_WhenRequiredSchedulerHasNoEnabledTarget_FailsWithoutThrowing()
    {
        var result = await CheckReadinessAsync(
            new VendorSessionProjectionOptions
            {
                SchedulerEnabled = true,
                RequiredForEnvironment = true
            });

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["enabled_target_count"].Should().Be(0);
        result.Data["last_success_at"].Should().Be("NEVER");
    }

    private static VendorSessionProjectionOptions ValidLocalOptions(
        VendorSessionProjectionHealthTargetReadModel target) => new()
    {
        SchedulerEnabled = true,
        RequiredForEnvironment = true,
        ActivationEnvironment = "HikCentralLocal",
        LocalNonProductionEndpointAcknowledged = true,
        ExpectedDatabaseName = "exitpass_hikcentral_local_dev",
        ExpectedTargetSiteId = target.SiteId,
        ExpectedTargetSiteGroupId = target.SiteGroupId,
        ExpectedTargetVendorSystemId = target.VendorSystemId,
        ExpectedTargetParkingLotIndexCode = target.ParkingLotIndexCode,
        DefaultPollIntervalSeconds = 60,
        NormalFreshnessTargetSeconds = 60,
        MaxProjectionAgeMinutes = 1
    };

    private static VendorSessionProjectionOptions ValidManagedOptions() => new()
    {
        SchedulerEnabled = true,
        RequiredForEnvironment = true,
        ActivationMode = VendorSessionProjectionOptions.ManagedDeploymentActivationMode,
        ActivationEnvironment = "Development",
        ManagedDeploymentApproved = true,
        ExpectedDatabaseName = "exitpass_hikcentral_local_uat",
        ExpectedEndpointHost = "hikcentral-uat.example",
        ExpectedEndpointScheme = "https",
        ExpectedEndpointPort = 443,
        DefaultPollIntervalSeconds = 60,
        NormalFreshnessTargetSeconds = 60,
        MaxProjectionAgeMinutes = 1
    };

    private static VendorSessionProjectionOptions WithExpectedSite(
        VendorSessionProjectionOptions options,
        Guid siteId)
    {
        options.ExpectedTargetSiteId = siteId;
        return options;
    }

    private static IReadOnlyList<string> Validate(
        VendorSessionProjectionOptions options,
        string environmentName,
        VendorSessionProjectionHealthTargetReadModel target) =>
        VendorSessionProjectionStartupValidationHostedService.ValidateEnabledConfiguration(
            options,
            environmentName,
            "SITE_ADAPTER",
            "https://hikcentral.nonproduction.invalid",
            "127.0.0.1",
            "exitpass_hikcentral_local_dev",
            [target],
            []);

    private static Dictionary<string, string?> ValidProcessActivationValues() => new()
    {
        [VendorSessionProjectionStartupValidationHostedService.LaunchProfileMarkerVariable] = "HikCentralLocal",
        ["ConnectionStrings__MainDatabase"] = "present",
        ["CentralPms__VendorPms__Provider"] = "SITE_ADAPTER",
        ["CentralPms__VendorPms__Environment"] = "HikCentralLocal",
        ["CentralPms__VendorPms__CentralPmsServiceIdentityId"] = "12000000-0000-0000-0000-000000000001",
        ["CentralPms__VendorPms__AdapterSecretMountRoot"] = "C:\\run\\secrets",
        ["CentralPms__VendorSessionProjections__SchedulerEnabled"] = "true",
        ["CentralPms__VendorSessionProjections__RequiredForEnvironment"] = "true",
        ["CentralPms__VendorSessionProjections__ActivationEnvironment"] = "HikCentralLocal",
        ["CentralPms__VendorSessionProjections__LocalNonProductionEndpointAcknowledged"] = "true",
        ["CentralPms__VendorSessionProjections__ExpectedDatabaseName"] = "present",
        ["CentralPms__VendorSessionProjections__ExpectedTargetSiteId"] = "present",
        ["CentralPms__VendorSessionProjections__ExpectedTargetSiteGroupId"] = "present",
        ["CentralPms__VendorSessionProjections__ExpectedTargetVendorSystemId"] = "present",
        ["CentralPms__VendorSessionProjections__ExpectedTargetParkingLotIndexCode"] = "present"
    };

    private static Dictionary<string, string?> ValidManagedProcessActivationValues() => new()
    {
        ["ConnectionStrings__MainDatabase"] = "present",
        ["CentralPms__VendorPms__Provider"] = "SITE_ADAPTER",
        ["CentralPms__VendorPms__Environment"] = "Development",
        ["CentralPms__VendorPms__CentralPmsServiceIdentityId"] = "12000000-0000-0000-0000-000000000001",
        ["CentralPms__VendorPms__AdapterSecretMountRoot"] = "/run/secrets",
        ["CentralPms__VendorSessionProjections__SchedulerEnabled"] = "true",
        ["CentralPms__VendorSessionProjections__RequiredForEnvironment"] = "true",
        ["CentralPms__VendorSessionProjections__ActivationMode"] = VendorSessionProjectionOptions.ManagedDeploymentActivationMode,
        ["CentralPms__VendorSessionProjections__ActivationEnvironment"] = "Development",
        ["CentralPms__VendorSessionProjections__ManagedDeploymentApproved"] = "true",
        ["CentralPms__VendorSessionProjections__AllowNonLoopbackDatabase"] = "false",
        ["CentralPms__VendorSessionProjections__AllowProductionEndpoint"] = "false",
        ["CentralPms__VendorSessionProjections__ExpectedDatabaseName"] = "exitpass_hikcentral_local_uat",
    };

    private static VendorSessionProjectionHealthTargetReadModel Target(
        bool enabled,
        DateTimeOffset? lastSuccessAt) => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        "LOCAL-TEST-LOT",
        "Local Test Lot",
        enabled,
        enabled ? VendorSessionProjectionHealthStatus.Healthy : VendorSessionProjectionHealthStatus.Disabled,
        lastSuccessAt,
        lastSuccessAt,
        null,
        0,
        null,
        null,
        null,
        0,
        60,
        180,
        100,
        lastSuccessAt,
        0,
        0,
        0,
        0,
        0);

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath}.");
    }

    private static DefaultHttpContext ProjectionSyncContext(string permission)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ReconciliationPolicyMetadata("VendorSessionProjectionSyncOperator")),
            "manual-projection-sync"));
        context.Request.Headers[CentralPmsRbacPolicyCatalog.UserIdHeaderName] = Guid.NewGuid().ToString("D");
        context.Request.Headers[CentralPmsRbacPolicyCatalog.PermissionsHeaderName] = permission;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static Microsoft.AspNetCore.Hosting.IWebHostEnvironment TestEnvironment()
    {
        var environment = Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        return environment;
    }

    private static async Task<HealthCheckResult> CheckReadinessAsync(
        VendorSessionProjectionOptions options,
        params VendorSessionProjectionHealthTarget[] targets)
    {
        var healthService = Substitute.For<IVendorSessionProjectionHealthService>();
        healthService.ListTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VendorSessionProjectionHealthTarget>>(targets));
        var services = new ServiceCollection();
        services.AddSingleton(healthService);
        await using var provider = services.BuildServiceProvider();
        var check = new VendorSessionProjectionReadinessHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options));

        return await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    private static VendorSessionProjectionHealthTarget HealthTarget(string freshnessClassification) => new(
        ProjectionSyncTargetId: Guid.Parse("10000000-0000-0000-0000-000000000001"),
        SiteId: Guid.Parse("20000000-0000-0000-0000-000000000001"),
        SiteGroupId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
        VendorSystemId: Guid.Parse("40000000-0000-0000-0000-000000000001"),
        ParkingLotIndexCode: "LOCAL-TEST-LOT",
        ParkingLotName: "Local Test Lot",
        Enabled: true,
        HealthStatus: freshnessClassification switch
        {
            "FAILED" => VendorSessionProjectionHealthStatus.Failing,
            "LOCK_CONTENDED_DEFERRED" => VendorSessionProjectionHealthStatus.Deferred,
            "CURRENT" => VendorSessionProjectionHealthStatus.Healthy,
            _ => VendorSessionProjectionHealthStatus.Degraded
        },
        LastAttemptAt: Now,
        LastSuccessAt: freshnessClassification == "NEVER_SYNCHRONIZED" ? null : Now,
        LastFailureAt: freshnessClassification == "FAILED" ? Now : null,
        FailureCount: freshnessClassification == "FAILED" ? 1 : 0,
        LastErrorCode: freshnessClassification == "FAILED" ? "PROJECTION_FAILURE" : null,
        LastErrorMessage: freshnessClassification == "FAILED"
            ? "Projection synchronization failed. Review the classified operational event."
            : null,
        LastLockContentionAt: freshnessClassification == "LOCK_CONTENDED_DEFERRED" ? Now : null,
        LockContentionCount: freshnessClassification == "LOCK_CONTENDED_DEFERRED" ? 1 : 0,
        PollIntervalSeconds: 60,
        LookbackWindowMinutes: 180,
        PageSize: 100,
        LatestProjectionLastRefreshedAt: Now,
        FreshnessAge: TimeSpan.Zero,
        FreshnessClassification: freshnessClassification,
        IsStale: freshnessClassification is "STALE" or "FAILED" or "NEVER_SYNCHRONIZED",
        TotalProjectionCount: 0,
        ActiveProjectionCount: 0,
        ExitedProjectionCount: 0,
        CardNumProjectionCount: 0,
        PlateLicenseProjectionCount: 0);
}
