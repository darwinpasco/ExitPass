namespace ExitPass.CentralPms.Domain.PaymentAttempts;

/// <summary>
/// Represents the canonical payment provider or payment rail code accepted by Central PMS.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 8.3 PaymentAttempt State Machine
///
/// Invariants Enforced:
/// - Central PMS must accept only configured and explicitly supported provider codes.
/// - Provider code handling must be deterministic and case-insensitive.
/// - Payment attempt creation must not silently coerce unknown provider codes.
/// </summary>
public sealed class PaymentProvider : IEquatable<PaymentProvider>
{
    /// <summary>
    /// GCash provider code.
    /// </summary>
    public static readonly PaymentProvider GCash = new("GCASH");

    /// <summary>
    /// Maya provider code.
    /// </summary>
    public static readonly PaymentProvider Maya = new("MAYA");

    /// <summary>
    /// Generic card provider code.
    /// </summary>
    public static readonly PaymentProvider Card = new("CARD");

    /// <summary>
    /// Generic bank provider code.
    /// </summary>
    public static readonly PaymentProvider Bank = new("BANK");

    /// <summary>
    /// PayMongo Checkout Session rail code.
    /// </summary>
    public static readonly PaymentProvider PayMongoCheckoutSession = new("PAYMONGO_CHECKOUT_SESSION");

    /// <summary>
    /// Gets the canonical provider or payment rail code.
    /// </summary>
    public string Code { get; }

    private PaymentProvider(string code)
    {
        Code = code;
    }

    /// <summary>
    /// Creates a <see cref="PaymentProvider"/> from the supplied canonical code.
    /// </summary>
    /// <param name="code">Canonical provider or rail code.</param>
    /// <returns>The matching <see cref="PaymentProvider"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the code is unsupported.</exception>
    public static PaymentProvider FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return code.Trim().ToUpperInvariant() switch
        {
            "GCASH" => GCash,
            "MAYA" => Maya,
            "CARD" => Card,
            "BANK" => Bank,
            "PAYMONGO_CHECKOUT_SESSION" => PayMongoCheckoutSession,
            _ => throw new ArgumentOutOfRangeException(nameof(code), $"Unsupported payment provider: {code}")
        };
    }

    /// <inheritdoc />
    public override string ToString() => Code;

    /// <inheritdoc />
    public bool Equals(PaymentProvider? other) => other is not null && Code == other.Code;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PaymentProvider other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);
}
