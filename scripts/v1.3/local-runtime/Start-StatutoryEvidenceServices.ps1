[CmdletBinding()]
param(
    [string] $MinioImage = 'minio/minio:latest',
    [string] $MinioClientImage = 'minio/mc:latest',
    [string] $ClamAvImage = 'clamav/clamav:stable',
    [switch] $PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$networkName = 'exitpass-ist-persistent'
$minioContainerName = 'exitpass-pitx-statutory-evidence-minio'
$clamAvContainerName = 'exitpass-pitx-statutory-evidence-clamav'
$minioNetworkAlias = 'exitpass-pitx-statutory-evidence-minio'
$clamAvNetworkAlias = 'exitpass-pitx-statutory-evidence-clamav'
$bucketName = 'exitpass-pitx-statutory-evidence'
$bucketReference = 'pitx-statutory-evidence-private'
$applicationAccessKey = 'exitpass-pitx-central-pms'
$applicationPolicyName = 'exitpass-pitx-statutory-evidence-rw'
$ownershipLabelName = 'com.exitpass.local-runtime'
$ownershipLabelValue = 'persistent-pitx-statutory-evidence'

$privateRoot = if ([string]::IsNullOrWhiteSpace($env:EXITPASS_PERSISTENT_IST_ROOT)) {
    'D:\SourceCodes\ExitPass.local\persistent-ist'
}
else {
    $env:EXITPASS_PERSISTENT_IST_ROOT
}
$evidenceRoot = Join-Path $privateRoot 'statutory-evidence'
$configurationRoot = Join-Path $evidenceRoot 'configuration'
$minioDataRoot = Join-Path $evidenceRoot 'minio-data'
$minioTlsRoot = Join-Path $configurationRoot 'minio-tls'
$minioEnvironmentFile = Join-Path $configurationRoot 'minio.env'
$centralPmsEnvironmentFile = Join-Path $configurationRoot 'central-pms-evidence.env'
$applicationPolicyFile = Join-Path $configurationRoot 'central-pms-bucket-policy.json'
$minioRootCertificatePath = Join-Path $minioTlsRoot 'root-ca.crt'
$minioServerCertificatePath = Join-Path $minioTlsRoot 'public.crt'
$minioServerPrivateKeyPath = Join-Path $minioTlsRoot 'private.key'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureMessage
    )

    $commandOutput = & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw $FailureMessage
    }

    # Keep dependency command output off the success stream so -PassThru emits
    # exactly one structured runtime descriptor to callers such as Central PMS.
    [void]$commandOutput
}

function New-RandomHexSecret {
    param([ValidateRange(16, 128)][int] $Bytes)

    $buffer = [byte[]]::new($Bytes)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($buffer)
    }
    finally {
        $generator.Dispose()
    }

    return (($buffer | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Content
    )

    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Write-Pem {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][byte[]] $Bytes
    )

    $base64 = [Convert]::ToBase64String($Bytes, [Base64FormattingOptions]::InsertLineBreaks)
    Write-Utf8NoBom $Path "-----BEGIN $Label-----`r`n$base64`r`n-----END $Label-----`r`n"
}

function Set-EnvironmentValue {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Value
    )

    $prefix = "$Name="
    $lines = [Collections.Generic.List[string]]::new()
    $matchCount = 0
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line.StartsWith($prefix, [StringComparison]::Ordinal)) {
            $lines.Add("$prefix$Value")
            $matchCount++
        }
        else {
            $lines.Add($line)
        }
    }
    if ($matchCount -gt 1) {
        throw "Private statutory-evidence configuration contains duplicate '$Name' values."
    }
    if ($matchCount -eq 0) {
        $lines.Add("$prefix$Value")
    }

    Write-Utf8NoBom $Path (($lines -join "`r`n") + "`r`n")
}

function Initialize-MinioTls {
    [void](New-Item -ItemType Directory -Path $minioTlsRoot -Force)
    $required = @($minioRootCertificatePath, $minioServerCertificatePath, $minioServerPrivateKeyPath)
    $present = @($required | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($present.Count -ne 0 -and $present.Count -ne $required.Count) {
        throw "Private MinIO TLS material is incomplete at '$minioTlsRoot'. Restore the missing files; do not regenerate one side."
    }

    if ($present.Count -eq 0) {
        $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
        $notAfter = [DateTimeOffset]::UtcNow.AddYears(2)
        $rootRsa = [Security.Cryptography.RSA]::Create(3072)
        $serverRsa = [Security.Cryptography.RSA]::Create(2048)
        $root = $null
        $serverPublic = $null
        $server = $null
        try {
            $rootRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
                'CN=ExitPass Persistent PITX Evidence Root CA',
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
                "CN=$minioNetworkAlias",
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
            $enhancedKeyUsages = [Security.Cryptography.OidCollection]::new()
            [void]$enhancedKeyUsages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
            $serverRequest.CertificateExtensions.Add(
                [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($enhancedKeyUsages, $true))
            $serverNames = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
            $serverNames.AddDnsName($minioNetworkAlias)
            $serverNames.AddIpAddress([Net.IPAddress]::Loopback)
            $serverRequest.CertificateExtensions.Add($serverNames.Build())
            $serial = [byte[]]::new(16)
            $serialGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
            try {
                $serialGenerator.GetBytes($serial)
            }
            finally {
                $serialGenerator.Dispose()
            }
            $serverPublic = $serverRequest.Create($root, $notBefore, $notAfter, $serial)
            $server = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($serverPublic, $serverRsa)

            if ($serverRsa -isnot [Security.Cryptography.RSACng]) {
                throw 'The persistent PITX launcher requires the Windows CNG RSA provider to export the MinIO TLS key.'
            }

            $serverPrivateKey = $serverRsa.Key.Export([Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
            $temporarySuffix = ".{0}.tmp" -f [Guid]::NewGuid().ToString('N')
            $temporaryRootCertificatePath = "$minioRootCertificatePath$temporarySuffix"
            $temporaryServerCertificatePath = "$minioServerCertificatePath$temporarySuffix"
            $temporaryServerPrivateKeyPath = "$minioServerPrivateKeyPath$temporarySuffix"
            try {
                Write-Pem $temporaryRootCertificatePath 'CERTIFICATE' $root.RawData
                Write-Pem $temporaryServerCertificatePath 'CERTIFICATE' $server.RawData
                Write-Pem $temporaryServerPrivateKeyPath 'PRIVATE KEY' $serverPrivateKey

                Move-Item -LiteralPath $temporaryRootCertificatePath -Destination $minioRootCertificatePath
                Move-Item -LiteralPath $temporaryServerCertificatePath -Destination $minioServerCertificatePath
                Move-Item -LiteralPath $temporaryServerPrivateKeyPath -Destination $minioServerPrivateKeyPath
            }
            finally {
                @(
                    $temporaryRootCertificatePath,
                    $temporaryServerCertificatePath,
                    $temporaryServerPrivateKeyPath
                ) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
                    Remove-Item -LiteralPath $_ -Force
                }
            }
        }
        finally {
            if ($null -ne $root) { $root.Dispose() }
            if ($null -ne $serverPublic) { $serverPublic.Dispose() }
            if ($null -ne $server) { $server.Dispose() }
            $rootRsa.Dispose()
            $serverRsa.Dispose()
        }
    }

    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($minioServerCertificatePath)
    try {
        if ($certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow.AddDays(30)) {
            throw 'The private MinIO server certificate expires within 30 days.'
        }
    }
    finally {
        $certificate.Dispose()
    }
}

function Get-EnvironmentValue {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name
    )

    $prefix = "$Name="
    $line = Get-Content -LiteralPath $Path |
        Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) } |
        Select-Object -Last 1
    if ($null -eq $line) {
        return $null
    }

    return $line.Substring($prefix.Length)
}

function Assert-EnvironmentValue {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name,
        [string] $ExpectedValue
    )

    $value = Get-EnvironmentValue $Path $Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Private statutory-evidence configuration is missing '$Name' in $Path."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedValue) -and $value -cne $ExpectedValue) {
        throw "Private statutory-evidence configuration '$Name' does not match the persistent PITX contract."
    }

    return $value
}

function Initialize-PrivateConfiguration {
    [void](New-Item -ItemType Directory -Path $configurationRoot -Force)
    [void](New-Item -ItemType Directory -Path $minioDataRoot -Force)

    $minioExists = Test-Path -LiteralPath $minioEnvironmentFile -PathType Leaf
    $centralExists = Test-Path -LiteralPath $centralPmsEnvironmentFile -PathType Leaf
    if ($minioExists -xor $centralExists) {
        throw 'Persistent statutory-evidence credentials are incomplete. Preserve the existing file and restore its matching private configuration; do not regenerate one side.'
    }

    if (-not $minioExists) {
        $rootAccessKey = New-RandomHexSecret 16
        $rootSecretKey = New-RandomHexSecret 32
        $applicationSecretKey = New-RandomHexSecret 32

        Write-Utf8NoBom $minioEnvironmentFile @"
MINIO_ROOT_USER=$rootAccessKey
MINIO_ROOT_PASSWORD=$rootSecretKey
"@
        Write-Utf8NoBom $centralPmsEnvironmentFile @"
CentralPms__StatutoryEvidence__Upload__ProviderType=S3_COMPATIBLE
CentralPms__StatutoryEvidence__Upload__Endpoint=https://${minioNetworkAlias}:9000
CentralPms__StatutoryEvidence__Upload__PublicUploadEndpoint=https://${minioNetworkAlias}:9000
CentralPms__StatutoryEvidence__Upload__Region=us-east-1
CentralPms__StatutoryEvidence__Upload__BucketName=$bucketName
CentralPms__StatutoryEvidence__Upload__BucketReference=$bucketReference
CentralPms__StatutoryEvidence__Upload__AccessKeyId=$applicationAccessKey
CentralPms__StatutoryEvidence__Upload__SecretAccessKey=$applicationSecretKey
CentralPms__StatutoryEvidence__Upload__EnvironmentPartition=persistent-pitx-local
CentralPms__StatutoryEvidence__Upload__AuthorizationTtlSeconds=300
CentralPms__StatutoryEvidence__Upload__MaxContentLengthBytes=5242880
CentralPms__StatutoryEvidence__Upload__AllowedContentTypes__0=image/jpeg
CentralPms__StatutoryEvidence__Upload__AllowedContentTypes__1=image/png
CentralPms__StatutoryEvidence__Upload__RequireSha256Checksum=true
CentralPms__StatutoryEvidence__Upload__RequireTlsForNonLocal=true
CentralPms__StatutoryEvidence__Upload__RequireServerSideEncryptionMetadata=false
CentralPms__StatutoryEvidence__Channel__EnvironmentScope=LOCAL_TEST
CentralPms__StatutoryEvidence__Channel__SeniorCitizenDocumentProfileCode=SENIOR_CITIZEN_ID_FRONT_BACK_V1
CentralPms__StatutoryEvidence__Channel__PwdDocumentProfileCode=PWD_ID_FRONT_BACK_V1
CentralPms__StatutoryEvidence__Channel__RequiredDocumentProfileVersion=1
CentralPms__StatutoryEvidence__Channel__SingleDocumentItemRole=SINGLE_DOCUMENT
CentralPms__StatutoryEvidence__Channel__ExpectedJpegMediaClass=IMAGE_JPEG
CentralPms__StatutoryEvidence__ScanWorker__Enabled=true
CentralPms__StatutoryEvidence__ScanWorker__PollIntervalSeconds=2
CentralPms__StatutoryEvidence__ScanWorker__BatchSize=5
CentralPms__StatutoryEvidence__ScanWorker__MaxConcurrency=2
CentralPms__StatutoryEvidence__ScanWorker__LeaseSeconds=120
CentralPms__StatutoryEvidence__ScanWorker__ScanTimeoutSeconds=30
CentralPms__StatutoryEvidence__ScanWorker__ValidationTimeoutSeconds=15
CentralPms__StatutoryEvidence__ScanWorker__MaxAttempts=3
CentralPms__StatutoryEvidence__ScanWorker__InitialRetryDelaySeconds=15
CentralPms__StatutoryEvidence__ScanWorker__MaxRetryDelaySeconds=120
CentralPms__StatutoryEvidence__ScanWorker__JitterSeconds=3
CentralPms__StatutoryEvidence__ScanWorker__MaxContentLengthBytes=5242880
CentralPms__StatutoryEvidence__ScanWorker__MaxDecodedWidth=6000
CentralPms__StatutoryEvidence__ScanWorker__MaxDecodedHeight=6000
CentralPms__StatutoryEvidence__ScanWorker__MaxDecodedPixelCount=36000000
CentralPms__StatutoryEvidence__ScanWorker__MaxHeaderProbeBytes=131072
CentralPms__StatutoryEvidence__ScanWorker__ScannerProvider=CLAMAV_COMPATIBLE
CentralPms__StatutoryEvidence__ScanWorker__ScannerEndpoint=$clamAvNetworkAlias
CentralPms__StatutoryEvidence__ScanWorker__ScannerPort=3310
CentralPms__StatutoryEvidence__ScanWorker__ScannerHealthTimeoutSeconds=5
CentralPms__StatutoryEvidence__ScanWorker__WorkerId=central-pms-pitx-statutory-evidence-scan-worker
"@
    }

    Set-EnvironmentValue $centralPmsEnvironmentFile 'CentralPms__StatutoryEvidence__Upload__Endpoint' "https://${minioNetworkAlias}:9000"
    Set-EnvironmentValue $centralPmsEnvironmentFile 'CentralPms__StatutoryEvidence__Upload__PublicUploadEndpoint' "https://${minioNetworkAlias}:9000"
    Set-EnvironmentValue $centralPmsEnvironmentFile 'CentralPms__StatutoryEvidence__Upload__RequireTlsForNonLocal' 'true'

    $policy = @{
        Version = '2012-10-17'
        Statement = @(
            @{
                Effect = 'Allow'
                Action = @('s3:GetBucketLocation', 's3:ListBucket')
                Resource = @("arn:aws:s3:::$bucketName")
            },
            @{
                Effect = 'Allow'
                Action = @('s3:GetObject', 's3:PutObject', 's3:DeleteObject')
                Resource = @("arn:aws:s3:::$bucketName/*")
            }
        )
    } | ConvertTo-Json -Depth 8
    Write-Utf8NoBom $applicationPolicyFile $policy
}

function Get-ContainerInspection {
    param([Parameter(Mandatory)][string] $Name)

    $containerName = (& docker.exe ps -a --filter "name=^/$Name$" --format '{{.Names}}')
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect Docker container '$Name'."
    }
    if ([string]::IsNullOrWhiteSpace($containerName)) {
        return $null
    }

    return ((& docker.exe inspect $Name) | ConvertFrom-Json)[0]
}

function Assert-OwnedContainer {
    param(
        [Parameter(Mandatory)] $Inspection,
        [Parameter(Mandatory)][string] $ExpectedName,
        [Parameter(Mandatory)][string] $ExpectedImage,
        [string] $RequiredDestination,
        [string] $RequiredSource
    )

    if ($Inspection.Name.TrimStart('/') -cne $ExpectedName -or
        $Inspection.Config.Image -cne $ExpectedImage -or
        $Inspection.Config.Labels.$ownershipLabelName -cne $ownershipLabelValue -or
        -not $Inspection.NetworkSettings.Networks.PSObject.Properties.Name.Contains($networkName)) {
        throw "Container '$ExpectedName' exists but is not the owned persistent PITX statutory-evidence container. It will not be changed."
    }

    if (-not [string]::IsNullOrWhiteSpace($RequiredDestination)) {
        $mount = $Inspection.Mounts | Where-Object { $_.Destination -ceq $RequiredDestination } | Select-Object -First 1
        if ($null -eq $mount -or $mount.Source -cne $RequiredSource -or -not $mount.RW) {
            throw "Container '$ExpectedName' does not use the approved persistent evidence-data mount."
        }
    }
}

function Ensure-MinioContainer {
    $inspection = Get-ContainerInspection $minioContainerName
    if ($null -ne $inspection) {
        Assert-OwnedContainer $inspection $minioContainerName $MinioImage '/data' $minioDataRoot
        $requiredTlsMounts = @(
            @{ Destination = '/root/.minio/certs/public.crt'; Source = $minioServerCertificatePath },
            @{ Destination = '/root/.minio/certs/private.key'; Source = $minioServerPrivateKeyPath },
            @{ Destination = '/root/.minio/certs/CAs/evidence-root-ca.crt'; Source = $minioRootCertificatePath }
        )
        $tlsReady = $true
        foreach ($requiredMount in $requiredTlsMounts) {
            $mount = $inspection.Mounts | Where-Object { $_.Destination -ceq $requiredMount.Destination } | Select-Object -First 1
            if ($null -eq $mount -or $mount.Source -cne $requiredMount.Source -or $mount.RW) {
                $tlsReady = $false
            }
        }
        if (-not $tlsReady) {
            if ($inspection.State.Running) {
                Invoke-CheckedCommand docker.exe @('stop', $inspection.Id) "Could not stop '$minioContainerName' for its private TLS upgrade."
            }
            Invoke-CheckedCommand docker.exe @('rm', $inspection.Id) "Could not recreate '$minioContainerName' with private TLS."
            $inspection = $null
        }
    }

    if ($null -eq $inspection) {
        $containerId = (& docker.exe run --detach `
            --name $minioContainerName `
            --network $networkName `
            --network-alias $minioNetworkAlias `
            --restart unless-stopped `
            --env-file $minioEnvironmentFile `
            --mount "type=bind,source=$minioDataRoot,target=/data" `
            --mount "type=bind,source=$minioServerCertificatePath,target=/root/.minio/certs/public.crt,readonly" `
            --mount "type=bind,source=$minioServerPrivateKeyPath,target=/root/.minio/certs/private.key,readonly" `
            --mount "type=bind,source=$minioRootCertificatePath,target=/root/.minio/certs/CAs/evidence-root-ca.crt,readonly" `
            --label "$ownershipLabelName=$ownershipLabelValue" `
            --label 'com.exitpass.component=statutory-evidence-object-storage' `
            --health-cmd 'curl --fail --silent --show-error --cacert /root/.minio/certs/CAs/evidence-root-ca.crt https://127.0.0.1:9000/minio/health/ready || exit 1' `
            --health-interval 5s `
            --health-timeout 3s `
            --health-retries 30 `
            --health-start-period 10s `
            $MinioImage server /data --console-address ':9001').Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
            throw 'Could not start the private persistent PITX MinIO container.'
        }
        $inspection = Get-ContainerInspection $minioContainerName
    }
    else {
        Assert-OwnedContainer $inspection $minioContainerName $MinioImage '/data' $minioDataRoot
        if (-not $inspection.State.Running) {
            Invoke-CheckedCommand docker.exe @('start', $inspection.Id) "Could not restart '$minioContainerName'."
            $inspection = Get-ContainerInspection $minioContainerName
        }
    }

    Assert-OwnedContainer $inspection $minioContainerName $MinioImage '/data' $minioDataRoot
}

function Ensure-ClamAvContainer {
    $inspection = Get-ContainerInspection $clamAvContainerName
    if ($null -eq $inspection) {
        $containerId = (& docker.exe run --detach `
            --name $clamAvContainerName `
            --network $networkName `
            --network-alias $clamAvNetworkAlias `
            --restart unless-stopped `
            --label "$ownershipLabelName=$ownershipLabelValue" `
            --label 'com.exitpass.component=statutory-evidence-malware-scanner' `
            --health-cmd 'clamdscan --ping 1 || exit 1' `
            --health-interval 10s `
            --health-timeout 5s `
            --health-retries 30 `
            --health-start-period 30s `
            $ClamAvImage).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
            throw 'Could not start the persistent PITX ClamAV container.'
        }
        $inspection = Get-ContainerInspection $clamAvContainerName
    }
    else {
        Assert-OwnedContainer $inspection $clamAvContainerName $ClamAvImage
        if (-not $inspection.State.Running) {
            Invoke-CheckedCommand docker.exe @('start', $inspection.Id) "Could not restart '$clamAvContainerName'."
            $inspection = Get-ContainerInspection $clamAvContainerName
        }
    }

    Assert-OwnedContainer $inspection $clamAvContainerName $ClamAvImage
}

function Wait-ForContainerHealth {
    param(
        [Parameter(Mandatory)][string] $Name,
        [ValidateRange(1, 600)][int] $Attempts = 180
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $inspection = Get-ContainerInspection $Name
        if ($null -eq $inspection -or -not $inspection.State.Running) {
            throw "Evidence dependency '$Name' stopped before it became ready."
        }
        if ($inspection.State.Health.Status -eq 'healthy') {
            return
        }
        if ($inspection.State.Health.Status -eq 'unhealthy') {
            throw "Evidence dependency '$Name' reported an unhealthy state. Inspect with: docker logs $Name"
        }
        if ($attempt -eq $Attempts) {
            throw "Evidence dependency '$Name' did not become ready. Inspect with: docker logs $Name"
        }

        Start-Sleep -Seconds 2
    }
}

function Invoke-MinioClient {
    param(
        [Parameter(Mandatory)][string] $AliasName,
        [Parameter(Mandatory)][string] $AccessKey,
        [Parameter(Mandatory)][string] $SecretKey,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureMessage,
        [string[]] $AdditionalDockerArguments = @()
    )

    $environmentName = "MC_HOST_$AliasName"
    $previousValue = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
    [Environment]::SetEnvironmentVariable(
        $environmentName,
        "https://${AccessKey}:${SecretKey}@${minioNetworkAlias}:9000",
        'Process')
    try {
        $rootCertificateMount = "type=bind,source=$minioRootCertificatePath,target=/tmp/exitpass-evidence-root-ca.crt,readonly"
        $dockerArguments = @(
            'run', '--rm', '--network', $networkName,
            '--env', $environmentName,
            '--env', 'SSL_CERT_FILE=/tmp/exitpass-evidence-root-ca.crt',
            '--mount', $rootCertificateMount) +
            $AdditionalDockerArguments + @($MinioClientImage) + $Arguments
        Invoke-CheckedCommand docker.exe $dockerArguments $FailureMessage
    }
    finally {
        [Environment]::SetEnvironmentVariable($environmentName, $previousValue, 'Process')
    }
}

function Initialize-PrivateBucket {
    $rootAccessKey = Assert-EnvironmentValue $minioEnvironmentFile 'MINIO_ROOT_USER'
    $rootSecretKey = Assert-EnvironmentValue $minioEnvironmentFile 'MINIO_ROOT_PASSWORD'
    $applicationSecretKey = Assert-EnvironmentValue $centralPmsEnvironmentFile 'CentralPms__StatutoryEvidence__Upload__SecretAccessKey'
    [void](Assert-EnvironmentValue $centralPmsEnvironmentFile 'CentralPms__StatutoryEvidence__Upload__AccessKeyId' $applicationAccessKey)

    Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('mb', '--ignore-existing', "pitxadmin/$bucketName") 'Could not initialize the private statutory-evidence bucket.'
    Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('anonymous', 'set', 'none', "pitxadmin/$bucketName") 'Could not enforce the private statutory-evidence bucket policy.'

    $existingUser = $true
    try {
        Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('admin', 'user', 'info', 'pitxadmin', $applicationAccessKey) 'Application user is absent.'
    }
    catch {
        $existingUser = $false
    }
    if (-not $existingUser) {
        Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('admin', 'user', 'add', 'pitxadmin', $applicationAccessKey, $applicationSecretKey) 'Could not create the Central PMS statutory-evidence storage identity.'
    }

    $policyMount = "type=bind,source=$applicationPolicyFile,target=/tmp/central-pms-bucket-policy.json,readonly"
    $existingPolicy = $true
    try {
        Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('admin', 'policy', 'info', 'pitxadmin', $applicationPolicyName) 'Application policy is absent.'
    }
    catch {
        $existingPolicy = $false
    }
    if (-not $existingPolicy) {
        Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('admin', 'policy', 'create', 'pitxadmin', $applicationPolicyName, '/tmp/central-pms-bucket-policy.json') 'Could not create the Central PMS statutory-evidence bucket policy.' @('--mount', $policyMount)
    }
    Invoke-MinioClient 'pitxadmin' $rootAccessKey $rootSecretKey @('admin', 'policy', 'attach', 'pitxadmin', $applicationPolicyName, '--user', $applicationAccessKey) 'Could not attach the statutory-evidence bucket policy.'

    Invoke-MinioClient 'pitxapp' $applicationAccessKey $applicationSecretKey @('ls', "pitxapp/$bucketName") 'The Central PMS statutory-evidence storage identity is not ready.'
}

if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
    throw 'Docker Desktop is required for persistent PITX statutory evidence services.'
}
$network = (& docker.exe network ls --filter "name=^$networkName$" --format '{{.Name}}')
if ($LASTEXITCODE -ne 0 -or $network -ne $networkName) {
    throw "Docker network '$networkName' is unavailable. Start the persistent PITX resources first."
}

Initialize-PrivateConfiguration
Initialize-MinioTls
Ensure-MinioContainer
Ensure-ClamAvContainer
Wait-ForContainerHealth $minioContainerName 180
Wait-ForContainerHealth $clamAvContainerName 240
Initialize-PrivateBucket
Invoke-CheckedCommand docker.exe @('exec', $clamAvContainerName, 'clamdscan', '--ping', '1') 'The persistent PITX ClamAV scanner is not ready.'

$result = [pscustomobject]@{
    MinioContainerName = $minioContainerName
    ClamAvContainerName = $clamAvContainerName
    NetworkName = $networkName
    BucketName = $bucketName
    CentralPmsEnvironmentFile = $centralPmsEnvironmentFile
    MinioRootCertificatePath = $minioRootCertificatePath
    PrivateStateRoot = $evidenceRoot
}

Write-Host 'Persistent PITX statutory evidence services are ready.'
Write-Host "Object storage: $minioContainerName (private Docker network only)"
Write-Host "Malware scanner: $clamAvContainerName (CLAMAV_COMPATIBLE)"
Write-Host "Private state: $evidenceRoot"
if ($PassThru) {
    return $result
}
