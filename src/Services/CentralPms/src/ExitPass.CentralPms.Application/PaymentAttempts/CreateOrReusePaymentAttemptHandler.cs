using System.Diagnostics;
using System.Diagnostics.Metrics;
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
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

/// <summary>
/// Handles the create-or-reuse payment attempt use case.
/// </summary>
/// <remarks>
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.21 Audit and Traceability
/// - 10.7.4 One Active Payment Attempt Per Session
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 8.3 PaymentAttempt State Machine
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - only Central PMS may create or reuse a PaymentAttempt
/// - existence of ParkingSession and TariffSnapshot must be confirmed before the create-or-reuse path is invoked
/// - valid idempotent replay must be decided by the authoritative DB-backed create-or-reuse path
/// - competing active payment attempt must be rejected deterministically by the authoritative DB-backed path
/// </remarks>
public sealed class CreateOrReusePaymentAttemptHandler : ICreateOrReusePaymentAttemptUseCase
{
    /// <summary>
    /// Activity source for create-or-reuse payment attempt spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.PaymentAttempts");

    /// <summary>
    /// Metrics meter for payment attempt application metrics.
    /// </summary>
    private static readonly Meter Meter =
        new("ExitPass.CentralPms.Application.PaymentAttempts", "1.0.0");

    /// <summary>
    /// Counts successful create-or-reuse payment attempt executions.
    /// </summary>
    private static readonly Counter<long> AttemptSucceededCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.payment_attempt.create_or_reuse.succeeded",
            unit: "{attempt}",
            description: "Counts successful create-or-reuse payment attempt executions.");

    /// <summary>
    /// Counts rejected create-or-reuse payment attempt executions.
    /// </summary>
    private static readonly Counter<long> AttemptRejectedCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.payment_attempt.create_or_reuse.rejected",
            unit: "{attempt}",
            description: "Counts rejected create-or-reuse payment attempt executions.");

    /// <summary>
    /// Counts unexpected failures during create-or-reuse payment attempt execution.
    /// </summary>
    private static readonly Counter<long> AttemptFailedCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.payment_attempt.create_or_reuse.failed",
            unit: "{attempt}",
            description: "Counts unexpected failures during create-or-reuse payment attempt execution.");

    private readonly IParkingSessionReadRepository _parkingSessionReadRepository;
    private readonly ITariffSnapshotReadRepository _tariffSnapshotReadRepository;
    private readonly IPaymentAttemptDbRoutineGateway _paymentAttemptDbRoutineGateway;
    private readonly IPaymentAttemptCreationPolicy _paymentAttemptCreationPolicy;
    private readonly IProviderHandoffFactory _providerHandoffFactory;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<CreateOrReusePaymentAttemptHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateOrReusePaymentAttemptHandler"/> class.
    /// </summary>
    public CreateOrReusePaymentAttemptHandler(
        IParkingSessionReadRepository parkingSessionReadRepository,
        ITariffSnapshotReadRepository tariffSnapshotReadRepository,
        IPaymentAttemptDbRoutineGateway paymentAttemptDbRoutineGateway,
        IPaymentAttemptCreationPolicy paymentAttemptCreationPolicy,
        IProviderHandoffFactory providerHandoffFactory,
        ISystemClock systemClock,
        ILogger<CreateOrReusePaymentAttemptHandler> logger)
    {
        _parkingSessionReadRepository = parkingSessionReadRepository;
        _tariffSnapshotReadRepository = tariffSnapshotReadRepository;
        _paymentAttemptDbRoutineGateway = paymentAttemptDbRoutineGateway;
        _paymentAttemptCreationPolicy = paymentAttemptCreationPolicy;
        _providerHandoffFactory = providerHandoffFactory;
        _systemClock = systemClock;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new payment attempt or reuses an idempotent prior attempt through the authoritative DB-backed path.
    /// </summary>
    /// <param name="command">Create-or-reuse payment attempt command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A created or reused payment attempt result.</returns>
    public async Task<CreateOrReusePaymentAttemptResult> ExecuteAsync(
        CreateOrReusePaymentAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("CreateOrReusePaymentAttempt", ActivityKind.Internal);

        activity?.SetTag("operation", "create_or_reuse_payment_attempt");
        activity?.SetTag("parking_session_id", command.ParkingSessionId);
        activity?.SetTag("tariff_snapshot_id", command.TariffSnapshotId);
        activity?.SetTag("payment_provider_code", command.PaymentProviderCode);
        activity?.SetTag("idempotency_key", command.IdempotencyKey);
        activity?.SetTag("requested_by", command.RequestedBy);
        activity?.SetTag("correlation_id", command.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["parking_session_id"] = command.ParkingSessionId,
            ["tariff_snapshot_id"] = command.TariffSnapshotId,
            ["payment_provider_code"] = command.PaymentProviderCode,
            ["idempotency_key"] = command.IdempotencyKey,
            ["requested_by"] = command.RequestedBy,
            ["correlation_id"] = command.CorrelationId
        });

        _logger.LogInformation("CreateOrReusePaymentAttempt started.");

        try
        {
            var parkingSession = await _parkingSessionReadRepository.GetByIdAsync(
                command.ParkingSessionId,
                cancellationToken);

            if (parkingSession is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Parking session not found");
                activity?.SetTag("rejection_reason", "PARKING_SESSION_NOT_FOUND");

                AttemptRejectedCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "PARKING_SESSION_NOT_FOUND"),
                    new KeyValuePair<string, object?>("payment_provider_code", command.PaymentProviderCode));

                _logger.LogWarning(
                    "Payment attempt creation rejected because parking session was not found.");

                throw new ParkingSessionNotFoundException(command.ParkingSessionId);
            }

            if (!parkingSession.IsEligibleForPaymentAttempt())
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Parking session not eligible");
                activity?.SetTag("rejection_reason", "PARKING_SESSION_NOT_ELIGIBLE");

                AttemptRejectedCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "PARKING_SESSION_NOT_ELIGIBLE"),
                    new KeyValuePair<string, object?>("payment_provider_code", command.PaymentProviderCode));

                _logger.LogWarning(
                    "Payment attempt creation rejected because parking session is not eligible for payment attempt creation.");

                throw new InvalidOperationException(
                    $"Parking session '{command.ParkingSessionId}' is not eligible for payment attempt creation.");
            }

            var tariffSnapshot = await _tariffSnapshotReadRepository.GetByIdAsync(
                command.TariffSnapshotId,
                cancellationToken);

            if (tariffSnapshot is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Tariff snapshot not found");
                activity?.SetTag("rejection_reason", "TARIFF_SNAPSHOT_NOT_FOUND");

                AttemptRejectedCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "TARIFF_SNAPSHOT_NOT_FOUND"),
                    new KeyValuePair<string, object?>("payment_provider_code", command.PaymentProviderCode));

                _logger.LogWarning(
                    "Payment attempt creation rejected because tariff snapshot was not found.");

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

            _logger.LogInformation("Invoking authoritative DB-backed create-or-reuse payment attempt routine.");

            var dbStart = _systemClock.UtcNow;

            var dbResult = await _paymentAttemptDbRoutineGateway.CreateOrReusePaymentAttemptAsync(
                dbRequest,
                cancellationToken);

            var dbDuration = _systemClock.UtcNow - dbStart;

            activity?.SetTag("db_outcome_code", dbResult.OutcomeCode);
            activity?.SetTag("payment_attempt_id", dbResult.PaymentAttemptId);
            activity?.SetTag("attempt_status", dbResult.AttemptStatus);
            activity?.SetTag("was_reused", dbResult.WasReused);
            activity?.SetTag("db.duration_ms", dbDuration.TotalMilliseconds);

            _logger.LogInformation(
                "Authoritative DB routine completed with outcome code {OutcomeCode}.",
                dbResult.OutcomeCode);

            ThrowForRejectedOutcome(dbResult, command.TariffSnapshotId);

            var result = new CreateOrReusePaymentAttemptResult
            {
                PaymentAttemptId = dbResult.PaymentAttemptId,
                ParkingSessionId = dbResult.ParkingSessionId,
                TariffSnapshotId = dbResult.TariffSnapshotId,
                AttemptStatus = dbResult.AttemptStatus,
                PaymentProviderCode = dbResult.PaymentProviderCode,
                WasReused = dbResult.WasReused,
                ProviderHandoff = _providerHandoffFactory.CreatePlaceholder(provider, dbResult.PaymentAttemptId)
            };

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("payment_attempt_id", result.PaymentAttemptId);
            activity?.SetTag("attempt_status", result.AttemptStatus);
            activity?.SetTag("provider_handoff_type", result.ProviderHandoff.Type);

            AttemptSucceededCounter.Add(
                1,
                new KeyValuePair<string, object?>("payment_provider_code", result.PaymentProviderCode),
                new KeyValuePair<string, object?>("was_reused", result.WasReused));

            if (result.WasReused)
            {
                _logger.LogInformation(
                    "Payment attempt was reused successfully. payment_attempt_id={PaymentAttemptId} attempt_status={AttemptStatus}",
                    result.PaymentAttemptId,
                    result.AttemptStatus);
            }
            else
            {
                _logger.LogInformation(
                    "Payment attempt was created successfully. payment_attempt_id={PaymentAttemptId} attempt_status={AttemptStatus}",
                    result.PaymentAttemptId,
                    result.AttemptStatus);
            }

            return result;
        }
        catch (Exception ex) when (IsExpectedBusinessRejection(ex))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.RecordException(ex);
            activity?.SetTag("rejection_exception_type", ex.GetType().Name);

            AttemptRejectedCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", ex.GetType().Name),
                new KeyValuePair<string, object?>("payment_provider_code", command.PaymentProviderCode));

            _logger.LogWarning(
                ex,
                "CreateOrReusePaymentAttempt was rejected by fail-closed business rules.");

            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            AttemptFailedCounter.Add(
                1,
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
                new KeyValuePair<string, object?>("payment_provider_code", command.PaymentProviderCode));

            _logger.LogError(
                ex,
                "Unexpected failure while creating or reusing payment attempt.");

            throw;
        }
    }

    /// <summary>
    /// Converts DB outcome codes into deterministic domain exceptions.
    /// </summary>
    /// <param name="dbResult">DB routine result.</param>
    /// <param name="tariffSnapshotId">Tariff snapshot ID associated with the request.</param>
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

    /// <summary>
    /// Determines whether an exception is an expected fail-closed business rejection.
    /// </summary>
    /// <param name="ex">Exception to evaluate.</param>
    /// <returns><c>true</c> if the exception is an expected business rejection; otherwise, <c>false</c>.</returns>
    private static bool IsExpectedBusinessRejection(Exception ex)
    {
        return ex is ParkingSessionNotFoundException
            or TariffSnapshotNotFoundException
            or TariffSnapshotNotEligibleException
            or ActivePaymentAttemptAlreadyExistsException
            or IdempotencyConflictException
            or InvalidOperationException;
    }
}
