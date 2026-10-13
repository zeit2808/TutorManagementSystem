namespace TutorManagement.SharedKernel.Domain;

public abstract record DomainEvent : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
