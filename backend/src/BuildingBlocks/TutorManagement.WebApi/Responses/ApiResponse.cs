namespace TutorManagement.WebApi.Responses;

public sealed class ApiResponse<T>
{
    public bool Success { get; set; }

    public T? Data { get; set; }

    public List<ApiError> Errors { get; set; } = [];

    public string TraceId { get; set; } = string.Empty;

    public static ApiResponse<T> Ok(T data, string traceId) => new()
    {
        Success = true,
        Data = data,
        TraceId = traceId
    };

    public static ApiResponse<T> Failure(IEnumerable<ApiError> errors, string traceId) => new()
    {
        Success = false,
        Errors = errors.ToList(),
        TraceId = traceId
    };
}
