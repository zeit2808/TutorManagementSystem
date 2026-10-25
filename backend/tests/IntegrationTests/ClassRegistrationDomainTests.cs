using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.IntegrationTests;

public sealed class ClassRegistrationDomainTests
{
    [Fact]
    public void Each_application_has_an_independent_review_state()
    {
        var now = DateTimeOffset.UtcNow;
        var centerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var first = ClassApplication.Create(centerId, Guid.NewGuid(), userId, submissionId,
            Guid.NewGuid().ToString(), new string('A', 64), "  Nguyen   Van A ", now);
        var second = ClassApplication.Create(centerId, Guid.NewGuid(), userId, submissionId,
            Guid.NewGuid().ToString(), new string('A', 64), "Nguyen Van A", now);
        first.Approve(Guid.NewGuid(), now.AddMinutes(1));
        second.Reject(Guid.NewGuid(), "Trung tam khong mo lop nay", now.AddMinutes(2));

        Assert.Equal(ClassApplicationStatus.Approved, first.Status);
        Assert.Equal(ClassApplicationStatus.Rejected, second.Status);
        Assert.Equal("Nguyen Van A", first.FullName);
        Assert.Throws<InvalidOperationException>(() => first.Reject(Guid.NewGuid(), "late", now));
        Assert.Throws<InvalidOperationException>(() => second.Approve(Guid.NewGuid(), now));
    }

    [Fact]
    public void Linked_student_uses_user_identity_not_a_name_match()
    {
        var centerId = Guid.NewGuid();
        var first = Student.CreateForUser(centerId, Guid.NewGuid(), "ST-ONE", "Same Name", DateTimeOffset.UtcNow);
        var second = Student.CreateForUser(centerId, Guid.NewGuid(), "ST-TWO", "Same Name", DateTimeOffset.UtcNow);
        Assert.Equal(first.FullName, second.FullName);
        Assert.NotEqual(first.UserId, second.UserId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Archived_class_cannot_reopen_registration()
    {
        var now = DateTimeOffset.UtcNow;
        var group = TeachingClass.Create(Guid.NewGuid(), Guid.NewGuid(), "MATH-01", "Math class", 20,
            Guid.NewGuid(), now);
        group.SetRegistrationOpen(true, now);
        group.Archive(now.AddDays(1));
        Assert.False(group.RegistrationOpen);
        Assert.Throws<InvalidOperationException>(() => group.SetRegistrationOpen(true, now.AddDays(2)));
    }
}
