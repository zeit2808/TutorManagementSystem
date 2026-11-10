using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Finance.Api.Tuition;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Contracts.Payments;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Finance.Api.Payments;

/// <summary>
/// "Tiền chưa rõ của ai" (docs/THANH-TOAN-QR-KE-HOACH.md, Đợt 4). Owner, manager and staff see the queue and assign;
/// owner and manager mark a transfer as not tuition; the owner decides held copies and retries failed ones.
/// </summary>
[ApiController, ApiVersion(1.0), Tags("16 · Tuition")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/bank-transactions")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class BankTransfersController(PaymentQueueWorkflow workflow) : TuitionControllerBase
{
    /// <summary><c>status</c>: Queue (needs review, held or failed — the default), NeedsReview, Held, Failed, Received, Booked, Ignored or All.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet]
    [ProducesResponseType<ApiResponse<TuitionPage<BankTransferDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] string status = "Queue", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken token = default)
    {
        if (IsTeacher()) return Forbidden();
        if (!IsValidPage(page, pageSize)) return InvalidQuery("page must be 1–1000000 and pageSize 1–100.", "page");
        return Mapped(await workflow.ListAsync(centerId, status, page, pageSize, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpPost("{transferId:guid}/assign"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<BankTransferDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid centerId, Guid transferId, [FromBody] AssignBankTransferRequest request,
        CancellationToken token)
    {
        if (IsTeacher()) return Forbidden();
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Mapped(await workflow.AssignAsync(centerId, transferId, request.StudentId, request.ChargeIds,
            request.ExpectedVersion, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{transferId:guid}/ignore"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<BankTransferDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ignore(Guid centerId, Guid transferId, [FromBody] IgnoreBankTransferRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Mapped(await workflow.IgnoreAsync(centerId, transferId, request.Reason, request.ExpectedVersion, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("{transferId:guid}/resolve-held"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<BankTransferDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveHeld(Guid centerId, Guid transferId, [FromBody] ResolveHeldTransferRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Mapped(await workflow.ResolveHeldAsync(centerId, transferId, request.IsDuplicate, request.Reason,
            request.ExpectedVersion, actor, token));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("{transferId:guid}/retry"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<BankTransferDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Retry(Guid centerId, Guid transferId, [FromBody] RetryBankTransferRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actor)) return InvalidIdentity();
        return Mapped(await workflow.RetryAsync(centerId, transferId, request.ExpectedVersion, actor, token));
    }

    private IActionResult Mapped<T>(Result<T> result)
    {
        if (result.IsSuccess || result.Error != QueueErrors.NotFound) return Done(result);
        return NotFound(ApiResponse<object>.Failure([new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
            HttpContext.TraceIdentifier, result.Error.Message));
    }
}
