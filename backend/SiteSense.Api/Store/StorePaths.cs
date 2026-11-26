using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiteSense.Api.Store;

internal static partial class StorePaths
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,128}\z")]
    private static partial Regex ValidName();

    public static bool IsValidName(string value) => ValidName().IsMatch(value);

    // Ids and tenant ids become path segments; this blocks path traversal.
    public static void CheckName(string value, string paramName)
    {
        if (!IsValidName(value))
            throw new ArgumentException($"'{value}' is not a valid name.", paramName);
    }

    // Ids are case-sensitive but macOS and Windows disks are not: "DEV-1" becomes "!d!e!v-1", and '!' is not a valid id character (Q76).
    public static string FileName(string name) =>
        string.Concat(name.Select(c => char.IsAsciiLetterUpper(c) ? $"!{char.ToLowerInvariant(c)}" : c.ToString()));
}
