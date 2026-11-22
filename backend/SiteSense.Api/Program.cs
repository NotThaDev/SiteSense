using SiteSense.Api.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

var app = builder.Build();
app.Use(Correlation.Middleware);

app.Run();
