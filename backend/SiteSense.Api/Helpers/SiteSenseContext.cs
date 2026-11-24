using Microsoft.Extensions.Options;
using SiteSense.Api.Store;

namespace SiteSense.Api.Helpers;

// Set once by Init in Program.cs; tests replace both (Q67).
public static class SiteSenseContext
{
    public static IDocumentStore Store { get; set; } = null!;
    public static TimeProvider Time { get; set; } = TimeProvider.System;

    // Validates Store:RootPath here (NF-04): the store is created before the host is built, so ValidateOnStart would come too late.
    public static void Init(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(StoreOptions.Section).Get<StoreOptions>() ?? new StoreOptions();
        var result = new StoreOptionsValidator().Validate(null, options);
        if (result.Failed)
            throw new OptionsValidationException(StoreOptions.Section, typeof(StoreOptions), result.Failures);

        Store = new FileDocumentStore(options.RootPath);
    }
}
