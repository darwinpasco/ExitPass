using ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ParkingSessionInvoiceCustomerInformationServiceTests
{
    [Fact]
    public async Task Save_TrimsValuesAndPreservesInternalSpacingForSharedAuthority()
    {
        var repository = new RecordingRepository();
        var service = new ParkingSessionInvoiceCustomerInformationService(repository);

        var result = await service.SaveAsync(Command(
            customerName: "  ABC  Corporation  ",
            address: "  1 Main  Street  ",
            tin: " 123-456-789 ",
            businessStyle: " ABC Retail "), CancellationToken.None);

        result.Status.Should().Be(InvoiceCustomerInformationSaveStatus.Created);
        repository.Saved!.CustomerName.Should().Be("ABC  Corporation");
        repository.Saved.CustomerAddress.Should().Be("1 Main  Street");
        repository.Saved.CustomerTin.Should().Be("123-456-789");
        repository.Saved.BusinessStyle.Should().Be("ABC Retail");
    }

    [Fact]
    public async Task Save_RejectsAllEmptyAndWhitespaceOnlyValues()
    {
        var repository = new RecordingRepository();
        var service = new ParkingSessionInvoiceCustomerInformationService(repository);

        var result = await service.SaveAsync(Command(" ", "\t", null, "\r\n"), CancellationToken.None);

        result.Status.Should().Be(InvoiceCustomerInformationSaveStatus.Invalid);
        result.ValidationErrors.Should().Contain("customer_information_required");
        repository.Saved.Should().BeNull();
    }

    [Theory]
    [InlineData(161, 1, 1, 1, "customer_name_too_long")]
    [InlineData(1, 301, 1, 1, "customer_address_too_long")]
    [InlineData(1, 1, 41, 1, "customer_tin_too_long")]
    [InlineData(1, 1, 1, 161, "business_style_too_long")]
    public async Task Save_RejectsFieldLengthViolations(
        int nameLength,
        int addressLength,
        int tinLength,
        int styleLength,
        string expectedError)
    {
        var service = new ParkingSessionInvoiceCustomerInformationService(new RecordingRepository());

        var result = await service.SaveAsync(Command(
            new string('N', nameLength),
            new string('A', addressLength),
            new string('T', tinLength),
            new string('B', styleLength)), CancellationToken.None);

        result.Status.Should().Be(InvoiceCustomerInformationSaveStatus.Invalid);
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Fact]
    public async Task Save_RejectsInvalidActorAndVersion()
    {
        var service = new ParkingSessionInvoiceCustomerInformationService(new RecordingRepository());
        var command = Command("Name", null, null, null) with
        {
            ExpectedVersion = 0,
            Actor = new InvoiceCustomerInformationActor(Guid.NewGuid(), Guid.NewGuid(), InvoiceCustomerInformationSourceChannels.OperatorConsole)
        };

        var result = await service.SaveAsync(command, CancellationToken.None);

        result.ValidationErrors.Should().Contain(["expected_version_invalid", "server_actor_required"]);
    }

    [Fact]
    public async Task SharedService_ReadReturnsOperatorSavedAuthoritativeRecord()
    {
        var record = Record();
        var repository = new RecordingRepository { ReadRecord = record };
        var service = new ParkingSessionInvoiceCustomerInformationService(repository);

        var result = await service.ReadAsync(record.ParkingSessionId, new InvoiceCustomerInformationScope(Guid.NewGuid(), null), CancellationToken.None);

        result.Status.Should().Be(InvoiceCustomerInformationReadStatus.Found);
        result.Record.Should().Be(record);
    }

    private static SaveParkingSessionInvoiceCustomerInformationCommand Command(
        string? customerName,
        string? address,
        string? tin,
        string? businessStyle) =>
        new(
            Guid.NewGuid(), customerName, address, tin, businessStyle, null,
            new InvoiceCustomerInformationActor(Guid.NewGuid(), null, InvoiceCustomerInformationSourceChannels.OperatorConsole),
            new InvoiceCustomerInformationScope(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid());

    private static ParkingSessionInvoiceCustomerInformationRecord Record() =>
        new(Guid.NewGuid(), "ABC Corporation", "Makati City", "123-456-789", "ABC Retail", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class RecordingRepository : IParkingSessionInvoiceCustomerInformationRepository
    {
        public SaveParkingSessionInvoiceCustomerInformationCommand? Saved { get; private set; }
        public ParkingSessionInvoiceCustomerInformationRecord? ReadRecord { get; init; }

        public Task<InvoiceCustomerInformationReadResult> ReadAsync(Guid parkingSessionId, InvoiceCustomerInformationScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(new InvoiceCustomerInformationReadResult(
                ReadRecord is null ? InvoiceCustomerInformationReadStatus.NotSupplied : InvoiceCustomerInformationReadStatus.Found,
                ReadRecord));

        public Task<InvoiceCustomerInformationSaveResult> SaveAsync(SaveParkingSessionInvoiceCustomerInformationCommand command, CancellationToken cancellationToken)
        {
            Saved = command;
            return Task.FromResult(new InvoiceCustomerInformationSaveResult(
                InvoiceCustomerInformationSaveStatus.Created,
                Record(),
                []));
        }
    }
}
