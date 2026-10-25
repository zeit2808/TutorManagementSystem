using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Application.Centers.ApproveCenter;
using TutorManagement.Modules.Organizations.Application.Centers.CreateCenter;
using TutorManagement.Modules.Organizations.Application.Centers.GetCenter;
using TutorManagement.Modules.Organizations.Application.Centers.ListCenters;
using TutorManagement.Modules.Organizations.Application.Centers.ListCenterChanges;
using TutorManagement.Modules.Organizations.Application.Centers.RequestCenterChange;
using TutorManagement.Modules.Organizations.Application.Centers.ReviewCenterChange;
using TutorManagement.Modules.Organizations.Application.Centers.ReinstateCenter;
using TutorManagement.Modules.Organizations.Application.Centers.RejectCenter;
using TutorManagement.Modules.Organizations.Application.Centers.SuspendCenter;
using TutorManagement.Modules.Organizations.Application.Rooms;
using TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

namespace TutorManagement.Modules.Organizations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrganizationsInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OrganizationDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", OrganizationDbContext.Schema)));

        services.AddScoped<ICenterRepository, CenterRepository>();
        services.AddScoped<TutorManagement.Modules.Organizations.Application.Centers.CenterLifecycleWorkflow>();
        services.AddScoped<ICenterChangeRequestRepository, CenterChangeRequestRepository>();
        services.AddScoped<IRoomRepository, RoomRepository>();
        services.AddScoped<IRoomArchiveGuard, RoomArchiveGuard>();
        services.AddScoped<IOrganizationCenterReader, OrganizationCenterReader>();
        services.AddScoped<IOrganizationUnitOfWork>(provider =>
            provider.GetRequiredService<OrganizationDbContext>());
        services.AddScoped<ICommandHandler<CreateRoomCommand, RoomDto>, CreateRoomCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateRoomCommand, RoomDto>, UpdateRoomCommandHandler>();
        services.AddScoped<ICommandHandler<ArchiveRoomCommand, RoomDto>, ArchiveRoomCommandHandler>();
        services.AddScoped<ICommandHandler<ReactivateRoomCommand, RoomDto>, ReactivateRoomCommandHandler>();
        services.AddScoped<IQueryHandler<GetRoomQuery, RoomDto>, GetRoomQueryHandler>();
        services.AddScoped<IQueryHandler<ListRoomsQuery, List<RoomDto>>, ListRoomsQueryHandler>();
        services.AddScoped<ICommandHandler<CreateCenterCommand, CenterDto>, CreateCenterCommandHandler>();
        services.AddScoped<ICommandHandler<ApproveCenterCommand, CenterDto>, ApproveCenterCommandHandler>();
        services.AddScoped<ICommandHandler<RejectCenterCommand, CenterDto>, RejectCenterCommandHandler>();
        services.AddScoped<ICommandHandler<SuspendCenterCommand, CenterDto>, SuspendCenterCommandHandler>();
        services.AddScoped<ICommandHandler<ReinstateCenterCommand, CenterDto>, ReinstateCenterCommandHandler>();
        services.AddScoped<ICommandHandler<RequestCenterChangeCommand, CenterChangeRequestDto>, RequestCenterChangeCommandHandler>();
        services.AddScoped<ICommandHandler<ReviewCenterChangeCommand, CenterChangeRequestDto>, ReviewCenterChangeCommandHandler>();
        services.AddScoped<IQueryHandler<GetCenterQuery, CenterDto>, GetCenterQueryHandler>();
        services.AddScoped<IQueryHandler<ListCenterChangesQuery, List<CenterChangeRequestDto>>, ListCenterChangesQueryHandler>();
        services.AddScoped<
            IQueryHandler<ListCentersQuery, List<CenterDto>>,
            ListCentersQueryHandler>();

        return services;
    }
}
