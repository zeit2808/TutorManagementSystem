namespace TutorManagement.WebApi.Responses;

public sealed class ApiError
{
    public ApiError()
    {
    }

    public ApiError(string code, string message, string? field = null)
    {
        Code = code;
        Message = message;
        Field = field;
    }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Field { get; set; }
}
