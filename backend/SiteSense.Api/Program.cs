using Scalar.AspNetCore;
using SiteSense.Api.Auth;
using SiteSense.Api.Diagnostics;
using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

SiteSenseContext.Init(builder);
builder.AddSiteSenseAuth();
builder.Services.AddHealthChecks();
builder.Services.AddControllers()
    // A missing required field fails as 400 instead of defaulting to 0 or null.
    .AddJsonOptions(o => o.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
// Same rule for the OpenAPI generator, so the schema marks the same fields required.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectRequiredConstructorParameters = true);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();
app.Use(Correlation.Middleware);

// First: every unhandled exception becomes a generic 500 ProblemDetails.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A stale version is the client's conflict, not a server error (JDB-10).
    StatusCodeSelector = ex => ex is ConcurrencyException ? StatusCodes.Status412PreconditionFailed : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseAuthentication();
// A valid token for a tenant not in configuration is forbidden on every endpoint, whatever its policy (Q14).
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && !SiteSenseContext.Tenants.ContainsKey(ctx.User.TenantId()))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
// Served in every environment (Q69).
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();
app.MapControllers();

app.Run();
