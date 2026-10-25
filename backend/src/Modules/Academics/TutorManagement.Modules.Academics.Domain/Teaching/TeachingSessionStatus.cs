namespace TutorManagement.Modules.Academics.Domain.Teaching;

/// <summary>D6: a session is scheduled, cancelled before it starts, or recorded as not held afterwards.</summary>
public enum TeachingSessionStatus
{
    Scheduled = 0,
    Cancelled = 1,
    NotHeld = 2
}
