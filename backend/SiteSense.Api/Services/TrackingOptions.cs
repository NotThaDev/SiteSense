using Microsoft.Extensions.Options;

namespace SiteSense.Api.Services;

// Plausibility filter (SS-24, Q26): a jump faster than VMaxMps is dropped; ResetAfter drops in a row restart the track.
// Area hysteresis (SS-25, Q27): a candidate area wins at once when the point is inside it by AreaMarginM (or the accuracy, if larger),
// or after AreaConfirmPoints points in a row name it.
public sealed class TrackingOptions
{
    public const string Section = "Tracking";

    public double VMaxMps { get; set; } = 7;

    public int ResetAfter { get; set; } = 3;

    public double AreaMarginM { get; set; } = 2;

    public int AreaConfirmPoints { get; set; } = 3;
}

public sealed class TrackingOptionsValidator : IValidateOptions<TrackingOptions>
{
    public ValidateOptionsResult Validate(string? name, TrackingOptions options)
    {
        if (!(options.VMaxMps > 0))
            return ValidateOptionsResult.Fail("Tracking:VMaxMps must be greater than 0.");
        if (options.ResetAfter <= 0)
            return ValidateOptionsResult.Fail("Tracking:ResetAfter must be greater than 0.");
        if (!(options.AreaMarginM > 0))
            return ValidateOptionsResult.Fail("Tracking:AreaMarginM must be greater than 0.");
        if (options.AreaConfirmPoints <= 0)
            return ValidateOptionsResult.Fail("Tracking:AreaConfirmPoints must be greater than 0.");
        return ValidateOptionsResult.Success;
    }
}
