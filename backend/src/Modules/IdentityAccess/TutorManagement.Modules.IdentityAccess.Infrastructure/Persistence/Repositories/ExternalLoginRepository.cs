using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class ExternalLoginRepository(IdentityDbContext db) : IExternalLoginRepository
{
    public async Task AcquireAsync(string subject, CancellationToken token)
    {
        var key = $"google-login:{subject}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
    }

    public Task<Guid?> FindUserIdAsync(string subject, CancellationToken token) =>
        db.ExternalLogins.Where(x => x.Issuer == "https://accounts.google.com" && x.Subject == subject)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(token);

    public async Task AddAsync(Guid userId, string subject, DateTimeOffset now, CancellationToken token) =>
        await db.ExternalLogins.AddAsync(ExternalLogin.CreateGoogle(userId, subject, now), token);
}
