using System.Security.Claims;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;

namespace ExitPass.CentralPms.Api.Endpoints;

internal static class OperatorConsoleFiscalReportingAccess
{
    public static async Task<bool> IsOperationsSupervisorAsync(
        ClaimsPrincipal principal,
        Guid userId,
        ICentralPmsRbacRepository repository,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated == true && principal.FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("role"))
            .Concat(principal.FindAll("role_code"))
            .Any(claim => string.Equals(
                claim.Value,
                ApprovedIdentityRoleCatalog.OperationsSupervisor,
                StringComparison.Ordinal)))
        {
            return true;
        }

        return userId != Guid.Empty && await repository.UserHasAnyRoleAsync(
            userId,
            [ApprovedIdentityRoleCatalog.OperationsSupervisor],
            cancellationToken).ConfigureAwait(false);
    }

    public static IResult Denied(Guid correlationId) =>
        Results.Json(
            new
            {
                errorCode = "OPERATOR_CONSOLE_FISCAL_REPORTING_ROLE_REQUIRED",
                message = "Fiscal Reporting is available only to an Operations Supervisor.",
                correlationId,
                retryable = false
            },
            statusCode: StatusCodes.Status403Forbidden);
}
