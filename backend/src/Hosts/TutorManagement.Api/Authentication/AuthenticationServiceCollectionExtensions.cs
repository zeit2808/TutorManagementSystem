using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddTutorAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        var jwtOptions = section.Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is missing.");

        if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
            string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
            string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            throw new InvalidOperationException(
                "JWT issuer, audience and signing key are required.");
        }

        byte[] signingKey;
        try
        {
            signingKey = Convert.FromBase64String(jwtOptions.SigningKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "JWT signing key must be valid Base64.",
                exception);
        }

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key must contain at least 256 bits.");
        }

        if (jwtOptions.AccessTokenMinutes is < 1 or > 60)
        {
            throw new InvalidOperationException(
                "JWT access token lifetime must be between 1 and 60 minutes.");
        }

        if (jwtOptions.RefreshTokenDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                "Refresh token lifetime must be between 1 and 90 days.");
        }

        services.Configure<JwtOptions>(section);
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "unique_name",
                    RoleClaimType = "role"
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirst("sub")?.Value;
                        var sid = context.Principal?.FindFirst("sid")?.Value;
                        if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(sid, out var sessionId))
                        {
                            context.Fail("The session is no longer active.");
                            return;
                        }

                        try
                        {
                            if (!await context.HttpContext.RequestServices.GetRequiredService<IAuthSessionRepository>()
                                    .IsActiveAsync(sessionId, userId, context.HttpContext.RequestAborted))
                            {
                                context.Fail("The session is no longer active.");
                            }
                        }
                        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
                        {
                            // The browser left (reload, navigation). Nobody reads the answer; don't log an auth error.
                            context.Fail("The request was cancelled by the client.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await WriteFailureAsync(
                            context.Response,
                            context.HttpContext.TraceIdentifier,
                            "Authentication required.",
                            new ApiError(
                                "unauthorized",
                                "A valid access token is required."));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await WriteFailureAsync(
                            context.Response,
                            context.HttpContext.TraceIdentifier,
                            "Access denied.",
                            new ApiError(
                                "forbidden",
                                "You do not have permission to access this resource."));
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                "PlatformAdminOnly",
                policy => policy.RequireRole("PlatformAdmin"));
        });

        return services;
    }

    private static Task WriteFailureAsync(
        HttpResponse response,
        string traceId,
        string message,
        ApiError error) =>
        response.WriteAsJsonAsync(
            ApiResponse<object>.Failure([error], traceId, message));
}
