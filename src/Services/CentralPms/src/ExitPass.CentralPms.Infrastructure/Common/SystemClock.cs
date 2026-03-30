using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Infrastructure.Common;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}