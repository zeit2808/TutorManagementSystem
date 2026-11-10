using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Modules.Finance.Application.Payments;

namespace TutorManagement.Modules.Finance.Api.Payments;

/// <summary>
/// SePay's doorbell (docs/THANH-TOAN-QR-KE-HOACH.md, Round 2d). Answers with fixed bodies so content negotiation
/// can never turn a good delivery into 406. 200 tells SePay to stop retrying; 401/500 make it retry.
/// Not part of the front-end contract.
/// </summary>
[ApiController, ApiVersion(1.0), ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v{version:apiVersion}/payments/webhooks/sepay/{endpointKey}")]
public sealed class SepayWebhookController(SepayWebhookReceiver receiver) : ControllerBase
{
    public const int MaxBodyBytes = 64 * 1024;

    [AllowAnonymous, EnableRateLimiting("payment-webhook"), HttpPost, RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string endpointKey, CancellationToken token)
    {
        var body = await ReadBodyAsync(token);
        if (body is null) return Json(StatusCodes.Status413PayloadTooLarge, "{\"success\":false}");
        var outcome = await receiver.ReceiveAsync(endpointKey, body, Request.Headers["X-SePay-Signature"].ToString(),
            Request.Headers["X-SePay-Timestamp"].ToString(), token);
        return outcome switch
        {
            WebhookOutcome.Accepted => Json(StatusCodes.Status200OK, "{\"success\":true}"),
            WebhookOutcome.Unauthorized => Json(StatusCodes.Status401Unauthorized, "{\"success\":false}"),
            // Not StatusCodeResult: client-error mapping would turn it into ProblemDetails and then 406.
            _ => new ContentResult { StatusCode = StatusCodes.Status404NotFound, Content = string.Empty }
        };
    }

    private async Task<byte[]?> ReadBodyAsync(CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static ContentResult Json(int status, string content) =>
        new() { StatusCode = status, ContentType = "application/json", Content = content };
}
