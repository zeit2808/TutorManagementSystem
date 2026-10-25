using System.Text;
using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public sealed class TeachingClass : AggregateRoot<Guid>
{
    private TeachingClass() : base(Guid.Empty) { }

    private TeachingClass(Guid centerId, Guid subjectId, string code, string name, int maxStudents,
        Guid actorId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        SubjectId = subjectId;
        Code = NormalizeCode(code);
        Name = NormalizeName(name);
        MaxStudents = maxStudents;
        CreatedByUserId = actorId;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid SubjectId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int MaxStudents { get; private set; }
    public int EnrolledCount { get; private set; }
    public bool RegistrationOpen { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public int Version { get; private set; }
    public bool IsActive => ArchivedAtUtc is null;

    public static TeachingClass Create(Guid centerId, Guid subjectId, string code, string name,
        int maxStudents, Guid actorId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || subjectId == Guid.Empty || actorId == Guid.Empty ||
            !AreDetailsValid(code, name, maxStudents))
            throw new ArgumentException("Class details are invalid.");
        return new(centerId, subjectId, code, name, maxStudents, actorId, now);
    }

    public void Update(string name, int maxStudents, DateTimeOffset now)
    {
        if (!IsActive || !IsValidName(name) || maxStudents is < 1 or > 500 || maxStudents < EnrolledCount)
            throw new InvalidOperationException("Class cannot be updated with these details.");
        var normalized = NormalizeName(name);
        if (Name == normalized && MaxStudents == maxStudents) return;
        Name = normalized;
        MaxStudents = maxStudents;
        UpdatedAtUtc = now;
        Version++;
    }

    public void Enroll(DateTimeOffset now)
    {
        if (!IsActive || EnrolledCount >= MaxStudents)
            throw new InvalidOperationException("Class is archived or full.");
        EnrolledCount++;
        UpdatedAtUtc = now;
        Version++;
    }

    public void SetRegistrationOpen(bool isOpen, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("Archived classes cannot accept registration.");
        if (RegistrationOpen == isOpen) return;
        RegistrationOpen = isOpen;
        UpdatedAtUtc = now;
        Version++;
    }

    public void Withdraw(DateTimeOffset now)
    {
        if (EnrolledCount < 1) throw new InvalidOperationException("Class enrollment count is invalid.");
        EnrolledCount--;
        UpdatedAtUtc = now;
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        if (!IsActive || EnrolledCount > 0)
            throw new InvalidOperationException("Only an empty active class can be archived.");
        ArchivedAtUtc = now;
        RegistrationOpen = false;
        UpdatedAtUtc = now;
        Version++;
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive) throw new InvalidOperationException("Class is already active.");
        ArchivedAtUtc = null;
        UpdatedAtUtc = now;
        Version++;
    }

    public static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    public static string NormalizeName(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Normalize(NormalizationForm.FormC);
    public static bool IsValidCode(string? value) => !string.IsNullOrWhiteSpace(value) &&
        NormalizeCode(value).Length is >= 2 and <= 20 &&
        NormalizeCode(value).All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
    public static bool IsValidName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        NormalizeName(value).Length is >= 2 and <= 100 && !value.Any(char.IsControl);
    public static bool AreDetailsValid(string? code, string? name, int maxStudents) =>
        IsValidCode(code) && IsValidName(name) && maxStudents is >= 1 and <= 500;
}
