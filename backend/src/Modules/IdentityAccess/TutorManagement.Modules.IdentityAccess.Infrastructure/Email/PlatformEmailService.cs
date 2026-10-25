using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Email;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed class PlatformEmailService(IdentityDbContext db, IClock clock, IConfiguration configuration)
    : IPlatformEmailService
{
    public const string TestSubject = "Email thử — " + EmailBranding.AppName;

    // The Worker owns delivery settings; the API reads the same key so both agree on "gave up".
    private int MaximumAttempts => int.TryParse(configuration[$"{EmailDeliveryOptions.SectionName}:MaximumAttempts"],
        out var value) && value is >= 1 and <= 20 ? value : 5;

    public async Task<EmailOverviewDto> OverviewAsync(CancellationToken token)
    {
        var max = MaximumAttempts;
        var now = clock.UtcNow;
        var open = db.EmailOutboxMessages.AsNoTracking().Where(x => x.ProcessedAtUtc == null);
        var recent = await db.EmailOutboxMessages.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(20)
            .Select(x => new Row(x.Id, x.Recipient, x.Subject, x.AttemptCount, x.LastError, x.CreatedAtUtc,
                x.ProcessedAtUtc, x.ProcessingId, x.ProcessingUntilUtc, x.SecurityTokenId)).ToListAsync(token);
        return new EmailOverviewDto
        {
            Pending = await open.CountAsync(x => x.AttemptCount == 0, token),
            Retrying = await open.CountAsync(x => x.AttemptCount > 0 && (x.AttemptCount < max || x.SecurityTokenId != null), token),
            Failed = await open.CountAsync(x => x.AttemptCount >= max && x.SecurityTokenId == null, token),
            SentLast24Hours = await db.EmailOutboxMessages.AsNoTracking()
                .CountAsync(x => x.ProcessedAtUtc >= now.AddHours(-24) && x.LastError == null, token),
            MaximumAttempts = max,
            Recent = recent.Select(x => ToDto(x, max)).ToList()
        };
    }

    public async Task<Result<OutboxEmailDto>> QueueTestAsync(Guid actorId, string? to, CancellationToken token)
    {
        string recipient;
        if (string.IsNullOrWhiteSpace(to))
        {
            var own = await db.Users.AsNoTracking().Where(x => x.Id == actorId).Select(x => x.Email).FirstOrDefaultAsync(token);
            if (string.IsNullOrWhiteSpace(own)) return Result<OutboxEmailDto>.Failure(PlatformEmailErrors.NoOwnEmail);
            recipient = own;
        }
        else if (!IsEmail(to)) return Result<OutboxEmailDto>.Failure(PlatformEmailErrors.InvalidRecipient);
        else recipient = to.Trim();

        var now = clock.UtcNow;
        if (await db.EmailOutboxMessages.AnyAsync(x => x.Subject == TestSubject && x.CreatedAtUtc > now.AddSeconds(-30), token))
            return Result<OutboxEmailDto>.Failure(PlatformEmailErrors.TooSoon);

        var nl = Environment.NewLine;
        var message = EmailOutboxMessage.Create(recipient, TestSubject,
            $"Xin chào,{nl}{nl}Đây là email thử của {EmailBranding.AppName}, gửi lúc " +
            $"{now.ToOffset(TimeSpan.FromHours(7)):HH:mm dd/MM/yyyy} (giờ Việt Nam).{nl}" +
            $"Nhận được thư này nghĩa là Worker đang gửi email thật thành công.{nl}",
            now);
        await db.EmailOutboxMessages.AddAsync(message, token);
        await db.SaveChangesAsync(token);
        return Result<OutboxEmailDto>.Success(ToDto(new Row(message.Id, message.Recipient, message.Subject, 0, null,
            message.CreatedAtUtc, null, null, null, null), MaximumAttempts));
    }

    private static bool IsEmail(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 254) return false;
        try { return string.Equals(new MailAddress(trimmed).Address, trimmed, StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }

    private static OutboxEmailDto ToDto(Row x, int max) => new()
    {
        Id = x.Id,
        Recipient = x.Recipient,
        Subject = x.Subject,
        Attempts = x.Attempts,
        LastError = x.LastError,
        CreatedAtUtc = x.CreatedAtUtc,
        ProcessedAtUtc = x.ProcessedAtUtc,
        Status = x.ProcessedAtUtc is not null ? (x.LastError is null ? "Sent" : "Skipped")
            : x.Attempts == 0 ? "Pending"
            : x.Attempts >= max && x.SecurityTokenId is null ? "Failed"
            : "Retrying",
        // While unclaimed, ProcessingUntilUtc is the "not before" time of the next try.
        NextAttemptAtUtc = x.ProcessedAtUtc is null && x.ProcessingId is null && x.Attempts > 0 &&
            (x.Attempts < max || x.SecurityTokenId is not null) ? x.ProcessingUntilUtc : null
    };

    private sealed record Row(Guid Id, string Recipient, string Subject, int Attempts, string? LastError,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? ProcessedAtUtc, Guid? ProcessingId, DateTimeOffset? ProcessingUntilUtc,
        Guid? SecurityTokenId);
}
