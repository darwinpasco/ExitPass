using System.Net.Sockets;
using System.Text;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.StatutoryEvidence;

public sealed class ClamAvStatutoryEvidenceScanner : IStatutoryEvidenceScanner
{
    private readonly StatutoryEvidenceScanWorkerOptions _options;

    public ClamAvStatutoryEvidenceScanner(IOptions<StatutoryEvidenceScanWorkerOptions> options)
    {
        _options = options.Value;
    }

    public async Task<StatutoryEvidenceMalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        if (string.Equals(_options.ScannerProvider, StatutoryEvidenceScanConstants.ScannerProviderNoopTestOnly, StringComparison.OrdinalIgnoreCase))
        {
            return new("CLEAN", true, false, null);
        }

        if (string.IsNullOrWhiteSpace(_options.ScannerEndpoint))
        {
            return new("SCANNER_UNAVAILABLE", false, true, "SCANNER_UNAVAILABLE");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ScanTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.ScannerEndpoint, _options.ScannerPort, timeout.Token);
            await using var network = client.GetStream();
            await WriteAsciiAsync(network, "zINSTREAM\0"u8.ToArray(), timeout.Token);

            var buffer = new byte[8192];
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                if (read == 0)
                {
                    break;
                }

                await WriteLengthAsync(network, read, timeout.Token);
                await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            await WriteLengthAsync(network, 0, timeout.Token);
            await network.FlushAsync(timeout.Token);

            var response = await ReadClamAvResponseAsync(network, timeout.Token);
            return ParseResponse(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("SCANNER_TIMEOUT", false, true, "SCANNER_TIMEOUT");
        }
        catch (SocketException)
        {
            return new("SCANNER_UNAVAILABLE", false, true, "SCANNER_UNAVAILABLE");
        }
        catch (IOException)
        {
            return new("SCANNER_ERROR_RETRYABLE", false, true, "SCANNER_ERROR_RETRYABLE");
        }
    }

    private static StatutoryEvidenceMalwareScanResult ParseResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new("MALFORMED_SCANNER_RESPONSE", false, true, "MALFORMED_SCANNER_RESPONSE");
        }

        response = response.TrimEnd('\0', '\r', '\n', ' ', '\t');

        if (response.EndsWith(" OK", StringComparison.OrdinalIgnoreCase))
        {
            return new("CLEAN", true, false, null);
        }

        if (response.Contains(" FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return new("MALICIOUS", false, false, "MALWARE_DETECTED");
        }

        if (response.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return new("SCANNER_ERROR_RETRYABLE", false, true, "SCANNER_ERROR_RETRYABLE");
        }

        return new("MALFORMED_SCANNER_RESPONSE", false, true, "MALFORMED_SCANNER_RESPONSE");
    }

    private static async Task WriteLengthAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var bytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(length));
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
    }

    private static Task WriteAsciiAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken) =>
        stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken).AsTask();

    private static async Task<string?> ReadClamAvResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        using var response = new MemoryStream();
        while (response.Length < 4096)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var value = buffer[0];
            if (value is 0 or (byte)'\n')
            {
                break;
            }

            if (value != '\r')
            {
                response.WriteByte(value);
            }
        }

        return response.Length == 0
            ? null
            : Encoding.ASCII.GetString(response.ToArray());
    }
}
