FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore ExitPass.sln
RUN dotnet publish src/Services/CouponService/src/ExitPass.CouponService.Api/ExitPass.CouponService.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ExitPass.CouponService.Api.dll"]
