using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Domain.Sessions.Exceptions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.CentralPms.Domain.Tariffs.Exceptions;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

public sealed class CreateOrReusePaymentAttemptHandler : ICreateOrReusePaymentAttemptUseCase
{
    private readonly IParkingSessionReadRepository _parkingSessionReadRepository;
    private readonly ITariffSnapshotReadRepository _tariffSnapshotReadRepository;
    private readonly IPaymentAttemptDbRoutineGateway _paymentAttemptDbRoutineGateway;
    private readonly IPaymentAttemptCreationPolicy _paymentAttemptCreationPolicy;
    private readonly IProviderHandoffFactory _providerHandoffFactory;
    private readonly ISystemClock _systemClock;

    public CreateOrReusePaymentAttemptHandler(
        IParkingSessionReadRepository parkingSessionReadRepository,
        ITariffSnapshotReadRepository tariffSnapshotReadRepository,
        IPaymentAttemptDbRoutineGateway paymentAttemptDbRoutineGateway,
        IPaymentAttemptCreationPolicy paymentAttemptCreationPolicy,
        IProviderHandoffFactory providerHandoffFactory,
        ISystemClock systemClock)
    {
        _parkingSessionReadRepository = parkingSessionReadRepository;
        _tariffSnapshotReadRepository = tariffSnapshotReadRepository;
        _paymentAttemptDbRoutineGateway = paymentAttemptDbRoutineGateway;
        _paymentAttemptCreationPolicy = paymentAttemptCreationPolicy;
        _providerHandoffFactory = providerHandoffFactory;
        _systemClock = systemClock;
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    /// - 10.7.4 One Active Payment Attempt Per Session
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    /// - 8.3 PaymentAttempt State Machine
    ///
    /// Invariants Enforced:
    /// - only Central PMS may create or reuse a PaymentAttempt
    /// - tariff snapshot must be eligible before attempt creation
    /// - valid replay returns original result
    /// - competing active payment attempt must be rejected deterministically
    /// </summary>
    public async Task<CreateOrReusePaymentAttemptResult> ExecuteAsync(
        CreateOrReusePaymentAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parkingSession = await _parkingSessionReadRepository.GetByIdAsync(command.ParkingSessionId, cancellationToken);
        if (parkingSession is null)
        {
            throw new ParkingSessionNotFoundException(command.ParkingSessionId);
        }

        if (!parkingSession.IsEligibleForPaymentAttempt())
        {
            throw new InvalidOperationException($"Parking session '{command.ParkingSessionId}' is not eligible for payment attempt creation.");
        }

        var tariffSnapshot = await _tariffSnapshotReadRepository.GetByIdAsync(command.TariffSnapshotId, cancellationToken);
        if (tariffSnapshot is null)
        {
            throw new TariffSnapshotNotFoundException(command.TariffSnapshotId);
        }

        var provider = PaymentProvider.FromCode(command.PaymentProviderCode);

        _paymentAttemptCreationPolicy.ValidateRequest(new CreateOrReusePaymentAttemptPolicyInput
        {
            ParkingSessionId = command.ParkingSessionId,
            TariffSnapshotId = command.TariffSnapshotId,
            PaymentProvider = provider,
            IdempotencyKey = command.IdempotencyKey
        });

        _paymentAttemptCreationPolicy.ValidateSnapshotEligibility(tariffSnapshot, _systemClock);

        var dbRequest = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = command.ParkingSessionId,
            TariffSnapshotId = command.TariffSnapshotId,
            PaymentProviderCode = command.PaymentProviderCode,
            IdempotencyKey = command.IdempotencyKey,
            RequestedBy = command.RequestedBy,
            CorrelationId = command.CorrelationId,
            RequestedAt = _systemClock.UtcNow
        };

        var dbResult = await _paymentAttemptDbRoutineGateway.CreateOrReusePaymentAttemptAsync(dbRequest, cancellationToken);

        ThrowForRejectedOutcome(dbResult, command.TariffSnapshotId);

        return new CreateOrReusePaymentAttemptResult
        {
            PaymentAttemptId = dbResult.PaymentAttemptId,
            ParkingSessionId = dbResult.ParkingSessionId,
            TariffSnapshotId = dbResult.TariffSnapshotId,
            AttemptStatus = dbResult.AttemptStatus,
            PaymentProviderCode = dbResult.PaymentProviderCode,
            WasReused = dbResult.WasReused,
            ProviderHandoff = _providerHandoffFactory.CreatePlaceholder(provider, dbResult.PaymentAttemptId)
        };
    }

    private void ThrowForRejectedOutcome(CreateOrReusePaymentAttemptDbResult dbResult, Guid tariffSnapshotId)
    {
        switch (dbResult.OutcomeCode)
        {
            case "CREATED":
            case "REUSED":
                return;
            case "REJECTED_ACTIVE_ATTEMPT_EXISTS":
                throw new ActivePaymentAttemptAlreadyExistsException(dbResult.ParkingSessionId);
            case "REJECTED_IDEMPOTENCY_CONFLICT":
                throw new IdempotencyConflictException(dbResult.IdempotencyKey ?? string.Empty);
            case "REJECTED_SNAPSHOT_NOT_FOUND":
                throw new TariffSnapshotNotFoundException(tariffSnapshotId);
            default:
                throw new TariffSnapshotNotEligibleException(
                    tariffSnapshotId,
                    TariffSnapshotStatus.Invalidated,
                    _systemClock.UtcNow,
                    null);
        }
    }
}