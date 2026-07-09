# ExitPass Central PMS + Operator Console Local Runtime Smoke Record v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS + Operator Console Local Runtime Smoke Record |
| Version | v1.0 |
| Date | 2026-07-09 |
| Branch | `docs/local-runtime-smoke-central-pms-operator-console` |
| Scope | Local Windows development runtime smoke after Windows App Control / Smart App Control unblock |
| Result classification | PASS |

This record is documentation-only. It does not modify source code, schema, tests, runtime configuration, POS Server state, payment state, fiscal state, ExitAuthorization state, gate state, refund/reversal state, rendering behavior, UAT evidence, or UAT runbooks.

No UAT scenarios were run. No POS Server runtime endpoints were called. No fiscal issuance flows were run.

## 2. Purpose

This record documents whether the local Windows development machine can run Central PMS and Operator Console UI together after Windows App Control / Smart App Control was unblocked.

The smoke was limited to build, local startup, existing focused tests, and safe local UI/static reachability. It did not exercise fiscal issuance, payment confirmation, POS Server integration, ExitAuthorization, gate behavior, refund/reversal, document rendering, or UAT flows.

## 3. Environment

| Item | Value |
| --- | --- |
| OS | `Microsoft Windows NT 10.0.26200.0` from `[System.Environment]::OSVersion.VersionString` |
| OS WMI lookup | `Get-CimInstance Win32_OperatingSystem` returned access denied in this shell |
| .NET SDK | `8.0.421` |
| Node.js | `v24.16.0` |
| npm | `11.13.0` using `npm.cmd --version` |
| PowerShell npm shim | `npm --version` failed because `npm.ps1` is blocked by execution policy; `npm.cmd` works |
| Branch | `docs/local-runtime-smoke-central-pms-operator-console` |
| Date/time captured | `2026-07-09T10:35:03.2607232+08:00` |

## 4. Pre-Checks

Pre-check commands:

| Command | Result |
| --- | --- |
| `git branch --show-current` | `docs/local-runtime-smoke-central-pms-operator-console` |
| `git status --short --untracked-files=all` | Clean before creating this record |
| `dotnet --version` | `8.0.421` |
| `node --version` | `v24.16.0` |
| `npm.cmd --version` | `11.13.0` |

Only this documentation file is expected to change in the final worktree.

## 5. Central PMS Build Result

Command:

```text
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj
```

Sandbox result:

- Failed inside the sandbox because .NET restore/build could not read the user NuGet configuration.
- Exact blocker:

```text
Failed to read NuGet.Config due to unauthorized access. Path: 'C:\Users\darwi\AppData\Roaming\NuGet\NuGet.Config'.
Access to the path 'C:\Users\darwi\AppData\Roaming\NuGet\NuGet.Config' is denied.
```

Approved outside-sandbox rerun:

- Result: passed.
- Restore state: all projects up-to-date for restore.
- Build completed with existing compiler warnings, primarily missing XML comment warnings and nullable/async warnings in existing code/tests.
- No build errors were reported on the approved rerun.

Assessment:

- Central PMS builds successfully on this machine when the process can read local NuGet configuration.
- The initial failure is a local shell/sandbox access issue, not a product build defect.

## 6. Central PMS Runtime Startup Result

Command:

```text
dotnet run --no-build --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj
```

Execution method:

- Started in a controlled background process.
- Captured stdout/stderr.
- Waited 12 seconds.
- Stopped the process before finishing.
- Verified the local HTTP smoke port was no longer reachable after shutdown.

Result:

- Central PMS started successfully.
- Process was still running after 12 seconds and was then stopped.
- No stderr output was captured.

Listening URLs:

```text
https://localhost:56064
http://localhost:56065
```

Relevant startup log excerpts:

```text
Vendor session projection scheduler is disabled by configuration.
The ASP.NET Core developer certificate is not trusted.
Now listening on: https://localhost:56064
Now listening on: http://localhost:56065
Application started. Press Ctrl+C to shut down.
Hosting environment: Development
Content root path: D:\SourceCodes\ExitPass\src\Services\CentralPms\src\ExitPass.CentralPms.Api
```

Notes:

- The untrusted ASP.NET Core developer certificate warning is a local developer trust warning.
- No local DB/config startup blocker appeared during this smoke window.
- No Central PMS runtime endpoint calls were needed to prove startup.

## 7. Operator Console UI Startup Result

Command:

```text
npm.cmd --prefix src\Services\OperatorConsoleUi run dev
```

Execution method:

- Started through a controlled PowerShell job.
- Captured output.
- Waited for Vite readiness.
- Stopped the job before finishing.

Result:

- Operator Console UI Vite dev server started successfully.
- Vite selected port `5175` during the first startup run.

Relevant startup output:

```text
VITE v7.3.6 ready
Local:   http://localhost:5175/
```

Additional fixed-port route reachability run:

```text
npm.cmd --prefix src\Services\OperatorConsoleUi run dev -- --port 5179 --strictPort
```

Result:

- Vite started successfully on `http://localhost:5179/`.
- The job was stopped after route checks.
- Port `5179` was no longer reachable after shutdown.

## 8. Operator Console Page Reachability

Validation method:

- Server-side/static HTTP reachability through the local Vite development server.
- No browser automation or manual visual browser check was performed from Codex.
- Manual browser validation remains pending if visual confirmation is required.

Routes checked through Vite on `http://localhost:5179`:

| Route | Result |
| --- | --- |
| `/operator-console/fiscal-issuance-status` | HTTP `200` |
| `/operator-console/audit/fiscal-status-views` | HTTP `200` |

Interpretation:

- The local Operator Console dev server can serve the app shell for both fiscal status visibility routes.
- This is static/frontend reachability only. It does not prove backend data availability, RBAC behavior, fiscal status data retrieval, or report data retrieval.

## 9. Existing Focused Validation

### Operator Console UI Tests

Command:

```text
npm.cmd --prefix src\Services\OperatorConsoleUi test -- --run src/App.test.tsx
```

Result:

```text
Test Files  1 passed (1)
Tests       64 passed (64)
```

### Central PMS Build

Command:

```text
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj
```

Result:

- Passed on approved outside-sandbox rerun.
- Existing compiler warnings observed.
- No build errors.

### Focused Central PMS Unit Tests

Command:

```text
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~OperatorConsoleFiscalIssuanceStatusServiceTests|FullyQualifiedName~OperatorConsoleFiscalStatusViewAuditReportServiceTests|FullyQualifiedName~FiscalIssuanceStatusReadServiceTests|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests" --no-restore
```

Result:

```text
Passed!  - Failed: 0, Passed: 34, Skipped: 0, Total: 34
```

Notes:

- The test run emitted existing compiler warnings from the test project and referenced projects.
- The selected tests are focused on fiscal status visibility, fiscal status view-audit reporting, fiscal status read service behavior, and RBAC policy mapping.

## 10. Runtime Boundaries Preserved

The smoke preserved these boundaries:

- no POS Server calls;
- no fiscal issuance run;
- no fiscal retry;
- no fiscal readback;
- no fiscal writeback;
- no payment confirmation;
- no ExitAuthorization;
- no gate behavior;
- no refund/reversal;
- no PDF generation;
- no HTML generation;
- no QR generation;
- no final BIR statutory wording;
- no raw evidence access;
- no UAT scenario execution.

The only HTTP route checks were local Vite static/app-shell reads for the two Operator Console UI routes. No POS Server runtime endpoint was called.

## 11. Result Classification

Classification:

```text
PASS
```

Reason:

- Central PMS build passed after allowing local NuGet configuration access.
- Central PMS runtime started locally and listened on Kestrel URLs.
- Operator Console UI Vite runtime started locally.
- The fiscal status viewer route and fiscal status view-audit report route returned HTTP `200` from Vite.
- Focused Operator Console UI tests passed.
- Focused Central PMS fiscal/operator-console unit tests passed.
- No runtime boundary violations were performed.

## 12. Known Follow-Ups

Known follow-ups:

1. If developers want `npm --version` to work from PowerShell, resolve the local execution policy or continue using `npm.cmd`.
2. If HTTPS browser validation is needed, trust the local ASP.NET Core developer certificate.
3. If visual validation is required, manually open the Vite URL in a browser and check the two Operator Console routes.
4. If full backend data-path validation is later required, prepare a safe local DB/config fixture and a separate read-only validation plan before calling Central PMS endpoints.
