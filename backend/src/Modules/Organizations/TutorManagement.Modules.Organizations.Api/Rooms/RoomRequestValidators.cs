using FluentValidation;
using TutorManagement.Modules.Organizations.Contracts.Rooms;
using TutorManagement.Modules.Organizations.Domain.Rooms;

namespace TutorManagement.Modules.Organizations.Api.Rooms;

public sealed class CreateRoomRequestValidator : AbstractValidator<CreateRoomRequest>
{
    public CreateRoomRequestValidator()
    {
        RuleFor(request => request.Name).Must(Room.IsValidName)
            .WithMessage("Name must have 2–100 characters and no control characters.");
        RuleFor(request => request.LocationNote).Must(Room.IsValidLocationNote)
            .WithMessage("Location note must have at most 200 characters and no control characters.");
    }
}

public sealed class UpdateRoomRequestValidator : AbstractValidator<UpdateRoomRequest>
{
    public UpdateRoomRequestValidator()
    {
        RuleFor(request => request.Name).Must(Room.IsValidName)
            .WithMessage("Name must have 2–100 characters and no control characters.");
        RuleFor(request => request.LocationNote).Must(Room.IsValidLocationNote)
            .WithMessage("Location note must have at most 200 characters and no control characters.");
        RuleFor(request => request.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}

public sealed class RoomVersionRequestValidator : AbstractValidator<RoomVersionRequest>
{
    public RoomVersionRequestValidator() => RuleFor(request => request.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
}
