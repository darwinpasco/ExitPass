namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Non-secret options for reading one mounted HikCentral AppSecret file.
/// </summary>
public sealed class HikCentralGateSecretFileOptions
{
    public const int DefaultMaxSecretBytes = 4096;
    public const int MaximumAllowedSecretBytes = 16 * 1024;

    public string? SecretFilePath { get; set; }

    public int MaxSecretBytes { get; set; } = DefaultMaxSecretBytes;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(SecretFilePath))
        {
            errors.Add("HIKCENTRAL_SECRET_FILE_PATH_REQUIRED");
        }
        else if (SecretFilePath.Contains('\0'))
        {
            errors.Add("HIKCENTRAL_SECRET_FILE_PATH_INVALID");
        }
        else
        {
            try
            {
                if (!Path.IsPathFullyQualified(SecretFilePath.Trim()))
                {
                    errors.Add("HIKCENTRAL_SECRET_FILE_PATH_ABSOLUTE_REQUIRED");
                }
            }
            catch (ArgumentException)
            {
                errors.Add("HIKCENTRAL_SECRET_FILE_PATH_INVALID");
            }
            catch (NotSupportedException)
            {
                errors.Add("HIKCENTRAL_SECRET_FILE_PATH_INVALID");
            }
            catch (PathTooLongException)
            {
                errors.Add("HIKCENTRAL_SECRET_FILE_PATH_INVALID");
            }
        }

        if (MaxSecretBytes <= 0)
        {
            errors.Add("HIKCENTRAL_SECRET_FILE_MAX_BYTES_INVALID");
        }
        else if (MaxSecretBytes > MaximumAllowedSecretBytes)
        {
            errors.Add("HIKCENTRAL_SECRET_FILE_MAX_BYTES_UNREASONABLE");
        }

        return errors;
    }
}
