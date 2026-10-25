using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.SharedKernel.Exceptions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Rooms;

public sealed record CreateRoomCommand(Guid CenterId, Guid ActorId, string Code, string Name,
    string? LocationNote) : ICommand<RoomDto>;

public sealed record UpdateRoomCommand(Guid CenterId, Guid RoomId, Guid ActorId, int ExpectedVersion,
    string Code, string Name, string? LocationNote) : ICommand<RoomDto>;

public sealed record ArchiveRoomCommand(Guid CenterId, Guid RoomId, Guid ActorId, int ExpectedVersion) : ICommand<RoomDto>;

public sealed record ReactivateRoomCommand(Guid CenterId, Guid RoomId, Guid ActorId, int ExpectedVersion) : ICommand<RoomDto>;

public sealed record GetRoomQuery(Guid CenterId, Guid RoomId) : IQuery<RoomDto>;

public sealed record ListRoomsQuery(Guid CenterId, bool IncludeArchived) : IQuery<List<RoomDto>>;

public sealed class CreateRoomCommandHandler(
    IOrganizationCenterReader centers, IRoomRepository rooms, IOrganizationUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CreateRoomCommand, RoomDto>
{
    public async Task<Result<RoomDto>> HandleAsync(CreateRoomCommand command, CancellationToken cancellationToken)
    {
        if (command.CenterId == Guid.Empty || command.ActorId == Guid.Empty ||
            !Room.AreDetailsValid(command.Code, command.Name, command.LocationNote))
            return Result<RoomDto>.Failure(RoomErrors.InvalidDetails);

        if (!await RoomWriteGuard.IsApprovedAsync(centers, command.CenterId, cancellationToken))
            return Result<RoomDto>.Failure(RoomErrors.CenterNotReady);
        var duplicate = await RoomWriteGuard.CheckDuplicatesAsync(rooms, command.CenterId,
            command.Code, command.Name, null, cancellationToken);
        if (duplicate != Error.None) return Result<RoomDto>.Failure(duplicate);

        var room = Room.Create(command.CenterId, command.Code, command.Name,
            command.LocationNote, command.ActorId, clock.UtcNow);
        await rooms.AddAsync(room, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OrganizationDuplicateException exception)
        {
            return Result<RoomDto>.Failure(RoomWriteGuard.DuplicateError(exception));
        }
        return Result<RoomDto>.Success(RoomDto.From(room));
    }
}

public sealed class UpdateRoomCommandHandler(
    IOrganizationCenterReader centers, IRoomRepository rooms, IOrganizationUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UpdateRoomCommand, RoomDto>
{
    public async Task<Result<RoomDto>> HandleAsync(UpdateRoomCommand command, CancellationToken cancellationToken)
    {
        if (command.CenterId == Guid.Empty || command.RoomId == Guid.Empty || command.ActorId == Guid.Empty ||
            command.ExpectedVersion < 0 ||
            !Room.AreDetailsValid(command.Code, command.Name, command.LocationNote))
            return Result<RoomDto>.Failure(RoomErrors.InvalidDetails);

        if (!await RoomWriteGuard.IsApprovedAsync(centers, command.CenterId, cancellationToken))
            return Result<RoomDto>.Failure(RoomErrors.CenterNotReady);
        var room = await rooms.FindAsync(command.CenterId, command.RoomId, cancellationToken);
        if (room is null) return Result<RoomDto>.Failure(RoomErrors.NotFound);
        if (room.Version != command.ExpectedVersion) return Result<RoomDto>.Failure(RoomErrors.ConcurrentChange);
        if (!room.IsActive) return Result<RoomDto>.Failure(RoomErrors.ArchivedCannotEdit);

        var duplicate = await RoomWriteGuard.CheckDuplicatesAsync(rooms, command.CenterId,
            command.Code, command.Name, room.Id, cancellationToken);
        if (duplicate != Error.None) return Result<RoomDto>.Failure(duplicate);
        if (!room.Update(command.Code, command.Name, command.LocationNote,
            command.ActorId, clock.UtcNow))
            return Result<RoomDto>.Success(RoomDto.From(room));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OrganizationDuplicateException exception)
        {
            return Result<RoomDto>.Failure(RoomWriteGuard.DuplicateError(exception));
        }
        catch (ConflictException)
        {
            return Result<RoomDto>.Failure(RoomErrors.ConcurrentChange);
        }
        return Result<RoomDto>.Success(RoomDto.From(room));
    }
}

public sealed class ArchiveRoomCommandHandler(
    IOrganizationCenterReader centers, IRoomRepository rooms, IOrganizationUnitOfWork unitOfWork,
    IRoomArchiveGuard archiveGuard, IClock clock)
    : ICommandHandler<ArchiveRoomCommand, RoomDto>
{
    public async Task<Result<RoomDto>> HandleAsync(ArchiveRoomCommand command, CancellationToken cancellationToken)
    {
        if (command.CenterId == Guid.Empty || command.RoomId == Guid.Empty || command.ActorId == Guid.Empty || command.ExpectedVersion < 0)
            return Result<RoomDto>.Failure(RoomErrors.InvalidDetails);
        if (!await RoomWriteGuard.IsApprovedAsync(centers, command.CenterId, cancellationToken))
            return Result<RoomDto>.Failure(RoomErrors.CenterNotReady);
        await using var guard = await archiveGuard.BeginAsync(command.CenterId, command.RoomId, cancellationToken);
        var room = await rooms.FindAsync(command.CenterId, command.RoomId, cancellationToken);
        if (room is null) return Result<RoomDto>.Failure(RoomErrors.NotFound);
        if (room.Version != command.ExpectedVersion) return Result<RoomDto>.Failure(RoomErrors.ConcurrentChange);
        if (await archiveGuard.HasFutureSessionsAsync(command.CenterId, command.RoomId, clock.UtcNow, cancellationToken))
            return Result<RoomDto>.Failure(RoomErrors.HasFutureSessions);
        if (!room.Archive(command.ActorId, clock.UtcNow)) return Result<RoomDto>.Failure(RoomErrors.AlreadyArchived);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            return Result<RoomDto>.Failure(RoomErrors.ConcurrentChange);
        }
        await guard.CommitAsync(cancellationToken);
        return Result<RoomDto>.Success(RoomDto.From(room));
    }
}

public sealed class ReactivateRoomCommandHandler(
    IOrganizationCenterReader centers, IRoomRepository rooms, IOrganizationUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ReactivateRoomCommand, RoomDto>
{
    public async Task<Result<RoomDto>> HandleAsync(ReactivateRoomCommand command, CancellationToken cancellationToken)
    {
        if (command.CenterId == Guid.Empty || command.RoomId == Guid.Empty || command.ActorId == Guid.Empty || command.ExpectedVersion < 0)
            return Result<RoomDto>.Failure(RoomErrors.InvalidDetails);
        if (!await RoomWriteGuard.IsApprovedAsync(centers, command.CenterId, cancellationToken))
            return Result<RoomDto>.Failure(RoomErrors.CenterNotReady);
        var room = await rooms.FindAsync(command.CenterId, command.RoomId, cancellationToken);
        if (room is null) return Result<RoomDto>.Failure(RoomErrors.NotFound);
        if (room.Version != command.ExpectedVersion) return Result<RoomDto>.Failure(RoomErrors.ConcurrentChange);
        if (!room.Reactivate(command.ActorId, clock.UtcNow)) return Result<RoomDto>.Failure(RoomErrors.AlreadyActive);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            return Result<RoomDto>.Failure(RoomErrors.ConcurrentChange);
        }
        return Result<RoomDto>.Success(RoomDto.From(room));
    }
}

public sealed class GetRoomQueryHandler(IRoomRepository rooms) : IQueryHandler<GetRoomQuery, RoomDto>
{
    public async Task<Result<RoomDto>> HandleAsync(GetRoomQuery query, CancellationToken cancellationToken)
    {
        var room = await rooms.FindAsync(query.CenterId, query.RoomId, cancellationToken);
        return room is null ? Result<RoomDto>.Failure(RoomErrors.NotFound) : Result<RoomDto>.Success(RoomDto.From(room));
    }
}

public sealed class ListRoomsQueryHandler(IRoomRepository rooms) : IQueryHandler<ListRoomsQuery, List<RoomDto>>
{
    public async Task<Result<List<RoomDto>>> HandleAsync(ListRoomsQuery query, CancellationToken cancellationToken)
    {
        var roomsInCenter = await rooms.ListAsync(query.CenterId, query.IncludeArchived, cancellationToken);
        return Result<List<RoomDto>>.Success(roomsInCenter.Select(RoomDto.From).ToList());
    }
}

internal static class RoomWriteGuard
{
    public static async Task<bool> IsApprovedAsync(IOrganizationCenterReader centers, Guid centerId, CancellationToken cancellationToken) =>
        (await centers.FindAsync(centerId, cancellationToken))?.IsApproved == true;

    public static async Task<Error> CheckDuplicatesAsync(IRoomRepository rooms, Guid centerId,
        string code, string name, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (await rooms.CodeExistsAsync(centerId, Room.NormalizeCode(code), excludingId, cancellationToken))
            return RoomErrors.CodeAlreadyExists;
        if (await rooms.NameExistsAsync(centerId, Room.NormalizeName(name).ToUpperInvariant(), excludingId, cancellationToken))
            return RoomErrors.NameAlreadyExists;
        return Error.None;
    }

    public static Error DuplicateError(OrganizationDuplicateException exception) => exception.ConstraintName switch
    {
        "ux_rooms_center_code" => RoomErrors.CodeAlreadyExists,
        "ux_rooms_center_name" => RoomErrors.NameAlreadyExists,
        _ => throw exception
    };
}
