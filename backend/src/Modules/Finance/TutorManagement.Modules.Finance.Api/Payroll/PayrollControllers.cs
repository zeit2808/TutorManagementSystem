using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Finance.Application.Payroll;
using TutorManagement.Modules.Finance.Contracts.Payroll;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Finance.Api.Payroll;

public abstract class PayrollControllerBase : ControllerBase
{
    protected const string MonthRoute = "{month:regex(^\\d{{4}}-\\d{{2}}$)}";

    protected bool TryGetUser(out Guid userId) =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out userId) && userId != Guid.Empty;

    protected bool TryGetActor(out PayrollActor actor)
    {
        actor = null!;
        if (!TryGetUser(out var userId)) return false;
        var role = (HttpContext.Items[CenterAccessPolicyNames.RoleItemKey] as string) switch
        {
            "CenterOwner" => PayrollRole.Owner,
            "CenterAdmin" => PayrollRole.Manager,
            "Teacher" => PayrollRole.Teacher,
            _ => PayrollRole.Staff
        };
        actor = new PayrollActor(userId, role);
        return true;
    }

    protected IActionResult InvalidIdentity() => Unauthorized(ApiResponse<object>.Failure(
        [new ApiError("invalid_identity", "A valid user identity is required.")], HttpContext.TraceIdentifier, "Unauthorized."));

    protected IActionResult Failure(Error error)
    {
        var response = ApiResponse<object>.Failure([new ApiError(error.Code, error.Message, error.Field)],
            HttpContext.TraceIdentifier, error.Message);
        if (error == PayrollErrors.NotFound) return NotFound(response);
        if (error == PayrollErrors.Forbidden || error == PayrollErrors.Disabled)
            return StatusCode(StatusCodes.Status403Forbidden, response);
        if (error.Code == "validation_error") return BadRequest(response);
        if (error.Code == PayrollErrors.ConcurrentChange.Code) return Conflict(response);
        return UnprocessableEntity(response);
    }

    protected IActionResult Done<T>(Result<T> result) =>
        result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
}

/// <summary>A1/A2: the platform admin switches payroll on or off per center and picks the method. Never shows money.</summary>
[ApiController, ApiVersion(1.0), Tags("17 · Payroll")]
[Route("api/v{version:apiVersion}/platform/centers/{centerId:guid}/payroll-config")]
[Authorize(Roles = "PlatformAdmin")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class PlatformPayrollConfigController(PayrollWorkflow workflow) : PayrollControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PayrollConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, CancellationToken token) =>
        Done(await workflow.GetConfigAsync(centerId, token));

    [HttpPut, Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(Guid centerId, [FromBody] SetPayrollConfigRequest request, CancellationToken token)
    {
        if (!TryGetUser(out var actor)) return InvalidIdentity();
        return Done(await workflow.SetConfigAsync(centerId, request.Enabled, request.Mode, request.EffectiveMonth,
            request.Reason, request.ExpectedVersion, actor, token));
    }
}

/// <summary>A1: one table of every operating center and its payroll switch, so the admin finds it in one place.</summary>
[ApiController, ApiVersion(1.0), Tags("17 · Payroll")]
[Route("api/v{version:apiVersion}/platform/payroll-configs")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class PlatformPayrollOverviewController(PayrollWorkflow workflow) : PayrollControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PayrollConfigOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token) =>
        Ok(HttpContext.ToApiResponse(await workflow.ConfigOverviewAsync(token)));
}

/// <summary>
/// docs/LUONG-VA-CHIA-THU-NHAP.md. Owner: everything. Manager: everything except closing, reopening and payouts.
/// Teacher: own figures and reports. Staff: nothing.
/// </summary>
[ApiController, ApiVersion(1.0), Tags("17 · Payroll")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/payroll")]
[Authorize(Policy = CenterAccessPolicyNames.Member)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class PayrollController(PayrollWorkflow workflow) : PayrollControllerBase
{
    /// <summary>Whether payroll is on and what the caller may do; for the menu.</summary>
    [HttpGet("status")]
    [ProducesResponseType<ApiResponse<PayrollStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(Guid centerId, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Ok(HttpContext.ToApiResponse(await workflow.StatusAsync(centerId, actor, token)));
    }

    [HttpGet("months")]
    [ProducesResponseType<ApiResponse<List<PayrollMonthSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Months(Guid centerId, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.ListMonthsAsync(centerId, actor, token));
    }

    /// <summary>Teachers get only their own line, payouts and reports; no pool, expenses or other people.</summary>
    [HttpGet("months/" + MonthRoute)]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Month(Guid centerId, string month, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.GetMonthAsync(centerId, month, actor, token));
    }

    [HttpPost("months/" + MonthRoute + "/close")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(Guid centerId, string month, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.CloseAsync(centerId, month, actor, token));
    }

    [HttpPost("months/" + MonthRoute + "/reopen"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(Guid centerId, string month, [FromBody] PayrollReasonRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.ReopenAsync(centerId, month, request.Reason, actor, token));
    }

    [HttpPost("months/" + MonthRoute + "/adjustments"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddAdjustment(Guid centerId, string month, [FromBody] AddPayrollAdjustmentRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AddAdjustmentAsync(centerId, month, request.TeacherId, request.Amount, request.Reason,
            actor, token));
    }

    [HttpPost("adjustments/{adjustmentId:guid}/void"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VoidAdjustment(Guid centerId, Guid adjustmentId, [FromBody] PayrollReasonRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.VoidAdjustmentAsync(centerId, adjustmentId, request.Reason, actor, token));
    }

    [HttpPost("months/" + MonthRoute + "/expenses"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddExpense(Guid centerId, string month, [FromBody] AddSharedExpenseRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AddExpenseAsync(centerId, month, request.SpentOn, request.Category, request.Amount,
            request.Note, actor, token));
    }

    [HttpPost("expenses/{expenseId:guid}/void"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VoidExpense(Guid centerId, Guid expenseId, [FromBody] PayrollReasonRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.VoidExpenseAsync(centerId, expenseId, request.Reason, actor, token));
    }

    [HttpGet("rates")]
    [ProducesResponseType<ApiResponse<List<PayRateDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rates(Guid centerId, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.ListRatesAsync(centerId, actor, token));
    }

    [HttpPost("rates"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayRateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddRate(Guid centerId, [FromBody] AddPayRateRequest request, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AddRateAsync(centerId, request.TeacherId, request.ClassId, request.Amount,
            request.EffectiveFrom, actor, token));
    }

    [HttpPost("rates/{rateId:guid}/void"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayRateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VoidRate(Guid centerId, Guid rateId, [FromBody] PayrollReasonRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.VoidRateAsync(centerId, rateId, request.Reason, actor, token));
    }

    [HttpPost("months/" + MonthRoute + "/payouts"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPayout(Guid centerId, string month, [FromBody] AddPayrollPayoutRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AddPayoutAsync(centerId, month, request.TeacherId, request.Amount, request.Method,
            request.PaidOn, request.Note, request.RequestId, actor, token));
    }

    [HttpPost("payouts/{payoutId:guid}/void"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VoidPayout(Guid centerId, Guid payoutId, [FromBody] PayrollReasonRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.VoidPayoutAsync(centerId, payoutId, request.Reason, actor, token));
    }

    [HttpPost("months/" + MonthRoute + "/disputes"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddDispute(Guid centerId, string month, [FromBody] AddPayrollDisputeRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AddDisputeAsync(centerId, month, request.Message, actor, token));
    }

    [HttpPost("disputes/{disputeId:guid}/resolve"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PayrollMonthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveDispute(Guid centerId, Guid disputeId, [FromBody] ResolvePayrollDisputeRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.ResolveDisputeAsync(centerId, disputeId, request.Note, actor, token));
    }
}

/// <summary>C15: the caller's earnings in every center with payroll on.</summary>
[ApiController, ApiVersion(1.0), Tags("17 · Payroll")]
[Route("api/v{version:apiVersion}/me/earnings")]
[Authorize]
public sealed class MyEarningsController(PayrollWorkflow workflow) : PayrollControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<MyEarningsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] int? year, CancellationToken token)
    {
        if (!TryGetUser(out var userId)) return InvalidIdentity();
        var wanted = year ?? DateTime.UtcNow.AddHours(7).Year;
        if (wanted is < 2000 or > 2100)
            return BadRequest(ApiResponse<object>.Failure([new ApiError("validation_error", "year must be 2000–2100.", "year")],
                HttpContext.TraceIdentifier, "Request validation failed."));
        return Ok(HttpContext.ToApiResponse(await workflow.MyEarningsAsync(userId, wanted, token)));
    }
}
