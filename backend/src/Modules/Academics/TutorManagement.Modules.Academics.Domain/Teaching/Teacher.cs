using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

// A teaching resource of one center. It may be linked to one login (UserId) so that person can see
// their own sessions and take attendance; the same login can hold teacher profiles in several centers.
public sealed class Teacher : AggregateRoot<Guid>
{
    private Teacher() : base(Guid.Empty) { }
    private Teacher(Guid centerId, string code, string fullName, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Code = TeachingClass.NormalizeCode(code);
        FullName = TeachingClass.NormalizeName(fullName);
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public Guid? UserId { get; private set; }
    public int Version { get; private set; }
    public bool IsActive => ArchivedAtUtc is null;

    public static Teacher Create(Guid centerId, string code, string fullName, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || !TeachingClass.IsValidCode(code) || !TeachingClass.IsValidName(fullName))
            throw new ArgumentException("Teacher details are invalid.");
        return new(centerId, code, fullName, now);
    }

    public void Rename(string fullName)
    {
        if (!IsActive || !TeachingClass.IsValidName(fullName)) throw new InvalidOperationException("Teacher cannot be updated.");
        var normalized = TeachingClass.NormalizeName(fullName);
        if (FullName == normalized) return;
        FullName = normalized;
        Version++;
    }

    public void LinkAccount(Guid? userId)
    {
        if (!IsActive) throw new InvalidOperationException("Archived teachers cannot be linked.");
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        if (UserId == userId) return;
        UserId = userId;
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("Teacher is already archived.");
        ArchivedAtUtc = now;
        Version++;
    }

    public void Reactivate()
    {
        if (IsActive) throw new InvalidOperationException("Teacher is already active.");
        ArchivedAtUtc = null;
        Version++;
    }
}
