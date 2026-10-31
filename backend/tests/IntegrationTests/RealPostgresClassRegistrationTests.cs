using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Codes;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

public sealed class RealPostgresClassRegistrationTests
{
    [LocalPostgresFact]
    public async Task Multi_class_review_is_idempotent_and_one_user_has_one_student_per_center()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = DateTimeOffset.UtcNow;
        var clock = new FixedClock(now);
        var reviewer = User.CreateExternal($"reviewer-{Guid.NewGuid():N}@example.invalid", now);
        var firstUser = User.CreateExternal($"student-{Guid.NewGuid():N}@example.invalid", now);
        var secondUser = User.CreateExternal($"student-{Guid.NewGuid():N}@example.invalid", now);
        var center = Center.Create("Registration E2E", "Registration E2E",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"registration-{Guid.NewGuid():N}@example.invalid", "0900000000", now);
        center.Approve(reviewer.Id, now);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(reviewer, firstUser, secondUser);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.Add(center);
                await organization.SaveChangesAsync();
            }
            var subject = Subject.Create(center.Id, "SUB-E2E", "Registration subject", null, reviewer.Id, now);
            var first = TeachingClass.Create(center.Id, subject.Id, "CLS-1", "First class", 10, reviewer.Id, now);
            var second = TeachingClass.Create(center.Id, subject.Id, "CLS-2", "Second class", 10, reviewer.Id, now);
            var third = TeachingClass.Create(center.Id, subject.Id, "CLS-3", "Third class", 10, reviewer.Id, now);
            first.SetRegistrationOpen(true, now);
            second.SetRegistrationOpen(true, now);
            third.SetRegistrationOpen(true, now);
            await using (var academic = new AcademicsDbContext(academicOptions))
            {
                academic.Subjects.Add(subject);
                academic.Classes.AddRange(first, second, third);
                await academic.SaveChangesAsync();
            }

            var key = Guid.NewGuid().ToString();
            var missingKey = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, firstUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678", [first.Id], "", default));
            Assert.Equal(ClassRegistrationErrors.InvalidKey, missingKey.Error);
            Assert.Equal("Idempotency-Key", missingKey.Error.Field);
            var invalidClasses = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, firstUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678", [first.Id, first.Id], key, default));
            Assert.Equal(ClassRegistrationErrors.InvalidClasses, invalidClasses.Error);
            var submitted = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, firstUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678",
                    [first.Id, second.Id], key, default));
            Assert.True(submitted.IsSuccess);
            Assert.False(submitted.Value.Replayed);
            Assert.Equal(2, submitted.Value.Items.Count);
            Assert.Single(submitted.Value.Items.Select(x => x.SubmissionId).Distinct());

            var retried = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, firstUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678",
                    [second.Id, first.Id], key, default));
            Assert.True(retried.IsSuccess);
            Assert.True(retried.Value.Replayed);
            Assert.Equal(submitted.Value.Items.Select(x => x.Id).Order(),
                retried.Value.Items.Select(x => x.Id).Order());
            var simultaneousRetries = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                WithWorkflowAsync(academicOptions, clock, flow => flow.SubmitAsync(center.Id,
                    firstUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678", [first.Id, second.Id], key, default))));
            Assert.All(simultaneousRetries, result =>
            {
                Assert.True(result.IsSuccess);
                Assert.Equal(submitted.Value.Items.Select(x => x.Id).Order(),
                    result.Value.Items.Select(x => x.Id).Order());
            });
            var differentPayload = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, firstUser.Id, "Other Name", "Phụ huynh Một", "0912345678", [first.Id, second.Id], key, default));
            Assert.Equal(ClassRegistrationErrors.KeyReused, differentPayload.Error);

            var simultaneousReviews = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                WithWorkflowAsync(academicOptions, clock, flow => flow.ApproveAsync(center.Id,
                    submitted.Value.Items[0].Id, reviewer.Id, default))));
            Assert.All(simultaneousReviews, result => Assert.True(result.IsSuccess));
            Assert.Equal(simultaneousReviews[0].Value.Id, simultaneousReviews[1].Value.Id);
            var reject = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.RejectAsync(center.Id, submitted.Value.Items[1].Id, reviewer.Id, "Not suitable", default));
            Assert.True(reject.IsSuccess);
            Assert.Equal("Rejected", reject.Value.Status);
            var reapply = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, firstUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678",
                    [submitted.Value.Items[1].ClassId], Guid.NewGuid().ToString(), default));
            Assert.True(reapply.IsSuccess);
            var secondApproval = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.ApproveAsync(center.Id, reapply.Value.Items[0].Id, reviewer.Id, default));
            Assert.True(secondApproval.IsSuccess);

            var twin = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.SubmitAsync(center.Id, secondUser.Id, "Same Student Name", "Phụ huynh Một", "0912345678",
                    [third.Id], Guid.NewGuid().ToString(), default));
            Assert.True(twin.IsSuccess);
            var twinApproval = await WithWorkflowAsync(academicOptions, clock, flow =>
                flow.ApproveAsync(center.Id, twin.Value.Items[0].Id, reviewer.Id, default));
            Assert.True(twinApproval.IsSuccess);

            await using var verify = new AcademicsDbContext(academicOptions);
            Assert.Equal(2, await verify.Students.CountAsync(x => x.CenterId == center.Id));
            Assert.Equal(1, await verify.Students.CountAsync(x => x.CenterId == center.Id && x.UserId == firstUser.Id));
            Assert.Equal(2, await (from enrollment in verify.Enrollments
                                   join student in verify.Students on enrollment.StudentId equals student.Id
                                   where student.UserId == firstUser.Id && enrollment.WithdrawnAtUtc == null
                                   select enrollment.Id).CountAsync());

            // Each decision emails the student once; the concurrent duplicate approval sends nothing more.
            await using var mailbox = new IdentityDbContext(identityOptions);
            var firstMails = await mailbox.EmailOutboxMessages.AsNoTracking()
                .Where(x => x.Recipient == firstUser.Email).OrderBy(x => x.CreatedAtUtc).ToListAsync();
            Assert.Equal(3, firstMails.Count);
            Assert.Equal(2, firstMails.Count(x => x.Subject.Contains("đã được duyệt", StringComparison.Ordinal)));
            var rejection = Assert.Single(firstMails, x => x.Subject.Contains("chưa được nhận", StringComparison.Ordinal));
            Assert.Contains("Lý do: Not suitable", rejection.Body, StringComparison.Ordinal);
            Assert.Contains("(CLS-", rejection.Subject, StringComparison.Ordinal);
            Assert.All(firstMails, x => Assert.Contains("https://app.example.com/student", x.Body, StringComparison.Ordinal));
            Assert.Single(await mailbox.EmailOutboxMessages.AsNoTracking().Where(x => x.Recipient == secondUser.Email).ToListAsync());
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM academics.enrollments WHERE center_id = @center;
                DELETE FROM academics.class_applications WHERE center_id = @center;
                DELETE FROM academics.students WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.centers WHERE id = @center;
                DELETE FROM iam.email_outbox_messages WHERE recipient IN (@firstEmail, @secondEmail);
                DELETE FROM iam.users WHERE id IN (@reviewer, @first, @second);
                """, connection);
            cleanup.Parameters.AddWithValue("firstEmail", firstUser.Email);
            cleanup.Parameters.AddWithValue("secondEmail", secondUser.Email);
            cleanup.Parameters.AddWithValue("center", center.Id);
            cleanup.Parameters.AddWithValue("reviewer", reviewer.Id);
            cleanup.Parameters.AddWithValue("first", firstUser.Id);
            cleanup.Parameters.AddWithValue("second", secondUser.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task<T> WithWorkflowAsync<T>(DbContextOptions<AcademicsDbContext> options,
        IClock clock, Func<ClassRegistrationWorkflow, Task<T>> action)
    {
        await using var db = new AcademicsDbContext(options);
        var mail = new ClassApplicationMail(new ScheduleChangeStore(db, clock, Links), new StudentTimetableStore(db));
        return await action(new ClassRegistrationWorkflow(new ClassRegistrationStore(db),
            new TeachingStore(db), new AcademicsCodeGenerator(db), clock, mail: mail));
    }

    private static readonly ServiceProvider Links = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "https://app.example.com" })
            .Build())
        .BuildServiceProvider();

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
