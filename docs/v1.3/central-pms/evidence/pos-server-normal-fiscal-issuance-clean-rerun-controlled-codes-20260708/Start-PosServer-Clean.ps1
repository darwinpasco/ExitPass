$ErrorActionPreference = 'Stop'
$evidence = 'D:\SourceCodes\ExitPass\docs\v1.3\central-pms\evidence\pos-server-normal-fiscal-issuance-clean-rerun-controlled-codes-20260708'
$pids = Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
foreach ($pidValue in $pids) {
    if ($pidValue) {
        Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = 'dotnet'
$psi.Arguments = 'D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\bin\Debug\net8.0\ExitPass.PosServer.Api.dll --urls http://localhost:5000'
$psi.WorkingDirectory = 'D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api'
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.EnvironmentVariables['ASPNETCORE_URLS'] = 'http://localhost:5000'
$psi.EnvironmentVariables['ConnectionStrings__PosServer'] = 'Host=localhost;Port=5433;Database=posserver_api_smoke_validation_local;Username=exitpass;Password=change_me'
$process = [System.Diagnostics.Process]::Start($psi)
$process.Id | Set-Content -Encoding ASCII "$evidence\posserver-runtime.pid.txt"
Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -Action { if ($EventArgs.Data) { Add-Content -Encoding ASCII -Path 'D:\SourceCodes\ExitPass\docs\v1.3\central-pms\evidence\pos-server-normal-fiscal-issuance-clean-rerun-controlled-codes-20260708\posserver-runtime.stdout.log' -Value $EventArgs.Data } } | Out-Null
Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -Action { if ($EventArgs.Data) { Add-Content -Encoding ASCII -Path 'D:\SourceCodes\ExitPass\docs\v1.3\central-pms\evidence\pos-server-normal-fiscal-issuance-clean-rerun-controlled-codes-20260708\posserver-runtime.stderr.log' -Value $EventArgs.Data } } | Out-Null
$process.BeginOutputReadLine()
$process.BeginErrorReadLine()
Start-Sleep -Seconds 5
