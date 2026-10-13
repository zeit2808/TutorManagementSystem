namespace TutorManagement.SharedKernel.Domain;

public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity(TId id)
    {
        Id = id;
    }

    public TId Id { get; protected init; }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
