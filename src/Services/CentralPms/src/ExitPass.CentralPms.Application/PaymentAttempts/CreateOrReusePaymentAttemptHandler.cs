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
    /// - existence of ParkingSession and TariffSnapshot must be confirmed before the create-or-reuse path is invoked
    /// - valid idempotent replay must be decided by the authoritative DB-backed create-or-reuse path
    /// - competing active payment attempt must be rejected deterministically by the authoritative DB-backed path
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
            throw new InvalidOperationException(
                $"Parking session '{command.ParkingSessionId}' is not eligible for payment attempt creation.");
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

        // Important:
        // Do NOT pre-validate snapshot eligibility here.
        // The authoritative create-or-reuse DB routine must decide whether the request is:
        // - CREATED
        // - REUSED
        // - REJECTED_IDEMPOTENCY_CONFLICT
        // - REJECTED_ACTIVE_ATTEMPT_EXISTS
        // - snapshot/session invalid
        //
        // Pre-validating eligibility here breaks valid idempotent replay once the snapshot has
        // already been consumed by the first successful create.

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

        var dbResult = await _paymentAttemptDbRoutineGateway.CreateOrReusePaymentAttemptAsync(
            dbRequest,
            cancellationToken);

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

            case "REJECTED_SNAPSHOT_INVALID":
            case "REJECTED_SNAPSHOT_EXPIRED":
            case "REJECTED_SNAPSHOT_ALREADY_BOUND":
            case "REJECTED_SNAPSHOT_SESSION_MISMATCH":
                throw new TariffSnapshotNotEligibleException(
                    tariffSnapshotId,
                    TariffSnapshotStatus.Invalidated,
                    _systemClock.UtcNow,
                    null);

            default:
                throw new InvalidOperationException(
                    $"Unsupported create-or-reuse payment attempt outcome '{dbResult.OutcomeCode}'.");
        }
    }
}
