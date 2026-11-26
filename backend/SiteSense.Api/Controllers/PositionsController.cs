using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;

namespace SiteSense.Api.Controllers;

// Only gateways (scope positions:write) may post positions (IDN-05).
[ApiController]
[Route("api/v1/positions")]
[Authorize(Policy = SiteSensePolicies.CanWritePositions)]
public sealed class PositionsController : ControllerBase
{
    // shortcut: guard only, answers 501; SS-22 replaces the body with batch ingestion (202).
    [HttpPost]
    public IActionResult Post()
    {
        // Not a throw: the exception handler would turn NotImplementedException into a 500.
        return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Position ingestion is not implemented yet.");
    }
}
