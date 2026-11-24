using Microsoft.Extensions.Options;

namespace SiteSense.Api.Store;

public sealed class StoreOptions
{
    public const string Section = "Store";

    public string RootPath { get; set; } = "";
}

public sealed class StoreOptionsValidator : IValidateOptions<StoreOptions>
{
    public ValidateOptionsResult Validate(string? name, StoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
            return ValidateOptionsResult.Fail("Store:RootPath is required.");

        var probe = Path.Combine(options.RootPath, $".write-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(options.RootPath);
            File.WriteAllText(probe, "");
            return ValidateOptionsResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ValidateOptionsResult.Fail($"Store:RootPath '{options.RootPath}' is not writable: {ex.Message}");
        }
        finally
        {
            try { File.Delete(probe); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
