using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Teaching;

/// <summary>D4: signed-in users find centers (that opted in) and their classes open for registration.</summary>
[ApiController, ApiVersion(1.0), Tags("12 · Course directory"), Authorize]
[Route("api/v{version:apiVersion}/directory")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
public sealed class CourseDirectoryController(CourseDirectory directory) : TeachingControllerBase
{
    /// <summary>Search by center or class name; filter by subject name.</summary>
    [HttpGet("centers")]
    [ProducesResponseType<ApiResponse<TeachingPage<DirectoryCenterDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Centers([FromQuery] string? search = null, [FromQuery] string? subject = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (search?.Length > 100 || subject?.Length > 100) return InvalidSearch();
        return Ok(HttpContext.ToApiResponse(await directory.ListCentersAsync(search, subject, page, pageSize, token)));
    }

    /// <summary>Subject names that currently have open classes, for the filter.</summary>
    [HttpGet("subjects")]
    [ProducesResponseType<ApiResponse<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Subjects(CancellationToken token) =>
        Ok(HttpContext.ToApiResponse((await directory.ListSubjectsAsync(token)).ToList()));

    [HttpGet("centers/{centerId:guid}")]
    [ProducesResponseType<ApiResponse<DirectoryCenterDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Center(Guid centerId, [FromQuery] string? subject = null,
        CancellationToken token = default)
    {
        if (subject?.Length > 100) return InvalidSearch();
        var result = await directory.GetCenterAsync(centerId, subject, token);
        return result.IsSuccess
            ? Ok(HttpContext.ToApiResponse(result.Value))
            : NotFound(ApiResponse<object>.Failure([new ApiError(result.Error.Code, result.Error.Message)],
                HttpContext.TraceIdentifier, result.Error.Message));
    }
}
