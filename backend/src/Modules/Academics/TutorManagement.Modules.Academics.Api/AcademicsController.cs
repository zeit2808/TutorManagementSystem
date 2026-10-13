using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/modules/academics")]
public sealed class AcademicsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<ModuleStatusResponse>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<ModuleStatusResponse>> GetModuleStatus() =>
        Ok(HttpContext.ToApiResponse(new ModuleStatusResponse("Academics", "ready")));
}

public sealed class ModuleStatusResponse
{
    public ModuleStatusResponse()
    {
    }

    public ModuleStatusResponse(string name, string status)
    {
        Name = name;
        Status = status;
    }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
