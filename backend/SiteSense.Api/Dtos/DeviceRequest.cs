using System.ComponentModel.DataAnnotations;
using SiteSense.Api.Store;

namespace SiteSense.Api.Dtos;

// Only the pseudonymous id: no other device field is accepted (PRV-01).
public sealed record DeviceRequest([Required] string DeviceId) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (DeviceId is not null && !FileDocumentStore.IsValidName(DeviceId))
            yield return new ValidationResult("deviceId may contain only letters, digits, '-' and '_' (1-128 characters).", [nameof(DeviceId)]);
    }
}
