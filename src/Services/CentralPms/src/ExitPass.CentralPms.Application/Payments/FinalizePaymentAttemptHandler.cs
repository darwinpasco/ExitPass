using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Common;
using Npgsql;

namespace ExitPass.CentralPms.Application.Payments;

public sealed class FinalizePaymentAttemptHandler : IFinalizePaymentAttemptUseCase
{
    private readonly IFinalizePaymentAttemptGateway _gateway;
    private readonly ISystemClock _systemClock;

    public FinalizePaymentAttemptHandler(
        IFinalizePaymentAttemptGateway gateway,
        ISystemClock systemClock)
    {
        _gateway = gateway;
        _systemClock = systemClock;
    }

    public async Task<FinalizePaymentAttemptResult> ExecuteAsync(
        FinalizePaymentAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException("PaymentAttemptId is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.FinalAttemptStatus))
        {
            throw new ArgumentException("FinalAttemptStatus is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            throw new ArgumentException("RequestedBy is required.", nameof(command));
        }

        var dbResult = await _gateway.FinalizeAsync(
            new FinalizePaymentAttemptDbRequest
            {
                PaymentAttemptId = command.PaymentAttemptId,
                FinalAttemptStatus = command.FinalAttemptStatus,
                RequestedBy = command.RequestedBy,
                CorrelationId = command.CorrelationId,
                RequestedAt = _systemClock.UtcNow
            },
            cancellationToken);

        return new FinalizePaymentAttemptResult(
            PaymentAttemptId: dbResult.PaymentAttemptId,
            AttemptStatus: dbResult.AttemptStatus);
    }
}
