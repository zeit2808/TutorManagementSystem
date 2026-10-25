using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public sealed class Enrollment : Entity<Guid>
{
    private Enrollment() : base(Guid.Empty) { }
    private Enrollment(Guid centerId, Guid classId, Guid studentId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        StudentId = studentId;
        EnrolledAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public DateTimeOffset EnrolledAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public string? WithdrawalReason { get; private set; }
    public Guid? WithdrawnByUserId { get; private set; }
    public bool IsActive => WithdrawnAtUtc is null;

    public static Enrollment Create(Guid centerId, Guid classId, Guid studentId, DateTimeOffset now) =>
        new(centerId, classId, studentId, now);

    /// <summary>Decision D7: withdrawal takes effect immediately and always records why and by whom.</summary>
    public void Withdraw(DateTimeOffset now, string reason, Guid actorId)
    {
        if (!IsActive) throw new InvalidOperationException("Enrollment is already withdrawn.");
        if (!IsValidReason(reason) || actorId == Guid.Empty) throw new ArgumentException("A withdrawal reason is required.");
        WithdrawnAtUtc = now;
        WithdrawalReason = reason.Trim();
        WithdrawnByUserId = actorId;
    }

    public static bool IsValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500 && !reason.Any(c => char.IsControl(c) && c != '\n');
}
