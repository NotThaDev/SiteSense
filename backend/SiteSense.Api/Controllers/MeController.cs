using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;

namespace SiteSense.Api.Controllers;

// The caller's tenant and roles, as the API reads them from the token (Q78).
[ApiController]
[Route("api/v1/me")]
public sealed class MeController : ControllerBase
{
    [HttpGet]
    public MeResponse Get() =>
        new(User.TenantId(), User.FindAll(SiteSenseClaims.RoleClaim).Select(c => c.Value).ToList());
}
