namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class SecurityAuditEvent
{
    private SecurityAuditEvent() { }

    private SecurityAuditEvent(Guid? userId, string eventType, DateTimeOffset occurredAtUtc,
        string? traceId, string? clientIp, string? userAgent)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        EventType = eventType;
        OccurredAtUtc = occurredAtUtc;
        TraceId = Truncate(traceId, 100);
        ClientIp = Truncate(clientIp, 45);
        UserAgent = Truncate(userAgent, 256);
    }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? TraceId { get; private set; }
    public string? ClientIp { get; private set; }
    public string? UserAgent { get; private set; }
    public User? User { get; private set; }

    public static SecurityAuditEvent Create(Guid? userId, string eventType, DateTimeOffset occurredAtUtc,
        string? traceId = null, string? clientIp = null, string? userAgent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return new SecurityAuditEvent(userId, eventType, occurredAtUtc, traceId, clientIp, userAgent);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return sanitized.Length == 0 ? null : sanitized[..Math.Min(sanitized.Length, maxLength)];
    }
}
