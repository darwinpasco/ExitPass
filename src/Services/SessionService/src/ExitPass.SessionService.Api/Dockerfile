FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore ExitPass.sln
RUN dotnet publish src/Services/SessionService/src/ExitPass.SessionService.Api/ExitPass.SessionService.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ExitPass.SessionService.Api.dll"]
