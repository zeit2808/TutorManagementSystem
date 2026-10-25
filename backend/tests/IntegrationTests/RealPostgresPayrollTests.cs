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
using TutorManagement.Modules.Finance.Application.Payroll;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payroll;
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

/// <summary>Payroll rules (docs/LUONG-VA-CHIA-THU-NHAP.md) on pure values.</summary>
public sealed class PayrollRuleTests
{
    [Fact]
    public void Rates_prefer_the_class_override_in_force_on_the_day()
    {
        var center = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        var cls = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var rates = new[]
        {
            PayRate.Create(center, teacher, null, 200_000, new DateOnly(2026, 1, 1), actor, now),
            PayRate.Create(center, teacher, cls, 250_000, new DateOnly(2026, 3, 10), actor, now),
            PayRate.Create(center, teacher, null, 220_000, new DateOnly(2026, 3, 1), actor, now)
        };
        Assert.Equal(200_000, PayRate.For(rates, teacher, cls, new DateOnly(2026, 2, 28))!.Amount);
        Assert.Equal(220_000, PayRate.For(rates, teacher, cls, new DateOnly(2026, 3, 9))!.Amount);
        Assert.Equal(250_000, PayRate.For(rates, teacher, cls, new DateOnly(2026, 3, 10))!.Amount);
        Assert.Equal(220_000, PayRate.For(rates, teacher, Guid.NewGuid(), new DateOnly(2026, 3, 10))!.Amount);
        Assert.Null(PayRate.For(rates, Guid.NewGuid(), cls, new DateOnly(2026, 3, 10)));
        Assert.False(PayRate.IsValidAmount(150_500));
        Assert.False(PayrollAdjustment.IsValidAmount(0));
        Assert.True(PayrollAdjustment.IsValidAmount(-50_000));
        Assert.True(PayMonth.TryParse("2026-03", out var month));
        Assert.Equal(new DateOnly(2026, 3, 31), PayMonth.End(month));
        Assert.False(PayMonth.TryParse("2026-13", out _));
    }
}

/// <summary>
/// Payroll against real PostgreSQL. M1 (family): tuition actually collected shared by class cycle and minutes taught,
/// a substitute's share, shared costs, a report blocking the close, closing in order, a late correction carried into
/// the next month, advances and payouts, reopening. M2: per-session rates with class overrides, missing rates and a
/// negative month blocking the close. The admin switch and the teacher's cross-center earnings.
/// </summary>
public sealed class RealPostgresPayrollTests
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private static DateTimeOffset Local(int month, int day, int hour = 18) =>
        new DateTimeOffset(2026, month, day, hour, 0, 0, Vietnam.GetUtcOffset(new DateTime(2026, month, day))).ToUniversalTime();

    [LocalPostgresFact]
    public async Task Family_revenue_share_and_per_session_pay_follow_the_payroll_rules()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var clock = new MutableClock(Local(3, 20, 9));
        var past = Local(1, 1, 8);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var dad = User.CreateExternal($"ba-{marker}@example.invalid", past);
        var mom = User.CreateExternal($"me-{marker}@example.invalid", past);
        var family = NewCenter($"Gia đình {marker}", marker, "fam", past);
        var hired = NewCenter($"Trung tâm thuê {marker}", marker, "hire", past);
        family.Approve(dad.Id, past);
        hired.Approve(dad.Id, past);
        var room = Room.Create(family.Id, "P1", "Phòng 1", null, dad.Id, past);
        var roomB = Room.Create(hired.Id, "P1", "Phòng 1", null, dad.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var options = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var services = new ServiceCollection().BuildServiceProvider();

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(dad, mom);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.AddRange(family, hired);
                organization.Rooms.AddRange(room, roomB);
                await organization.SaveChangesAsync();
            }

            // ---- family center: Ba teaches T9A on Thursdays (90'), Mẹ teaches T9B on Tuesdays (60') ----
            var subject = Subject.Create(family.Id, "MATH", $"Toán {marker}", null, dad.Id, past);
            var ba = Teacher.Create(family.Id, "GV01", "Ba", past);
            var me = Teacher.Create(family.Id, "GV02", "Mẹ", past);
            ba.LinkAccount(dad.Id);
            me.LinkAccount(mom.Id);
            var t9a = TeachingClass.Create(family.Id, subject.Id, "T9A", "Toán 9A", 20, dad.Id, past);
            var t9b = TeachingClass.Create(family.Id, subject.Id, "T9B", "Toán 9B", 20, dad.Id, past);
            var an = Student.Create(family.Id, "HS-000001", "Nguyễn An", past);
            var binh = Student.Create(family.Id, "HS-000002", "Lê Bình", past);
            // T9A 15/01 … 02/04 → cycles 15/01–14/02, 15/02–14/03, 15/03–14/04. T9B 03/03 … 07/04 → cycle 03/03–02/04.
            var t9aSessions = Enumerable.Range(0, 12).Select(week => TeachingSession.Create(family.Id, t9a.Id, ba.Id, room.Id,
                Local(1, 15).AddDays(7 * week), Local(1, 15).AddDays(7 * week).AddMinutes(90), past)).ToArray();
            var t9bSessions = Enumerable.Range(0, 6).Select(week => TeachingSession.Create(family.Id, t9b.Id, me.Id, room.Id,
                Local(3, 3, 17).AddDays(7 * week), Local(3, 3, 17).AddDays(7 * week).AddMinutes(60), past)).ToArray();
            await using (var db = new AcademicsDbContext(options))
            {
                db.Subjects.Add(subject);
                db.Teachers.AddRange(ba, me);
                db.Classes.AddRange(t9a, t9b);
                db.Students.AddRange(an, binh);
                db.Sessions.AddRange(t9aSessions);
                db.Sessions.AddRange(t9bSessions);
                db.Enrollments.AddRange(Enrollment.Create(family.Id, t9a.Id, an.Id, past),
                    Enrollment.Create(family.Id, t9b.Id, binh.Id, past));
                await db.SaveChangesAsync();
            }

            // Sheets: Feb 05, 12, 26 Ba; 19/02 Mẹ substitutes. March 05, 12, 19 Ba; T9B 03, 10, 17 Mẹ. January: none.
            var byDate = t9aSessions.ToDictionary(x => FinanceTime.LocalDate(x.StartUtc));
            await SubmitAsync(connectionString, dad.Id, byDate[new DateOnly(2026, 2, 5)], ba.Id);
            await SubmitAsync(connectionString, dad.Id, byDate[new DateOnly(2026, 2, 12)], ba.Id);
            await SubmitAsync(connectionString, dad.Id, byDate[new DateOnly(2026, 2, 19)], me.Id);
            await SubmitAsync(connectionString, dad.Id, byDate[new DateOnly(2026, 2, 26)], ba.Id);
            foreach (var day in new[] { 5, 12, 19 }) await SubmitAsync(connectionString, dad.Id, byDate[new DateOnly(2026, 3, day)], ba.Id);
            foreach (var session in t9bSessions.Take(3)) await SubmitAsync(connectionString, dad.Id, session, me.Id);

            await using (var finance = new FinanceDbContext(financeOptions))
            {
                finance.ClassFees.Add(ClassFee.Create(family.Id, t9a.Id, 800_000, new DateOnly(2026, 1, 1), dad.Id, past));
                finance.ClassFees.Add(ClassFee.Create(family.Id, t9b.Id, 600_000, new DateOnly(2026, 1, 1), dad.Id, past));
                // C2 lets the admin start this month at the earliest; a February start is seeded directly.
                var config = PayrollConfig.Create(family.Id);
                finance.PayrollConfigs.Add(config);
                finance.PayrollConfigChanges.Add(config.Change(true, PayrollMode.RevenueShare, new DateOnly(2026, 2, 1),
                    "Gia đình tự dạy", dad.Id, past));
                await finance.SaveChangesAsync();
            }

            TuitionWorkflow Tuition(FinanceDbContext db) =>
                new(new TuitionStore(db, clock, services), new TuitionAcademicsReader(db), new NoNames(), clock);
            PayrollWorkflow Payroll(FinanceDbContext db) =>
                new(new PayrollStore(db), new PayrollAcademicsReader(db), new TuitionStore(db, clock, services),
                    new TuitionAcademicsReader(db), new NoNames(), clock);
            var owner = new PayrollActor(dad.Id, PayrollRole.Owner);
            var manager = new PayrollActor(mom.Id, PayrollRole.Manager);
            var momAsTeacher = new PayrollActor(mom.Id, PayrollRole.Teacher);

            await using (var db = new FinanceDbContext(financeOptions))
            {
                // G1: An 800k on 10/02 (cycle 1); An 1.6M on 05/03 (cycles 2, 3); Bình 900k on 06/03 (T9B + 300k ahead).
                Assert.True((await Tuition(db).RecordPaymentAsync(family.Id, an.Id, 800_000, "Cash", new DateOnly(2026, 2, 10),
                    null, null, null, null, dad.Id, default)).IsSuccess);
                Assert.True((await Tuition(db).RecordPaymentAsync(family.Id, an.Id, 1_600_000, "Cash", new DateOnly(2026, 3, 5),
                    null, null, null, null, dad.Id, default)).IsSuccess);
                Assert.True((await Tuition(db).RecordPaymentAsync(family.Id, binh.Id, 900_000, "BankTransfer",
                    new DateOnly(2026, 3, 6), null, null, null, null, dad.Id, default)).IsSuccess);
            }

            // ---- M1: months in scope, February's share ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var months = (await Payroll(db).ListMonthsAsync(family.Id, owner, default)).Value;
                Assert.Equal(["2026-03", "2026-02"], months.Select(x => x.Month));
                Assert.Equal(PayrollErrors.Forbidden, (await Payroll(db).ListMonthsAsync(family.Id, momAsTeacher, default)).Error);

                var feb = (await Payroll(db).GetMonthAsync(family.Id, "2026-02", owner, default)).Value;
                Assert.Equal("RevenueShare", feb.Mode);
                Assert.Equal(800_000, feb.Pool!.Collected);
                var baFeb = feb.Lines.Single(x => x.TeacherId == ba.Id);
                Assert.Equal(800_000, baFeb.Base);                       // cycle 1: only Ba's sheets (January has none)
                Assert.Equal(3, baFeb.Sessions);
                Assert.Equal(270, baFeb.Minutes);
                var meFeb = feb.Lines.Single(x => x.TeacherId == me.Id);
                Assert.Equal(0, meFeb.Base);
                Assert.Equal(1, meFeb.Sessions);                         // the substitute session counts as Mẹ's work

                var own = (await Payroll(db).GetMonthAsync(family.Id, "2026-02", momAsTeacher, default)).Value;
                Assert.Null(own.Pool);
                Assert.Equal(me.Id, Assert.Single(own.Lines).TeacherId);
                Assert.Empty(own.Expenses);

                var march = (await Payroll(db).GetMonthAsync(family.Id, "2026-03", owner, default)).Value;
                Assert.Contains("earlier_month_open", march.CloseBlockers);
                Assert.Equal(PayrollErrors.CannotClose.Code, (await Payroll(db).CloseAsync(family.Id, "2026-03", owner, default)).Error.Code);
            }

            // ---- C14: a report blocks the close until resolved; only the owner closes ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var reported = (await Payroll(db).AddDisputeAsync(family.Id, "2026-02", "Thiếu tiền buổi dạy thay 19/02?",
                    momAsTeacher, default)).Value;
                var dispute = Assert.Single(reported.Disputes);
                var feb = (await Payroll(db).GetMonthAsync(family.Id, "2026-02", owner, default)).Value;
                Assert.Contains("open_disputes", feb.CloseBlockers);
                Assert.Equal(PayrollErrors.Forbidden, (await Payroll(db).CloseAsync(family.Id, "2026-02", manager, default)).Error);
                Assert.True((await Payroll(db).ResolveDisputeAsync(family.Id, dispute.Id,
                    "Tiền kỳ 1 thu tháng 2 thuộc Ba; buổi 19/02 tính ở kỳ 2", manager, default)).IsSuccess);
                var closed = (await Payroll(db).CloseAsync(family.Id, "2026-02", owner, default)).Value;
                Assert.Equal("Closed", closed.Status);
                Assert.Equal(PayrollErrors.MonthClosed, (await Payroll(db).AddExpenseAsync(family.Id, "2026-02",
                    new DateOnly(2026, 2, 3), "Điện", 100_000, "Tiền điện tháng 2", owner, default)).Error);
            }

            // ---- March: share by cycle and minutes, a shared cost, an advance ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.Equal(PayrollErrors.InvalidExpense, (await Payroll(db).AddExpenseAsync(family.Id, "2026-03",
                    new DateOnly(2026, 2, 28), "Thuê phòng", 300_000, "Tiền phòng", owner, default)).Error);
                var march = (await Payroll(db).AddExpenseAsync(family.Id, "2026-03", new DateOnly(2026, 3, 15), "Thuê phòng",
                    300_000, "Tiền phòng tháng 3", manager, default)).Value;
                Assert.Equal(2_500_000, march.Pool!.Collected);
                Assert.Equal(300_000, march.Pool.Prepaid);                                    // G2: not shared yet
                Assert.Equal(300_000, march.Pool.Expenses);
                Assert.Equal(1_900_000, march.Pool.Distributable);
                // Raw: Ba 600k (cycle 2: 3 of 4 sessions) + 800k (cycle 3: his own and planned) = 1.4M; Mẹ 200k + 600k = 0.8M.
                Assert.Equal(1_210_000, march.Lines.Single(x => x.TeacherId == ba.Id).Base);
                Assert.Equal(690_000, march.Lines.Single(x => x.TeacherId == me.Id).Base);
                Assert.Equal(1_900_000, march.Lines.Sum(x => x.Base));

                Assert.Equal(PayrollErrors.Forbidden, (await Payroll(db).AddPayoutAsync(family.Id, "2026-03", ba.Id, 100_000,
                    "Cash", null, null, null, manager, default)).Error);
                var advanced = (await Payroll(db).AddPayoutAsync(family.Id, "2026-03", ba.Id, 1_000_000, "Cash",
                    new DateOnly(2026, 3, 18), "Tạm ứng", Guid.NewGuid(), owner, default)).Value;
                var payout = Assert.Single(advanced.Payouts);
                Assert.Equal("PC-000001", payout.NumberText);
                Assert.True(payout.Advance);
            }

            // ---- C9: a correction in closed February (12/02 was taught by Mẹ) is carried into March ----
            await CorrectTeacherAsync(connectionString, byDate[new DateOnly(2026, 2, 12)], me.Id);
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var feb = (await Payroll(db).GetMonthAsync(family.Id, "2026-02", owner, default)).Value;
                Assert.Equal(800_000, feb.Lines.Single(x => x.TeacherId == ba.Id).Base);      // frozen
                var march = (await Payroll(db).GetMonthAsync(family.Id, "2026-03", owner, default)).Value;
                var baMarch = march.Lines.Single(x => x.TeacherId == ba.Id);
                Assert.Equal(-400_000, baMarch.Carried);
                Assert.Equal("2026-02", Assert.Single(baMarch.CarriedFrom).FromMonth);
                Assert.Equal(400_000, march.Lines.Single(x => x.TeacherId == me.Id).Carried);
                Assert.Equal(810_000, baMarch.Net);
                Assert.Contains("overpaid", march.CloseBlockers);                             // 1.0M advanced > 810k

                Assert.Equal(PayrollErrors.InvalidAdjustment, (await Payroll(db).AddAdjustmentAsync(family.Id, "2026-03", ba.Id,
                    150_500, "Thưởng", manager, default)).Error);
                march = (await Payroll(db).AddAdjustmentAsync(family.Id, "2026-03", ba.Id, 200_000, "Thưởng dạy thêm", manager,
                    default)).Value;
                Assert.Equal(1_010_000, march.Lines.Single(x => x.TeacherId == ba.Id).Net);
                Assert.Empty(march.CloseBlockers);
                var closed = (await Payroll(db).CloseAsync(family.Id, "2026-03", owner, default)).Value;
                Assert.Equal("Closed", closed.Status);
                Assert.Equal(-400_000, closed.Lines.Single(x => x.TeacherId == ba.Id).Carried);
                Assert.Equal(2, await db.PayrollCarries.CountAsync(x => x.CenterId == family.Id && x.IntoMonth == new DateOnly(2026, 3, 1)));
            }

            // ---- reopen only the latest closed month; payouts on a closed month are capped ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.Equal(PayrollErrors.CannotReopen, (await Payroll(db).ReopenAsync(family.Id, "2026-02", "Sửa lại", owner,
                    default)).Error);
                Assert.Equal(PayrollErrors.InvalidReason, (await Payroll(db).ReopenAsync(family.Id, "2026-03", "x", owner,
                    default)).Error);
                var reopened = (await Payroll(db).ReopenAsync(family.Id, "2026-03", "Kiểm tra lại chi phí", owner, default)).Value;
                Assert.Equal("Draft", reopened.Status);
                Assert.Equal(0, await db.PayrollCarries.CountAsync(x => x.CenterId == family.Id));
                Assert.Equal(-400_000, reopened.Lines.Single(x => x.TeacherId == ba.Id).Carried);  // recomputed, not lost
                Assert.Equal("Closed", (await Payroll(db).CloseAsync(family.Id, "2026-03", owner, default)).Value.Status);

                Assert.Equal(PayrollErrors.PayoutTooLarge, (await Payroll(db).AddPayoutAsync(family.Id, "2026-03", me.Id, 2_000_000,
                    "BankTransfer", null, null, null, owner, default)).Error);
                var paid = (await Payroll(db).AddPayoutAsync(family.Id, "2026-03", me.Id, 1_090_000, "BankTransfer", null, null,
                    null, owner, default)).Value;
                var meLine = paid.Lines.Single(x => x.TeacherId == me.Id);
                Assert.Equal(1_090_000, meLine.Net);
                Assert.Equal(0, meLine.Remaining);
                Assert.Equal("PC-000002", paid.Payouts.First(x => x.TeacherId == me.Id).NumberText);
                Assert.False(paid.Payouts.First(x => x.TeacherId == me.Id).Advance);
            }

            // ---- M2 center: per-session rates, class override from 10/03, a substitute on their own rate ----
            var x = Teacher.Create(hired.Id, "GV01", "Thầy Xuân", past);
            var y = Teacher.Create(hired.Id, "GV02", "Cô Yến", past);
            x.LinkAccount(dad.Id);                                         // the same login teaches in both centers (C15)
            var subjectB = Subject.Create(hired.Id, "MATH", $"Toán B {marker}", null, dad.Id, past);
            var c1 = TeachingClass.Create(hired.Id, subjectB.Id, "C1", "Lớp 1", 20, dad.Id, past);
            var c1Sessions = Enumerable.Range(0, 4).Select(week => TeachingSession.Create(hired.Id, c1.Id, x.Id, roomB.Id,
                Local(3, 2, 8).AddDays(7 * week), Local(3, 2, 8).AddDays(7 * week).AddMinutes(60), past)).ToArray();
            await using (var db = new AcademicsDbContext(options))
            {
                db.Subjects.Add(subjectB);
                db.Teachers.AddRange(x, y);
                db.Classes.Add(c1);
                db.Sessions.AddRange(c1Sessions);
                await db.SaveChangesAsync();
            }
            await SubmitAsync(connectionString, dad.Id, c1Sessions[0], x.Id);       // 02/03
            await SubmitAsync(connectionString, dad.Id, c1Sessions[1], y.Id);       // 09/03, substitute
            await SubmitAsync(connectionString, dad.Id, c1Sessions[2], x.Id);       // 16/03
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var config = PayrollConfig.Create(hired.Id);
                db.PayrollConfigs.Add(config);
                db.PayrollConfigChanges.Add(config.Change(true, PayrollMode.PerSession, new DateOnly(2026, 3, 1), "Thuê giáo viên",
                    dad.Id, past));
                await db.SaveChangesAsync();
            }
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var hiredOwner = new PayrollActor(dad.Id, PayrollRole.Owner);
                Assert.True((await Payroll(db).AddRateAsync(hired.Id, x.Id, null, 200_000, new DateOnly(2026, 1, 1), hiredOwner,
                    default)).IsSuccess);
                Assert.True((await Payroll(db).AddRateAsync(hired.Id, x.Id, c1.Id, 250_000, new DateOnly(2026, 3, 10), hiredOwner,
                    default)).IsSuccess);
                Assert.Equal(PayrollErrors.InvalidRate, (await Payroll(db).AddRateAsync(hired.Id, y.Id, null, 180_500,
                    new DateOnly(2026, 3, 1), hiredOwner, default)).Error);
                Assert.Equal(PayrollErrors.WrongMode, (await Payroll(db).AddExpenseAsync(hired.Id, "2026-03",
                    new DateOnly(2026, 3, 2), "Điện", 100_000, "Tiền điện", hiredOwner, default)).Error);

                var march = (await Payroll(db).GetMonthAsync(hired.Id, "2026-03", hiredOwner, default)).Value;
                Assert.Null(march.Pool);
                Assert.Equal(450_000, march.Lines.Single(l => l.TeacherId == x.Id).Base);   // 200k (02/03) + 250k (16/03)
                Assert.Single(march.MissingRates);                                         // Yến has no rate yet
                Assert.Contains("missing_rates", march.CloseBlockers);

                Assert.True((await Payroll(db).AddRateAsync(hired.Id, y.Id, null, 180_000, new DateOnly(2026, 3, 1), hiredOwner,
                    default)).IsSuccess);
                march = (await Payroll(db).AddAdjustmentAsync(hired.Id, "2026-03", x.Id, -500_000, "Phạt nghỉ không báo",
                    hiredOwner, default)).Value;
                var yLine = march.Lines.Single(l => l.TeacherId == y.Id);
                Assert.Equal(180_000, yLine.Base);                                           // L6: the substitute's own rate
                Assert.True(Assert.Single(yLine.Items).Substitute);
                Assert.Contains("negative_net", march.CloseBlockers);                        // L9
                var penalty = march.Adjustments.Single();
                march = (await Payroll(db).VoidAdjustmentAsync(hired.Id, penalty.Id, "Nhập nhầm", hiredOwner, default)).Value;
                Assert.Empty(march.CloseBlockers);
                Assert.Equal("Closed", (await Payroll(db).CloseAsync(hired.Id, "2026-03", hiredOwner, default)).Value.Status);
            }

            // ---- C15: Ba's login earns in both centers ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var mine = await Payroll(db).MyEarningsAsync(dad.Id, 2026, default);
                Assert.Equal(2, mine.Centers.Count);
                var familyYear = mine.Centers.Single(c => c.CenterId == family.Id);
                Assert.Equal(1_810_000, familyYear.YearNet);             // Feb 800k + Mar 1,010k
                Assert.Equal(1_000_000, familyYear.YearPaid);
                Assert.Equal(450_000, mine.Centers.Single(c => c.CenterId == hired.Id).YearNet);
                Assert.Equal(1_460_000, mine.Months.Single(m => m.Month == "2026-03").Net);
                Assert.Equal(2_260_000, mine.YearNet);
            }

            // ---- A1/A2/C2: the admin switch ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var config = (await Payroll(db).GetConfigAsync(family.Id, default)).Value;
                Assert.True(config.EnabledThisMonth);
                Assert.Equal("2026-03", config.LatestClosedMonth);
                Assert.Equal(PayrollErrors.ConfigMonthClosed, (await Payroll(db).SetConfigAsync(family.Id, true, "PerSession",
                    "2026-03", "Đổi phương thức", config.Version, dad.Id, default)).Error);
                Assert.Equal(PayrollErrors.InvalidConfig, (await Payroll(db).SetConfigAsync(family.Id, true, "PerSession",
                    "2026-02", "Đổi phương thức", config.Version, dad.Id, default)).Error);
                Assert.Equal(PayrollErrors.InvalidConfig, (await Payroll(db).SetConfigAsync(family.Id, true, "PerSession",
                    "2026-04", "x", config.Version, dad.Id, default)).Error);
                Assert.Equal(PayrollErrors.ConfigVersion, (await Payroll(db).SetConfigAsync(family.Id, true, "PerSession",
                    "2026-04", "Đổi phương thức", config.Version + 5, dad.Id, default)).Error);
                var changed = (await Payroll(db).SetConfigAsync(family.Id, true, "PerSession", "2026-04", "Thuê thêm giáo viên",
                    config.Version, dad.Id, default)).Value;
                Assert.Equal("PerSession", changed.Mode);
                Assert.Equal("RevenueShare", changed.ModeThisMonth);
                Assert.Equal(2, changed.History.Count);
                Assert.Equal("Thuê thêm giáo viên", changed.History[0].Reason);

                // The admin overview lists every operating center with this month and the scheduled change.
                var overview = await Payroll(db).ConfigOverviewAsync(default);
                Assert.Equal("2026-03", overview.CurrentMonth);
                var row = Assert.Single(overview.Centers, x => x.CenterId == family.Id);
                Assert.True(row.EnabledThisMonth);
                Assert.Equal("RevenueShare", row.ModeThisMonth);
                Assert.Equal("2026-04", row.NextMonth);
                Assert.Equal("PerSession", row.NextMode);
                Assert.Equal("2026-03", row.LatestClosedMonth);
                Assert.Equal("Approved", row.CenterStatus);
                Assert.Contains(overview.Centers, x => x.CenterId == hired.Id && x.EnabledThisMonth && x.ModeThisMonth == "PerSession");
            }

            // ---- G2 + C9: in April Bình's credit pays the new T9B cycle; that money belongs to closed March ----
            clock.Now = Local(4, 5, 9);
            await using (var db = new FinanceDbContext(financeOptions))
            {
                // Spreading credit onto a newly billed cycle adds an allocation to an existing receipt (used to fail).
                Assert.Equal(0, (await Tuition(db).StudentAccountAsync(family.Id, binh.Id, default)).Value.Credit);
                var april = (await Payroll(db).GetMonthAsync(family.Id, "2026-04", owner, default)).Value;
                Assert.Equal("PerSession", april.Mode);
                var meApril = april.Lines.Single(x => x.TeacherId == me.Id);
                // March now shares 2.5M − 300k costs = 2.2M at 0.88: Mẹ 1.1M raw → 968k (frozen 690k), Ba 1.4M → 1,232k (1,210k).
                Assert.Equal(278_000, meApril.Carried);
                Assert.Equal("2026-03", Assert.Single(meApril.CarriedFrom).FromMonth);
                Assert.Equal(22_000, april.Lines.Single(x => x.TeacherId == ba.Id).Carried);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            foreach (var centerId in new[] { family.Id, hired.Id })
            {
                await using var cleanup = new NpgsqlCommand("""
                    DELETE FROM finance.payroll_month_lines WHERE month_id IN (SELECT id FROM finance.payroll_months WHERE center_id = @c);
                    DELETE FROM finance.payroll_months WHERE center_id = @c;
                    DELETE FROM finance.payroll_carries WHERE center_id = @c;
                    DELETE FROM finance.payroll_month_events WHERE center_id = @c;
                    DELETE FROM finance.payroll_payouts WHERE center_id = @c;
                    DELETE FROM finance.payout_counters WHERE center_id = @c;
                    DELETE FROM finance.payroll_disputes WHERE center_id = @c;
                    DELETE FROM finance.payroll_adjustments WHERE center_id = @c;
                    DELETE FROM finance.shared_expenses WHERE center_id = @c;
                    DELETE FROM finance.pay_rates WHERE center_id = @c;
                    DELETE FROM finance.payroll_config_changes WHERE center_id = @c;
                    DELETE FROM finance.payroll_configs WHERE center_id = @c;
                    DELETE FROM finance.receipt_allocations WHERE receipt_id IN (SELECT id FROM finance.receipts WHERE center_id = @c);
                    DELETE FROM finance.receipts WHERE center_id = @c;
                    DELETE FROM finance.receipt_counters WHERE center_id = @c;
                    DELETE FROM finance.tuition_charges WHERE center_id = @c;
                    DELETE FROM finance.class_billing_anchors WHERE center_id = @c;
                    DELETE FROM finance.class_fees WHERE center_id = @c;
                    DELETE FROM academics.attendance_sheets WHERE center_id = @c;
                    DELETE FROM academics.enrollments WHERE center_id = @c;
                    DELETE FROM academics.teaching_sessions WHERE center_id = @c;
                    DELETE FROM academics.classes WHERE center_id = @c;
                    DELETE FROM academics.students WHERE center_id = @c;
                    DELETE FROM academics.teachers WHERE center_id = @c;
                    DELETE FROM academics.subjects WHERE center_id = @c;
                    DELETE FROM organization.rooms WHERE center_id = @c;
                    DELETE FROM organization.center_status_history WHERE center_id = @c;
                    DELETE FROM organization.centers WHERE id = @c;
                    """, connection);
                cleanup.Parameters.AddWithValue("c", centerId);
                await cleanup.ExecuteNonQueryAsync();
            }

            await using var users = new NpgsqlCommand("DELETE FROM iam.users WHERE id IN (@a, @b)", connection);
            users.Parameters.AddWithValue("a", dad.Id);
            users.Parameters.AddWithValue("b", mom.Id);
            await users.ExecuteNonQueryAsync();
        }
    }

    private static Center NewCenter(string name, string marker, string tag, DateTimeOffset now) =>
        Center.Create($"{name} LLC", name,
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"{tag}-{marker}@example.invalid", "0900000000", now);

    /// <summary>A submitted sheet with who actually taught (C5); entries are irrelevant to pay.</summary>
    private static async Task SubmitAsync(string connectionString, Guid actorId, TeachingSession session, Guid actualTeacherId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO academics.attendance_sheets (session_id, center_id, class_id, status, version, actual_teacher_id,
                created_at_utc, updated_at_utc, updated_by_user_id, submitted_at_utc, submitted_by_user_id)
            VALUES (@s, @c, @cls, 'Submitted', 1, @t, now(), now(), @u, now(), @u)
            """, connection);
        command.Parameters.AddWithValue("s", session.Id);
        command.Parameters.AddWithValue("c", session.CenterId);
        command.Parameters.AddWithValue("cls", session.ClassId);
        command.Parameters.AddWithValue("t", actualTeacherId);
        command.Parameters.AddWithValue("u", actorId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CorrectTeacherAsync(string connectionString, TeachingSession session, Guid actualTeacherId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE academics.attendance_sheets SET actual_teacher_id = @t, version = version + 1 WHERE session_id = @s",
            connection);
        command.Parameters.AddWithValue("s", session.Id);
        command.Parameters.AddWithValue("t", actualTeacherId);
        await command.ExecuteNonQueryAsync();
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
