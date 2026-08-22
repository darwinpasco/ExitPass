using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExitPass.CentralPms.Api.Services;

/// <summary>
/// Fails startup before scheduling when projection activation is incomplete or ambiguous.
/// </summary>
public sealed class VendorSessionProjectionStartupValidationHostedService(
    IConfiguration configuration,
    IHostEnvironment environment,
    IServiceScopeFactory scopeFactory,
    IOptions<VendorSessionProjectionOptions> options,
    ILogger<VendorSessionProjectionStartupValidationHostedService> logger) :
    IHostedService,
    IHikCentralLiveActivationGate
{
    public const string RequiredEnvironmentName = "HikCentralLocal";
    public const string LaunchProfileMarkerVariable = "EXITPASS_HIKCENTRAL_LAUNCH_PROFILE";

    private static readonly string[] RequiredProcessScopedVariables =
    [
        "ConnectionStrings__MainDatabase",
        "CentralPms__VendorPms__Provider",
        "CentralPms__VendorPms__Environment",
        "CentralPms__VendorPms__CentralPmsServiceIdentityId",
        "CentralPms__VendorPms__AdapterSecretMountRoot",
        "CentralPms__VendorSessionProjections__SchedulerEnabled",
        "CentralPms__VendorSessionProjections__RequiredForEnvironment",
        "CentralPms__VendorSessionProjections__ActivationEnvironment",
        "CentralPms__VendorSessionProjections__LocalNonProductionEndpointAcknowledged",
        "CentralPms__VendorSessionProjections__ExpectedDatabaseName",
        "CentralPms__VendorSessionProjections__ExpectedTargetSiteId",
        "CentralPms__VendorSessionProjections__ExpectedTargetSiteGroupId",
        "CentralPms__VendorSessionProjections__ExpectedTargetVendorSystemId",
        "CentralPms__VendorSessionProjections__ExpectedTargetParkingLotIndexCode"
    ];

    private static readonly string[] RequiredManagedProcessScopedVariables =
    [
        "ConnectionStrings__MainDatabase",
        "CentralPms__VendorPms__Provider",
        "CentralPms__VendorPms__Environment",
        "CentralPms__VendorPms__CentralPmsServiceIdentityId",
        "CentralPms__VendorPms__AdapterSecretMountRoot",
        "CentralPms__VendorSessionProjections__SchedulerEnabled",
        "CentralPms__VendorSessionProjections__RequiredForEnvironment",
        "CentralPms__VendorSessionProjections__ActivationMode",
        "CentralPms__VendorSessionProjections__ActivationEnvironment",
        "CentralPms__VendorSessionProjections__ManagedDeploymentApproved",
        "CentralPms__VendorSessionProjections__AllowNonLoopbackDatabase",
        "CentralPms__VendorSessionProjections__AllowProductionEndpoint",
        "CentralPms__VendorSessionProjections__ExpectedDatabaseName"
    ];

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var projectionOptions = options.Value;
        projectionOptions.ThrowIfInvalid();

        var hasLegacyConfiguration = configuration.GetSection("HIKCENTRAL").GetChildren().Any();
        if (HasAmbiguousLegacyConfiguration(configuration))
        {
            throw new InvalidOperationException(
                "HIKCENTRAL_LEGACY_CURRENT_CONFIGURATION_AMBIGUOUS");
        }

        var provider = configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:Provider"];
        var liveAdapterConfigured = string.Equals(
            provider?.Trim(),
            CentralPmsVendorPmsAdapterOptions.SiteAdapterProvider,
            StringComparison.OrdinalIgnoreCase);

        if (!liveAdapterConfigured)
        {
            if (projectionOptions.SchedulerEnabled)
            {
                throw new InvalidOperationException(
                    "HIKCENTRAL_PROJECTION_STARTUP_VALIDATION_FAILED: PROJECTION_PROVIDER_MUST_BE_SITE_ADAPTER");
            }

            if (hasLegacyConfiguration)
            {
                logger.LogWarning(
                    "Legacy HikCentral configuration is present but ignored; projection scheduler remains disabled.");
            }

            logger.LogInformation(
                "Vendor session projection startup validation completed. scheduler_enabled=false required_for_environment={RequiredForEnvironment}",
                projectionOptions.RequiredForEnvironment);
            return;
        }

        await EnsureActivatedAsync(cancellationToken);

        logger.LogInformation(
            "HikCentral live activation gate passed. scheduler_enabled={SchedulerEnabled} required_for_environment={RequiredForEnvironment} environment={EnvironmentName}",
            projectionOptions.SchedulerEnabled,
            projectionOptions.RequiredForEnvironment,
            environment.EnvironmentName);
    }

    /// <inheritdoc />
    public async Task EnsureActivatedAsync(CancellationToken cancellationToken)
    {
        var projectionOptions = options.Value;
        projectionOptions.ThrowIfInvalid();

        if (HasAmbiguousLegacyConfiguration(configuration))
        {
            throw new InvalidOperationException(
                "HIKCENTRAL_LEGACY_CURRENT_CONFIGURATION_AMBIGUOUS");
        }

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IVendorSessionProjectionHealthReadRepository>();
        var targets = await repository.ListTargetsAsync(cancellationToken);
        var connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("PROJECTION_MAIN_DATABASE_CONFIGURATION_MISSING");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var processActivationErrors = projectionOptions.UsesLocalProfileActivation()
            ? ValidateProcessScopedActivationConfiguration(
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process))
            : ValidateManagedProcessScopedActivationConfiguration(
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
        var errors = ValidateEnabledConfiguration(
            projectionOptions,
            environment.EnvironmentName,
            configuration[$"{CentralPmsVendorPmsAdapterOptions.SectionName}:Provider"],
            null,
            connection.Host,
            connection.Database,
            targets,
            processActivationErrors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"HIKCENTRAL_PROJECTION_STARTUP_VALIDATION_FAILED: {string.Join(", ", errors)}");
        }

    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static IReadOnlyList<string> ValidateEnabledConfiguration(
        VendorSessionProjectionOptions options,
        string environmentName,
        string? provider,
        string? baseUrl,
        string databaseHost,
        string databaseName,
        IReadOnlyList<VendorSessionProjectionHealthTargetReadModel> targets,
        IReadOnlyList<string> processActivationErrors)
    {
        if (!options.UsesLocalProfileActivation())
        {
            return ValidateManagedDeploymentConfiguration(
                options,
                environmentName,
                provider,
                baseUrl,
                databaseHost,
                databaseName,
                targets,
                processActivationErrors);
        }

        var errors = new List<string>(processActivationErrors);
        if (!string.Equals(provider?.Trim(), CentralPmsVendorPmsAdapterOptions.SiteAdapterProvider, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("PROJECTION_PROVIDER_MUST_BE_SITE_ADAPTER");
        }

        if (!string.Equals(environmentName, RequiredEnvironmentName, StringComparison.Ordinal))
        {
            errors.Add("PROJECTION_HIKCENTRAL_LOCAL_ENVIRONMENT_REQUIRED");
        }

        if (!string.Equals(options.ActivationEnvironment?.Trim(), RequiredEnvironmentName, StringComparison.Ordinal))
        {
            errors.Add("PROJECTION_ACTIVATION_ENVIRONMENT_MISMATCH");
        }

        if (!options.SchedulerEnabled)
        {
            errors.Add("PROJECTION_LIVE_ADAPTER_REQUIRES_SCHEDULER_ENABLED");
        }

        if (!options.RequiredForEnvironment || !options.LocalNonProductionEndpointAcknowledged)
        {
            errors.Add("PROJECTION_LOCAL_ACTIVATION_ACKNOWLEDGEMENT_REQUIRED");
        }

        if (options.DefaultPollIntervalSeconds != 60 || options.NormalFreshnessTargetSeconds != 60)
        {
            errors.Add("PROJECTION_SIXTY_SECOND_TIMING_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(options.ExpectedDatabaseName) ||
            !string.Equals(options.ExpectedDatabaseName.Trim(), databaseName, StringComparison.Ordinal))
        {
            errors.Add("PROJECTION_DATABASE_IDENTITY_MISMATCH");
        }

        var enabledTargets = targets.Where(target => target.Enabled).ToArray();
        if (enabledTargets.Length == 0)
        {
            errors.Add("PROJECTION_ENABLED_TARGET_REQUIRED");
        }

        if (enabledTargets.Any(target =>
            target.SiteId == Guid.Empty ||
            target.SiteGroupId == Guid.Empty ||
            target.VendorSystemId == Guid.Empty ||
            string.IsNullOrWhiteSpace(target.ParkingLotIndexCode)))
        {
            errors.Add("PROJECTION_TARGET_SCOPE_INCOMPLETE");
        }

        if (enabledTargets.Any(target => target.PollIntervalSeconds != 60))
        {
            errors.Add("PROJECTION_TARGET_POLL_INTERVAL_MUST_BE_SIXTY_SECONDS");
        }

        if (enabledTargets.Select(target => target.ProjectionSyncTargetId).Distinct().Count() != enabledTargets.Length)
        {
            errors.Add("PROJECTION_TARGET_IDENTITY_DUPLICATE");
        }


        if (enabledTargets
            .GroupBy(target => new
            {
                target.SiteId,
                target.SiteGroupId,
                target.VendorSystemId,
                ParkingLotIndexCode = target.ParkingLotIndexCode.Trim().ToUpperInvariant()
            })
            .Any(group => group.Count() > 1))
        {
            errors.Add("PROJECTION_TARGET_SCOPE_DUPLICATE");
        }

        if (options.MaxProjectionAgeMinutes != 1)
        {
            errors.Add("PROJECTION_LOCAL_MAX_AGE_MUST_BE_ONE_MINUTE");
        }

        if (!IsLoopbackHost(databaseHost))
        {
            errors.Add("PROJECTION_LOCAL_DATABASE_MUST_BE_LOOPBACK");
        }

        if (enabledTargets.Length != 1)
        {
            errors.Add("PROJECTION_LOCAL_SINGLE_TARGET_REQUIRED");
        }
        else
        {
            var target = enabledTargets[0];
            if (target.SiteId != options.ExpectedTargetSiteId ||
                target.SiteGroupId != options.ExpectedTargetSiteGroupId ||
                target.VendorSystemId != options.ExpectedTargetVendorSystemId ||
                !string.Equals(
                    target.ParkingLotIndexCode,
                    options.ExpectedTargetParkingLotIndexCode?.Trim(),
                    StringComparison.Ordinal))
            {
                errors.Add("PROJECTION_LOCAL_TARGET_IDENTITY_MISMATCH");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateManagedDeploymentConfiguration(
        VendorSessionProjectionOptions options,
        string environmentName,
        string? provider,
        string? baseUrl,
        string databaseHost,
        string databaseName,
        IReadOnlyList<VendorSessionProjectionHealthTargetReadModel> targets,
        IReadOnlyList<string> processActivationErrors)
    {
        var errors = new List<string>(processActivationErrors);
        if (!string.Equals(
            options.ActivationMode,
            VendorSessionProjectionOptions.ManagedDeploymentActivationMode,
            StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("PROJECTION_MANAGED_ACTIVATION_MODE_REQUIRED");
        }

        if (!options.ManagedDeploymentApproved)
        {
            errors.Add("PROJECTION_MANAGED_DEPLOYMENT_APPROVAL_REQUIRED");
        }

        if (!string.Equals(
            provider?.Trim(),
            CentralPmsVendorPmsAdapterOptions.SiteAdapterProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("PROJECTION_PROVIDER_MUST_BE_SITE_ADAPTER");
        }

        if (string.IsNullOrWhiteSpace(options.ActivationEnvironment) ||
            !string.Equals(options.ActivationEnvironment.Trim(), environmentName, StringComparison.Ordinal))
        {
            errors.Add("PROJECTION_ACTIVATION_ENVIRONMENT_MISMATCH");
        }

        if (!options.SchedulerEnabled || !options.RequiredForEnvironment)
        {
            errors.Add("PROJECTION_MANAGED_SCHEDULER_REQUIRED");
        }

        if (options.DefaultPollIntervalSeconds != 60 ||
            options.NormalFreshnessTargetSeconds != 60 ||
            options.MaxProjectionAgeMinutes != 1)
        {
            errors.Add("PROJECTION_MANAGED_SIXTY_SECOND_FRESHNESS_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(options.ExpectedDatabaseName) ||
            !string.Equals(options.ExpectedDatabaseName.Trim(), databaseName, StringComparison.Ordinal))
        {
            errors.Add("PROJECTION_DATABASE_IDENTITY_MISMATCH");
        }

        if (!IsLoopbackHost(databaseHost) && !options.AllowNonLoopbackDatabase)
        {
            errors.Add("PROJECTION_NON_LOOPBACK_DATABASE_NOT_APPROVED");
        }

        var enabledTargets = targets.Where(target => target.Enabled).ToArray();
        if (enabledTargets.Length == 0)
        {
            errors.Add("PROJECTION_MANAGED_TARGET_REQUIRED");
        }

        if (enabledTargets.Any(target =>
            target.SiteId == Guid.Empty ||
            target.SiteGroupId == Guid.Empty ||
            target.VendorSystemId == Guid.Empty ||
            string.IsNullOrWhiteSpace(target.ParkingLotIndexCode)))
        {
            errors.Add("PROJECTION_TARGET_SCOPE_INCOMPLETE");
        }

        if (enabledTargets.Any(target => target.PollIntervalSeconds != 60))
        {
            errors.Add("PROJECTION_TARGET_POLL_INTERVAL_MUST_BE_SIXTY_SECONDS");
        }

        if (enabledTargets.Select(target => target.ProjectionSyncTargetId).Distinct().Count() != enabledTargets.Length)
        {
            errors.Add("PROJECTION_TARGET_IDENTITY_DUPLICATE");
        }

        if (enabledTargets
            .GroupBy(target => new
            {
                target.SiteId,
                target.SiteGroupId,
                target.VendorSystemId,
                ParkingLotIndexCode = target.ParkingLotIndexCode.Trim().ToUpperInvariant()
            })
            .Any(group => group.Count() > 1))
        {
            errors.Add("PROJECTION_TARGET_SCOPE_DUPLICATE");
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> ValidateManagedProcessScopedActivationConfiguration(
        Func<string, string?> readProcessVariable)
    {
        ArgumentNullException.ThrowIfNull(readProcessVariable);
        var errors = RequiredManagedProcessScopedVariables
            .Where(name => string.IsNullOrWhiteSpace(readProcessVariable(name)))
            .Select(name => $"PROJECTION_PROCESS_CONFIGURATION_MISSING_{NormalizeVariableName(name)}")
            .ToList();

        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorPms__Provider",
            CentralPmsVendorPmsAdapterOptions.SiteAdapterProvider,
            "PROJECTION_PROCESS_PROVIDER_MUST_BE_SITE_ADAPTER");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__SchedulerEnabled",
            "true",
            "PROJECTION_PROCESS_SCHEDULER_ENABLEMENT_REQUIRED");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__RequiredForEnvironment",
            "true",
            "PROJECTION_PROCESS_REQUIRED_ENVIRONMENT_FLAG_REQUIRED");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__ActivationMode",
            VendorSessionProjectionOptions.ManagedDeploymentActivationMode,
            "PROJECTION_PROCESS_MANAGED_ACTIVATION_MODE_REQUIRED");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__ManagedDeploymentApproved",
            "true",
            "PROJECTION_PROCESS_MANAGED_DEPLOYMENT_APPROVAL_REQUIRED");

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> ValidateProcessScopedActivationConfiguration(
        Func<string, string?> readProcessVariable)
    {
        ArgumentNullException.ThrowIfNull(readProcessVariable);
        var errors = RequiredProcessScopedVariables
            .Where(name => string.IsNullOrWhiteSpace(readProcessVariable(name)))
            .Select(name => $"PROJECTION_PROCESS_CONFIGURATION_MISSING_{NormalizeVariableName(name)}")
            .ToList();

        AddExactProcessValueError(
            errors,
            readProcessVariable,
            LaunchProfileMarkerVariable,
            RequiredEnvironmentName,
            "PROJECTION_HIKCENTRAL_LOCAL_LAUNCH_PROFILE_REQUIRED");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorPms__Provider",
            CentralPmsVendorPmsAdapterOptions.SiteAdapterProvider,
            "PROJECTION_PROCESS_PROVIDER_MUST_BE_SITE_ADAPTER");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__SchedulerEnabled",
            "true",
            "PROJECTION_PROCESS_SCHEDULER_ENABLEMENT_REQUIRED");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__RequiredForEnvironment",
            "true",
            "PROJECTION_PROCESS_REQUIRED_ENVIRONMENT_FLAG_REQUIRED");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__ActivationEnvironment",
            RequiredEnvironmentName,
            "PROJECTION_PROCESS_ACTIVATION_ENVIRONMENT_MISMATCH");
        AddExactProcessValueError(
            errors,
            readProcessVariable,
            "CentralPms__VendorSessionProjections__LocalNonProductionEndpointAcknowledged",
            "true",
            "PROJECTION_PROCESS_OPERATOR_ACKNOWLEDGEMENT_REQUIRED");

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Detects unsafe partial mixing of obsolete and current HikCentral configuration hierarchies.
    /// </summary>
    public static bool HasAmbiguousLegacyConfiguration(IConfiguration configuration) =>
        configuration.GetSection("HIKCENTRAL").GetChildren().Any() ||
        configuration.GetSection($"{CentralPmsVendorPmsAdapterOptions.SectionName}:HikCentral").GetChildren().Any();

    private static bool IsLoopbackHost(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
        string.Equals(host, "::1", StringComparison.Ordinal);

    private static bool IsProductionMarkedHost(string host) =>
        host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(label => label.Equals("prod", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("production", StringComparison.OrdinalIgnoreCase));

    private static void AddExactProcessValueError(
        ICollection<string> errors,
        Func<string, string?> readProcessVariable,
        string variableName,
        string expected,
        string error)
    {
        if (!string.Equals(readProcessVariable(variableName)?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(error);
        }
    }

    private static string NormalizeVariableName(string name) =>
        new(name.Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_').ToArray());
}
