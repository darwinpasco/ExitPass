using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Validates terminal cash commands and coordinates durable Central PMS persistence.
/// </summary>
public sealed class TerminalCashPaymentService : ITerminalCashPaymentService
{
    /// <summary>
    /// Semantic hash source version for terminal cash payment commands.
    /// </summary>
    public const string SemanticHashSourceVersion = "terminal-cash-payment:sha256:v1";

    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITerminalCashPaymentRepository _repository;
    private readonly ISystemClock _clock;

    /// <summary>
    /// Creates a terminal cash payment service.
    /// </summary>
    public TerminalCashPaymentService(
        ITerminalCashPaymentRepository repository,
        ISystemClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<TerminalCashPaymentResult> CreateOrReadAsync(
        TerminalCashPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        var normalized = Normalize(command);
        var repositoryCommand = new TerminalCashPaymentRepositoryCommand(
            normalized,
            IdempotencyScope: BuildIdempotencyScope(normalized),
            SemanticRequestHash: ComputeSemanticHash(normalized),
            SemanticHashSourceVersion,
            RequestedAt: _clock.UtcNow);

        return await _repository.CreateOrReadAsync(repositoryCommand, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TerminalCashPaymentReadback?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        CancellationToken cancellationToken)
    {
        if (terminalCashTenderId == Guid.Empty)
        {
            throw new TerminalCashPaymentRejectedException(
                "MISSING_TERMINAL_CASH_TENDER_RECORD",
                "Terminal cash tender id is required.");
        }

        return _repository.GetByTerminalCashTenderIdAsync(terminalCashTenderId, cancellationToken);
    }

    private static void Validate(TerminalCashPaymentCommand command)
    {
        Require(command.TerminalCashTenderId, "TERMINAL_CASH_TENDER_ID_REQUIRED", "Terminal cash tender id is required.");
        Require(command.CashCustodySessionId, "CASH_CUSTODY_SESSION_ID_REQUIRED", "Cash custody session id is required.");
        Require(command.ParkingSessionId, "PARKING_SESSION_ID_REQUIRED", "Parking session id is required.");
        Require(command.TariffSnapshotId, "TARIFF_SNAPSHOT_ID_REQUIRED", "Tariff snapshot id is required.");
        Require(command.SiteId, "SITE_ID_REQUIRED", "Site id is required.");
        Require(command.SiteGroupId, "SITE_GROUP_ID_REQUIRED", "Site group id is required.");
        Require(command.CorrelationId, "CORRELATION_ID_REQUIRED", "Correlation id is required.");
        Require(command.CashierId, "CASHIER_ID_REQUIRED", "Cashier id is required.");
        Require(command.CashierSessionReference, "CASHIER_SESSION_REFERENCE_REQUIRED", "Cashier session reference is required.");
        Require(command.CashierShiftId, "CASHIER_SHIFT_ID_REQUIRED", "Cashier shift id is required.");
        Require(command.TerminalId, "TERMINAL_ID_REQUIRED", "Terminal id is required.");
        Require(command.PosServerId, "POS_SERVER_ID_REQUIRED", "POS Server id is required.");
        Require(command.LocalEventReference, "LOCAL_EVENT_REFERENCE_REQUIRED", "Local event reference is required.");
        Require(command.IdempotencyKey, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency key is required.");

        if (!string.Equals(command.Currency?.Trim(), "PHP", StringComparison.OrdinalIgnoreCase))
        {
            throw Rejected("UNSUPPORTED_CURRENCY", "Only PHP cash payments are supported in this slice.");
        }

        if (command.AmountDueMinorUnits <= 0)
        {
            throw Rejected("INVALID_CASH_AMOUNTS", "Amount due must be positive.");
        }

        if (command.AmountTenderedMinorUnits < command.AmountDueMinorUnits)
        {
            throw Rejected("INVALID_CASH_AMOUNTS", "Amount tendered must be greater than or equal to amount due.");
        }

        if (command.ChangeDueMinorUnits != command.AmountTenderedMinorUnits - command.AmountDueMinorUnits)
        {
            throw Rejected("INVALID_CASH_AMOUNTS", "Change due must equal amount tendered minus amount due.");
        }

        if (command.CashReceivedAt == default)
        {
            throw Rejected("CASH_RECEIVED_AT_REQUIRED", "Cash received timestamp is required.");
        }

        foreach (var denomination in command.DenominationEntries ?? [])
        {
            Require(denomination.DenominationCode, "DENOMINATION_CODE_REQUIRED", "Denomination code is required.");
            if (denomination.DenominationValueMinorUnits <= 0 || denomination.Quantity <= 0)
            {
                throw Rejected("INVALID_DENOMINATION_ENTRY", "Denomination value and quantity must be positive.");
            }
        }
    }

    private static TerminalCashPaymentCommand Normalize(TerminalCashPaymentCommand command)
    {
        return command with
        {
            CashierId = command.CashierId.Trim(),
            CashierSessionReference = command.CashierSessionReference.Trim(),
            CashierShiftId = command.CashierShiftId.Trim(),
            TerminalId = command.TerminalId.Trim(),
            PosServerId = command.PosServerId.Trim(),
            Currency = command.Currency.Trim().ToUpperInvariant(),
            LocalEventReference = command.LocalEventReference.Trim(),
            IdempotencyKey = command.IdempotencyKey.Trim(),
            DenominationEntries = (command.DenominationEntries ?? [])
                .OrderBy(entry => entry.DenominationCode, StringComparer.Ordinal)
                .ThenBy(entry => entry.DenominationValueMinorUnits)
                .Select(entry => entry with { DenominationCode = entry.DenominationCode.Trim().ToUpperInvariant() })
                .ToArray()
        };
    }

    private static string BuildIdempotencyScope(TerminalCashPaymentCommand command) =>
        $"terminal-cash-payment:{command.SiteId:N}:{command.TerminalId}:{command.TerminalCashTenderId:N}";

    private static string ComputeSemanticHash(TerminalCashPaymentCommand command)
    {
        var source = new
        {
            version = SemanticHashSourceVersion,
            terminalCashTenderId = command.TerminalCashTenderId,
            cashCustodySessionId = command.CashCustodySessionId,
            parkingSessionId = command.ParkingSessionId,
            tariffSnapshotId = command.TariffSnapshotId,
            cashierId = command.CashierId,
            cashierSessionReference = command.CashierSessionReference,
            cashierShiftId = command.CashierShiftId,
            terminalId = command.TerminalId,
            siteId = command.SiteId,
            siteGroupId = command.SiteGroupId,
            posServerId = command.PosServerId,
            currency = command.Currency,
            amountDueMinorUnits = command.AmountDueMinorUnits,
            amountTenderedMinorUnits = command.AmountTenderedMinorUnits,
            changeDueMinorUnits = command.ChangeDueMinorUnits,
            cashReceivedAt = command.CashReceivedAt.ToUniversalTime(),
            denominationEntries = command.DenominationEntries,
            localEventReference = command.LocalEventReference
        };

        var json = JsonSerializer.Serialize(source, HashJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static void Require(Guid value, string errorCode, string message)
    {
        if (value == Guid.Empty)
        {
            throw Rejected(errorCode, message);
        }
    }

    private static void Require(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Rejected(errorCode, message);
        }
    }

    private static TerminalCashPaymentRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
