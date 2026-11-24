using Scalar.AspNetCore;
using SiteSense.Api.Helpers;

var builder = WebApplication.CreateBuilder(args);

SiteSenseContext.Init(builder);
builder.Services.AddHealthChecks();
builder.Services.AddControllers()
    // A missing required field fails as 400 instead of defaulting to 0 or null.
    .AddJsonOptions(o => o.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
// Same rule for the OpenAPI generator, so the schema marks the same fields required.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectRequiredConstructorParameters = true);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// First: every unhandled exception becomes a generic 500 ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
// Served in every environment (Q69).
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

app.Run();
