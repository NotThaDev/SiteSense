using Hangfire;
using Scalar.AspNetCore;
using SiteSense.Api.Auth;
using SiteSense.Api.BackgroundJobs;
using SiteSense.Api.Helpers;
using SiteSense.Api.Realtime;
using SiteSense.Api.Services;
using SiteSense.Api.Store;

var builder = WebApplication.CreateBuilder(args);

SiteSenseContext.Init(builder);
// Local, not static: each test host runs this file again. A non-bool value fails at startup.
var testMode = builder.Configuration.GetValue<bool>(SiteSensePolicies.TestModeSetting);
builder.AddSiteSenseAuth(testMode);
builder.Services.AddHealthChecks();
builder.AddSiteSenseJobs();
// Ordered processing (TRK-02): one singleton, also a hosted service so its startup scan runs before the server starts.
builder.Services.AddSingleton<PositionProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PositionProcessor>());
// Site jobs: one keyed registration per job name, e.g. AddKeyedTransient<ISiteJob, SilentDevicesJob>("silent-devices") (SS-30, SS-33).
builder.Services.AddKeyedTransient<ITenantJob, RetentionJob>(RetentionJob.Name);
builder.Services.AddControllers()
    // A missing required field fails as 400 instead of defaulting to 0 or null.
    .AddJsonOptions(o => o.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
// Same rule for the OpenAPI generator, so the schema marks the same fields required.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectRequiredConstructorParameters = true);
builder.Services.AddProblemDetails();
// Realtime: the publisher is the only producer of events; the sender pushes them to the hub groups.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IEventSender, SignalRSender>();
builder.Services.AddSingleton<EventPublisher>();
builder.Services.AddSingleton<IRealtimePublisher>(sp => sp.GetRequiredService<EventPublisher>());
builder.Services.AddOpenApi();

var app = builder.Build();
if (testMode) app.Logger.LogWarning("Test mode is on: /api/v1/test endpoints are enabled.");

// First: every unhandled exception becomes a generic 500 ProblemDetails.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A stale version is the client's conflict, not a server error (JDB-10).
    StatusCodeSelector = ex => ex is ConcurrencyException ? StatusCodes.Status412PreconditionFailed : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
// Test mode off: the whole /api/v1/test prefix is 404 for every caller, before authentication can answer 401 (C-7.2a).
app.Use(async (ctx, next) =>
{
    if (!testMode && ctx.Request.Path.StartsWithSegments("/api/v1/test", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});
app.UseAuthentication();
// Cookie calls need a custom header a foreign site cannot add: CSRF defence next to SameSite=Lax. Browser navigation under /api/v1/auth is exempt.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true
        // Same test as the Smart scheme selector: anything but Bearer is a cookie call.
        && !ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        && ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !(HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase))
        && ctx.Request.Headers["X-CSRF"] != "1")
    {
        await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Missing CSRF header",
            detail: "Calls authenticated by cookie need the header 'X-CSRF: 1'.").ExecuteAsync(ctx);
        return;
    }
    await next();
});
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
app.MapHub<SiteHub>(SiteHub.RealtimePath, o => o.CloseOnAuthenticationExpiration = true);
// Recurring site jobs, one line each, every minute (JOB-01), e.g.:
// SiteJobRunner.Schedule(app.Services.GetRequiredService<IRecurringJobManager>(), "silent-devices");
// Retention runs hourly per tenant (Q96). Not on the InMemory test host: a wall-clock run must not delete test data mid-test.
if (builder.Configuration["Hangfire:Storage"] != "InMemory")
    SiteJobRunner.ScheduleTenantJob(app.Services.GetRequiredService<IRecurringJobManager>(), RetentionJob.Name, Cron.Hourly());

app.Run();
