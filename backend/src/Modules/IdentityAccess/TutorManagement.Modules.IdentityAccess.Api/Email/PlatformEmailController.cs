using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.IdentityAccess.Application.Email;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Email;

public sealed class SendTestEmailRequest
{
    /// <summary>Leave empty to send to your own login email.</summary>
    public string? To { get; set; }
}

/// <summary>Checks real email delivery: the outbox status and a test message sent by the Worker.</summary>
[ApiController, ApiVersion(1.0), Tags("93 · Platform email")]
[Authorize(Roles = "PlatformAdmin")]
[Route("api/v{version:apiVersion}/platform/email")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
public sealed class PlatformEmailController(IPlatformEmailService service) : ControllerBase
{
    /// <summary>Counts and the 20 latest messages (no bodies).</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<EmailOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken token) =>
        Ok(HttpContext.ToApiResponse(await service.OverviewAsync(token)));

    /// <summary>Queues a test email; the Worker delivers it with the configured provider (File or Smtp).</summary>
    [HttpPost("test"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<OutboxEmailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendTest([FromBody] SendTestEmailRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty)
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError("invalid_identity", "A valid user identity is required.")], HttpContext.TraceIdentifier, "Unauthorized."));
        var result = await service.QueueTestAsync(actor, request.To, token);
        if (result.IsSuccess) return Ok(HttpContext.ToApiResponse(result.Value, "Test email queued."));
        var response = ApiResponse<object>.Failure([new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
            HttpContext.TraceIdentifier, result.Error.Message);
        return result.Error == PlatformEmailErrors.TooSoon
            ? StatusCode(StatusCodes.Status429TooManyRequests, response)
            : BadRequest(response);
    }
}
