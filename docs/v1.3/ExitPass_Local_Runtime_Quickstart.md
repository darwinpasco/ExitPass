# ExitPass Local Runtime Quickstart

## Repositories

- `D:\SourceCodes\ExitPass`
- `D:\SourceCodes\ExitPass-PoSServer`
- `D:\SourceCodes\ExitPass-ManagementPlatform`
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal`

Install each frontend's dependencies once with `npm.cmd ci` before starting it.

## Canonical Ports

| Component | Local/manual address |
| --- | --- |
| Central PMS | `https://localhost:56064`, `http://127.0.0.1:56065` |
| Payment Orchestrator | `https://localhost:56062`, `http://127.0.0.1:56063` |
| PITX Level 3 POS Server | `https://localhost:56066`, `http://127.0.0.1:56067` |
| APT UI | `http://localhost:5173` |
| WebPay | `http://localhost:5174` |
| Operator Console | `http://127.0.0.1:5175` |
| Management Platform | `http://127.0.0.1:5178/management-platform/` |

Ports 5176 and 5177 are reserved for disposable review runtimes.

## Start

Run each command in its own PowerShell window.

```powershell
# ExitPass backend window 1
cd D:\SourceCodes\ExitPass
powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Start-CentralPms.ps1

# ExitPass backend window 2
cd D:\SourceCodes\ExitPass
powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Start-PaymentOrchestrator.ps1

# PITX Level 3 POS Server
cd D:\SourceCodes\ExitPass-PoSServer
.\scripts\Start-PosServerPitxLocal.ps1

# Management Platform
cd D:\SourceCodes\ExitPass-ManagementPlatform
powershell -ExecutionPolicy Bypass -File .\scripts\Start-ManagementPlatformLocal.ps1

# WebPay with the local PITX context
cd D:\SourceCodes\ExitPass
powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Start-WebPayPitx.ps1

# APT with the local PITX context
cd D:\SourceCodes\ExitPass-AssistedPaymentTerminal
powershell -ExecutionPolicy Bypass -File .\scripts\Start-AptPitxLocal.ps1

# Operator Console
cd D:\SourceCodes\ExitPass
powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Start-OperatorConsole.ps1
```

Start the components in the order shown: Central PMS, Payment Orchestrator, POS Server, Management Platform, WebPay, APT, and Operator Console. Management Platform and Operator Console proxy same-origin `/v1` requests to Central PMS. WebPay proxies same-origin `/v1` requests to Payment Orchestrator. The APT native host targets Central PMS over HTTPS. Central PMS keeps Site-specific fiscal routing; PITX Level 3 resolves to POS Server ID `3a138565-1b88-55f8-c83d-5380db6edccc`. Environment-specific proxy variables still override the browser UI defaults.

After startup, the POS Server health endpoints are `http://127.0.0.1:56067/health/live` and `http://127.0.0.1:56067/health/ready`.

## Stop

Press `Ctrl+C` in each launcher window. The POS launcher stops its local API container and removes its temporary HTTPS certificate. The APT launcher stops its Vite child process and restores the tracked `apt-config.json` before exiting.

Docker Compose ports in the 808x range remain valid for container topology. The persistent PITX Central PMS route remains Site-specific at `http://exitpass-r41-pos-server:8080/`; the POS launcher supplies that internal network alias while exposing `56066/56067` to the host. Container addresses are distinct from the 5606x local/manual ports. Disposable review runtimes must use explicitly selected temporary ports.
