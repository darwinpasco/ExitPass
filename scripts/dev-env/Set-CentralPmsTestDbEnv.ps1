param(
    [string]$ConnectionString = "Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me;Include Error Detail=true"
)

$ErrorActionPreference = "Stop"

$variables = @(
    "EXITPASS_TEST_MAIN_DB",
    "EXITPASS_INTEGRATION_DB",
    "EXITPASS_TEST_DB_CONNECTION_STRING",
    "ConnectionStrings__MainDatabase"
)

foreach ($name in $variables) {
    Set-Item -Path "Env:$name" -Value $ConnectionString
}

Write-Host "Central PMS test database environment variables set for this PowerShell process."
Write-Host "Connection string: $ConnectionString"
Write-Host "Dot-source this script to keep the values in your current shell:"
Write-Host ". .\scripts\dev-env\Set-CentralPmsTestDbEnv.ps1"
