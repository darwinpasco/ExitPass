using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.Security;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.ManagementPlatform;

public sealed class DeviceAdministrationLifecycleIntegrationTests
{
    [Fact]
    public async Task Administration_RejectsActiveUserWithoutSystemAdministratorRole()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
        var (_, siteId, siteGroupId) = await ResolveContextAsync(connectionString);
        var ordinaryUserId = await SeedOrdinaryUserAsync(connectionString);
        var administration = new DeviceAdministrationService(
            new PostgresDeviceAdministrationRepository(connectionString),
            TimeProvider.System);

        var action = async () => await administration.RegisterAsync(
            new("Unauthorized APT", DeviceTypes.AssistedPaymentTerminal, null,
                siteId, siteGroupId, "APT-UNAUTHORIZED"),
            ordinaryUserId, Guid.NewGuid(), CancellationToken.None);

        (await action.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("SYSTEM_ADMINISTRATOR_REQUIRED");
    }

    [Fact]
    public async Task AptLifecycle_EnforcesScopeReplayRotationReplacementAndRuntimeTrust()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
        var (administratorId, siteId, siteGroupId) = await ResolveContextAsync(connectionString);
        var administration = new DeviceAdministrationService(
            new PostgresDeviceAdministrationRepository(connectionString),
            TimeProvider.System);
        var runtimeTrust = new CentralPmsRbacRepository(connectionString);

        var registered = await administration.RegisterAsync(
            new("PITX APT governed proof", DeviceTypes.AssistedPaymentTerminal,
                "Disposable lifecycle proof", siteId, siteGroupId, "APT-PROOF-01"),
            administratorId, Guid.NewGuid(), CancellationToken.None);
        registered.LifecycleStatus.Should().Be("REGISTERED");
        registered.DeviceType.Should().Be(DeviceTypes.AssistedPaymentTerminal);

        var firstProvisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        firstProvisioning.ProvisioningCredential.Should().HaveLength(64);

        var wrongContext = async () => await administration.EnrollAsync(
            new(firstProvisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "WRONG-TERMINAL"),
            new string('1', 64), Guid.NewGuid(), CancellationToken.None);
        (await wrongContext.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("ENROLLMENT_CONTEXT_MISMATCH");

        var wrongReferenceProvisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        var wrongReference = async () => await administration.EnrollAsync(
            new(wrongReferenceProvisioning.ProvisioningCredential, $"{registered.DeviceReference}-OTHER",
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "APT-PROOF-01"),
            new string('2', 64), Guid.NewGuid(), CancellationToken.None);
        (await wrongReference.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("ENROLLMENT_CONTEXT_MISMATCH");

        var wrongTypeProvisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        var wrongType = async () => await administration.EnrollAsync(
            new(wrongTypeProvisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.OperatorConsole, siteId, siteGroupId, "APT-PROOF-01"),
            null, Guid.NewGuid(), CancellationToken.None);
        (await wrongType.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("ENROLLMENT_CONTEXT_MISMATCH");

        var wrongScopeProvisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        var wrongScope = async () => await administration.EnrollAsync(
            new(wrongScopeProvisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, Guid.NewGuid(), siteGroupId, "APT-PROOF-01"),
            new string('3', 64), Guid.NewGuid(), CancellationToken.None);
        (await wrongScope.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("ENROLLMENT_CONTEXT_MISMATCH");

        var provisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        var fingerprintV1 = new string('a', 64);
        var enrolled = await administration.EnrollAsync(
            new(provisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "APT-PROOF-01"),
            fingerprintV1, Guid.NewGuid(), CancellationToken.None);
        enrolled.CredentialVersion.Should().Be(1);
        enrolled.RuntimeCredential.Should().BeNull();

        var referenceV1 = $"device-mtls-sha256:{fingerprintV1}";
        var trustV1 = await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV1, CancellationToken.None);
        trustV1.Should().NotBeNull();
        trustV1!.SiteIds.Should().ContainSingle().Which.Should().Be(siteId);
        var preCashAuthorization = await runtimeTrust.GetAptDeviceAuthorizationAsync(
            enrolled.ServiceIdentityId, siteId, CancellationToken.None);
        preCashAuthorization.Should().NotBeNull();
        preCashAuthorization!.Active.Should().BeTrue();
        preCashAuthorization.SiteAssigned.Should().BeTrue();
        preCashAuthorization.TerminalIdentifier.Should().Be("APT-PROOF-01");
        preCashAuthorization.OwningServiceName.Should().Be("ExitPass.AssistedPaymentTerminal");
        var suspendedSession = await SeedActiveAptSessionAsync(connectionString, enrolled.ServiceIdentityId);

        var replay = async () => await administration.EnrollAsync(
            new(provisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "APT-PROOF-01"),
            fingerprintV1, Guid.NewGuid(), CancellationToken.None);
        (await replay.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("PROVISIONING_CREDENTIAL_ALREADY_USED");

        (await administration.SuspendAsync(registered.DeviceId, "SECURITY_REVIEW", administratorId,
            Guid.NewGuid(), CancellationToken.None)).LifecycleStatus.Should().Be("SUSPENDED");
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV1, CancellationToken.None))
            .Should().BeNull();
        (await ReadSessionStatusAsync(connectionString, suspendedSession)).Should().Be("REVOKED");

        (await administration.ReactivateAsync(registered.DeviceId, "SECURITY_REVIEW_COMPLETE", administratorId,
            Guid.NewGuid(), CancellationToken.None)).LifecycleStatus.Should().Be("ACTIVE");
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV1, CancellationToken.None))
            .Should().NotBeNull();
        var rotatedSession = await SeedActiveAptSessionAsync(connectionString, enrolled.ServiceIdentityId);

        var rotation = await administration.RotateAsync(registered.DeviceId, "SCHEDULED_ROTATION", administratorId,
            Guid.NewGuid(), CancellationToken.None);
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV1, CancellationToken.None))
            .Should().BeNull("rotation invalidates the previous credential before new material is accepted");
        (await ReadSessionStatusAsync(connectionString, rotatedSession)).Should().Be("REVOKED");

        var fingerprintV2 = new string('b', 64);
        var rotated = await administration.EnrollAsync(
            new(rotation.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "APT-PROOF-01"),
            fingerprintV2, Guid.NewGuid(), CancellationToken.None);
        rotated.CredentialVersion.Should().Be(2);
        var referenceV2 = $"device-mtls-sha256:{fingerprintV2}";
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV2, CancellationToken.None))
            .Should().NotBeNull();
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV1, CancellationToken.None))
            .Should().BeNull();

        var replacement = await administration.ReplaceAsync(registered.DeviceId,
            new("PITX APT governed replacement", null, null, null, "APT-PROOF-02", "HARDWARE_REPLACEMENT"),
            administratorId, Guid.NewGuid(), CancellationToken.None);
        replacement.ReplacedDevice.LifecycleStatus.Should().Be("REPLACED");
        replacement.ReplacementDevice.SiteId.Should().Be(siteId);
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV2, CancellationToken.None))
            .Should().BeNull();

        var fingerprintV3 = new string('c', 64);
        var replacementEnrollment = await administration.EnrollAsync(
            new(replacement.Provisioning.ProvisioningCredential,
                replacement.ReplacementDevice.DeviceReference, DeviceTypes.AssistedPaymentTerminal,
                siteId, siteGroupId, "APT-PROOF-02"), fingerprintV3, Guid.NewGuid(), CancellationToken.None);
        replacementEnrollment.CredentialVersion.Should().Be(1);
        var referenceV3 = $"device-mtls-sha256:{fingerprintV3}";
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV3, CancellationToken.None))
            .Should().NotBeNull();

        var oldDetails = await administration.GetAsync(registered.DeviceId, CancellationToken.None);
        oldDetails.ReplacementDeviceId.Should().Be(replacement.ReplacementDevice.DeviceId);
        var newDetails = await administration.GetAsync(replacement.ReplacementDevice.DeviceId, CancellationToken.None);
        newDetails.PredecessorDeviceId.Should().Be(registered.DeviceId);

        (await administration.RevokeAsync(replacement.ReplacementDevice.DeviceId, "DEVICE_RETIRED", administratorId,
            Guid.NewGuid(), CancellationToken.None)).LifecycleStatus.Should().Be("REVOKED");
        (await runtimeTrust.GetGovernedDeviceAuthenticationAsync(referenceV3, CancellationToken.None))
            .Should().BeNull();
        var terminalReactivation = async () => await administration.ReactivateAsync(
            replacement.ReplacementDevice.DeviceId, "NOT_ALLOWED", administratorId,
            Guid.NewGuid(), CancellationToken.None);
        (await terminalReactivation.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("DEVICE_LIFECYCLE_TRANSITION_INVALID");

        await AssertAuditCoverageAndNoSecretsAsync(connectionString, registered.DeviceId,
            ["DeviceRegister", "DeviceProvision", "DeviceEnroll", "DeviceSuspend",
                "DeviceReactivate", "DeviceRotate", "DeviceReplace"],
            firstProvisioning.ProvisioningCredential, provisioning.ProvisioningCredential,
            rotation.ProvisioningCredential);
        await AssertAuditCoverageAndNoSecretsAsync(connectionString, replacement.ReplacementDevice.DeviceId,
            ["DeviceRegisterReplacement", "DeviceEnroll", "DeviceRevoke"],
            replacement.Provisioning.ProvisioningCredential);
    }

    [Fact]
    public async Task OperatorConsoleBinding_SurvivesApplicationRecreationAndFollowsCanonicalCredentialLifecycle()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
        var (administratorId, siteId, siteGroupId) = await ResolveContextAsync(connectionString);
        var administration = new DeviceAdministrationService(
            new PostgresDeviceAdministrationRepository(connectionString), TimeProvider.System);

        var registered = await administration.RegisterAsync(
            new("PITX Operator Console governed proof", DeviceTypes.OperatorConsole,
                null, siteId, siteGroupId, "OC-PROOF-01"),
            administratorId, Guid.NewGuid(), CancellationToken.None);
        var provisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        DeviceEnrollmentResultDto enrolled;
        string browserCookie;
        await using (var firstApplication = new CustomWebApplicationFactory())
        {
            using var client = firstApplication.CreateClient();
            using var response = await client.PostAsJsonAsync(
                "/v1/devices/enrollment",
                new EnrollDeviceRequest(provisioning.ProvisioningCredential, registered.DeviceReference,
                    DeviceTypes.OperatorConsole, siteId, siteGroupId, "OC-PROOF-01"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl?.NoStore.Should().BeTrue();
            enrolled = (await response.Content.ReadFromJsonAsync<DeviceEnrollmentResultDto>())!;
            enrolled.Should().NotBeNull();
            enrolled.RuntimeCredential.Should().NotBeNullOrWhiteSpace();
            browserCookie = await EstablishBrowserCookieAsync(firstApplication, enrolled.RuntimeCredential!);
            var initialStatus = await ReadBrowserBindingStatusAsync(firstApplication, browserCookie);
            initialStatus.StatusCode.Should().Be(HttpStatusCode.NoContent);
            initialStatus.RefreshedCookie.Should().StartWith(
                $"{OperatorConsoleDeviceBindingCookie.CookieName}=");
        }

        enrolled.RuntimeCredentialType.Should().Be("BROWSER_KEY");
        var cookieCredential = browserCookie[(browserCookie.IndexOf('=') + 1)..];
        var expectedHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(cookieCredential))).ToLowerInvariant();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT binding.device_status::text,binding.browser_key_thumbprint,
                   assignment.assignment_type::text,assignment.assignment_status::text,
                   EXISTS (SELECT 1 FROM identity.device_lifecycle_history history
                           WHERE history.device_id=binding.operator_device_binding_id
                             AND history.action_code='ENROLL')
            FROM operator_console.operator_device_bindings binding
            JOIN sites.device_assignments assignment
              ON assignment.service_identity_id=binding.service_identity_id
             AND assignment.unassigned_at IS NULL
            WHERE binding.operator_device_binding_id=@device_id;
            """, connection);
        command.Parameters.AddWithValue("device_id", registered.DeviceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("ACTIVE");
        reader.GetString(1).Should().Be(expectedHash);
        reader.GetString(2).Should().Be("SERVICE_PRINCIPAL");
        reader.GetString(3).Should().Be("ACTIVE");
        reader.GetBoolean(4).Should().BeTrue();
        await reader.CloseAsync();

        await using (var ageBinding = new NpgsqlCommand("""
            UPDATE operator_console.operator_device_bindings
            SET created_at=now()-interval '5 years',
                updated_at=now()-interval '5 years',
                last_seen_at=now()-interval '5 years'
            WHERE operator_device_binding_id=@device_id;
            """, connection))
        {
            ageBinding.Parameters.AddWithValue("device_id", registered.DeviceId);
            (await ageBinding.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await using var restartedApplication = new CustomWebApplicationFactory();
        var afterRestart = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        afterRestart.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a multi-year-old opaque proof remains valid after service recreation while canonical trust is current");
        afterRestart.RefreshedCookie!.ToLowerInvariant().Should().Contain("max-age=34560000",
            "successful validation renews browser proof retention without imposing a server-side trust TTL");

        await using (var verifyOldBinding = new NpgsqlCommand("""
            SELECT last_seen_at < now()-interval '4 years',device_status::text
            FROM operator_console.operator_device_bindings
            WHERE operator_device_binding_id=@device_id;
            """, connection))
        {
            verifyOldBinding.Parameters.AddWithValue("device_id", registered.DeviceId);
            await using var oldBindingReader = await verifyOldBinding.ExecuteReaderAsync();
            (await oldBindingReader.ReadAsync()).Should().BeTrue();
            oldBindingReader.GetBoolean(0).Should().BeTrue();
            oldBindingReader.GetString(1).Should().Be("ACTIVE");
        }

        await using (var expireCredential = new NpgsqlCommand("""
            UPDATE identity.device_credential_history
            SET expires_at=now()-interval '1 minute'
            WHERE device_id=@device_id AND credential_status='ACTIVE';
            """, connection))
        {
            expireCredential.Parameters.AddWithValue("device_id", registered.DeviceId);
            (await expireCredential.ExecuteNonQueryAsync()).Should().Be(1);
        }
        var explicitlyExpired = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        explicitlyExpired.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        explicitlyExpired.Body.Should().Contain("OPERATOR_DEVICE_BINDING_EXPIRED");
        await using (var restoreCredential = new NpgsqlCommand("""
            UPDATE identity.device_credential_history SET expires_at=NULL
            WHERE device_id=@device_id AND credential_status='ACTIVE';
            """, connection))
        {
            restoreCredential.Parameters.AddWithValue("device_id", registered.DeviceId);
            (await restoreCredential.ExecuteNonQueryAsync()).Should().Be(1);
        }
        (await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        await using (var expireServiceIdentity = new NpgsqlCommand("""
            UPDATE identity.service_identities
            SET effective_to=now()-interval '1 minute'
            WHERE service_identity_id=(
                SELECT service_identity_id FROM identity.devices WHERE device_id=@device_id);
            """, connection))
        {
            expireServiceIdentity.Parameters.AddWithValue("device_id", registered.DeviceId);
            (await expireServiceIdentity.ExecuteNonQueryAsync()).Should().Be(1);
        }
        var serviceIdentityExpired = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        serviceIdentityExpired.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        serviceIdentityExpired.Body.Should().Contain("OPERATOR_DEVICE_BINDING_REVOKED");
        await using (var restoreServiceIdentity = new NpgsqlCommand("""
            UPDATE identity.service_identities
            SET effective_to=NULL
            WHERE service_identity_id=(
                SELECT service_identity_id FROM identity.devices WHERE device_id=@device_id);
            """, connection))
        {
            restoreServiceIdentity.Parameters.AddWithValue("device_id", registered.DeviceId);
            (await restoreServiceIdentity.ExecuteNonQueryAsync()).Should().Be(1);
        }
        (await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        await using (var invalidateAssignment = new NpgsqlCommand("""
            UPDATE sites.device_assignments
            SET assignment_status='SUSPENDED'
            WHERE service_identity_id=(
                    SELECT service_identity_id FROM identity.devices WHERE device_id=@device_id)
              AND site_id=@site_id AND assignment_type='SERVICE_PRINCIPAL'
              AND assignment_status='ACTIVE' AND unassigned_at IS NULL;
            """, connection))
        {
            invalidateAssignment.Parameters.AddWithValue("device_id", registered.DeviceId);
            invalidateAssignment.Parameters.AddWithValue("site_id", siteId);
            (await invalidateAssignment.ExecuteNonQueryAsync()).Should().Be(1);
        }
        var invalidAssignment = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        invalidAssignment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        invalidAssignment.Body.Should().Contain("OPERATOR_DEVICE_OUTSIDE_AUTHORIZED_SITE");
        await using (var restoreAssignment = new NpgsqlCommand("""
            UPDATE sites.device_assignments
            SET assignment_status='ACTIVE'
            WHERE service_identity_id=(
                    SELECT service_identity_id FROM identity.devices WHERE device_id=@device_id)
              AND site_id=@site_id AND assignment_type='SERVICE_PRINCIPAL'
              AND assignment_status='SUSPENDED' AND unassigned_at IS NULL;
            """, connection))
        {
            restoreAssignment.Parameters.AddWithValue("device_id", registered.DeviceId);
            restoreAssignment.Parameters.AddWithValue("site_id", siteId);
            (await restoreAssignment.ExecuteNonQueryAsync()).Should().Be(1);
        }
        (await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await administration.SuspendAsync(registered.DeviceId, "WORKSTATION_LOCKDOWN", administratorId,
            Guid.NewGuid(), CancellationToken.None)).LifecycleStatus.Should().Be("SUSPENDED");
        var suspended = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        suspended.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        suspended.Body.Should().Contain("OPERATOR_DEVICE_BINDING_REVOKED");
        suspended.RefreshedCookie.Should().BeNull(
            "temporary suspension must not delete or replace the existing browser proof");

        var suspendedState = await ReadOperatorBindingLifecycleStateAsync(connection, registered.DeviceId);
        suspendedState.DeviceLifecycle.Should().Be("SUSPENDED");
        suspendedState.ServiceIdentityStatus.Should().Be("SUSPENDED");
        suspendedState.CredentialVersion.Should().Be(1);
        suspendedState.CredentialStatus.Should().Be("ACTIVE");
        suspendedState.BindingStatus.Should().Be("SUSPENDED");
        suspendedState.ProofHash.Should().Be(expectedHash);
        suspendedState.AssignmentStatus.Should().Be("SUSPENDED");

        (await administration.ReactivateAsync(registered.DeviceId, "WORKSTATION_LOCKDOWN_CLEARED", administratorId,
            Guid.NewGuid(), CancellationToken.None)).LifecycleStatus.Should().Be("ACTIVE");
        var reactivated = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        reactivated.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "reactivation restores the same browser proof without enrollment or credential rotation");
        reactivated.RefreshedCookie.Should().StartWith(
            $"{OperatorConsoleDeviceBindingCookie.CookieName}=");

        var reactivatedState = await ReadOperatorBindingLifecycleStateAsync(connection, registered.DeviceId);
        reactivatedState.DeviceLifecycle.Should().Be("ACTIVE");
        reactivatedState.ServiceIdentityStatus.Should().Be("ACTIVE");
        reactivatedState.CredentialVersion.Should().Be(1);
        reactivatedState.CredentialStatus.Should().Be("ACTIVE");
        reactivatedState.BindingStatus.Should().Be("ACTIVE");
        reactivatedState.ProofHash.Should().Be(expectedHash);
        reactivatedState.AssignmentStatus.Should().Be("ACTIVE");

        var rotation = await administration.RotateAsync(registered.DeviceId, "SCHEDULED_ROTATION", administratorId,
            Guid.NewGuid(), CancellationToken.None);
        var duringRotation = await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie);
        duringRotation.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        duringRotation.Body.Should().Contain("OPERATOR_DEVICE_CREDENTIAL_ROTATION_REQUIRED",
            "a valid unconsumed rotation package must make the invalidated predecessor proof actionable");

        var rotatedEnrollment = await administration.EnrollAsync(
            new(rotation.ProvisioningCredential, registered.DeviceReference, DeviceTypes.OperatorConsole,
                siteId, siteGroupId, "OC-PROOF-01"), null, Guid.NewGuid(), CancellationToken.None);
        var rotatedCookie = await EstablishBrowserCookieAsync(restartedApplication, rotatedEnrollment.RuntimeCredential!);
        (await ReadBrowserBindingStatusAsync(restartedApplication, browserCookie)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "credential rotation invalidates the predecessor browser proof");
        (await ReadBrowserBindingStatusAsync(restartedApplication, rotatedCookie)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var rotatedState = await ReadOperatorBindingLifecycleStateAsync(connection, registered.DeviceId);
        rotatedState.DeviceLifecycle.Should().Be("ACTIVE");
        rotatedState.CredentialVersion.Should().Be(2);
        rotatedState.CredentialStatus.Should().Be("ACTIVE");

        var replacement = await administration.ReplaceAsync(registered.DeviceId,
            new("PITX Operator Console replacement proof", null, null, null,
                "OC-PROOF-02", "HARDWARE_REPLACEMENT"),
            administratorId, Guid.NewGuid(), CancellationToken.None);
        var replaced = await ReadBrowserBindingStatusAsync(restartedApplication, rotatedCookie);
        replaced.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        replaced.Body.Should().Contain("OPERATOR_DEVICE_REPLACEMENT_ENROLLMENT_REQUIRED",
            "the predecessor proof must expose only the valid successor replacement enrollment path");

        var predecessorDetails = await administration.GetAsync(registered.DeviceId, CancellationToken.None);
        predecessorDetails.Device.LifecycleStatus.Should().Be("REPLACED");
        predecessorDetails.ReplacementDeviceId.Should().Be(replacement.ReplacementDevice.DeviceId);
        var successorDetails = await administration.GetAsync(
            replacement.ReplacementDevice.DeviceId, CancellationToken.None);
        successorDetails.Device.LifecycleStatus.Should().Be("PROVISIONING_PENDING");
        successorDetails.PredecessorDeviceId.Should().Be(registered.DeviceId);

        var replacementEnrollment = await administration.EnrollAsync(
            new(replacement.Provisioning.ProvisioningCredential,
                replacement.ReplacementDevice.DeviceReference, DeviceTypes.OperatorConsole,
                siteId, siteGroupId, "OC-PROOF-02"), null, Guid.NewGuid(), CancellationToken.None);
        var replacementCookie = await EstablishBrowserCookieAsync(
            restartedApplication, replacementEnrollment.RuntimeCredential!);
        var predecessorAfterSuccessorEnrollment = await ReadBrowserBindingStatusAsync(
            restartedApplication, rotatedCookie);
        predecessorAfterSuccessorEnrollment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        predecessorAfterSuccessorEnrollment.Body.Should().Contain("OPERATOR_DEVICE_BINDING_REVOKED",
            "the consumed replacement package must not make the predecessor proof reusable");
        (await ReadBrowserBindingStatusAsync(restartedApplication, replacementCookie)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var successorState = await ReadOperatorBindingLifecycleStateAsync(
            connection, replacement.ReplacementDevice.DeviceId);
        successorState.DeviceLifecycle.Should().Be("ACTIVE");
        successorState.CredentialVersion.Should().Be(1);
        successorState.CredentialStatus.Should().Be("ACTIVE");

        (await administration.RevokeAsync(replacement.ReplacementDevice.DeviceId, "WORKSTATION_RETIRED",
            administratorId, Guid.NewGuid(), CancellationToken.None)).LifecycleStatus.Should().Be("REVOKED");
        var revoked = await ReadBrowserBindingStatusAsync(restartedApplication, replacementCookie);
        revoked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoked.Body.Should().Contain("OPERATOR_DEVICE_BINDING_REVOKED");
    }

    [Fact]
    public async Task ProvisioningCancellation_InvalidatesOutstandingCredentialAndReturnsInitialDeviceToRegistered()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
        var (administratorId, siteId, siteGroupId) = await ResolveContextAsync(connectionString);
        var administration = new DeviceAdministrationService(
            new PostgresDeviceAdministrationRepository(connectionString), TimeProvider.System);
        var registered = await administration.RegisterAsync(
            new("Cancelled APT proof", DeviceTypes.AssistedPaymentTerminal, null,
                siteId, siteGroupId, "APT-CANCELLED-01"),
            administratorId, Guid.NewGuid(), CancellationToken.None);
        var provisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);

        var cancelled = await administration.CancelProvisioningAsync(
            registered.DeviceId, "ADMINISTRATOR_CANCELLED", administratorId,
            Guid.NewGuid(), CancellationToken.None);
        cancelled.LifecycleStatus.Should().Be("REGISTERED");
        cancelled.LastProvisioningStatus.Should().Be("CANCELLED");

        var replay = async () => await administration.EnrollAsync(
            new(provisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "APT-CANCELLED-01"),
            new string('e', 64), Guid.NewGuid(), CancellationToken.None);
        (await replay.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("PROVISIONING_CREDENTIAL_ALREADY_USED");

        var details = await administration.GetAsync(registered.DeviceId, CancellationToken.None);
        details.ProvisioningHistory.Should().ContainSingle(item =>
            item.Status == "CANCELLED" && item.CancelledAt.HasValue);
        await AssertAuditCoverageAndNoSecretsAsync(connectionString, registered.DeviceId,
            ["DeviceRegister", "DeviceProvision", "DeviceCancelProvisioning", "DeviceEnrollRejected"],
            provisioning.ProvisioningCredential);
    }

    [Fact]
    public async Task ExpiredProvisioningCredential_FailsClosedAndIsAudited()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString();
        var (administratorId, siteId, siteGroupId) = await ResolveContextAsync(connectionString);
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        var administration = new DeviceAdministrationService(
            new PostgresDeviceAdministrationRepository(connectionString), clock);
        var registered = await administration.RegisterAsync(
            new("Expired APT proof", DeviceTypes.AssistedPaymentTerminal, null,
                siteId, siteGroupId, "APT-EXPIRED-01"),
            administratorId, Guid.NewGuid(), CancellationToken.None);
        var provisioning = await administration.ProvisionAsync(
            registered.DeviceId, administratorId, Guid.NewGuid(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(16));

        var expired = async () => await administration.EnrollAsync(
            new(provisioning.ProvisioningCredential, registered.DeviceReference,
                DeviceTypes.AssistedPaymentTerminal, siteId, siteGroupId, "APT-EXPIRED-01"),
            new string('d', 64), Guid.NewGuid(), CancellationToken.None);
        (await expired.Should().ThrowAsync<DeviceAdministrationException>())
            .Which.ErrorCode.Should().Be("PROVISIONING_CREDENTIAL_EXPIRED");

        var details = await administration.GetAsync(registered.DeviceId, CancellationToken.None);
        details.Device.LastProvisioningStatus.Should().Be("EXPIRED");
        details.RecentAdministrativeActions.Should().Contain(action =>
            action.Action == "ENROLL_REJECTED" && action.ReasonCode == "PROVISIONING_CREDENTIAL_EXPIRED");
    }

    private static async Task<string> EstablishBrowserCookieAsync(
        CustomWebApplicationFactory application,
        string runtimeCredential)
    {
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operator-console/device-binding/establish")
        {
            Content = JsonContent.Create(new { proof = runtimeCredential })
        };
        request.Headers.Add("Origin", "https://localhost");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value =>
            value.StartsWith($"{OperatorConsoleDeviceBindingCookie.CookieName}=", StringComparison.Ordinal));
        setCookie.ToLowerInvariant().Should().Contain("httponly")
            .And.Contain("secure")
            .And.Contain("samesite=strict");
        return setCookie.Split(';', 2)[0];
    }

    private static async Task<BrowserBindingStatus> ReadBrowserBindingStatusAsync(
        CustomWebApplicationFactory application,
        string browserCookie)
    {
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/operator-console/device-binding/status");
        request.Headers.TryAddWithoutValidation("Cookie", browserCookie).Should().BeTrue();
        using var response = await client.SendAsync(request);
        var refreshedCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.SingleOrDefault(value => value.StartsWith(
                $"{OperatorConsoleDeviceBindingCookie.CookieName}=", StringComparison.Ordinal))
            : null;
        return new BrowserBindingStatus(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            refreshedCookie);
    }

    private static async Task<OperatorBindingLifecycleState> ReadOperatorBindingLifecycleStateAsync(
        NpgsqlConnection connection,
        Guid deviceId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT device.lifecycle_status::text,service.identity_status::text,
                   credential.credential_version,credential.credential_status::text,
                   binding.device_status::text,binding.browser_key_thumbprint,
                   assignment.assignment_status::text
            FROM identity.devices device
            JOIN identity.service_identities service
              ON service.service_identity_id=device.service_identity_id
            JOIN identity.device_credential_history credential
              ON credential.device_id=device.device_id
             AND credential.credential_version=device.credential_version
            JOIN operator_console.operator_device_bindings binding
              ON binding.operator_device_binding_id=device.device_id
            JOIN sites.device_assignments assignment
              ON assignment.service_identity_id=device.service_identity_id
             AND assignment.unassigned_at IS NULL
            WHERE device.device_id=@device_id;
            """, connection);
        command.Parameters.AddWithValue("device_id", deviceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new OperatorBindingLifecycleState(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static async Task<(Guid AdministratorId, Guid SiteId, Guid SiteGroupId)> ResolveContextAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT user_record.user_id,site.site_id,site.site_group_id
            FROM identity.users user_record
            JOIN identity.user_roles assignment
              ON assignment.user_id=user_record.user_id
             AND assignment.assignment_status='ACTIVE' AND assignment.revoked_at IS NULL
            JOIN identity.roles role ON role.role_id=assignment.role_id
             AND role.role_code='SYSTEM_ADMINISTRATOR' AND role.role_status='ACTIVE'
            CROSS JOIN LATERAL (
                SELECT candidate.site_id,candidate.site_group_id
                FROM sites.sites candidate
                JOIN sites.site_groups site_group ON site_group.site_group_id=candidate.site_group_id
                WHERE candidate.site_status='ACTIVE' AND site_group.site_group_status='ACTIVE'
                LIMIT 1) site
            WHERE user_record.user_status='ACTIVE'
            LIMIT 1;
            """, connection);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync()) return (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
        }

        var administratorId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");
        await using var seed = new NpgsqlCommand("""
            INSERT INTO identity.users (
                user_id,username,display_name,user_type,user_status,effective_from,created_at,updated_at)
            VALUES (@administrator_id,@username,'Device Administration Integration Administrator',
                    'INTERNAL_ADMIN','ACTIVE',now(),now(),now());

            INSERT INTO identity.user_roles (
                user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_at,effective_from,created_at,updated_at)
            SELECT gen_random_uuid(),@administrator_id,role_id,'ACTIVE','INTEGRATION_TEST',
                   now(),now(),now(),now()
            FROM identity.roles WHERE role_code='SYSTEM_ADMINISTRATOR' AND role_status='ACTIVE';

            INSERT INTO sites.site_groups (
                site_group_id,site_group_code,site_group_name,timezone_name,default_currency_code,
                site_group_status,effective_from,created_at,updated_at)
            VALUES (@site_group_id,@site_group_code,'Device Administration Integration Group',
                    'Asia/Manila','PHP','ACTIVE',now(),now(),now());

            INSERT INTO sites.sites (
                site_id,site_group_id,site_code,site_name,site_type,timezone_name,country_code,
                site_status,effective_from,created_at,updated_at)
            VALUES (@site_id,@site_group_id,@site_code,'Device Administration Integration Site',
                    'TERMINAL','Asia/Manila','PH','ACTIVE',now(),now(),now());
            """, connection);
        seed.Parameters.AddWithValue("administrator_id", administratorId);
        seed.Parameters.AddWithValue("username", $"device-admin-it-{suffix}");
        seed.Parameters.AddWithValue("site_group_id", siteGroupId);
        seed.Parameters.AddWithValue("site_group_code", $"DA-IT-G-{suffix[..12]}");
        seed.Parameters.AddWithValue("site_id", siteId);
        seed.Parameters.AddWithValue("site_code", $"DA-IT-S-{suffix[..12]}");
        var affected = await seed.ExecuteNonQueryAsync();
        if (affected != 4) throw new InvalidOperationException("Failed to seed the isolated System Administrator and Site fixture.");
        return (administratorId, siteId, siteGroupId);
    }

    private static async Task AssertAuditCoverageAndNoSecretsAsync(
        string connectionString,
        Guid deviceId,
        IReadOnlyCollection<string> expectedEventTypes,
        params string[] secrets)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT array_agg(event_type ORDER BY occurred_at),string_agg(summary,' ')
            FROM audit.audit_events
            WHERE target_entity_type='DEVICE' AND target_entity_id=@device_id;
            """, connection);
        command.Parameters.AddWithValue("device_id", deviceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        var eventTypes = reader.GetFieldValue<string[]>(0);
        eventTypes.Should().Contain(expectedEventTypes);
        var summaries = reader.GetString(1);
        foreach (var secret in secrets) summaries.Should().NotContain(secret);
    }

    private static async Task<Guid> SeedActiveAptSessionAsync(string connectionString, Guid deviceServiceIdentityId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO identity.users (
                user_id,username,display_name,user_type,user_status,effective_from,
                credential_version,authorization_epoch,created_at,updated_at)
            VALUES (@user_id,@username,'APT Session Revocation Proof','OPERATIONS_USER','ACTIVE',now(),
                    1,1,now(),now());
            INSERT INTO identity.local_credentials (
                local_credential_id,user_id,credential_status,password_verifier,verifier_salt,
                verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
                credential_version,activated_at,last_changed_at,created_at,updated_at)
            VALUES (@credential_id,@user_id,'ACTIVE',decode(repeat('ab',32),'hex'),
                    decode(repeat('cd',16),'hex'),'ARGON2ID',1,1,1,now(),now(),now(),now());
            INSERT INTO identity.human_sessions (
                human_session_id,session_reference,session_secret_hash,user_id,
                authentication_provider,local_credential_id,session_audience,
                device_service_identity_id,session_status,assurance_context_code,
                mfa_requirement_satisfied,authenticated_at,last_seen_at,idle_expires_at,
                absolute_expires_at,credential_version_snapshot,authorization_epoch_snapshot,
                correlation_id)
            VALUES (@session_id,gen_random_uuid(),encode(digest(gen_random_uuid()::text,'sha256'),'hex'),
                    @user_id,'LOCAL',@credential_id,'APT',@device_service_identity_id,'ACTIVE',
                    'PASSWORD',false,now(),now(),now()+interval '15 minutes',now()+interval '1 hour',
                    1,1,gen_random_uuid());
            """, connection);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("credential_id", credentialId);
        command.Parameters.AddWithValue("username", $"apt-session-proof-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("device_service_identity_id", deviceServiceIdentityId);
        (await command.ExecuteNonQueryAsync()).Should().Be(3);
        return sessionId;
    }

    private static async Task<Guid> SeedOrdinaryUserAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var userId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO identity.users (
                user_id,username,display_name,user_type,user_status,effective_from,created_at,updated_at)
            VALUES (@user_id,@username,'Device Administration Unauthorized User',
                    'OPERATIONS_USER','ACTIVE',now(),now(),now());
            INSERT INTO identity.user_roles (
                user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_at,effective_from,created_at,updated_at)
            SELECT gen_random_uuid(),@user_id,role_id,'ACTIVE','INTEGRATION_TEST',
                   now(),now(),now(),now()
            FROM identity.roles WHERE role_code='APT_CASHIER_OPERATOR' AND role_status='ACTIVE';
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("username", $"device-admin-denied-{Guid.NewGuid():N}");
        (await command.ExecuteNonQueryAsync()).Should().Be(2);
        return userId;
    }

    private static async Task<string> ReadSessionStatusAsync(string connectionString, Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT session_status::text FROM identity.human_sessions WHERE human_session_id=@session_id;",
            connection);
        command.Parameters.AddWithValue("session_id", sessionId);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("APT session was not found."));
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private sealed record BrowserBindingStatus(
        HttpStatusCode StatusCode,
        string Body,
        string? RefreshedCookie);

    private sealed record OperatorBindingLifecycleState(
        string DeviceLifecycle,
        string ServiceIdentityStatus,
        long CredentialVersion,
        string CredentialStatus,
        string BindingStatus,
        string ProofHash,
        string AssignmentStatus);
}
