using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Modules.Finance.Api.Tuition;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Finance.Api.Payments;

/// <summary>
/// Reconciliation and alerts (docs/THANH-TOAN-QR-KE-HOACH.md, Đợt 5). Owner and managers read them; the owner asks for a
/// reconciliation now and marks an alert as seen.
/// </summary>
[ApiController, ApiVersion(1.0), Tags("16 · Tuition")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/tuition/payment-provider")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class PaymentOpsController(PaymentOpsWorkflow workflow) : TuitionControllerBase
{
    /// <summary>Reconciliation runs, newest first. Each night at 02:00 the last 3 days are checked again.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("reconciliations")]
    [ProducesResponseType<ApiResponse<TuitionPage<ReconciliationRunDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRuns(Guid centerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidQuery("page must be 1–1000000 and pageSize 1–100.", "page");
        return Ok(HttpContext.ToApiResponse(await workflow.ListRunsAsync(centerId, page, pageSize, token)));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("reconciliations/{runId:guid}")]
    [ProducesResponseType<ApiResponse<ReconciliationRunDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRun(Guid centerId, Guid runId, CancellationToken token) =>
        Mapped(await workflow.GetRunAsync(centerId, runId, token));

    /// <summary>Reconcile now (the Worker starts within half a minute). Needs a connection in Chạy thử or Tự động.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), EnableRateLimiting("payment-intent"), HttpPost("reconciliations")]
    [ProducesResponseType<ApiResponse<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestRun(Guid centerId, CancellationToken token) =>
        Done(await workflow.RequestAsync(centerId, token));

    /// <summary>
    /// Đợt 5 metrics for the last <c>days</c> days (1–31): doorbells, redeliveries, bad signatures, what found each
    /// transfer, statuses, latency from transfer to Auto receipt, manual work, receipt conflicts and the oldest wait.
    /// </summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("metrics")]
    [ProducesResponseType<ApiResponse<PaymentMetricsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Metrics(Guid centerId, [FromQuery] int days = 7, CancellationToken token = default) =>
        Done(await workflow.MetricsAsync(centerId, days, token));

    /// <summary><c>state</c>: Open (default), Acknowledged, Resolved or All.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("alerts")]
    [ProducesResponseType<ApiResponse<List<PaymentAlertDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAlerts(Guid centerId, [FromQuery] string state = "Open", CancellationToken token = default) =>
        Done(await workflow.ListAlertsAsync(centerId, state, token));

    /// <summary>The owner has seen it: no more reminders. A condition alert opens again if it goes away and comes back.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("alerts/{alertId:guid}/acknowledge")]
    [ProducesResponseType<ApiResponse<PaymentAlertDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Acknowledge(Guid centerId, Guid alertId, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Mapped(await workflow.AcknowledgeAsync(centerId, alertId, actor, token));
    }

    private IActionResult Mapped<T>(Result<T> result)
    {
        if (result.IsSuccess || (result.Error != PaymentOpsErrors.RunNotFound && result.Error != PaymentOpsErrors.AlertNotFound))
            return Done(result);
        return NotFound(ApiResponse<object>.Failure([new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
            HttpContext.TraceIdentifier, result.Error.Message));
    }
}
