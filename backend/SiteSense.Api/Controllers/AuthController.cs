using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;

namespace SiteSense.Api.Controllers;

// Browser login (BFF): the API talks to Keycloak, the UI only gets a cookie.
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Login(string? returnUrl) =>
        // Local URLs only, else anyone could use us as an open redirect.
        Challenge(new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" }, OpenIdConnectDefaults.AuthenticationScheme);

    [HttpGet("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Logout() =>
        SignOut(new AuthenticationProperties { RedirectUri = "/" }, AuthSetup.CookieScheme, OpenIdConnectDefaults.AuthenticationScheme);
}
