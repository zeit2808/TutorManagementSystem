using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Application.Rooms;
using TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.SharedKernel.Exceptions;

namespace TutorManagement.IntegrationTests;

public sealed class RoomCatalogTests
{
    private static readonly Guid Actor = Guid.Parse("f96e0197-bd86-4f53-965b-528b469d1596");

    [Fact]
    public async Task Room_codes_and_names_are_unique_per_center()
    {
        var state = new State();
        var first = await state.Create.HandleAsync(new(state.CenterA, Actor, " a2 ", "  Phòng   A2 ", "Tầng 2"), default);
        Assert.True(first.IsSuccess);
        Assert.Equal("A2", first.Value.Code);
        Assert.Equal("Phòng A2", first.Value.Name);

        Assert.Equal(RoomErrors.CodeAlreadyExists,
            (await state.Create.HandleAsync(new(state.CenterA, Actor, "A2", "Phòng B", null), default)).Error);
        Assert.Equal(RoomErrors.NameAlreadyExists,
            (await state.Create.HandleAsync(new(state.CenterA, Actor, "B2", "phòng a2", null), default)).Error);
        Assert.True((await state.Create.HandleAsync(new(state.CenterB, Actor, "A2", "Phòng A2", null), default)).IsSuccess);
        Assert.Equal(2, state.SaveCount);
    }

    [Fact]
    public async Task Room_cannot_be_accessed_through_another_center_route()
    {
        var state = new State();
        var created = await state.Create.HandleAsync(new(state.CenterA, Actor, "A2", "Phòng A2", null), default);
        var id = created.Value.Id;
        Assert.Equal(RoomErrors.NotFound, (await state.Get.HandleAsync(new(state.CenterB, id), default)).Error);
        Assert.Equal(RoomErrors.NotFound,
            (await state.Update.HandleAsync(new(state.CenterB, id, Actor, 0, "A3", "Phòng A3", null), default)).Error);
        Assert.Equal(RoomErrors.NotFound,
            (await state.Archive.HandleAsync(new(state.CenterB, id, Actor, 0), default)).Error);
        Assert.Empty((await state.List.HandleAsync(new(state.CenterB, true), default)).Value);
    }

    [Fact]
    public async Task Archive_retains_room_and_history_and_reactivation_requires_current_version()
    {
        var state = new State();
        var created = await state.Create.HandleAsync(new(state.CenterA, Actor, "A2", "Phòng A2", null), default);
        var id = created.Value.Id;
        var archived = await state.Archive.HandleAsync(new(state.CenterA, id, Actor, 0), default);
        Assert.True(archived.IsSuccess);
        Assert.False(archived.Value.IsActive);
        var archivedEntity = await ((IRoomRepository)state).FindAsync(state.CenterA, id, default);
        Assert.Equal(Actor, archivedEntity!.ArchivedByUserId);
        Assert.Empty((await state.List.HandleAsync(new(state.CenterA, false), default)).Value);
        Assert.Single((await state.List.HandleAsync(new(state.CenterA, true), default)).Value);
        Assert.Equal(RoomErrors.ConcurrentChange,
            (await state.Reactivate.HandleAsync(new(state.CenterA, id, Actor, 0), default)).Error);
        Assert.Equal(RoomErrors.ArchivedCannotEdit,
            (await state.Update.HandleAsync(new(state.CenterA, id, Actor, 1, "A2", "Phòng A2", null), default)).Error);
        Assert.Equal(RoomErrors.CodeAlreadyExists,
            (await state.Create.HandleAsync(new(state.CenterA, Actor, "A2", "Phòng B", null), default)).Error);

        var restored = await state.Reactivate.HandleAsync(new(state.CenterA, id, Actor, 1), default);
        Assert.True(restored.IsSuccess);
        Assert.True(restored.Value.IsActive);
        Assert.Equal(2, restored.Value.Version);
    }

    [Fact]
    public async Task Mutations_require_approved_center_and_stale_writes_conflict()
    {
        var state = new State();
        var created = await state.Create.HandleAsync(new(state.CenterA, Actor, "A2", "Phòng A2", null), default);
        var unchanged = await state.Update.HandleAsync(new(state.CenterA, created.Value.Id, Actor,
            0, "a2", " Phòng A2 ", null), default);
        Assert.True(unchanged.IsSuccess);
        Assert.Equal(1, state.SaveCount);

        state.FailNextSave = true;
        var conflict = await state.Update.HandleAsync(new(state.CenterA, created.Value.Id, Actor,
            0, "A2", "Phòng A2", "Tầng 3"), default);
        Assert.Equal(RoomErrors.ConcurrentChange, conflict.Error);

        state.CenterAIsApproved = false;
        Assert.Equal(RoomErrors.CenterNotReady,
            (await state.Create.HandleAsync(new(state.CenterA, Actor, "B2", "Phòng B2", null), default)).Error);
        Assert.Equal(RoomErrors.CenterNotReady,
            (await state.Archive.HandleAsync(new(state.CenterA, created.Value.Id, Actor, 1), default)).Error);
    }

    [Fact]
    public void Domain_rejects_invalid_code_and_location_note()
    {
        Assert.False(Room.IsValidCode("phòng 2"));
        Assert.False(Room.AreDetailsValid("A2", " ", null));
        Assert.False(Room.IsValidLocationNote(new string('x', 201)));
        Assert.Throws<ArgumentException>(() => Room.Create(Guid.NewGuid(), "A2", "Phòng A2", new string('x', 201),
            Actor, DateTimeOffset.UtcNow));
    }

    private sealed class State : IOrganizationCenterReader, IRoomRepository, IRoomArchiveGuard,
        IRoomArchiveTransaction, IOrganizationUnitOfWork, IClock
    {
        private readonly List<Room> _rooms = [];
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public Guid CenterA { get; } = Guid.NewGuid();
        public Guid CenterB { get; } = Guid.NewGuid();
        public bool CenterAIsApproved { get; set; } = true;
        public int SaveCount { get; private set; }
        public bool FailNextSave { get; set; }
        public CreateRoomCommandHandler Create => new(this, this, this, this);
        public UpdateRoomCommandHandler Update => new(this, this, this, this);
        public ArchiveRoomCommandHandler Archive => new(this, this, this, this, this);
        public ReactivateRoomCommandHandler Reactivate => new(this, this, this, this);
        public GetRoomQueryHandler Get => new(this);
        public ListRoomsQueryHandler List => new(this);

        public Task<CenterAccessSnapshot?> FindAsync(Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult(centerId == CenterA
                ? new CenterAccessSnapshot(CenterA, "Center A", CenterAIsApproved)
                : centerId == CenterB ? new CenterAccessSnapshot(CenterB, "Center B", true) : null);

        public Task<IReadOnlyCollection<CenterAccessSnapshot>> ListByIdsAsync(
            IReadOnlyCollection<Guid> centerIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CenterAccessSnapshot>>(
                centerIds.Select(id => new CenterAccessSnapshot(id, "Center", id != CenterA || CenterAIsApproved)).ToArray());

        public Task<Room?> FindAsync(Guid centerId, Guid roomId, CancellationToken cancellationToken) =>
            Task.FromResult(_rooms.SingleOrDefault(room => room.CenterId == centerId && room.Id == roomId));

        public Task<IReadOnlyList<Room>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Room>>(_rooms
                .Where(room => room.CenterId == centerId && (includeArchived || room.IsActive)).ToArray());

        public Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId,
            CancellationToken cancellationToken) => Task.FromResult(_rooms.Any(room => room.CenterId == centerId &&
                room.NormalizedCode == normalizedCode && room.Id != excludingId));

        public Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId,
            CancellationToken cancellationToken) => Task.FromResult(_rooms.Any(room => room.CenterId == centerId &&
                room.NormalizedName == normalizedName && room.Id != excludingId));

        public Task AddAsync(Room room, CancellationToken cancellationToken)
        {
            _rooms.Add(room);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new ConflictException("Simulated stale room version.");
            }
            SaveCount++;
            return Task.FromResult(1);
        }

        public Task<IRoomArchiveTransaction> BeginAsync(Guid centerId, Guid roomId, CancellationToken token) =>
            Task.FromResult<IRoomArchiveTransaction>(this);
        public Task<bool> HasFutureSessionsAsync(Guid centerId, Guid roomId, DateTimeOffset now, CancellationToken token) =>
            Task.FromResult(false);
        public Task CommitAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
