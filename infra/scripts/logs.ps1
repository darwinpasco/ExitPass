param(
    [string]$Service = ""
)

$compose = ".\infra\docker\docker-compose.yml"
$envFile = ".\infra\env\.env.dev"

if (!(Test-Path $compose)) {
    throw "Compose file not found: $compose"
}

if (!(Test-Path $envFile)) {
    throw "Env file not found: $envFile"
}

if ([string]::IsNullOrWhiteSpace($Service)) {
    docker compose --env-file $envFile -f $compose logs -f
}
else {
    docker compose --env-file $envFile -f $compose logs -f $Service
}
