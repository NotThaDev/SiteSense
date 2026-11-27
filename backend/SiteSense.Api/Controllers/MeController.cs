using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;

namespace SiteSense.Api.Controllers;

// The caller's tenant, roles and capabilities, as the API reads them from the token (Q78).
[ApiController]
[Route("api/v1/me")]
public sealed class MeController(IAuthorizationService auth) : ControllerBase
{
    [HttpGet]
    public async Task<MeResponse> Get()
    {
        async Task<bool> Can(string policy) => (await auth.AuthorizeAsync(User, policy)).Succeeded;
        return new(User.TenantId(), User.FindAll(SiteSenseClaims.RoleClaim).Select(c => c.Value).ToList(), User.Identity?.Name,
            new Capabilities(await Can(SiteSensePolicies.CanManage), await Can(SiteSensePolicies.CanSeeIndividuals), await Can(SiteSensePolicies.CanSeeAggregates)));
    }
}
