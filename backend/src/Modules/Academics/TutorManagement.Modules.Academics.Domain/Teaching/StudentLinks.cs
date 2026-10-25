using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

/// <summary>
/// M3: a one-time link that attaches whoever opens it (a student login) to one profile the center created by hand.
/// The link, not an email address, proves who the student is. Only the SHA-256 of the token is stored.
/// </summary>
public sealed class StudentActivationLink : Entity<Guid>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private StudentActivationLink() : base(Guid.Empty) { }

    private StudentActivationLink(Guid centerId, Guid studentId, string tokenHash, Guid createdByUserId,
        DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        StudentId = studentId;
        TokenHash = tokenHash;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
        ExpiresAtUtc = now.Add(Lifetime);
    }

    public Guid CenterId { get; private set; }
    public Guid StudentId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public Guid? UsedByUserId { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    /// <summary>The person who opened the link said "Không phải tôi".</summary>
    public DateTimeOffset? DeclinedAtUtc { get; private set; }

    public static StudentActivationLink Create(Guid centerId, Guid studentId, string tokenHash, Guid createdByUserId,
        DateTimeOffset now)
    {
        if (centerId == Guid.Empty || studentId == Guid.Empty || createdByUserId == Guid.Empty ||
            tokenHash.Length != 64) throw new ArgumentException("Activation link details are invalid.");
        return new(centerId, studentId, tokenHash, createdByUserId, now);
    }

    public bool IsActiveAt(DateTimeOffset now) => UsedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > now;

    public void MarkUsed(Guid userId, DateTimeOffset now)
    {
        if (!IsActiveAt(now) || userId == Guid.Empty) throw new InvalidOperationException("The link is not active.");
        UsedAtUtc = now;
        UsedByUserId = userId;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (UsedAtUtc is not null || RevokedAtUtc is not null) return;
        RevokedAtUtc = now;
    }

    public void Decline(DateTimeOffset now)
    {
        if (!IsActiveAt(now)) throw new InvalidOperationException("The link is not active.");
        DeclinedAtUtc = now;
        RevokedAtUtc = now;
    }
}

/// <summary>
/// M3: a shareable link for one class plus a short code. A student login with the right code joins the class at
/// once (pre-approved by the center). The center can see, revoke and re-code it.
/// </summary>
public sealed class ClassInviteLink : Entity<Guid>
{
    public const int CodeLength = 6;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public static readonly int[] AllowedValidDays = [1, 7, 30];

    private ClassInviteLink() : base(Guid.Empty) { }

    private ClassInviteLink(Guid centerId, Guid classId, string token, string code, DateTimeOffset expiresAtUtc,
        int? maxUses, Guid createdByUserId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        Token = token;
        Code = code;
        ExpiresAtUtc = expiresAtUtc;
        MaxUses = maxUses;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    /// <summary>The unguessable part of the URL; kept so the center can copy the link again.</summary>
    public string Token { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public int? MaxUses { get; private set; }
    public int UseCount { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static ClassInviteLink Create(Guid centerId, Guid classId, string token, string code, int validDays,
        int? maxUses, Guid createdByUserId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || classId == Guid.Empty || createdByUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(token) || token.Length > 64 || code.Length != CodeLength ||
            !AllowedValidDays.Contains(validDays) || maxUses is < 1 or > 500)
            throw new ArgumentException("Class invite details are invalid.");
        return new(centerId, classId, token, code, now.AddDays(validDays), maxUses, createdByUserId, now);
    }

    public bool IsActiveAt(DateTimeOffset now) =>
        RevokedAtUtc is null && ExpiresAtUtc > now && (MaxUses is null || UseCount < MaxUses);

    public bool Matches(string? code) =>
        code is not null && string.Equals(NormalizeCode(code), Code, StringComparison.Ordinal);

    public static string NormalizeCode(string code) => code.Trim().Replace(" ", "", StringComparison.Ordinal)
        .ToUpperInvariant();

    public void RecordUse()
    {
        UseCount++;
        Version++;
    }

    public void Recode(string code)
    {
        if (code.Length != CodeLength) throw new ArgumentException("Invalid code.", nameof(code));
        Code = code;
        Version++;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = now;
        Version++;
    }
}

/// <summary>Wrong-code counter per link and login: five misses lock that login out of the link for 15 minutes.</summary>
public sealed class ClassInviteAttempt
{
    private ClassInviteAttempt() { }

    public ClassInviteAttempt(Guid linkId, Guid userId)
    {
        LinkId = linkId;
        UserId = userId;
    }

    public Guid LinkId { get; private set; }
    public Guid UserId { get; private set; }
    public int FailedCount { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public bool IsLocked(DateTimeOffset now) => LockedUntilUtc > now;

    public void RecordFailure(DateTimeOffset now)
    {
        if (LockedUntilUtc <= now) LockedUntilUtc = null;
        FailedCount++;
        if (FailedCount < ClassInviteLink.MaxFailedAttempts) return;
        FailedCount = 0;
        LockedUntilUtc = now.Add(ClassInviteLink.LockDuration);
    }

    public void Reset()
    {
        FailedCount = 0;
        LockedUntilUtc = null;
    }
}

/// <summary>Audit row: who joined which class through which link, and whether a new profile was created.</summary>
public sealed class ClassInviteJoin : Entity<Guid>
{
    private ClassInviteJoin() : base(Guid.Empty) { }

    public ClassInviteJoin(Guid centerId, Guid linkId, Guid classId, Guid studentId, Guid userId, bool createdProfile,
        bool hadConflicts, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        LinkId = linkId;
        ClassId = classId;
        StudentId = studentId;
        UserId = userId;
        CreatedProfile = createdProfile;
        HadConflicts = hadConflicts;
        JoinedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid LinkId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid UserId { get; private set; }
    public bool CreatedProfile { get; private set; }
    public bool HadConflicts { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
}
