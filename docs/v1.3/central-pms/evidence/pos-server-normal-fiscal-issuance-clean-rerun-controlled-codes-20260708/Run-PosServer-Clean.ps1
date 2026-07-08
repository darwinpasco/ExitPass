$env:ASPNETCORE_URLS='http://localhost:5000'
$env:ConnectionStrings__PosServer='Host=localhost;Port=5433;Database=posserver_api_smoke_validation_local;Username=exitpass;Password=change_me'
dotnet 'D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\bin\Debug\net8.0\ExitPass.PosServer.Api.dll' --urls http://localhost:5000
