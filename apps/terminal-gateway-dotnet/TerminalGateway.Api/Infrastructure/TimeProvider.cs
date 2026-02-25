namespace TerminalGateway.Api.Infrastructure;

public interface ISystemTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemTimeProvider : ISystemTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
