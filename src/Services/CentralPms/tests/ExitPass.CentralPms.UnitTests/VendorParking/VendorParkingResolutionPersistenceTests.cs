using System.Reflection;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.VendorParking;

/// <summary>
/// Tests for vendor parking resolve persistence timestamp parameter binding.
/// </summary>
public sealed class VendorParkingResolutionPersistenceTests
{
    [Fact]
    public void ParkingSessionInsertParameters_ConvertEntryTimestampToUtc()
    {
        var entryTimestamp = DateTimeOffset.Parse("2026-06-16T17:30:04+08:00");
        var session = ParkingSession.Rehydrate(
            parkingSessionId: Guid.Parse("aaaaaaaa-1000-0000-0000-000000000001"),
            siteGroupId: "bbbbbbbb-1000-0000-0000-000000000001",
            siteId: "cccccccc-1000-0000-0000-000000000001",
            vendorSystemCode: "HIKCENTRAL",
            vendorSessionRef: "HIK-SESSION-1",
            identifierType: "TICKET",
            plateNumber: null,
            ticketNumber: "3519278781100",
            entryTimestamp,
            ParkingSessionStatus.PaymentRequired);

        using var command = new NpgsqlCommand();
        InvokePrivate(
            "AddParkingSessionInsertParameters",
            command,
            session,
            Guid.Parse("bbbbbbbb-1000-0000-0000-000000000001"),
            Guid.Parse("cccccccc-1000-0000-0000-000000000001"),
            Guid.Parse("dddddddd-1000-0000-0000-000000000001"),
            Guid.Parse("eeeeeeee-1000-0000-0000-000000000001"));

        Assert.Equal(entryTimestamp.ToUniversalTime(), TimestampParameter(command, "entry_at"));
        AssertNoNonUtcDateTimeOffsetParameters(command);
    }

    [Fact]
    public void TariffSnapshotInsertParameters_ConvertCalculatedAndExpiresTimestampsToUtc()
    {
        var calculatedAt = DateTimeOffset.Parse("2026-06-16T17:35:04+08:00");
        var expiresAt = DateTimeOffset.Parse("2026-06-16T18:35:04+08:00");
        var tariffSnapshot = TariffSnapshot.Rehydrate(
            tariffSnapshotId: Guid.Parse("aaaaaaaa-2000-0000-0000-000000000001"),
            parkingSessionId: Guid.Parse("bbbbbbbb-2000-0000-0000-000000000001"),
            sourceType: TariffSnapshotSourceType.Base,
            grossAmount: 50m,
            statutoryDiscountAmount: 0m,
            couponDiscountAmount: 0m,
            netPayable: 50m,
            currencyCode: "PHP",
            baseFeeAmount: 50m,
            tariffVersionReference: "HIK-TARIFF-1",
            policyVersionReference: null,
            calculatedAt,
            expiresAt,
            snapshotStatus: TariffSnapshotStatus.Active,
            supersedesTariffSnapshotId: null,
            consumedByPaymentAttemptId: null);

        using var command = new NpgsqlCommand();
        InvokePrivate(
            "AddTariffSnapshotInsertParameters",
            command,
            tariffSnapshot,
            Guid.Parse("cccccccc-2000-0000-0000-000000000001"),
            "HIK-TARIFF-1",
            Guid.Parse("dddddddd-2000-0000-0000-000000000001"));

        Assert.Equal(calculatedAt.ToUniversalTime(), TimestampParameter(command, "calculated_at"));
        Assert.Equal(expiresAt.ToUniversalTime(), TimestampParameter(command, "expires_at"));
        AssertNoNonUtcDateTimeOffsetParameters(command);
    }

    [Fact]
    public void NullableTimestampHelper_ReturnsDbNullForNull()
    {
        var result = InvokePrivate("ToUtcOrDbNull", new object?[] { null });

        Assert.Same(DBNull.Value, result);
    }

    [Fact]
    public void NullableTimestampHelper_ConvertsOffsetTimestampToUtc()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-16T17:30:04+08:00");

        var result = InvokePrivate("ToUtcOrDbNull", new object?[] { timestamp });

        var utc = Assert.IsType<DateTimeOffset>(result);
        Assert.Equal(timestamp.ToUniversalTime(), utc);
        Assert.Equal(TimeSpan.Zero, utc.Offset);
    }

    private static object? InvokePrivate(string methodName, params object?[] parameters)
    {
        var parameterTypes = parameters
            .Select(parameter => parameter?.GetType() ?? typeof(DateTimeOffset?))
            .ToArray();
        var method = typeof(VendorParkingResolutionPersistence)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName &&
                candidate.GetParameters().Length == parameterTypes.Length);

        return method.Invoke(null, parameters);
    }

    private static DateTimeOffset TimestampParameter(NpgsqlCommand command, string name)
    {
        var timestamp = Assert.IsType<DateTimeOffset>(command.Parameters[name].Value);
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);
        return timestamp;
    }

    private static void AssertNoNonUtcDateTimeOffsetParameters(NpgsqlCommand command)
    {
        foreach (NpgsqlParameter parameter in command.Parameters)
        {
            if (parameter.Value is DateTimeOffset timestamp)
            {
                Assert.Equal(TimeSpan.Zero, timestamp.Offset);
            }
        }
    }
}
