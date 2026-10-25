using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Application.Subjects.ArchiveSubject;
using TutorManagement.Modules.Academics.Application.Subjects.CreateSubject;
using TutorManagement.Modules.Academics.Application.Subjects.GetSubject;
using TutorManagement.Modules.Academics.Application.Subjects.ListSubjects;
using TutorManagement.Modules.Academics.Application.Subjects.ReactivateSubject;
using TutorManagement.Modules.Academics.Application.Subjects.UpdateSubject;
using TutorManagement.Modules.Academics.Domain.Subjects;

namespace TutorManagement.IntegrationTests;

public sealed class SubjectCatalogTests
{
    private static readonly Guid CenterA = Guid.Parse("6c9d299c-ae28-4721-a29a-080cae375350");
    private static readonly Guid CenterB = Guid.Parse("26cb9fb6-c270-4639-859e-794649659430");
    private static readonly Guid Actor = Guid.Parse("f96e0197-bd86-4f53-965b-528b469d1596");

    [Fact]
    public async Task Codes_and_names_are_unique_per_center_but_not_global()
    {
        var state = new State();
        var first = await state.Create.HandleAsync(new(CenterA, Actor, " math ", "  Toán   học ", null), default);
        Assert.True(first.IsSuccess);
        Assert.Equal("MATH", first.Value.Code);
        Assert.Equal("Toán học", first.Value.Name);

        var duplicateCode = await state.Create.HandleAsync(new(CenterA, Actor, "MATH", "Vật lý", null), default);
        Assert.Equal(SubjectErrors.CodeAlreadyExists, duplicateCode.Error);
        var duplicateName = await state.Create.HandleAsync(new(CenterA, Actor, "PHYSICS", "toán học", null), default);
        Assert.Equal(SubjectErrors.NameAlreadyExists, duplicateName.Error);

        var otherCenter = await state.Create.HandleAsync(new(CenterB, Actor, "math", "Toán học", null), default);
        Assert.True(otherCenter.IsSuccess);
        Assert.Equal(2, state.SaveCount);
    }

    [Fact]
    public async Task Subjects_cannot_be_read_or_changed_through_another_center_route()
    {
        var state = new State();
        var created = await state.Create.HandleAsync(new(CenterA, Actor, "MATH", "Toán", null), default);
        var id = created.Value.Id;

        var wrongCenterGet = await state.Get.HandleAsync(new(CenterB, id), default);
        var wrongCenterUpdate = await state.Update.HandleAsync(
            new(CenterB, id, Actor, 0, "MATH", "Toán mới", null), default);
        var wrongCenterArchive = await state.Archive.HandleAsync(new(CenterB, id, Actor, 0), default);
        Assert.Equal(SubjectErrors.NotFound, wrongCenterGet.Error);
        Assert.Equal(SubjectErrors.NotFound, wrongCenterUpdate.Error);
        Assert.Equal(SubjectErrors.NotFound, wrongCenterArchive.Error);
        Assert.Empty((await state.List.HandleAsync(new(CenterB, true), default)).Value);
        Assert.Equal("Toán", (await state.Get.HandleAsync(new(CenterA, id), default)).Value.Name);
    }

    [Fact]
    public async Task Archive_hides_subject_without_deleting_and_reactivate_restores_it()
    {
        var state = new State();
        var created = await state.Create.HandleAsync(new(CenterA, Actor, "MATH", "Toán", null), default);
        var id = created.Value.Id;
        var archived = await state.Archive.HandleAsync(new(CenterA, id, Actor, 0), default);
        Assert.True(archived.IsSuccess);
        Assert.False(archived.Value.IsActive);
        Assert.Empty((await state.List.HandleAsync(new(CenterA, false), default)).Value);
        Assert.Single((await state.List.HandleAsync(new(CenterA, true), default)).Value);
        Assert.Equal(SubjectErrors.ConcurrentChange,
            (await state.Reactivate.HandleAsync(new(CenterA, id, Actor, 0), default)).Error);
        Assert.Equal(SubjectErrors.AlreadyArchived,
            (await state.Archive.HandleAsync(new(CenterA, id, Actor, 1), default)).Error);
        Assert.Equal(SubjectErrors.ArchivedCannotEdit,
            (await state.Update.HandleAsync(new(CenterA, id, Actor, 1, "MATH", "Toán 2", null), default)).Error);
        Assert.Equal(SubjectErrors.CodeAlreadyExists,
            (await state.Create.HandleAsync(new(CenterA, Actor, "MATH", "Toán 2", null), default)).Error);

        var restored = await state.Reactivate.HandleAsync(new(CenterA, id, Actor, 1), default);
        Assert.True(restored.IsSuccess);
        Assert.True(restored.Value.IsActive);
        Assert.Single((await state.List.HandleAsync(new(CenterA, false), default)).Value);
        Assert.Equal(SubjectErrors.AlreadyActive,
            (await state.Reactivate.HandleAsync(new(CenterA, id, Actor, 2), default)).Error);
    }

    [Fact]
    public async Task Editing_same_details_is_a_no_op_and_concurrent_save_returns_conflict()
    {
        var state = new State();
        var created = await state.Create.HandleAsync(new(CenterA, Actor, "MATH", "Toán", null), default);
        var same = await state.Update.HandleAsync(new(CenterA, created.Value.Id, Actor, 0, "math", " Toán ", null), default);
        Assert.True(same.IsSuccess);
        Assert.Equal(1, state.SaveCount);

        state.FailNextSave = true;
        var racing = await state.Update.HandleAsync(new(CenterA, created.Value.Id, Actor, 0, "MATH", "Toán nâng cao", null), default);
        Assert.Equal(SubjectErrors.ConcurrentChange, racing.Error);
    }

    [Fact]
    public void Domain_rejects_invalid_details_and_preserves_history_on_archive()
    {
        Assert.False(Subject.IsValidCode("mã môn"));
        Assert.False(Subject.IsValidName(" "));
        Assert.False(Subject.IsValidDescription(new string('x', 501)));
        Assert.Throws<ArgumentException>(() => Subject.Create(CenterA, "a", "Toán", null, Actor, DateTimeOffset.UtcNow));

        var now = DateTimeOffset.UtcNow;
        var subject = Subject.Create(CenterA, "MATH", "Toán", null, Actor, now);
        subject.Archive(Actor, now.AddMinutes(1));
        Assert.Equal(Actor, subject.CreatedByUserId);
        Assert.Equal(Actor, subject.ArchivedByUserId);
        Assert.Equal(1, subject.Version);
    }

    private sealed class State : ISubjectRepository, IAcademicsUnitOfWork, ISubjectClassGuard, IClock
    {
        private readonly List<Subject> _subjects = [];
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public int SaveCount { get; private set; }
        public bool FailNextSave { get; set; }
        public CreateSubjectCommandHandler Create => new(this, this, this);
        public UpdateSubjectCommandHandler Update => new(this, this, this);
        public ArchiveSubjectCommandHandler Archive => new(this, this, this, this);
        public ReactivateSubjectCommandHandler Reactivate => new(this, this, this);
        public GetSubjectQueryHandler Get => new(this);
        public ListSubjectsQueryHandler List => new(this);

        public Task<Subject?> FindAsync(Guid centerId, Guid subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.SingleOrDefault(subject => subject.CenterId == centerId && subject.Id == subjectId));

        public Task<ISubjectClassTransaction> BeginAsync(Guid centerId, Guid subjectId, CancellationToken token) =>
            Task.FromResult<ISubjectClassTransaction>(new NoOpSubjectClassTransaction());

        public Task<bool> HasActiveClassesAsync(Guid centerId, Guid subjectId, CancellationToken token) =>
            Task.FromResult(false);

        private sealed class NoOpSubjectClassTransaction : ISubjectClassTransaction
        {
            public Task CommitAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        public Task<IReadOnlyList<Subject>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Subject>>(_subjects
                .Where(subject => subject.CenterId == centerId && (includeArchived || subject.IsActive)).ToArray());

        public Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.Any(subject => subject.CenterId == centerId &&
                subject.NormalizedCode == normalizedCode && subject.Id != excludingId));

        public Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.Any(subject => subject.CenterId == centerId &&
                subject.NormalizedName == normalizedName && subject.Id != excludingId));

        public Task AddAsync(Subject subject, CancellationToken cancellationToken)
        {
            _subjects.Add(subject);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new SubjectConcurrencyException(new InvalidOperationException("Simulated stale version."));
            }
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
