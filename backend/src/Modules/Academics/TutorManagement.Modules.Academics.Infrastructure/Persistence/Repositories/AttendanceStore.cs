using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class AttendanceStore(AcademicsDbContext db) : IAttendanceStore
{
    public Task<SessionAttendance?> FindSheetAsync(Guid centerId, Guid sessionId, CancellationToken token) =>
        db.AttendanceSheets.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == sessionId, token);

    public async Task<IReadOnlyList<AttendanceEntry>> ListEntriesAsync(Guid centerId, Guid sessionId,
        CancellationToken token) => await db.AttendanceEntries.Where(x => x.CenterId == centerId &&
            x.SessionId == sessionId).OrderBy(x => x.StudentId).ToArrayAsync(token);

    public async Task<IReadOnlyList<AttendanceRosterRow>> ListRosterAsync(Guid centerId, Guid classId,
        DateTimeOffset sessionStartUtc, CancellationToken token) =>
        await (from enrollment in db.Enrollments.AsNoTracking()
               join student in db.Students.AsNoTracking()
                   on new { enrollment.CenterId, Id = enrollment.StudentId }
                   equals new { student.CenterId, student.Id }
               where enrollment.CenterId == centerId && enrollment.ClassId == classId &&
                   enrollment.EnrolledAtUtc <= sessionStartUtc &&
                   (enrollment.WithdrawnAtUtc == null || enrollment.WithdrawnAtUtc > sessionStartUtc)
               orderby student.FullName, student.Id
               select new AttendanceRosterRow(enrollment.Id, student.Id, student.Code,
                   student.FullName)).ToArrayAsync(token);

    public async Task<IReadOnlyList<AttendanceRevision>> ListRevisionsAsync(Guid centerId, Guid sessionId,
        CancellationToken token) => await db.AttendanceRevisions.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.SessionId == sessionId)
            .OrderBy(x => x.Version).ToArrayAsync(token);

    public async Task<IReadOnlyDictionary<Guid, string>> ListApprovedAbsencesAsync(Guid centerId, Guid sessionId,
        CancellationToken token) =>
        (await db.StudentRequests.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.SessionId == sessionId &&
                x.Kind == StudentRequestKind.ExcusedAbsence && x.Status == StudentRequestStatus.Approved)
            .Select(x => new { x.StudentId, x.Reason }).ToListAsync(token))
            .GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.First().Reason);

    public Task AddSheetAsync(SessionAttendance sheet, CancellationToken token) =>
        db.AttendanceSheets.AddAsync(sheet, token).AsTask();
    public Task AddEntriesAsync(IEnumerable<AttendanceEntry> entries, CancellationToken token) =>
        db.AttendanceEntries.AddRangeAsync(entries, token);
    public Task AddRevisionAsync(AttendanceRevision revision, CancellationToken token) =>
        db.AttendanceRevisions.AddAsync(revision, token).AsTask();
}
