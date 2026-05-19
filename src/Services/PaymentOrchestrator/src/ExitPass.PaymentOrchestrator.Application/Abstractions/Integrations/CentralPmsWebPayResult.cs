namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Result wrapper for Central PMS calls used by WebPay orchestration.
/// </summary>
/// <typeparam name="T">Successful response payload type.</typeparam>
/// <param name="Succeeded">Indicates whether Central PMS returned a successful response.</param>
/// <param name="Value">Successful response payload.</param>
/// <param name="Error">Deterministic error payload.</param>
public sealed record CentralPmsWebPayResult<T>(
    bool Succeeded,
    T? Value,
    CentralPmsWebPayError? Error)
    where T : class
{
    /// <summary>
    /// Creates a successful Central PMS result.
    /// </summary>
    /// <param name="value">Successful response payload.</param>
    /// <returns>A successful result wrapper.</returns>
    public static CentralPmsWebPayResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CentralPmsWebPayResult<T>(true, value, null);
    }

    /// <summary>
    /// Creates a failed Central PMS result.
    /// </summary>
    /// <param name="error">Deterministic error payload.</param>
    /// <returns>A failed result wrapper.</returns>
    public static CentralPmsWebPayResult<T> Failure(CentralPmsWebPayError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CentralPmsWebPayResult<T>(false, null, error);
    }
}
