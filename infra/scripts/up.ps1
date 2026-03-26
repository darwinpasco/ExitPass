param(
    [ValidateSet("infra", "core", "full")]
    [string]$Profile = "infra"
)

$compose = ".\infra\docker\docker-compose.yml"
$envFile = ".\infra\env\.env.dev"

if (!(Test-Path $compose)) {
    throw "Compose file not found: $compose"
}

if (!(Test-Path $envFile)) {
    throw "Env file not found: $envFile"
}

switch ($Profile) {
    "infra" {
        docker compose --env-file $envFile -f $compose up -d postgres rabbitmq nginx
    }
    "core" {
        docker compose --env-file $envFile -f $compose up -d postgres rabbitmq nginx
        docker compose --env-file $envFile -f $compose up --build db-migrator
        docker compose --env-file $envFile -f $compose up -d --build centralpms-api
    }
    "full" {
        docker compose --env-file $envFile -f $compose up -d --build
    }
}
