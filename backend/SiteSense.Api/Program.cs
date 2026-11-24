using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using SiteSense.Api.Diagnostics;
using SiteSense.Api.Store;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

builder.Services.AddSingleton<IValidateOptions<StoreOptions>, StoreOptionsValidator>();
builder.Services.AddOptions<StoreOptions>().BindConfiguration(StoreOptions.Section).ValidateOnStart();
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
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
// Served in every environment (Q69).
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

app.Run();
