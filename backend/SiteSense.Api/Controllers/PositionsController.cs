using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models;
using SiteSense.Api.Services;

namespace SiteSense.Api.Controllers;

// Only gateways (scope positions:write) may post positions (IDN-05).
[ApiController]
[Route("api/v1/positions")]
[Authorize(Policy = SiteSensePolicies.CanWritePositions)]
public sealed class PositionsController(ILogger<PositionsController> logger, PositionProcessor processor) : ControllerBase
{
    // 202 only after the new positions are fsynced (NF-05); a retry of the same batch stores nothing twice (TRK-01).
    [HttpPost]
    [ProducesResponseType<PositionBatchResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Post(PositionBatchRequest request, CancellationToken ct)
    {
        var positions = request.Positions
            .Select(p => new Position(request.DeviceId, p.TimestampMs, p.Lat, p.Lon, p.AccuracyM))
            .ToList();
        var result = await PositionIngest.IngestAsync(User.TenantId(), request.DeviceId, positions, logger, ct);
        if (result is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Device not found");
        // Positions inside the perimeter were marked: hand the device to ordered processing (TRK-02).
        if (result.Received > result.OutsidePerimeter) processor.Enqueue(User.TenantId(), request.DeviceId);
        return StatusCode(StatusCodes.Status202Accepted, result);
    }
}
