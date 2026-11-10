using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Modules.Finance.Api.Tuition;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Contracts.Payments;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Finance.Api.Payments;

/// <summary>
/// The center's SePay connection (docs/THANH-TOAN-QR-KE-HOACH.md, Round 2c). Owner and managers see it; only the owner
/// changes it, with the password asked again. Secrets are never returned.
/// </summary>
[ApiController, ApiVersion(1.0), Tags("16 · Tuition")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/tuition/payment-provider")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class PaymentProviderController(PaymentProviderWorkflow workflow) : TuitionControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet]
    [ProducesResponseType<ApiResponse<PaymentProviderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, CancellationToken token) =>
        Ok(HttpContext.ToApiResponse(await workflow.GetAsync(centerId, token)));

    /// <summary>
    /// K18: what Shadow would have booked next to the transfer receipts staff recorded, for Vietnam dates [from, to]
    /// (at most 93 days). Switch to Auto only when <c>agrees</c> is true.
    /// </summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("shadow-report")]
    [ProducesResponseType<ApiResponse<ShadowReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShadowReport(Guid centerId, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromServices] TutorManagement.Modules.Finance.Application.Tuition.ITuitionAcademicsReader academics, CancellationToken token) =>
        Done(await workflow.ShadowReportAsync(centerId, from, to, academics, token));

    /// <summary>Pull new transactions now (Shadow or Auto only). The Worker picks it up within a second or two.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), EnableRateLimiting("payment-intent"), HttpPost("pull")]
    [ProducesResponseType<ApiResponse<PaymentProviderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pull(Guid centerId, CancellationToken token) =>
        Done(await workflow.RequestPullAsync(centerId, token));

    [Authorize(Policy = CenterAccessPolicyNames.Owner), EnableRateLimiting("password-authenticated"), HttpPut,
     Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PaymentProviderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(Guid centerId, [FromBody] SavePaymentProviderRequest request, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        if (string.IsNullOrEmpty(request.CurrentPassword))
            return InvalidQuery("currentPassword is required.", "currentPassword");
        var origin = new RequestOrigin(HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
        return Done(await workflow.SaveAsync(centerId, actorId, request.CurrentPassword, request.Mode, request.WebhookSecret,
            request.ApiToken, request.ExpectedVersion, origin, token));
    }
}
