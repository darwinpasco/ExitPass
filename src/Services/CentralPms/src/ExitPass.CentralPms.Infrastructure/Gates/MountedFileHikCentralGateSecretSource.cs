using System.Security.Cryptography;
using ExitPass.CentralPms.Application.Gates;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// Reads one bounded HikCentral AppSecret byte sequence from an explicitly mounted file.
/// </summary>
public sealed class MountedFileHikCentralGateSecretSource : IHikCentralGateSecretSource
{
    private static readonly IHikCentralGateSecretFileReader DefaultReader =
        new MountedFileHikCentralGateSecretFileReader();

    private readonly HikCentralGateSecretFileOptions _options;
    private readonly IHikCentralGateSecretFileReader _reader;

    public MountedFileHikCentralGateSecretSource(HikCentralGateSecretFileOptions options)
        : this(options, DefaultReader)
    {
    }

    internal MountedFileHikCentralGateSecretSource(
        HikCentralGateSecretFileOptions options,
        IHikCentralGateSecretFileReader reader)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    public async ValueTask<HikCentralGateSecretMaterial> GetSecretAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOptions(_options);

        byte[]? readBuffer = null;
        try
        {
            readBuffer = await _reader
                .ReadAsync(_options.SecretFilePath!.Trim(), _options.MaxSecretBytes, cancellationToken)
                .ConfigureAwait(false);

            if (readBuffer.Length == 0)
            {
                throw Rejected("HIKCENTRAL_SECRET_FILE_EMPTY", "HikCentral mounted secret file is empty.");
            }

            return new HikCentralGateSecretMaterial(readBuffer);
        }
        finally
        {
            if (readBuffer is not null)
            {
                CryptographicOperations.ZeroMemory(readBuffer);
            }
        }
    }

    private static void ValidateOptions(HikCentralGateSecretFileOptions options)
    {
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw Rejected(errors[0], "HikCentral mounted secret-file options are invalid.");
        }
    }

    private static HikCentralGateActionRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}

internal interface IHikCentralGateSecretFileReader
{
    ValueTask<byte[]> ReadAsync(string path, int maxSecretBytes, CancellationToken cancellationToken);
}

internal sealed class MountedFileHikCentralGateSecretFileReader : IHikCentralGateSecretFileReader
{
    private const int BufferSize = 4096;

    public async ValueTask<byte[]> ReadAsync(
        string path,
        int maxSecretBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectUnsafeFileSystemEntry(path);

        byte[]? buffer = null;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var length = stream.Length;
            if (length == 0)
            {
                throw Rejected("HIKCENTRAL_SECRET_FILE_EMPTY", "HikCentral mounted secret file is empty.");
            }

            if (length > maxSecretBytes)
            {
                throw Rejected("HIKCENTRAL_SECRET_FILE_TOO_LARGE", "HikCentral mounted secret file exceeds the configured size limit.");
            }

            if (length > int.MaxValue)
            {
                throw Rejected("HIKCENTRAL_SECRET_FILE_TOO_LARGE", "HikCentral mounted secret file exceeds the configured size limit.");
            }

            buffer = new byte[(int)length];
            await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

            if (stream.Position != length)
            {
                throw Rejected("HIKCENTRAL_SECRET_FILE_INCOMPLETE_READ", "HikCentral mounted secret file could not be read completely.");
            }

            var owned = buffer;
            buffer = null;
            return owned;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HikCentralGateActionRejectedException)
        {
            throw;
        }
        catch (EndOfStreamException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_INCOMPLETE_READ", "HikCentral mounted secret file could not be read completely.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_UNREADABLE", "HikCentral mounted secret file could not be opened safely.", ex);
        }
        catch (IOException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_UNREADABLE", "HikCentral mounted secret file could not be opened safely.", ex);
        }
        finally
        {
            if (buffer is not null)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }

    private static void RejectUnsafeFileSystemEntry(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_MISSING", "HikCentral mounted secret file was not found.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_MISSING", "HikCentral mounted secret file was not found.", ex);
        }
        catch (ArgumentException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_PATH_INVALID", "HikCentral mounted secret-file path is invalid.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_PATH_INVALID", "HikCentral mounted secret-file path is invalid.", ex);
        }
        catch (PathTooLongException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_PATH_INVALID", "HikCentral mounted secret-file path is invalid.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_UNREADABLE", "HikCentral mounted secret file could not be opened safely.", ex);
        }
        catch (IOException ex)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_UNREADABLE", "HikCentral mounted secret file could not be opened safely.", ex);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_IS_DIRECTORY", "HikCentral mounted secret-file path must identify a file.");
        }

        // This rejects Windows reparse points and Unix links where the target framework exposes them as ReparsePoint.
        // Link metadata remains inherently race-prone across platforms, so the file is still opened read-only with no write share.
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Rejected("HIKCENTRAL_SECRET_FILE_REPARSE_POINT_UNSUPPORTED", "HikCentral mounted secret file must not use filesystem indirection.");
        }
    }

    private static HikCentralGateActionRejectedException Rejected(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        innerException is null
            ? new HikCentralGateActionRejectedException(errorCode, message)
            : new HikCentralGateActionRejectedException(errorCode, message);
}
