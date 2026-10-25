namespace TutorManagement.WebApi.Responses;

public sealed class ApiResponse<T>
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }

    public List<ApiError>? Errors { get; set; }

    public string TraceId { get; set; } = string.Empty;

    public static ApiResponse<T> Ok(
        T data,
        string traceId,
        string message = "Request processed successfully") => new()
        {
            Success = true,
            Message = message,
            Data = data,
            Errors = null,
            TraceId = traceId
        };

    public static ApiResponse<T> Failure(
        IEnumerable<ApiError> errors,
        string traceId,
        string message = "Request failed") => new()
        {
            Success = false,
            Message = message,
            Data = default,
            Errors = errors.ToList(),
            TraceId = traceId
        };
}
