using BratnavaFC.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult ToResponse<T>(Result<T> result,
        int? overrideSuccessStatus = null)
    {
        var response = new ApiResponse<T>(
            result.Success,
            result.Data,
            result.Message,
            result.Error,
            result.Errors
        );
        var status = result.Success
            ? overrideSuccessStatus ?? (int)result.Status
            : (int)result.Status;
        return StatusCode(status, response);
    }

    protected IActionResult ToResponse(Result result,
        int? overrideSuccessStatus = null)
    {
        var response = new ApiResponse<object>(
            result.Success, null, result.Message, result.Error, result.Errors
        );
        var status = result.Success
            ? overrideSuccessStatus ?? (int)result.Status
            : (int)result.Status;
        return StatusCode(status, response);
    }
}
