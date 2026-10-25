using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Organizations.Domain.Centers;

/// <summary>
/// One row per lifecycle transition (D8). Unlike the review fields on <see cref="Center"/>, which only
/// remember the latest decision, this keeps every rejection, resubmission and suspension.
/// </summary>
public sealed class CenterStatusChange : Entity<Guid>
{
    private CenterStatusChange() : base(Guid.Empty) { }

    internal CenterStatusChange(Guid centerId, CenterStatus? fromStatus, CenterStatus toStatus,
        Guid? actorUserId, string? note, DateTimeOffset changedAtUtc) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = actorUserId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ChangedAtUtc = changedAtUtc;
    }

    public Guid CenterId { get; private set; }
    public CenterStatus? FromStatus { get; private set; }
    public CenterStatus ToStatus { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }
}
