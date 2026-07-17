using System.Net.Http;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Builds a signed HikCentral HTTP request message without sending it.
/// </summary>
public interface IHikCentralSignedHttpRequestBuilder
{
    /// <summary>
    /// Builds a signed request. The caller owns and must dispose the returned message.
    /// </summary>
    HttpRequestMessage Build(
        Uri baseAddress,
        HikCentralGateActionRequestPlan requestPlan,
        HikCentralSigningMaterial signingMaterial,
        HikCentralRequestSignature signature);
}
