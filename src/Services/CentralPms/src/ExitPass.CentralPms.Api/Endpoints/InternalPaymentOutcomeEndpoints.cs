using ExitPass.CentralPms.Contracts.Common;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal read endpoints for payment outcomes.
///
/// BRD:
/// - 9.21 Audit and Traceability
///
/// SDD:
/// - 10 Reporting / Query APIs
///
/// Invariants:
/// - Read endpoints must not mutate state
/// </summary>
public static class InternalPaymentOutcomeEndpoints
{
    public static IEndpointRouteBuilder MapInternalPaymentOutcomeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/payments")
            .WithTags("InternalPayments");

        group.MapGet("/outcome/{paymentAttemptId:guid}", HandleAsync)
            .WithName("GetPaymentOutcome")
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult HandleAsync(Guid paymentAttemptId)
    {
        // Placeholder for read model integration
        return Results.Ok(new
        {
            PaymentAttemptId = paymentAttemptId,
            Status = "NOT_IMPLEMENTED"
        });
    }
}
