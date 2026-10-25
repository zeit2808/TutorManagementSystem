namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>
/// H4: the day the class's first session took place, fixed once the class has started so cycles never shift
/// when an early session is later cancelled or moved.
/// </summary>
public sealed class ClassBillingAnchor
{
    private ClassBillingAnchor() { }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public DateOnly AnchorDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ClassBillingAnchor Create(Guid centerId, Guid classId, DateOnly anchorDate, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || classId == Guid.Empty) throw new ArgumentException("Class is required.");
        return new() { CenterId = centerId, ClassId = classId, AnchorDate = anchorDate, CreatedAtUtc = now };
    }
}
