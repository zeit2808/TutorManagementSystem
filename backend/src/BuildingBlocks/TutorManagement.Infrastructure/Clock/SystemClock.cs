using TutorManagement.Application.Abstractions.Clock;

namespace TutorManagement.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
