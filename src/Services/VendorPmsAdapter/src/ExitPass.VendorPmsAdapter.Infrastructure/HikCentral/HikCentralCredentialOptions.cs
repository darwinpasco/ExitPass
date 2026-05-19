namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// HikCentral Professional AK/SK credentials used for request signing.
/// </summary>
/// <param name="AccessKey">HikCentral app key sent as X-Ca-Key.</param>
/// <param name="SecretKey">HikCentral secret key used to calculate the request signature.</param>
public sealed record HikCentralCredentialOptions(string AccessKey, string SecretKey);
