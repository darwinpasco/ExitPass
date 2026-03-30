namespace ExitPass.CentralPms.Domain.Common;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}