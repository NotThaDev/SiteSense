using Microsoft.Extensions.Options;
using SiteSense.Api.Store;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IValidateOptions<StoreOptions>, StoreOptionsValidator>();
builder.Services.AddOptions<StoreOptions>().BindConfiguration(StoreOptions.Section).ValidateOnStart();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
