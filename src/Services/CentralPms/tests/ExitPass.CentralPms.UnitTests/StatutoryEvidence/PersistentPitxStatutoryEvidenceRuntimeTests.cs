using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryEvidence;

public sealed class PersistentPitxStatutoryEvidenceRuntimeTests
{
    [Fact]
    public void EvidenceLauncher_UsesPrivatePersistentOwnedServices()
    {
        var launcher = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Start-StatutoryEvidenceServices.ps1");

        launcher.Should().Contain("exitpass-pitx-statutory-evidence-minio");
        launcher.Should().Contain("exitpass-pitx-statutory-evidence-clamav");
        launcher.Should().Contain("exitpass-ist-persistent");
        launcher.Should().Contain("--restart unless-stopped");
        launcher.Should().Contain("persistent-pitx-statutory-evidence");
        launcher.Should().Contain("$minioDataRoot = Join-Path $evidenceRoot 'minio-data'");
        launcher.Should().Contain("anonymous', 'set', 'none");
        launcher.Should().NotContain("--publish");
    }

    [Fact]
    public void EvidenceLauncher_ConfiguresOpaqueS3AndRealClamAvPipeline()
    {
        var launcher = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Start-StatutoryEvidenceServices.ps1");

        launcher.Should().Contain("Upload__ProviderType=S3_COMPATIBLE");
        launcher.Should().Contain("Upload__Endpoint=https://${minioNetworkAlias}:9000");
        launcher.Should().Contain("Upload__PublicUploadEndpoint=https://${minioNetworkAlias}:9000");
        launcher.Should().Contain("Upload__BucketName=$bucketName");
        launcher.Should().Contain("Upload__RequireTlsForNonLocal=true");
        launcher.Should().Contain("/root/.minio/certs/private.key");
        launcher.Should().Contain("SSL_CERT_FILE=/tmp/exitpass-evidence-root-ca.crt");
        launcher.Should().Contain("ScanWorker__Enabled=true");
        launcher.Should().Contain("ScanWorker__ScannerProvider=CLAMAV_COMPATIBLE");
        launcher.Should().Contain("ScanWorker__ScannerEndpoint=$clamAvNetworkAlias");
        launcher.Should().NotContain("NOOP_TEST_ONLY");
        launcher.Should().NotContain("Upload__Endpoint=http://127.0.0.1");
        launcher.Should().NotContain("ScanWorker__ScannerEndpoint=127.0.0.1");
    }

    [Fact]
    public void CentralPmsLauncher_FailsClosedThroughEvidenceDependencyLauncher()
    {
        var launcher = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Start-CentralPms.ps1");

        launcher.Should().Contain("Start-StatutoryEvidenceServices.ps1");
        launcher.Should().Contain("Initialize-StatutoryEvidenceRuntime.ps1");
        launcher.Should().Contain("$evidenceRuntime = & $evidenceServicesLauncherPath -PassThru");
        launcher.Should().Contain("--env-file $evidenceRuntime.CentralPmsEnvironmentFile");
        launcher.Should().Contain("Persistent PITX statutory evidence services did not provide");
    }

    [Fact]
    public void GovernanceInitializer_IsTargetGuardedIdempotentAndFailClosed()
    {
        var initializer = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Initialize-StatutoryEvidenceRuntime.ps1");
        var sql = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Initialize-StatutoryEvidenceRuntime.sql");

        initializer.Should().Contain("DatabaseContainer = 'exitpass-ist-persistent-db'");
        initializer.Should().Contain("DatabaseName = 'exitpass_ist'");
        initializer.Should().Contain("ON_ERROR_STOP=1");
        initializer.Should().Contain("initialization failed closed");

        sql.Should().Contain("current_database() <> 'exitpass_ist'");
        sql.Should().Contain("pg_advisory_xact_lock");
        sql.Should().Contain("PITX_STATUTORY_EVIDENCE_LOCAL_REVIEW");
        sql.Should().Contain("APPROVED_ENABLED");
        sql.Should().Contain("LOCAL_TEST");
        sql.Should().Contain("ON CONFLICT (retention_class_code, retention_policy_version) DO UPDATE");
        sql.Should().Contain("service_identity_code = 'payment-orchestrator'");
        sql.Should().Contain("site_code = 'PITX-LEVEL-3'");
        sql.Should().Contain("capture_allowed");
        sql.Should().NotContain("UPDATE discounts.statutory_discount_decision_commands");
    }

    [Fact]
    public void StopHelper_PreservesEvidenceStateAndValidatesOwnership()
    {
        var stop = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "Stop-StatutoryEvidenceServices.ps1");

        stop.Should().Contain("persistent-pitx-statutory-evidence");
        stop.Should().Contain("docker.exe stop");
        stop.Should().Contain("PostgreSQL state were preserved");
        stop.Should().NotContain("docker.exe rm");
        stop.Should().NotContain("Remove-Item");
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
