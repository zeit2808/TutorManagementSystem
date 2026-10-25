using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public sealed class Student : AggregateRoot<Guid>
{
    private Student() : base(Guid.Empty) { }
    private Student(Guid centerId, string code, string fullName, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Code = TeachingClass.NormalizeCode(code);
        FullName = TeachingClass.NormalizeName(fullName);
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? ParentName { get; private set; }
    /// <summary>Normalized +84 mobile number.</summary>
    public string? ParentPhone { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public int Version { get; private set; }
    public bool IsActive => ArchivedAtUtc is null;
    /// <summary>Set when this profile was merged into another one; the profile is archived and keeps no data.</summary>
    public Guid? MergedIntoStudentId { get; private set; }
    /// <summary>Another profile of this center that is probably the same person (shown to the center, never acted on).</summary>
    public Guid? DuplicateCandidateId { get; private set; }
    public StudentDuplicateReason? DuplicateReason { get; private set; }

    public static Student Create(Guid centerId, string code, string fullName, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || !TeachingClass.IsValidCode(code) || !TeachingClass.IsValidName(fullName))
            throw new ArgumentException("Student details are invalid.");
        return new(centerId, code, fullName, now);
    }

    public static Student CreateForUser(Guid centerId, Guid userId, string code, string fullName, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        var student = Create(centerId, code, fullName, now);
        student.UserId = userId;
        return student;
    }

    public void Rename(string fullName)
    {
        if (!IsActive || !TeachingClass.IsValidName(fullName)) throw new InvalidOperationException("Student cannot be updated.");
        var normalized = TeachingClass.NormalizeName(fullName);
        if (FullName == normalized) return;
        FullName = normalized;
        Version++;
    }

    /// <summary>Replaces the name and parent contact together; the phone must already be normalized.</summary>
    public void UpdateProfile(string fullName, string parentName, string parentPhone)
    {
        if (!IsActive || !TeachingClass.IsValidName(fullName) || !ParentContact.IsValidName(parentName) ||
            !ParentContact.TryNormalizePhone(parentPhone, out var phone))
            throw new InvalidOperationException("Student cannot be updated.");
        var name = TeachingClass.NormalizeName(fullName);
        var parent = TeachingClass.NormalizeName(parentName);
        if (FullName == name && ParentName == parent && ParentPhone == phone) return;
        FullName = name;
        ParentName = parent;
        ParentPhone = phone;
        Version++;
    }

    /// <summary>Fills parent contact from an online application without overwriting what the center recorded.</summary>
    public void AdoptParentContactIfMissing(string? parentName, string? parentPhone)
    {
        if (ParentPhone is not null || !ParentContact.IsValidName(parentName) ||
            !ParentContact.TryNormalizePhone(parentPhone, out var phone)) return;
        ParentName = TeachingClass.NormalizeName(parentName!);
        ParentPhone = phone;
        Version++;
    }

    /// <summary>Attaches a student login. A profile has at most one login and a login one profile per center.</summary>
    public void LinkAccount(Guid userId)
    {
        if (userId == Guid.Empty || !IsActive || UserId.HasValue)
            throw new InvalidOperationException("Only an active profile without a login can be linked.");
        UserId = userId;
        Version++;
    }

    public void UnlinkAccount()
    {
        if (!UserId.HasValue) throw new InvalidOperationException("The profile has no linked login.");
        UserId = null;
        Version++;
    }

    public void FlagDuplicate(Guid otherStudentId, StudentDuplicateReason reason)
    {
        if (otherStudentId == Guid.Empty || otherStudentId == Id) throw new ArgumentException("Invalid duplicate.");
        if (DuplicateCandidateId == otherStudentId && DuplicateReason == reason) return;
        DuplicateCandidateId = otherStudentId;
        DuplicateReason = reason;
        Version++;
    }

    public void ClearDuplicate()
    {
        if (DuplicateCandidateId is null && DuplicateReason is null) return;
        DuplicateCandidateId = null;
        DuplicateReason = null;
        Version++;
    }

    /// <summary>The source of a merge: its data has moved to <paramref name="targetId"/>; it is archived.</summary>
    public void MarkMergedInto(Guid targetId, DateTimeOffset now)
    {
        if (targetId == Guid.Empty || targetId == Id || MergedIntoStudentId.HasValue)
            throw new InvalidOperationException("The profile cannot be merged.");
        MergedIntoStudentId = targetId;
        UserId = null;
        DuplicateCandidateId = null;
        DuplicateReason = null;
        ArchivedAtUtc ??= now;
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("Student is already archived.");
        ArchivedAtUtc = now;
        Version++;
    }

    public void Reactivate()
    {
        if (IsActive) throw new InvalidOperationException("Student is already active.");
        ArchivedAtUtc = null;
        Version++;
    }
}

/// <summary>Why the center is told that two profiles may be one student.</summary>
public enum StudentDuplicateReason
{
    /// <summary>Created through a class link with the same name as a profile that has no login (a hint only).</summary>
    SameName = 0,
    /// <summary>The holder of this profile's activation link signed in with a login that already has another profile here.</summary>
    SameAccount = 1
}
