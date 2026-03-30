namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

public interface IPaymentAttemptDbRoutineGateway
{
    Task<CreateOrReusePaymentAttemptDbResult> CreateOrReusePaymentAttemptAsync(
        CreateOrReusePaymentAttemptDbRequest request,
        CancellationToken cancellationToken);
}