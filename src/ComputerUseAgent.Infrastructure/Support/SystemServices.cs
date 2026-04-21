using ComputerUseAgent.Core.Interfaces;

namespace ComputerUseAgent.Infrastructure.Support;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class GuidIdGenerator : IIdGenerator
{
    public string CreateSessionId() => $"sess_{Guid.NewGuid():N}";

    public string CreateEventId() => $"evt_{Guid.NewGuid():N}";
}
