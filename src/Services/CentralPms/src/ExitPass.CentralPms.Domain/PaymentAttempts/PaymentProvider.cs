namespace ExitPass.CentralPms.Domain.PaymentAttempts;

public sealed class PaymentProvider : IEquatable<PaymentProvider>
{
    public static readonly PaymentProvider GCash = new("GCASH");
    public static readonly PaymentProvider Maya = new("MAYA");
    public static readonly PaymentProvider Card = new("CARD");
    public static readonly PaymentProvider Bank = new("BANK");

    public string Code { get; }

    private PaymentProvider(string code)
    {
        Code = code;
    }

    public static PaymentProvider FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return code.Trim().ToUpperInvariant() switch
        {
            "GCASH" => GCash,
            "MAYA" => Maya,
            "CARD" => Card,
            "BANK" => Bank,
            _ => throw new ArgumentOutOfRangeException(nameof(code), $"Unsupported payment provider: {code}")
        };
    }

    public override string ToString() => Code;

    public bool Equals(PaymentProvider? other) => other is not null && Code == other.Code;
    public override bool Equals(object? obj) => obj is PaymentProvider other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);
}