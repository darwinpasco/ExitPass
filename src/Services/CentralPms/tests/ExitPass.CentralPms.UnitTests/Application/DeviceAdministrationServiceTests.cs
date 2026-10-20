using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class DeviceAdministrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 2, 30, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Correlation = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid SiteGroup = Guid.NewGuid();

    [Fact]
    public async Task Provision_IssuesHighEntropyShortLivedCredentialAndPersistsOnlyHash()
    {
        var repository = Substitute.For<IDeviceAdministrationRepository>();
        var device = Summary(DeviceTypes.OperatorConsole);
        repository.IssueProvisioningAsync(Arg.Any<DeviceProvisioningCommand>(), Arg.Any<CancellationToken>())
            .Returns(device);
        var service = Sut(repository);

        var result = await service.ProvisionAsync(device.DeviceId, Actor, Correlation, CancellationToken.None);

        result.ProvisioningCredential.Length.Should().BeGreaterThanOrEqualTo(64);
        result.ExpiresAt.Should().Be(Now.AddMinutes(15));
        result.DisplayPolicy.Should().Contain("Shown once");
        await repository.Received(1).IssueProvisioningAsync(
            Arg.Is<DeviceProvisioningCommand>(command =>
                command.TokenHash.Length == 64 &&
                command.TokenHash != result.ProvisioningCredential &&
                command.Purpose == "ENROLLMENT" &&
                command.ExpiresAt == Now.AddMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enroll_OperatorConsole_IssuesDistinctRuntimeCredentialAndPersistsOnlyFingerprint()
    {
        var repository = Substitute.For<IDeviceAdministrationRepository>();
        DeviceEnrollmentCommand? captured = null;
        string? runtime = null;
        repository.EnrollAsync(Arg.Do<DeviceEnrollmentCommand>(value => captured = value), Arg.Do<string?>(value => runtime = value), Arg.Any<CancellationToken>())
            .Returns(call => new DeviceEnrollmentResultDto(Guid.NewGuid(), "DEV-1", Guid.NewGuid(), DeviceTypes.OperatorConsole, Site, SiteGroup, 1, call.ArgAt<string?>(1), "BROWSER_KEY", "Shown once", Correlation));
        var service = Sut(repository);
        var provisioning = new string('p', 64);

        var result = await service.EnrollAsync(
            new(provisioning, "DEV-1", DeviceTypes.OperatorConsole, Site, SiteGroup, null),
            null,
            Correlation,
            CancellationToken.None);

        result.RuntimeCredential.Should().NotBeNullOrWhiteSpace();
        result.RuntimeCredential.Should().Be(runtime);
        result.RuntimeCredential.Should().NotBe(provisioning);
        captured!.TokenHash.Should().HaveLength(64).And.NotBe(provisioning);
        captured.CredentialFingerprint.Should().HaveLength(64).And.NotBe(runtime);
        captured.CredentialReference.Should().StartWith("device-browser-sha256:");
    }

    [Fact]
    public async Task Enroll_Apt_RequiresPresentedCertificateFingerprint()
    {
        var service = Sut(Substitute.For<IDeviceAdministrationRepository>());
        var action = () => service.EnrollAsync(
            new(new string('p', 64), "DEV-1", DeviceTypes.AssistedPaymentTerminal, Site, SiteGroup, "APT-01", "APT-HARDWARE-001"),
            null,
            Correlation,
            CancellationToken.None);

        await action.Should().ThrowAsync<DeviceAdministrationException>()
            .Where(exception => exception.ErrorCode == "APT_CLIENT_CERTIFICATE_REQUIRED");
    }

    [Fact]
    public async Task Register_Apt_RequiresTerminalSerialNumberBeforePersistence()
    {
        var repository = Substitute.For<IDeviceAdministrationRepository>();
        var service = Sut(repository);
        var action = () => service.RegisterAsync(
            new("PITX APT", DeviceTypes.AssistedPaymentTerminal, null, Site, SiteGroup, "APT-01"),
            Actor,
            Correlation,
            CancellationToken.None);

        await action.Should().ThrowAsync<DeviceAdministrationException>()
            .Where(exception => exception.ErrorCode == "APT_TERMINAL_SERIAL_NUMBER_REQUIRED");
        await repository.DidNotReceive().RegisterAsync(Arg.Any<DeviceRegistrationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_Apt_RequiresTerminalIdentifierBeforePersistence()
    {
        var repository = Substitute.For<IDeviceAdministrationRepository>();
        var service = Sut(repository);
        var action = () => service.RegisterAsync(
            new("PITX APT", DeviceTypes.AssistedPaymentTerminal, null, Site, SiteGroup, null),
            Actor,
            Correlation,
            CancellationToken.None);

        await action.Should().ThrowAsync<DeviceAdministrationException>()
            .Where(exception => exception.ErrorCode == "APT_TERMINAL_IDENTIFIER_REQUIRED");
        await repository.DidNotReceive().RegisterAsync(Arg.Any<DeviceRegistrationCommand>(), Arg.Any<CancellationToken>());
    }

    private static DeviceAdministrationService Sut(IDeviceAdministrationRepository repository) =>
        new(repository, new FixedTimeProvider(Now));

    private static DeviceSummaryDto Summary(string type) => new(
        Guid.NewGuid(), "DEV-1", "PITX Device", type, null, "TERM-1", "APT-HARDWARE-001", Guid.NewGuid(),
        Site, "PITX", "PITX", SiteGroup, "PITX-GROUP", "PITX Group", "REGISTERED", null,
        0, "NOT_ISSUED", Now, Now, 1);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
