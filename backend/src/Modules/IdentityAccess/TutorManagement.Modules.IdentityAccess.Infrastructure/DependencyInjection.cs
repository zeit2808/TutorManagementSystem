using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Login;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.AcceptCenterInvitation;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.InviteCenterOwner;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Logout;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Refresh;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.ResendVerificationEmail;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.VerifyEmail;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;
using TutorManagement.Modules.IdentityAccess.Application.Centers;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Centers.GetCenterContext;
using TutorManagement.Modules.IdentityAccess.Application.Centers.GetMyCenters;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Seeding;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityAccessInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddIdentityAccessPersistence(connectionString);

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IExternalLoginRepository, ExternalLoginRepository>();
        services.AddSingleton<IGoogleIdentityVerifier, GoogleIdentityVerifier>();
        services.AddOptions<GoogleAuthOptions>().BindConfiguration(GoogleAuthOptions.SectionName);
        services.AddScoped<ILoginSecurityRepository, LoginSecurityRepository>();
        services.AddScoped<ISecurityAuditRepository, SecurityAuditRepository>();
        services.AddScoped<IPlatformAdminReader, PlatformAdminReader>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddScoped<IUserSecurityTokenRepository, UserSecurityTokenRepository>();
        services.AddScoped<IEmailOutboxRepository, EmailOutboxRepository>();
        services.AddScoped<ICenterInvitationRepository, CenterInvitationRepository>();
        services.AddScoped<ICenterMembershipRepository, CenterMembershipRepository>();
        services.AddScoped<ICenterMembershipReader, CenterMembershipReader>();
        services.AddScoped<ICenterAccessService, CenterAccessService>();
        services.AddScoped<IIdentityUnitOfWork>(
            serviceProvider => serviceProvider.GetRequiredService<IdentityDbContext>());
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IOneTimeTokenService, OneTimeTokenService>();
        services.AddSingleton<IPasswordResetOtpService, PasswordResetOtpService>();
        services.AddScoped<ICommandHandler<LoginCommand, AuthenticationResult>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<GoogleLoginCommand, AuthenticationResult>, GoogleLoginCommandHandler>();
        services.AddScoped<ICommandHandler<LinkGoogleCommand>, LinkGoogleCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshTokenCommand, AuthenticationResult>, RefreshTokenCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutAllCommand>, LogoutAllCommandHandler>();
        services.AddScoped<ICommandHandler<ChangePasswordCommand, AuthenticationResult>, ChangePasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ForgotPasswordCommand>, ForgotPasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ResetPasswordCommand>, ResetPasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ResendVerificationEmailCommand>, ResendVerificationEmailCommandHandler>();
        services.AddScoped<ICommandHandler<VerifyEmailCommand>, VerifyEmailCommandHandler>();
        services.AddScoped<
            ICommandHandler<InviteCenterOwnerCommand, CenterInvitationResult>,
            InviteCenterOwnerCommandHandler>();
        services.AddScoped<
            ICommandHandler<AcceptCenterInvitationCommand, AcceptedInvitationResult>,
            AcceptCenterInvitationCommandHandler>();
        services.AddScoped<
            IQueryHandler<GetMyCentersQuery, List<CenterContextDto>>,
            GetMyCentersQueryHandler>();
        services.AddScoped<ICurrentUserReader, CurrentUserReader>();
        services.AddScoped<ICenterMemberStore, CenterMemberStore>();
        services.AddSingleton<IFrontendLinks, TutorManagement.Modules.IdentityAccess.Infrastructure.Email.FrontendLinks>();
        services.AddScoped<TutorManagement.Modules.IdentityAccess.Application.Email.IPlatformEmailService,
            TutorManagement.Modules.IdentityAccess.Infrastructure.Email.PlatformEmailService>();
        services.AddScoped<TutorManagement.Modules.IdentityAccess.Contracts.Members.ICenterMemberDirectory, CenterMemberDirectory>();
        services.AddScoped<TutorManagement.Modules.IdentityAccess.Contracts.Members.IUserDirectory, UserDirectory>();
        services.AddScoped<CenterMemberWorkflow>();
        services.AddScoped<PlatformInvitationWorkflow>();
        services.AddScoped<IQueryHandler<GetCurrentUserQuery, CurrentUserDto>, GetCurrentUserQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetCenterContextQuery, CenterContextDto>,
            GetCenterContextQueryHandler>();
        services.AddScoped<IdentitySeeder>();
        return services;
    }

    public static IServiceCollection AddIdentityAccessEmailWorker(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddIdentityAccessPersistence(connectionString);
        services.AddIdentityAccessEmailDelivery(configuration);
        var auditRetentionDays = configuration.GetValue("SecurityAudit:RetentionDays", 90);
        if (auditRetentionDays is < 1 or > 3650)
            throw new InvalidOperationException("SecurityAudit:RetentionDays must be between 1 and 3650.");
        services.AddScoped(serviceProvider => new SecurityAuditRetentionProcessor(
            serviceProvider.GetRequiredService<IdentityDbContext>(), auditRetentionDays));
        return services;
    }

    private static IServiceCollection AddIdentityAccessPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema)));
        return services;
    }

    public static IServiceCollection AddIdentityAccessEmailDelivery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EmailDeliveryOptions>()
            .Bind(configuration.GetSection(EmailDeliveryOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EmailDeliveryOptions>, EmailDeliveryOptionsValidator>();
        services.AddSingleton<FileEmailDeliveryClient>();
        services.AddSingleton<SmtpEmailDeliveryClient>();
        services.AddSingleton<IEmailDeliveryClient>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<EmailDeliveryOptions>>()
                .Value;

            return options.Provider switch
            {
                EmailDeliveryProvider.File =>
                    serviceProvider.GetRequiredService<FileEmailDeliveryClient>(),
                EmailDeliveryProvider.Smtp =>
                    serviceProvider.GetRequiredService<SmtpEmailDeliveryClient>(),
                _ => throw new InvalidOperationException(
                    $"Unsupported email delivery provider '{options.Provider}'.")
            };
        });
        services.AddScoped<EmailOutboxProcessor>();
        return services;
    }
}
