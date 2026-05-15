using ExitPass.CentralPms.Contracts.Public.VendorParking;

namespace ExitPass.CentralPms.Api.Validation;

/// <summary>
/// Validates public vendor parking resolution requests before dispatching the application use case.
/// </summary>
public sealed class ResolveVendorParkingRequestValidator
{
    /// <summary>
    /// Validates the supplied request.
    /// </summary>
    /// <param name="request">Request to validate.</param>
    /// <returns>Validation errors, or an empty list when the request is valid.</returns>
    public IReadOnlyList<string> Validate(ResolveVendorParkingRequest? request)
    {
        var errors = new List<string>();

        if (request is null)
        {
            errors.Add("Request body is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.SiteGroupId))
        {
            errors.Add("siteGroupId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SiteId))
        {
            errors.Add("siteId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.VendorSystemId))
        {
            errors.Add("vendorSystemId is required.");
        }

        if (request.CorrelationId == Guid.Empty)
        {
            errors.Add("correlationId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PlateNumber) &&
            string.IsNullOrWhiteSpace(request.TicketReference))
        {
            errors.Add("Either plateNumber or ticketReference is required.");
        }

        ValidateIdentifier(request.PlateNumber, "plateNumber", errors);
        ValidateIdentifier(request.TicketReference, "ticketReference", errors);

        return errors;
    }

    private static void ValidateIdentifier(string? value, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
        {
            errors.Add($"{fieldName} must be 64 characters or fewer.");
        }

        if (trimmed.Any(char.IsControl))
        {
            errors.Add($"{fieldName} must not contain control characters.");
        }
    }
}
