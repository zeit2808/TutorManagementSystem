using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.IdentityAccess.Api.Authentication;
using TutorManagement.Modules.IdentityAccess.Api.Authorization;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddIdentityAccessApi(this IMvcBuilder builder)
    {
        builder.Services
            .AddOptions<RefreshTokenCookieOptions>()
            .BindConfiguration(RefreshTokenCookieOptions.SectionName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Name),
                "Refresh token cookie name is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Path) && options.Path.StartsWith('/'),
                "Refresh token cookie path must start with '/'.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IRefreshTokenCookieManager, RefreshTokenCookieManager>();
        builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        builder.Services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();
        builder.Services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
        builder.Services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
        builder.Services.AddScoped<
            IValidator<ResendVerificationEmailRequest>,
            ResendVerificationEmailRequestValidator>();
        builder.Services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailRequestValidator>();
        builder.Services.AddScoped<IValidator<InviteCenterOwnerRequest>, InviteCenterOwnerRequestValidator>();
        builder.Services.AddScoped<
            IValidator<AcceptCenterInvitationRequest>,
            AcceptCenterInvitationRequestValidator>();
        builder.Services.AddScoped<IAuthorizationHandler, CenterMembershipAuthorizationHandler>();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                CenterAccessPolicies.Member,
                policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new CenterMembershipRequirement(CenterAccessPolicies.MemberRoles)));
            options.AddPolicy(
                CenterAccessPolicies.Management,
                policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new CenterMembershipRequirement(CenterAccessPolicies.ManagementRoles)));
            options.AddPolicy(
                CenterAccessPolicies.Owner,
                policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new CenterMembershipRequirement(CenterAccessPolicies.OwnerRoles)));
        });
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
