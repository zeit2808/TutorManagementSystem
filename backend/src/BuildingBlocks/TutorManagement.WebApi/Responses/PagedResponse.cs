namespace TutorManagement.WebApi.Responses;

public sealed class PaginationMetadata
{
    public int Page { get; set; }

    public int Size { get; set; }

    public long TotalItems { get; set; }

    public int TotalPages => Size == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)Size);
}

public sealed class PagedResponse<T>
{
    public bool Success { get; set; }

    public List<T> Data { get; set; } = [];

    public PaginationMetadata Pagination { get; set; } = new();

    public List<ApiError> Errors { get; set; } = [];

    public string TraceId { get; set; } = string.Empty;
}
