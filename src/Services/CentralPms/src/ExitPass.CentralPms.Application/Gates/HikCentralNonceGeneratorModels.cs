namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Generates one HikCentral signing nonce.
/// </summary>
public interface IHikCentralNonceGenerator
{
    /// <summary>
    /// Generates a nonce that can be used by HikCentral AK/SK signing material.
    /// </summary>
    string Generate();
}
