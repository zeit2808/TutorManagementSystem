using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class ExternalLogin : Entity<Guid>
{
    private ExternalLogin() : base(Guid.Empty) { }

    private ExternalLogin(Guid userId, string issuer, string subject, DateTimeOffset createdAtUtc)
        : base(Guid.NewGuid())
    {
        UserId = userId;
        Issuer = issuer;
        Subject = subject;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }
    public string Issuer { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ExternalLogin CreateGoogle(Guid userId, string subject, DateTimeOffset now)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            throw new ArgumentException("External identity is invalid.");
        return new ExternalLogin(userId, "https://accounts.google.com", subject, now);
    }
}
