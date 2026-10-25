using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using TutorManagement.Modules.Organizations.Api.Centers;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Api.Rooms;
using TutorManagement.Modules.Organizations.Contracts.Rooms;

namespace TutorManagement.Modules.Organizations.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddOrganizationsApi(this IMvcBuilder builder)
    {
        builder.Services.AddScoped<IValidator<CreateCenterRequest>, CreateCenterRequestValidator>();
        builder.Services.AddScoped<IValidator<ReviewCenterRequest>, ReviewCenterRequestValidator>();
        builder.Services.AddScoped<IValidator<RequestCenterChangeRequest>, RequestCenterChangeRequestValidator>();
        builder.Services.AddScoped<IValidator<ReviewCenterChangeRequest>, ReviewCenterChangeRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateRoomRequest>, CreateRoomRequestValidator>();
        builder.Services.AddScoped<IValidator<UpdateRoomRequest>, UpdateRoomRequestValidator>();
        builder.Services.AddScoped<IValidator<RoomVersionRequest>, RoomVersionRequestValidator>();
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
