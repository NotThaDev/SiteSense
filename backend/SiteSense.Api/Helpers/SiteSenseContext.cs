using Microsoft.Extensions.Options;
using SiteSense.Api.Store;
using SiteSense.Api.Tenants;

namespace SiteSense.Api.Helpers;

// Set once by Init in Program.cs; tests replace Store and Time (Q67).
public static class SiteSenseContext
{
    public static IDocumentStore Store { get; set; } = null!;
    public static TimeProvider Time { get; set; } = TimeProvider.System;

    // Known tenants by id (ordinal, as store ids, Q76). Read-only after Init (Q14).
    public static IReadOnlyDictionary<string, TenantSettings> Tenants { get; private set; } = new Dictionary<string, TenantSettings>();

    // Validates Tenants and Store:RootPath here (NF-04): both are set before the host is built, so ValidateOnStart would come too late.
    public static void Init(WebApplicationBuilder builder)
    {
        var tenants = builder.Configuration.GetSection(TenantOptions.Section).Get<Dictionary<string, TenantSettings>>() ?? [];
        var tenantResult = new TenantOptionsValidator().Validate(null, tenants);
        if (tenantResult.Failed)
            throw new OptionsValidationException(TenantOptions.Section, typeof(Dictionary<string, TenantSettings>), tenantResult.Failures);

        var options = builder.Configuration.GetSection(StoreOptions.Section).Get<StoreOptions>() ?? new StoreOptions();
        var result = new StoreOptionsValidator().Validate(null, options);
        if (result.Failed)
            throw new OptionsValidationException(StoreOptions.Section, typeof(StoreOptions), result.Failures);

        Store = new FileDocumentStore(options.RootPath);
        Tenants = tenants.AsReadOnly();
    }
}
