using Microsoft.Extensions.Options;
using SiteSense.Api.Store;

namespace SiteSense.Api.Tenants;

public static class TenantOptions
{
    public const string Section = "Tenants";
}

public sealed class TenantSettings
{
    // Positions older than this are deleted (PRV-04).
    public int RetentionDays { get; set; }

    // A device on site silent for longer than this raises DeviceSilent (TRK-06).
    public int SilentAfterMinutes { get; set; }
}

public sealed class TenantOptionsValidator : IValidateOptions<Dictionary<string, TenantSettings>>
{
    public ValidateOptionsResult Validate(string? name, Dictionary<string, TenantSettings> tenants)
    {
        if (tenants.Count == 0)
            return ValidateOptionsResult.Fail("Tenants: at least one tenant is required.");

        var failures = new List<string>();
        foreach (var (id, settings) in tenants)
        {
            if (!FileDocumentStore.IsValidName(id))
                failures.Add($"Tenants: '{id}' is not a valid tenant id.");
            if (settings.RetentionDays < 1)
                failures.Add($"Tenants:{id}:RetentionDays must be at least 1.");
            if (settings.SilentAfterMinutes < 1)
                failures.Add($"Tenants:{id}:SilentAfterMinutes must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
