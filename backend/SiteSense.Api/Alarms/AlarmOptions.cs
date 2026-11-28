using Microsoft.Extensions.Options;

namespace SiteSense.Api.Alarms;

// An alarm is late when it is detected more than LateAfterMs after it happened (SS-29, Q33).
public sealed class AlarmOptions
{
    public const string Section = "Alarms";

    public long LateAfterMs { get; set; } = 60_000;
}

public sealed class AlarmOptionsValidator : IValidateOptions<AlarmOptions>
{
    public ValidateOptionsResult Validate(string? name, AlarmOptions options) =>
        options.LateAfterMs > 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Alarms:LateAfterMs must be greater than 0.");
}
