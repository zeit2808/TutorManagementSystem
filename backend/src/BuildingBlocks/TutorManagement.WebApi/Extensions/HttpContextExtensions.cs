using Microsoft.AspNetCore.Http;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.WebApi.Extensions;

public static class HttpContextExtensions
{
    public static ApiResponse<T> ToApiResponse<T>(this HttpContext context, T data) =>
        ApiResponse<T>.Ok(data, context.TraceIdentifier);
}
