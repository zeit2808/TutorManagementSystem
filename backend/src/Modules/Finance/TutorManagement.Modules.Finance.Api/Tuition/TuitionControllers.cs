using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Contracts.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Finance.Api.Tuition;

public abstract class TuitionControllerBase : ControllerBase
{
    protected bool TryGetActor(out Guid actorId) =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out actorId) && actorId != Guid.Empty;

    /// <summary>H13: owners, managers and staff see tuition; teachers do not.</summary>
    protected bool IsTeacher() => HttpContext.Items[CenterAccessPolicyNames.RoleItemKey] as string == "Teacher";

    protected IActionResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Failure(
        [new ApiError(TuitionErrors.TeacherForbidden.Code, TuitionErrors.TeacherForbidden.Message)],
        HttpContext.TraceIdentifier, TuitionErrors.TeacherForbidden.Message));

    protected IActionResult InvalidIdentity() => Unauthorized(ApiResponse<object>.Failure(
        [new ApiError("invalid_identity", "A valid user identity is required.")], HttpContext.TraceIdentifier, "Unauthorized."));

    protected IActionResult InvalidQuery(string message, string field) => BadRequest(ApiResponse<object>.Failure(
        [new ApiError("validation_error", message, field)], HttpContext.TraceIdentifier, "Request validation failed."));

    protected static bool IsValidPage(int page, int pageSize) => page is >= 1 and <= 1_000_000 && pageSize is >= 1 and <= 100;

    protected IActionResult Failure(Error error)
    {
        var response = ApiResponse<object>.Failure([new ApiError(error.Code, error.Message, error.Field)],
            HttpContext.TraceIdentifier, error.Message);
        if (error == TuitionErrors.NotFound) return NotFound(response);
        if (error.Code == "validation_error") return BadRequest(response);
        if (error.Code == TuitionErrors.ConcurrentChange.Code) return Conflict(response);
        return UnprocessableEntity(response);
    }

    protected IActionResult Done<T>(Result<T> result) =>
        result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);

    protected IActionResult Created(Result<PaymentIntentDto> result) => result.IsSuccess
        ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Payment QR ready."))
        : Failure(result.Error);
}

/// <summary>Tuition for the center (docs/HOC-PHI.md). Owner, manager and staff; teachers get 403.</summary>
[ApiController, ApiVersion(1.0), Tags("16 · Tuition")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class TuitionController(TuitionWorkflow workflow, PaymentIntentWorkflow intents) : TuitionControllerBase
{
    private static readonly string[] ChargeStatuses = ["All", "Open", "Overdue", "Paid", "Missed"];

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("tuition/summary")]
    [ProducesResponseType<ApiResponse<TuitionSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(Guid centerId, CancellationToken token) =>
        IsTeacher() ? Forbidden() : Ok(HttpContext.ToApiResponse(await workflow.SummaryAsync(centerId, token)));

    /// <summary>Classes with their fee for the running cycle, a pending new fee and the fee history.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("tuition/classes")]
    [ProducesResponseType<ApiResponse<List<ClassFeeRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Classes(Guid centerId, CancellationToken token) =>
        IsTeacher() ? Forbidden() : Ok(HttpContext.ToApiResponse((await workflow.ListClassFeesAsync(centerId, token)).ToList()));

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("classes/{classId:guid}/fee"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassFeeRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetFee(Guid centerId, Guid classId, [FromBody] SetClassFeeRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.SetFeeAsync(centerId, classId, request.MonthlyFee, actor, token));
    }

    /// <summary>status: All, Open (still owed), Overdue, Paid, Missed (has missed sessions, H18).</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("tuition/charges")]
    [ProducesResponseType<ApiResponse<TuitionPage<ChargeDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Charges(Guid centerId, [FromQuery] string status = "All", [FromQuery] Guid? classId = null,
        [FromQuery] Guid? studentId = null, [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken token = default)
    {
        if (IsTeacher()) return Forbidden();
        if (!IsValidPage(page, pageSize)) return InvalidQuery("page must be 1–1000000 and pageSize 1–100.", "page");
        if (!ChargeStatuses.Contains(status)) return InvalidQuery("status must be All, Open, Overdue, Paid or Missed.", "status");
        if (search?.Length > 100) return InvalidQuery("search must be at most 100 characters.", "search");
        return Ok(HttpContext.ToApiResponse(await workflow.ListChargesAsync(centerId, status, classId, studentId, search,
            page, pageSize, token)));
    }

    /// <summary>H18: reduce one cycle for missed sessions (amount 0 removes it).</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("tuition/charges/{chargeId:guid}/adjustment"),
     Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ChargeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjust(Guid centerId, Guid chargeId, [FromBody] AdjustChargeRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AdjustChargeAsync(centerId, chargeId, request.Amount, request.Sessions, request.Reason,
            actor, token));
    }

    /// <summary>One student's tuition: classes and cycles, charges, receipts, discounts, credit and the VietQR data.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("students/{studentId:guid}/tuition")]
    [ProducesResponseType<ApiResponse<StudentTuitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Student(Guid centerId, Guid studentId, CancellationToken token) =>
        IsTeacher() ? Forbidden() : Done(await workflow.StudentAccountAsync(centerId, studentId, token));

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpPost("students/{studentId:guid}/receipts"),
     Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ReceiptDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RecordPayment(Guid centerId, Guid studentId, [FromBody] RecordPaymentRequest request,
        CancellationToken token)
    {
        if (IsTeacher()) return Forbidden();
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        var result = await workflow.RecordPaymentAsync(centerId, studentId, request.Amount, request.Method, request.ReceivedOn,
            request.PayerName, request.Note, request.ChargeIds, request.RequestId, actor, token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Receipt recorded."))
            : Failure(result.Error);
    }

    /// <summary>The QR code for the student's open charges, amount and transfer content filled in.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), EnableRateLimiting("payment-intent"),
     HttpPost("students/{studentId:guid}/payment-intents"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PaymentIntentDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePaymentIntent(Guid centerId, Guid studentId,
        [FromBody] CreatePaymentIntentRequest request, CancellationToken token)
    {
        if (IsTeacher()) return Forbidden();
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Created(await intents.CreateForCenterAsync(centerId, studentId, request.ChargeIds, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("receipts")]
    [ProducesResponseType<ApiResponse<TuitionPage<ReceiptDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receipts(Guid centerId, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? method = null, [FromQuery] string? search = null, [FromQuery] bool includeVoided = true,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken token = default)
    {
        if (IsTeacher()) return Forbidden();
        if (!IsValidPage(page, pageSize)) return InvalidQuery("page must be 1–1000000 and pageSize 1–100.", "page");
        if (method is not null and not ("Cash" or "BankTransfer")) return InvalidQuery("method must be Cash or BankTransfer.", "method");
        if (search?.Length > 100) return InvalidQuery("search must be at most 100 characters.", "search");
        return Ok(HttpContext.ToApiResponse(await workflow.ListReceiptsAsync(centerId, from, to, method, search, includeVoided,
            page, pageSize, token)));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("receipts/{receiptId:guid}")]
    [ProducesResponseType<ApiResponse<ReceiptDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receipt(Guid centerId, Guid receiptId, CancellationToken token) =>
        IsTeacher() ? Forbidden() : Done(await workflow.GetReceiptAsync(centerId, receiptId, token));

    /// <summary>H12: the receipt stays with the reason; its money stops counting.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("receipts/{receiptId:guid}/void"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ReceiptDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Void(Guid centerId, Guid receiptId, [FromBody] VoidReceiptRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.VoidReceiptAsync(centerId, receiptId, request.Reason, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("students/{studentId:guid}/discounts"),
     Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentTuitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddDiscount(Guid centerId, Guid studentId, [FromBody] AddDiscountRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.AddDiscountAsync(centerId, studentId, request.ClassId, request.Kind, request.Value,
            request.FromDate, request.ToDate, request.Reason, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("discounts/{discountId:guid}/revoke")]
    [ProducesResponseType<ApiResponse<StudentTuitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeDiscount(Guid centerId, Guid discountId, CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.RevokeDiscountAsync(centerId, discountId, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("tuition/settings")]
    [ProducesResponseType<ApiResponse<FinanceSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(Guid centerId, CancellationToken token) =>
        IsTeacher() ? Forbidden() : Ok(HttpContext.ToApiResponse(await workflow.GetSettingsAsync(centerId, token)));

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("tuition/settings"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<FinanceSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(Guid centerId, [FromBody] UpdateFinanceSettingsRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Done(await workflow.UpdateSettingsAsync(centerId, request.BankBin, request.BankAccountNumber,
            request.BankAccountName, request.DueDays, request.EmailReminders, request.ExpectedVersion, actor, token));
    }
}

/// <summary>H14: the signed-in student's tuition in every center (read only).</summary>
[ApiController, ApiVersion(1.0), Tags("16 · Tuition")]
[Route("api/v{version:apiVersion}/me/tuition")]
[Authorize(Roles = "Student")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class MyTuitionController(StudentTuitionQuery query, PaymentIntentWorkflow intents) : TuitionControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<MyTuitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        return Ok(HttpContext.ToApiResponse(await query.ListAsync(userId, token)));
    }

    /// <summary>The QR code for the student's own charges; studentId is a profile from <c>GET me/tuition</c>.</summary>
    [EnableRateLimiting("payment-intent"), HttpPost("students/{studentId:guid}/payment-intents"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<PaymentIntentDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePaymentIntent(Guid studentId, [FromBody] CreatePaymentIntentRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        return Created(await intents.CreateForStudentAsync(userId, studentId, request.ChargeIds, token));
    }
}
