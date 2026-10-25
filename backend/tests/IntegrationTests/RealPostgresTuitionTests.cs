using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Tuition rules (docs/HOC-PHI.md) on pure values.</summary>
public sealed class TuitionMathTests
{
    [Fact]
    public void Cycles_count_from_the_anchor_and_keep_month_ends()
    {
        var anchor = new DateOnly(2026, 1, 31);
        Assert.Equal(new DateOnly(2026, 2, 28), TuitionMath.CycleStart(anchor, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), TuitionMath.CycleStart(anchor, 2));
        Assert.Equal(new DateOnly(2026, 2, 27), TuitionMath.CycleEnd(anchor, 0));
        Assert.Equal(0, TuitionMath.CycleIndexOf(anchor, new DateOnly(2026, 2, 27)));
        Assert.Equal(1, TuitionMath.CycleIndexOf(anchor, new DateOnly(2026, 2, 28)));
        Assert.Equal(-1, TuitionMath.CycleIndexOf(anchor, new DateOnly(2026, 1, 30)));
    }

    [Fact]
    public void Shares_and_discounts_round_to_a_thousand()
    {
        Assert.Equal(300_000, TuitionMath.Prorate(800_000, 3, 8));
        Assert.Equal(267_000, TuitionMath.Prorate(800_000, 1, 3));
        Assert.Equal(800_000, TuitionMath.Prorate(800_000, 9, 8));
        Assert.Equal(0, TuitionMath.Prorate(800_000, 0, 8));
        var actor = Guid.NewGuid();
        var center = Guid.NewGuid();
        var discounts = new[]
        {
            TuitionDiscount.Create(center, Guid.NewGuid(), Guid.NewGuid(), DiscountKind.Percent, 15, new DateOnly(2026, 1, 1), null, "Anh chị em", actor, DateTimeOffset.UtcNow),
            TuitionDiscount.Create(center, Guid.NewGuid(), Guid.NewGuid(), DiscountKind.Amount, 50_000, new DateOnly(2026, 1, 1), null, "Học bổng", actor, DateTimeOffset.UtcNow)
        };
        Assert.Equal(170_000, TuitionDiscount.Total(discounts, 800_000));
        Assert.Equal(800_000, TuitionDiscount.Total([.. discounts, .. discounts, .. discounts, .. discounts, .. discounts, .. discounts, .. discounts], 800_000));
        Assert.False(TuitionDiscount.IsValid(DiscountKind.Amount, 1500, new DateOnly(2026, 1, 1), null, "Lý do"));
        Assert.False(TuitionDiscount.IsValid(DiscountKind.Percent, 10, new DateOnly(2026, 1, 1), null, " "));
    }

    [Fact]
    public void Leaving_mid_cycle_keeps_what_was_paid_and_notes_the_sessions()
    {
        var charge = TuitionCharge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            new DateOnly(2026, 2, 15), new DateOnly(2026, 3, 14), 800_000, null, null, [], new DateOnly(2026, 2, 22),
            DateTimeOffset.UtcNow);
        charge.EndEarly(new DateOnly(2026, 2, 25), 4, 8, 300_000);
        Assert.Equal(300_000, charge.Amount);
        Assert.Equal(500_000, charge.WaivedAmount);
        Assert.Equal(400_000, charge.EndedValue);
        Assert.Throws<InvalidOperationException>(() => charge.Adjust(100_000, 1, "Nghỉ", Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}

/// <summary>
/// Tuition against real PostgreSQL: cycles from the class's first session, joining mid-cycle by sessions, leaving
/// mid-cycle (round money), fee changes from the next cycle, receipts with credit and voiding, discounts, the
/// center's own reduction for missed sessions (H18), reminders and the student's view.
/// </summary>
public sealed class RealPostgresTuitionTests
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private static DateTimeOffset Local(int month, int day, int hour = 18) =>
        new DateTimeOffset(2026, month, day, hour, 0, 0, Vietnam.GetUtcOffset(new DateTime(2026, month, day))).ToUniversalTime();

    [LocalPostgresFact]
    public async Task Monthly_cycles_payments_discounts_and_reductions_follow_the_tuition_rules()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var clock = new MutableClock(Local(3, 20, 9));
        var past = Local(1, 1, 8);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var owner = User.CreateExternal($"owner-{marker}@example.invalid", past);
        var anUser = User.CreateExternal($"an-{marker}@example.invalid", past);
        var center = Center.Create("Tuition LLC", $"Học phí {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"fee-{marker}@example.invalid", "0900000000", past);
        center.Approve(owner.Id, past);
        var room = Room.Create(center.Id, "P1", "Phòng 1", null, owner.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var options = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var services = new ServiceCollection().BuildServiceProvider();

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(owner, anUser);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.Add(center);
                organization.Rooms.Add(room);
                await organization.SaveChangesAsync();
            }
            var subject = Subject.Create(center.Id, "MATH", $"Toán {marker}", null, owner.Id, past);
            var teacher = Teacher.Create(center.Id, "GV01", "Cô Lan", past);
            var t9a = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 20, owner.Id, past);
            var t2 = TeachingClass.Create(center.Id, subject.Id, "T2", "Lớp hai", 20, owner.Id, past);
            var an = Student.CreateForUser(center.Id, anUser.Id, "HS-000001", "Nguyễn An", past);
            var binh = Student.Create(center.Id, "HS-000002", "Lê Bình", past);
            var chi = Student.Create(center.Id, "HS-000003", "Trần Chi", past);
            var dung = Student.Create(center.Id, "HS-000004", "Phạm Dũng", past);
            var em = Student.Create(center.Id, "HS-000005", "Đào Em", past);
            // T9A: every Thursday 18:00 from 15/01 → cycles 15/01–14/02 (5 sessions), 15/02–14/03 (4), 15/03–14/04.
            var t9aSessions = Enumerable.Range(0, 12)
                .Select(week => TeachingSession.Create(center.Id, t9a.Id, teacher.Id, room.Id, Local(1, 15).AddDays(7 * week),
                    Local(1, 15).AddDays(7 * week).AddMinutes(90), past)).ToArray();
            // One extra session on Sunday 01/03 the center cancels (H18: shown, not deducted).
            var cancelled = TeachingSession.Create(center.Id, t9a.Id, teacher.Id, room.Id, Local(3, 1), Local(3, 1).AddMinutes(90), past);
            cancelled.Cancel(past, "Giáo viên ốm", owner.Id);
            // T2 starts 20/02 on Fridays: its fee is first set on 20/03, so only the cycle running then is billed.
            var t2Sessions = Enumerable.Range(0, 8)
                .Select(week => TeachingSession.Create(center.Id, t2.Id, teacher.Id, room.Id, Local(2, 20, 8).AddDays(7 * week),
                    Local(2, 20, 8).AddDays(7 * week).AddMinutes(60), past)).ToArray();
            var enrollAn = Enrollment.Create(center.Id, t9a.Id, an.Id, past);
            var enrollBinh = Enrollment.Create(center.Id, t9a.Id, binh.Id, Local(2, 1, 9));       // joins before 05/02: 2 of 5
            var enrollChi = Enrollment.Create(center.Id, t9a.Id, chi.Id, past);
            enrollChi.Withdraw(Local(2, 25, 9), "Chuyển trường", owner.Id);                       // after 19/02, before 26/02
            var enrollDung = Enrollment.Create(center.Id, t2.Id, dung.Id, past);
            // Joins on 20/03 after the 19/03 session: billed today for 2 of the cycle's 3 sessions, before attending.
            var enrollEm = Enrollment.Create(center.Id, t9a.Id, em.Id, Local(3, 20, 8));
            await using (var db = new AcademicsDbContext(options))
            {
                db.Subjects.Add(subject);
                db.Teachers.Add(teacher);
                db.Classes.AddRange(t9a, t2);
                db.Students.AddRange(an, binh, chi, dung, em);
                db.Sessions.AddRange(t9aSessions);
                db.Sessions.AddRange(t2Sessions);
                db.Sessions.Add(cancelled);
                db.Enrollments.AddRange(enrollAn, enrollBinh, enrollChi, enrollDung, enrollEm);
                await db.SaveChangesAsync();
            }
            await using (var finance = new FinanceDbContext(financeOptions))
            {
                finance.ClassFees.Add(ClassFee.Create(center.Id, t9a.Id, 800_000, new DateOnly(2026, 1, 1), owner.Id, past));
                await finance.SaveChangesAsync();
            }

            TuitionWorkflow Workflow(FinanceDbContext db) =>
                new(new TuitionStore(db, clock, services), new TuitionAcademicsReader(db), new NoNames(), clock);

            // ---- billing: full months, a mid-cycle join by sessions, a mid-cycle leave with round money ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var page = await Workflow(db).ListChargesAsync(center.Id, "All", t9a.Id, null, null, 1, 100, default);
                var anCharges = page.Items.Where(x => x.StudentId == an.Id).OrderBy(x => x.CycleNumber).ToArray();
                Assert.Equal([1, 2, 3], anCharges.Select(x => x.CycleNumber));
                Assert.All(anCharges, x => Assert.Equal(800_000, x.Amount));
                Assert.Equal(new DateOnly(2026, 1, 15), anCharges[0].PeriodStart);
                Assert.Equal(new DateOnly(2026, 2, 14), anCharges[0].PeriodEnd);
                Assert.Equal(new DateOnly(2026, 3, 22), anCharges[2].DueDate);

                var binhFirst = page.Items.Single(x => x.StudentId == binh.Id && x.CycleNumber == 1);
                Assert.Equal(320_000, binhFirst.Amount);                // 800k × 2/5
                Assert.Equal(2, binhFirst.JoinSessions);
                Assert.Equal(5, binhFirst.PeriodSessions);
                Assert.Equal(new DateOnly(2026, 2, 8), binhFirst.DueDate); // 7 days from the day Bình joined

                var chiCharges = page.Items.Where(x => x.StudentId == chi.Id).OrderBy(x => x.CycleNumber).ToArray();
                Assert.Equal(2, chiCharges.Length);
                Assert.Equal(800_000, chiCharges[0].Amount);
                Assert.Equal(0, chiCharges[1].Amount);                  // left in cycle 2, nothing paid → not billed
                Assert.Equal(800_000, chiCharges[1].WaivedAmount);
                Assert.Equal(1, chiCharges[1].EndedSessionsAttended);
                Assert.Equal(4, chiCharges[1].EndedSessionsTotal);
                Assert.Equal(200_000, chiCharges[1].EndedValue);

                var emCharge = Assert.Single(page.Items, x => x.StudentId == em.Id);
                Assert.Equal(3, emCharge.CycleNumber);
                Assert.Equal(533_000, emCharge.Amount);              // 800k × 2/3 (26/03, 02/04 of 19/03–02/04), billed before attending
                Assert.Equal(new DateOnly(2026, 3, 27), emCharge.DueDate);

                // H18: the cancelled Sunday is listed on cycle 2, the amount is untouched.
                var anSecond = anCharges[1];
                Assert.Equal("Cancelled", Assert.Single(anSecond.Missed).Kind);
                Assert.Equal(800_000, anSecond.Amount);
                Assert.Equal(200_000, anSecond.PerSessionAmount);

                // Idempotent: a second read bills nothing new.
                var again = await Workflow(db).ListChargesAsync(center.Id, "All", null, null, null, 1, 100, default);
                Assert.Equal(page.TotalCount, again.TotalCount);
            }

            // ---- fees: a change applies from the next cycle; a class's first fee covers the running cycle ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.Equal(TuitionErrors.InvalidFee, (await Workflow(db).SetFeeAsync(center.Id, t9a.Id, 850_500, owner.Id, default)).Error);
                var row = (await Workflow(db).SetFeeAsync(center.Id, t9a.Id, 900_000, owner.Id, default)).Value;
                Assert.Equal(800_000, row.CurrentFee);
                Assert.Equal(900_000, row.NextFee);
                Assert.Equal(new DateOnly(2026, 4, 15), row.NextFeeFrom);
                var t2Row = (await Workflow(db).SetFeeAsync(center.Id, t2.Id, 500_000, owner.Id, default)).Value;
                Assert.Equal(500_000, t2Row.CurrentFee);
                Assert.Equal(new DateOnly(2026, 3, 20), t2Row.CurrentPeriodStart);
                var dungAccount = (await Workflow(db).StudentAccountAsync(center.Id, dung.Id, default)).Value;
                Assert.Equal(500_000, Assert.Single(dungAccount.Charges).Amount); // 20/02–19/03 is not billed back
            }

            // ---- receipts: oldest first, partial, credit, replay, void ----
            ReceiptDto second;
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var request = Guid.NewGuid();
                var first = (await Workflow(db).RecordPaymentAsync(center.Id, an.Id, 2_000_000, "Cash", null, "Mẹ An", null,
                    null, request, owner.Id, default)).Value;
                Assert.Equal("PT-000001", first.NumberText);
                Assert.Equal(2_000_000, first.Allocated);
                Assert.Equal([800_000L, 800_000L, 400_000L], first.Lines.Select(x => x.Amount));
                var replay = (await Workflow(db).RecordPaymentAsync(center.Id, an.Id, 2_000_000, "Cash", null, "Mẹ An", null,
                    null, request, owner.Id, default)).Value;
                Assert.Equal(first.Id, replay.Id);
                second = (await Workflow(db).RecordPaymentAsync(center.Id, an.Id, 1_000_000, "BankTransfer", null, null, null,
                    null, null, owner.Id, default)).Value;
                Assert.Equal("PT-000002", second.NumberText);
                Assert.Equal(400_000, second.Allocated);
                Assert.Equal(600_000, second.Credit);
                var account = (await Workflow(db).StudentAccountAsync(center.Id, an.Id, default)).Value;
                Assert.Equal(0, account.Owed);
                Assert.Equal(600_000, account.Credit);
                Assert.Equal("HS000001 HOC PHI", account.TransferContent);
                Assert.Equal(TuitionErrors.InvalidReceivedOn, (await Workflow(db).RecordPaymentAsync(center.Id, an.Id, 1000, "Cash",
                    new DateOnly(2026, 3, 21), null, null, null, null, owner.Id, default)).Error);
            }

            // ---- discount from cycle 2 reprices billed cycles; the center's reduction for a missed session ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var account = (await Workflow(db).AddDiscountAsync(center.Id, binh.Id, t9a.Id, "Percent", 10,
                    new DateOnly(2026, 2, 15), null, "Con giáo viên", owner.Id, default)).Value;
                var cycles = account.Charges.OrderBy(x => x.CycleNumber).ToArray();
                Assert.Equal(320_000, cycles[0].Amount);
                Assert.Equal(720_000, cycles[1].Amount);
                Assert.Equal(720_000, cycles[2].Amount);
                Assert.Equal(TuitionErrors.NotEnrolled, (await Workflow(db).AddDiscountAsync(center.Id, dung.Id, t9a.Id, "Amount",
                    50_000, new DateOnly(2026, 3, 1), null, "Thử", owner.Id, default)).Error);

                Assert.Equal(180_000, cycles[1].PerSessionAmount);
                var reduced = (await Workflow(db).AdjustChargeAsync(center.Id, cycles[1].Id, 180_000, 1, "Trung tâm huỷ 01/03",
                    owner.Id, default)).Value;
                Assert.Equal(540_000, reduced.Amount);
                Assert.Equal(180_000, reduced.AdjustmentAmount);
                Assert.Equal(TuitionErrors.InvalidAdjustment, (await Workflow(db).AdjustChargeAsync(center.Id, cycles[1].Id,
                    50_000, 1, null, owner.Id, default)).Error);
                var chiEnded = (await Workflow(db).ListChargesAsync(center.Id, "All", null, chi.Id, null, 1, 10, default))
                    .Items.Single(x => x.CycleNumber == 2);
                Assert.Equal(TuitionErrors.ChargeEnded, (await Workflow(db).AdjustChargeAsync(center.Id, chiEnded.Id, 0, null,
                    null, owner.Id, default)).Error);

                // Paid first, reduced later: the reduction becomes credit for the next cycle.
                var paidAn = (await Workflow(db).ListChargesAsync(center.Id, "All", null, an.Id, null, 1, 10, default))
                    .Items.Single(x => x.CycleNumber == 2);
                await Workflow(db).AdjustChargeAsync(center.Id, paidAn.Id, 200_000, 1, "Trung tâm huỷ 01/03", owner.Id, default);
                Assert.Equal(800_000, (await Workflow(db).StudentAccountAsync(center.Id, an.Id, default)).Value.Credit);
                await Workflow(db).AdjustChargeAsync(center.Id, paidAn.Id, 0, null, null, owner.Id, default);
                Assert.Equal(600_000, (await Workflow(db).StudentAccountAsync(center.Id, an.Id, default)).Value.Credit);
            }

            // ---- void: the money stops counting, the receipt stays ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.Equal(TuitionErrors.InvalidReason, (await Workflow(db).VoidReceiptAsync(center.Id, second.Id, "x", owner.Id, default)).Error);
                var voided = (await Workflow(db).VoidReceiptAsync(center.Id, second.Id, "Ghi nhầm số tiền", owner.Id, default)).Value;
                Assert.NotNull(voided.VoidedAtUtc);
                Assert.Equal(TuitionErrors.AlreadyVoided, (await Workflow(db).VoidReceiptAsync(center.Id, second.Id, "Lần nữa", owner.Id, default)).Error);
                var account = (await Workflow(db).StudentAccountAsync(center.Id, an.Id, default)).Value;
                Assert.Equal(400_000, account.Owed);
                Assert.Equal(0, account.Credit);
                Assert.Equal(2, account.Receipts.Count);
            }

            // ---- overdue → one reminder email for the student with a login; the student's own view ----
            clock.Now = Local(3, 30, 9);
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var summary = await Workflow(db).SummaryAsync(center.Id, default);
                Assert.True(summary.Overdue >= 400_000);
                var reminders = new TuitionReminders(new TuitionStore(db, clock, services), new TuitionAcademicsReader(db), clock);
                Assert.True(await reminders.RunAsync(default) >= 1);
                Assert.Equal(0, await reminders.RunAsync(default));
            }
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var count = new NpgsqlCommand(
                    "SELECT count(*) FROM iam.email_outbox_messages WHERE recipient = @r AND subject LIKE 'Nhắc học phí%'", connection);
                count.Parameters.AddWithValue("r", anUser.Email!);
                Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
            }
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var mine = await new StudentTuitionQuery(new TuitionStore(db, clock, services), new TuitionAcademicsReader(db), clock)
                    .ListAsync(anUser.Id, default);
                var view = Assert.Single(mine.Centers);
                Assert.Equal(400_000, view.Owed);
                Assert.Equal(400_000, view.Overdue);
                Assert.All(view.Receipts, x => Assert.Null(x.CreatedByName));
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM finance.receipt_allocations WHERE receipt_id IN (SELECT id FROM finance.receipts WHERE center_id = @c);
                DELETE FROM finance.receipts WHERE center_id = @c;
                DELETE FROM finance.receipt_counters WHERE center_id = @c;
                DELETE FROM finance.tuition_charges WHERE center_id = @c;
                DELETE FROM finance.tuition_discounts WHERE center_id = @c;
                DELETE FROM finance.class_billing_anchors WHERE center_id = @c;
                DELETE FROM finance.class_fees WHERE center_id = @c;
                DELETE FROM finance.finance_settings WHERE center_id = @c;
                DELETE FROM academics.enrollments WHERE center_id = @c;
                DELETE FROM academics.teaching_sessions WHERE center_id = @c;
                DELETE FROM academics.classes WHERE center_id = @c;
                DELETE FROM academics.students WHERE center_id = @c;
                DELETE FROM academics.teachers WHERE center_id = @c;
                DELETE FROM academics.subjects WHERE center_id = @c;
                DELETE FROM organization.rooms WHERE center_id = @c;
                DELETE FROM organization.center_status_history WHERE center_id = @c;
                DELETE FROM organization.centers WHERE id = @c;
                DELETE FROM iam.email_outbox_messages WHERE recipient LIKE @pattern;
                DELETE FROM iam.users WHERE id IN (@owner, @an);
                """, connection);
            cleanup.Parameters.AddWithValue("c", center.Id);
            cleanup.Parameters.AddWithValue("pattern", $"%-{marker}@example.invalid");
            cleanup.Parameters.AddWithValue("owner", owner.Id);
            cleanup.Parameters.AddWithValue("an", anUser.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class NoNames : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
