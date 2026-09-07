[CmdletBinding()]
param(
    [string] $PrivateRoot = 'D:\SourceCodes\ExitPass.local\persistent-ist',
    [switch] $PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$credentialReference = 'secret://exitpass/payment-orchestrator'
$certificateDirectory = Join-Path $PrivateRoot 'runtime\restart-41-business\statutory-mtls'
$metadataPath = Join-Path $certificateDirectory 'metadata.json'
$rootCertificatePath = Join-Path $certificateDirectory 'central-pms-root-ca.cer'
$rootCertificatePemPath = Join-Path $certificateDirectory 'central-pms-root-ca.crt'
$serverCertificatePath = Join-Path $certificateDirectory 'central-pms-server.pfx'
$serverPasswordPath = Join-Path $certificateDirectory 'central-pms-server-password'
$clientCertificatePath = Join-Path $certificateDirectory 'payment-orchestrator-client.pfx'
$clientPasswordPath = Join-Path $certificateDirectory 'payment-orchestrator-client-password'
$requiredPaths = @(
    $metadataPath,
    $rootCertificatePath,
    $serverCertificatePath,
    $serverPasswordPath,
    $clientCertificatePath,
    $clientPasswordPath
)

function New-RandomBytes {
    param([Parameter(Mandatory)][int] $Count)

    $bytes = [byte[]]::new($Count)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return $bytes
}

function New-RandomPassword {
    return [Convert]::ToBase64String((New-RandomBytes 32))
}

function Write-PrivateText {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Value
    )

    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Write-CertificatePem {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][Security.Cryptography.X509Certificates.X509Certificate2] $Certificate
    )

    $base64 = [Convert]::ToBase64String(
        $Certificate.RawData,
        [Base64FormattingOptions]::InsertLineBreaks)
    Write-PrivateText $Path "-----BEGIN CERTIFICATE-----`r`n$base64`r`n-----END CERTIFICATE-----`r`n"
}

function Test-ClientAuthenticationCertificate {
    param([Parameter(Mandatory)][Security.Cryptography.X509Certificates.X509Certificate2] $Certificate)

    return $Certificate.Extensions |
        Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } |
        Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.2' } |
        Select-Object -First 1
}

function Get-ExistingMaterial {
    $present = @($requiredPaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($present.Count -eq 0) {
        return $null
    }
    if ($present.Count -ne $requiredPaths.Count) {
        throw "The private statutory mTLS directory is incomplete at '$certificateDirectory'. Restore or remove only that directory before retrying."
    }

    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ($metadata.credentialReference -ne $credentialReference) {
        throw 'The existing statutory mTLS credential reference does not match the canonical Payment Orchestrator identity.'
    }

    $serverPassword = (Get-Content -LiteralPath $serverPasswordPath -Raw).TrimEnd("`r", "`n")
    $clientPassword = (Get-Content -LiteralPath $clientPasswordPath -Raw).TrimEnd("`r", "`n")
    $server = [Security.Cryptography.X509Certificates.X509Certificate2]::new($serverCertificatePath, $serverPassword)
    $client = [Security.Cryptography.X509Certificates.X509Certificate2]::new($clientCertificatePath, $clientPassword)
    $root = [Security.Cryptography.X509Certificates.X509Certificate2]::new($rootCertificatePath)
    try {
        $minimumExpiry = [DateTime]::UtcNow.AddDays(30)
        if (-not $server.HasPrivateKey -or $server.NotAfter.ToUniversalTime() -le $minimumExpiry) {
            throw 'The private Central PMS server certificate is missing its key or expires within 30 days.'
        }
        if (-not $client.HasPrivateKey -or $client.NotAfter.ToUniversalTime() -le $minimumExpiry -or
            -not (Test-ClientAuthenticationCertificate $client)) {
            throw 'The private Payment Orchestrator client certificate is invalid, missing its key, or expires within 30 days.'
        }
        if ($client.Thumbprint -ne $metadata.clientCertificateThumbprint -or
            $root.Thumbprint -ne $metadata.rootCertificateThumbprint) {
            throw 'The private statutory mTLS certificate metadata does not match the certificate files.'
        }
        if (-not (Test-Path -LiteralPath $rootCertificatePemPath -PathType Leaf)) {
            Write-CertificatePem $rootCertificatePemPath $root
        }

        return [pscustomobject]@{
            CredentialReference = $credentialReference
            RootCertificatePath = $rootCertificatePath
            RootCertificatePemPath = $rootCertificatePemPath
            ServerCertificatePath = $serverCertificatePath
            ServerPasswordPath = $serverPasswordPath
            ServerPassword = $serverPassword
            ClientCertificatePath = $clientCertificatePath
            ClientPasswordPath = $clientPasswordPath
            ClientThumbprint = $client.Thumbprint
        }
    }
    finally {
        $server.Dispose()
        $client.Dispose()
        $root.Dispose()
    }
}

function New-CertificateMaterial {
    [void](New-Item -ItemType Directory -Path $certificateDirectory -Force)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe $certificateDirectory '/inheritance:r' '/grant:r' "$identity`:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restrict the private certificate directory '$certificateDirectory'."
    }

    $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $notAfter = [DateTimeOffset]::UtcNow.AddYears(2)
    $rootRsa = [Security.Cryptography.RSA]::Create(3072)
    $serverRsa = [Security.Cryptography.RSA]::Create(2048)
    $clientRsa = [Security.Cryptography.RSA]::Create(2048)
    $root = $null
    $serverPublic = $null
    $server = $null
    $clientPublic = $null
    $client = $null
    try {
        $rootRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=ExitPass Local Central PMS Root CA',
            $rootRsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $rootRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
        $rootRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign,
                $true))
        $rootRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($rootRequest.PublicKey, $false))
        $root = $rootRequest.CreateSelfSigned($notBefore, $notAfter)

        $serverRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=exitpass-central-pms-pitx-local',
            $serverRsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $serverRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $serverRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment,
                $true))
        $serverEnhancedKeyUsages = [Security.Cryptography.OidCollection]::new()
        [void]$serverEnhancedKeyUsages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $serverRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
                $serverEnhancedKeyUsages,
                $true))
        $serverNames = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $serverNames.AddDnsName('exitpass-central-pms-pitx-local')
        $serverNames.AddDnsName('localhost')
        $serverNames.AddIpAddress([Net.IPAddress]::Loopback)
        $serverRequest.CertificateExtensions.Add($serverNames.Build())
        $serverPublic = $serverRequest.Create($root, $notBefore, $notAfter, (New-RandomBytes 16))
        $server = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey(
            $serverPublic,
            $serverRsa)

        $clientRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=ExitPass Payment Orchestrator WebPay',
            $clientRsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $clientRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $clientRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
                $true))
        $clientEnhancedKeyUsages = [Security.Cryptography.OidCollection]::new()
        [void]$clientEnhancedKeyUsages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.2'))
        $clientRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
                $clientEnhancedKeyUsages,
                $true))
        $clientPublic = $clientRequest.Create($root, $notBefore, $notAfter, (New-RandomBytes 16))
        $client = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey(
            $clientPublic,
            $clientRsa)

        $serverPassword = New-RandomPassword
        $clientPassword = New-RandomPassword
        [IO.File]::WriteAllBytes(
            $rootCertificatePath,
            $root.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Write-CertificatePem $rootCertificatePemPath $root
        [IO.File]::WriteAllBytes(
            $serverCertificatePath,
            $server.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $serverPassword))
        [IO.File]::WriteAllBytes(
            $clientCertificatePath,
            $client.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $clientPassword))
        Write-PrivateText $serverPasswordPath $serverPassword
        Write-PrivateText $clientPasswordPath $clientPassword
        Write-PrivateText $metadataPath (([ordered]@{
                    credentialReference = $credentialReference
                    rootCertificateThumbprint = $root.Thumbprint
                    serverCertificateThumbprint = $server.Thumbprint
                    clientCertificateThumbprint = $client.Thumbprint
                    createdAt = [DateTimeOffset]::UtcNow.ToString('O')
                } | ConvertTo-Json) + "`n")
    }
    finally {
        if ($null -ne $root) { $root.Dispose() }
        if ($null -ne $serverPublic) { $serverPublic.Dispose() }
        if ($null -ne $server) { $server.Dispose() }
        if ($null -ne $clientPublic) { $clientPublic.Dispose() }
        if ($null -ne $client) { $client.Dispose() }
        $rootRsa.Dispose()
        $serverRsa.Dispose()
        $clientRsa.Dispose()
    }
}

$material = Get-ExistingMaterial
if ($null -eq $material) {
    New-CertificateMaterial
    $material = Get-ExistingMaterial
    Write-Host "Provisioned private WebPay statutory mTLS certificates under '$certificateDirectory'."
}
else {
    Write-Host "Using existing private WebPay statutory mTLS certificates under '$certificateDirectory'."
}
Write-Host "Payment Orchestrator client certificate thumbprint: $($material.ClientThumbprint)"

if ($PassThru) {
    $material
}
