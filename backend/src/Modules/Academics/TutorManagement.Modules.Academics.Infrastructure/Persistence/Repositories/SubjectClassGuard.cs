using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class SubjectClassGuard(AcademicsDbContext db) : ISubjectClassGuard
{
    public async Task<ISubjectClassTransaction> BeginAsync(Guid centerId, Guid subjectId, CancellationToken token)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var key = $"subject:{centerId}:{subjectId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
            return new SubjectClassTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public Task<bool> HasActiveClassesAsync(Guid centerId, Guid subjectId, CancellationToken token) =>
        db.Classes.AnyAsync(x => x.CenterId == centerId && x.SubjectId == subjectId &&
            x.ArchivedAtUtc == null, token);

    private sealed class SubjectClassTransaction(IDbContextTransaction transaction) : ISubjectClassTransaction
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
