# ExitPass v1.2 Full Solution Test Classification

Date: 2026-05-15
Branch: `codex/v12-full-solution-test-classification`

## 1. Executive Summary

- `dotnet restore`: passed.
- `dotnet build --no-restore`: passed, 67 projects restored/built, 0 compile errors.
- `dotnet test --no-build --logger "console;verbosity=minimal"`: failed at solution level after running the test projects because VSTest attempted to run a production assembly: `ExitPass.PaymentOrchestrator.Application.dll`.
- Explicit `*Tests.csproj` runs: passed, 23/23 test projects, 205 passed, 0 failed, 0 skipped.
- Docker prerequisites were present enough for the known green baseline: PostgreSQL, RabbitMQ, Central PMS, observability services were running. `exitpass-nginx` was restarting, and mock payment/vendor PMS containers were unhealthy.
- Central PMS integration/contract tests passed after aligning test database env vars to the Central PMS container database target, `exitpass_v12_dev`.

## 2. Project Inventory

- Total projects discovered by restore/build: 67.
- Test projects discovered by `Get-ChildItem -Recurse -Filter *Tests.csproj`: 23.

Test projects:

| Area | Test projects |
| --- | ---: |
| Audit Event Service | 3 |
| Central PMS | 3 |
| Coupon Service | 3 |
| Gate Integration Service | 3 |
| Payment Orchestrator | 3 |
| Session Service | 3 |
| Vendor PMS Adapter | 3 |
| End-to-End | 1 |
| Security | 1 |

No WebPay, Mock Vendor PMS, or Mock Payment Provider test project was discovered in the current solution tree.

## 3. Known Green Service Baseline

| Project | Result | Tests | Notes |
| --- | --- | ---: | --- |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/ExitPass.CentralPms.ContractTests.csproj` | Passed | 16 | Requires Central PMS API on `localhost:8080` and DB env vars aligned to `exitpass_v12_dev`. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/ExitPass.CentralPms.IntegrationTests.csproj` | Passed | 66 | Passed with `EXITPASS_*` DB env vars set to host port `5433`, database `exitpass_v12_dev`. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.UnitTests/ExitPass.PaymentOrchestrator.UnitTests.csproj` | Passed | 20 | Explicit project run passes. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.ContractTests/ExitPass.PaymentOrchestrator.ContractTests.csproj` | Passed | 7 | Explicit project run passes. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.IntegrationTests/ExitPass.PaymentOrchestrator.IntegrationTests.csproj` | Passed | 12 | Explicit project run passes. |
| `src/Services/GateIntegrationService/tests/ExitPass.GateIntegrationService.UnitTests/ExitPass.GateIntegrationService.UnitTests.csproj` | Passed | 16 | Explicit project run passes. |
| `src/Services/GateIntegrationService/tests/ExitPass.GateIntegrationService.ContractTests/ExitPass.GateIntegrationService.ContractTests.csproj` | Passed | 7 | Explicit project run passes. |
| `src/Services/GateIntegrationService/tests/ExitPass.GateIntegrationService.IntegrationTests/ExitPass.GateIntegrationService.IntegrationTests.csproj` | Passed | 12 | Explicit project run passes. |

## 4. Additional Test Project Results

| Project | Result | Tests | Classification |
| --- | --- | ---: | --- |
| `src/Services/AuditEventService/tests/ExitPass.AuditEventService.UnitTests/ExitPass.AuditEventService.UnitTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/AuditEventService/tests/ExitPass.AuditEventService.ContractTests/ExitPass.AuditEventService.ContractTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/AuditEventService/tests/ExitPass.AuditEventService.IntegrationTests/ExitPass.AuditEventService.IntegrationTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/CouponService/tests/ExitPass.CouponService.UnitTests/ExitPass.CouponService.UnitTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/CouponService/tests/ExitPass.CouponService.ContractTests/ExitPass.CouponService.ContractTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/CouponService/tests/ExitPass.CouponService.IntegrationTests/ExitPass.CouponService.IntegrationTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/SessionService/tests/ExitPass.SessionService.UnitTests/ExitPass.SessionService.UnitTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/SessionService/tests/ExitPass.SessionService.ContractTests/ExitPass.SessionService.ContractTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/SessionService/tests/ExitPass.SessionService.IntegrationTests/ExitPass.SessionService.IntegrationTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/VendorPmsAdapter/tests/ExitPass.VendorPmsAdapter.UnitTests/ExitPass.VendorPmsAdapter.UnitTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/VendorPmsAdapter/tests/ExitPass.VendorPmsAdapter.ContractTests/ExitPass.VendorPmsAdapter.ContractTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `src/Services/VendorPmsAdapter/tests/ExitPass.VendorPmsAdapter.IntegrationTests/ExitPass.VendorPmsAdapter.IntegrationTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `tests/EndToEnd/ExitPass.EndToEndTests/ExitPass.EndToEndTests.csproj` | Passed | 1 | Placeholder/stub coverage. |
| `tests/Security/ExitPass.SecurityTests/ExitPass.SecurityTests.csproj` | Passed | 1 | Placeholder/stub coverage. |

## 5. Failing Projects And Failure Classification

### Solution-level `dotnet test --no-build`

- Command: `dotnet test --no-build --logger "console;verbosity=minimal"`
- Result: failed after test project execution.
- Failure type: test discovery/test host setup issue, not an individual test failure.
- Representative error:

```text
Testhost process for source(s) '...ExitPass.PaymentOrchestrator.Application.dll' exited with error:
An assembly specified in the application dependencies manifest (ExitPass.PaymentOrchestrator.Application.deps.json) was not found:
package: 'Castle.Core', version: '5.1.1'
path: 'lib/net6.0/Castle.Core.dll'
Test Run Aborted.
```

- Root cause summary: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/ExitPass.PaymentOrchestrator.Application.csproj` references `xunit` and `NSubstitute`. Because it references `xunit`, VSTest treats the production Application assembly as a test source during solution-level `dotnet test`.
- Classification: missing/incorrect test host setup or project metadata; likely production project package hygiene defect.
- Recommended next action: in a small branch, remove test-framework references from the production Application project or move the test-only code that required them into the UnitTests project. Verify `dotnet test --no-build` at solution level after the package graph is corrected.
- Codex next: yes, suitable for a focused branch. No broad behavior change should be needed.

## 6. Warning Debt

### Current compile status

- Initial `dotnet build --no-restore` completed with 0 errors and reported 57 warnings in the console output.
- A subsequent no-incremental build after all projects were current reported 0 warnings. The observed warning debt from the first full build is still worth tracking because it appeared during the fresh classification pass.

### CS1591 XML documentation warnings

Observed primarily in smoke/placeholder controllers and tests:

- Central PMS smoke/unit/contract/integration test classes and `CoreSmokeController`.
- Session Service `SessionsSmokeController`, `Program`, and smoke tests.
- Payment Orchestrator unit tests.
- Audit Event Service smoke controller, `Program`, and smoke tests.
- Vendor PMS Adapter smoke controller, `Program`, and smoke tests.
- Coupon Service smoke controller, `Program`, and smoke tests.

Classification: warning-only debt. Do not mix with behavior branches unless the branch is explicitly for XML documentation policy cleanup.

### Nullable warnings

Observed examples:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Controllers/PaymentAttemptsController.cs`: `CS8604` possible null request passed to validator.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/InternalPaymentAttemptFinalizationEndpoints.cs`: `CS8602` possible null dereference.

Classification: warning-only debt with possible request-validation edge cases. Should be handled in a focused Central PMS API nullability branch.

### Obsolete API warnings

Targeted scan:

```text
rg "RecordException|ActivityExtensions\.RecordException" src tests
```

Remaining matches are OpenTelemetry option properties such as `options.RecordException = true` and documentation text containing the word obsolete. No obsolete `ActivityExtensions.RecordException(...)` or `activity.RecordException(...)` call sites were found in the scanned source.

Classification: no active obsolete exception-recording call-site debt found by this scan.

### Package/security warnings

Command:

```text
dotnet list package --vulnerable --include-transitive
```

Result: no vulnerable packages reported for the projects in the solution using the configured NuGet sources.

### Analyzer warnings

No separate analyzer warnings were identified beyond compiler warning categories above.

## 7. Placeholder/Stub Tests

The following are placeholder smoke tests and should be replaced with behavior-specific coverage later:

- `tests/Security/ExitPass.SecurityTests/SmokeTests.cs`
- `tests/EndToEnd/ExitPass.EndToEndTests/SmokeTests.cs`
- `src/Services/AuditEventService/tests/ExitPass.AuditEventService.UnitTests/SmokeTests.cs`
- `src/Services/AuditEventService/tests/ExitPass.AuditEventService.ContractTests/SmokeTests.cs`
- `src/Services/AuditEventService/tests/ExitPass.AuditEventService.IntegrationTests/SmokeTests.cs`
- `src/Services/CouponService/tests/ExitPass.CouponService.UnitTests/SmokeTests.cs`
- `src/Services/CouponService/tests/ExitPass.CouponService.ContractTests/SmokeTests.cs`
- `src/Services/CouponService/tests/ExitPass.CouponService.IntegrationTests/SmokeTests.cs`
- `src/Services/SessionService/tests/ExitPass.SessionService.UnitTests/SmokeTests.cs`
- `src/Services/SessionService/tests/ExitPass.SessionService.ContractTests/SmokeTests.cs`
- `src/Services/SessionService/tests/ExitPass.SessionService.IntegrationTests/SmokeTests.cs`
- `src/Services/VendorPmsAdapter/tests/ExitPass.VendorPmsAdapter.UnitTests/SmokeTests.cs`
- `src/Services/VendorPmsAdapter/tests/ExitPass.VendorPmsAdapter.ContractTests/SmokeTests.cs`
- `src/Services/VendorPmsAdapter/tests/ExitPass.VendorPmsAdapter.IntegrationTests/SmokeTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/SmokeTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/SmokeTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/SmokeTests.cs`

Also observed: `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Domain/TariffSnapshotTests.cs` contains a placeholder-style tariff snapshot test name.

## 8. Environment-Dependent Failures And Preconditions

Current Docker snapshot:

- `exitpass-postgres`: running healthy, host port `5433`.
- `exitpass-rabbitmq`: running healthy, host ports `5672` and `15672`.
- `exitpass-central-pms`: running, host port `8080`.
- `exitpass-otel-collector`: running healthy.
- `exitpass-grafana`, `exitpass-prometheus`, `exitpass-jaeger`, `exitpass-pgadmin`: running.
- `exitpass-nginx`: restarting.
- `exitpass-mock-payment-provider`: unhealthy.
- `exitpass-mock-vendor-pms`: unhealthy.

Central PMS container database target:

```text
ConnectionStrings__MainDatabase=Host=postgres;Port=5432;Database=exitpass_v12_dev;Username=exitpass;Password=change_me
```

Matching local test env vars used:

```powershell
$cs = "Host=127.0.0.1;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me;Include Error Detail=true"
$env:EXITPASS_TEST_MAIN_DB = $cs
$env:EXITPASS_INTEGRATION_DB = $cs
$env:EXITPASS_TEST_DB_CONNECTION_STRING = $cs
$env:ConnectionStrings__MainDatabase = $cs
```

Environment-dependent classifications:

- Central PMS contract/integration tests depend on DB credentials matching `exitpass_v12_dev` and the Central PMS API listening on `localhost:8080`.
- Without the DB env alignment, these tests can fail with PostgreSQL `28P01` authentication errors.
- API-backed tests can fail with `localhost:8080` connection refused if the Central PMS container is not running.
- Future E2E/service-boundary tests may be blocked by currently unhealthy mock payment/vendor PMS containers and restarting nginx.
- RabbitMQ was available in this run, but RabbitMQ eventing remains a pending implementation epic rather than a failing test suite.

## 9. Pending Epics Not Yet Implemented

- RabbitMQ eventing.
- mTLS.
- HikCentral Professional vendor PMS integration.
- AUB API integration.
- WebPay UI/API.
- Reconciliation.
- Coupons/statutory discount flows.
- Security/RBAC.
- Observability dashboards.
- Persisted audit-event decision.
- Deployment/CI hardening.
- Full E2E/service-boundary validation.

## 10. Recommended Next Task Order

1. `codex/v12-payment-orchestrator-application-test-package-cleanup`: remove test-framework package references from the production Payment Orchestrator Application project and restore solution-level `dotnet test` behavior.
2. `codex/v12-central-pms-api-nullability-warning-cleanup`: address the Central PMS API nullable warnings without changing endpoint behavior.
3. `codex/v12-test-placeholder-inventory-to-real-coverage`: replace one placeholder service test group at a time, starting with Security and EndToEnd.
4. `codex/v12-mock-containers-healthcheck-classification`: classify and repair unhealthy mock payment/vendor PMS containers only if required for E2E readiness.
5. `codex/v12-ci-test-command-hardening`: update CI documentation/scripts to run explicit `*Tests.csproj` projects until the solution-level test discovery issue is fixed.

## 11. Commands Run

```powershell
git branch --show-current
git status --short
docker ps --format "table {{.Names}}\t{{.Ports}}\t{{.Status}}"
docker inspect exitpass-central-pms
rg --files -g "*.sln" -g "*Tests.csproj" -g "*.csproj"
rg -n "Placeholder_Should_Pass|SmokeTests|TODO|NotImplemented|Skip\s*=|Fact\(|Theory\(" src tests -g "*.cs"
dotnet restore
dotnet build --no-restore
$cs = "Host=127.0.0.1;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me;Include Error Detail=true"
$env:EXITPASS_TEST_MAIN_DB = $cs
$env:EXITPASS_INTEGRATION_DB = $cs
$env:EXITPASS_TEST_DB_CONNECTION_STRING = $cs
$env:ConnectionStrings__MainDatabase = $cs
dotnet test --no-build --logger "console;verbosity=minimal"
Get-ChildItem -Recurse -Filter *Tests.csproj | Sort-Object FullName | ForEach-Object { dotnet test $_.FullName --no-build --logger "console;verbosity=minimal" }
rg -n "RecordException|ActivityExtensions\.RecordException|obsolete|Obsolete\(" src tests -g "*.cs" -g "*.csproj"
rg -n "Microsoft.NET.Test.Sdk|IsTestProject|Castle.Core|Moq|xunit" src\Services\PaymentOrchestrator -g "*.csproj"
dotnet list package --vulnerable --include-transitive
dotnet build --no-restore -p:NoIncremental=true --verbosity:minimal
```

## 12. Validation Notes

- No production code changes were made for this classification.
- No DDL, appsettings, Docker, launchSettings, `.env`, secrets, or package files were changed.
- No tests were deleted, skipped, weakened, or refactored.
- Report path: `docs/engineering/ExitPass_v1.2_Full_Solution_Test_Classification.md`.
