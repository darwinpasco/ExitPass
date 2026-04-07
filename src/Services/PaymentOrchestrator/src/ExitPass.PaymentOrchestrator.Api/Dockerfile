# BRD:
# - 12 Payment Orchestration
#
# SDD:
# - 10.5 Payment Orchestrator APIs and Webhooks
#
# Invariants Enforced:
# - Runtime image must include native dependencies required by the PostgreSQL/Npgsql stack.
# - Build and runtime stages must remain deterministic and minimal.
# - Final container must run only the Payment Orchestrator API artifact.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared solution-level files first for better layer caching.
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY ExitPass.sln ./

# Copy project files needed for restore.
COPY src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/ExitPass.PaymentOrchestrator.Api.csproj src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/
COPY src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/ExitPass.PaymentOrchestrator.Application.csproj src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/
COPY src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/ExitPass.PaymentOrchestrator.Contracts.csproj src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/
COPY src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/ExitPass.PaymentOrchestrator.Infrastructure.csproj src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/
COPY src/BuildingBlocks/ExitPass.BuildingBlocks.Observability/ExitPass.BuildingBlocks.Observability.csproj src/BuildingBlocks/ExitPass.BuildingBlocks.Observability/
COPY src/BuildingBlocks/ExitPass.BuildingBlocks.Persistence/ExitPass.BuildingBlocks.Persistence.csproj src/BuildingBlocks/ExitPass.BuildingBlocks.Persistence/
COPY src/BuildingBlocks/ExitPass.BuildingBlocks.Messaging/ExitPass.BuildingBlocks.Messaging.csproj src/BuildingBlocks/ExitPass.BuildingBlocks.Messaging/

# Restore dependencies.
RUN dotnet restore src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/ExitPass.PaymentOrchestrator.Api.csproj

# Copy the full repository after restore to build and publish.
COPY . .

# Publish the API.
RUN dotnet publish src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/ExitPass.PaymentOrchestrator.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Required native dependency for the PostgreSQL/Npgsql stack.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Runtime defaults.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Copy published app.
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ExitPass.PaymentOrchestrator.Api.dll"]