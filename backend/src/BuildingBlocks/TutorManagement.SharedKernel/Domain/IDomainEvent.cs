namespace TutorManagement.SharedKernel.Domain;

public interface IDomainEvent
{
    Guid Id { get; }

    DateTimeOffset OccurredAtUtc { get; }
}
