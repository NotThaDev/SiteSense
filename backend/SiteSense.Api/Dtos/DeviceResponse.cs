using SiteSense.Api.Models;

namespace SiteSense.Api.Dtos;

public sealed record DeviceResponse(string DeviceId, string SiteId)
{
    public static DeviceResponse From(Device device) => new(device.Id, device.SiteId);
}
