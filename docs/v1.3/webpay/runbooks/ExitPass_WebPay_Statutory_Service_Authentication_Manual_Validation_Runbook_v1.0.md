# ExitPass WebPay Statutory Service Authentication Manual Validation Runbook

## Purpose

This runbook gives Darwin a deterministic local manual-validation path for the WebPay statutory service-authentication and customer-safe error mapping slice.

This is not controlled WebPay UAT. It validates the service-authentication boundary and browser-safe errors with a local Central PMS statutory contract stub plus the real Payment Orchestrator and WebPay UI.

## Scope

Included:

- WebPay browser
- Payment Orchestrator statutory WebPay routes
- Local Central PMS statutory contract stub
- Service identity and permission header validation
- Customer-safe error behavior
- Correlation tracing
- Browser developer-tools inspection

Excluded:

- Secure evidence upload
- Evidence storage
- Continuation URLs
- Ordinance availability UI
- Pay-regular behavior
- Approval/payment race arbitration
- Operator Console review
- APT
- POS Server
- Canonical database changes
- Production credentials

## Deterministic Fixture

| Item | Value |
| --- | --- |
| WebPay service identity | `9b000000-0000-0000-0000-000000000005` |
| Rejected identity | `9b000000-0000-0000-0000-000000000007` |
| Permission-denied identity | `9b000000-0000-0000-0000-000000000008` |
| Ticket | `WEBPAY-STAT-SERVICE-AUTH-001` |
| Plate | `SVC 0001` |
| Parking session | `20000000-0000-4000-8000-000000000001` |
| Original tariff snapshot | `30000000-0000-4000-8000-000000000001` |
| Site Group | `40000000-0000-4000-8000-000000000001` |
| Site | `50000000-0000-4000-8000-000000000001` |
| Vendor system | `60000000-0000-4000-8000-000000000001` |
| Decision command | `aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa` |
| Request reference | `bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb` |
| Amount | `PHP 137.50` |

Identity source:

- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests.cs`
- `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.UnitTests/Infrastructure/Integrations/CentralPmsWebPayClientTests.cs`

The identity is local/test-only, non-human, and limited to statutory WebPay submit/read posture in this harness.

## Ports and URLs

| Service | URL |
| --- | --- |
| Central PMS statutory stub | `http://127.0.0.1:5080` |
| Payment Orchestrator | `http://127.0.0.1:5081` |
| WebPay | `http://127.0.0.1:5173/?ticketReference=WEBPAY-STAT-SERVICE-AUTH-001` |

Browser routing uses the repository's same-origin WebPay model:

- Browser request URL: `http://127.0.0.1:5173/v1/webpay/parking-session`
- Vite proxy target: `http://127.0.0.1:5081`
- WebPay API paths: relative `/v1/webpay/...`

The browser must not call `http://127.0.0.1:5081/v1/webpay/...` directly. No CORS preflight is required for the same-origin WebPay path, and Payment Orchestrator does not need a local browser CORS exception for this harness.

The harness fails when a required port is occupied. Override ports only when the printed URLs are used consistently.

## Prerequisites

Run from:

```powershell
cd D:\SourceCodes\ExitPass-G-StatutoryServiceAuth
```

Required commands:

- `dotnet`
- `node`
- `npm.cmd`
- `powershell`

Install WebPay dependencies before browser use:

```powershell
cd D:\SourceCodes\ExitPass-G-StatutoryServiceAuth\src\Services\WebPayUi
npm.cmd ci
cd D:\SourceCodes\ExitPass-G-StatutoryServiceAuth
```

Run harness self-test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -SelfTest
```

Print all scenario commands:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario All -DryRun
```

Before browser testing a scenario, reset only WebPay statutory recovery metadata for the local origin:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Unavailable -ResetBrowserRecovery
```

Then open the printed URL in the same browser profile used for validation. The local-only query parameter `webpayStatutoryRecoveryReset=1` clears only `exitpass:webpay:statutory-discount-recovery:v1`; it does not clear unrelated browser storage and does not cancel any Central PMS authority.

## Scenario Commands

Run one scenario at a time. Stop or cleanup between scenarios.

### Valid

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Valid -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Valid -BrowserRouteProbe
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Valid -ProbeOnly
```

Expected:

- Statutory submit returns a decision.
- Durable readback succeeds after browser polling and returns `decisionCommandStatus = AWAITING_REVIEW`.
- Durable readback returns `overallResultClassification = PENDING_REVIEW`.
- WebPay displays `Awaiting review`.
- WebPay displays that the Senior Citizen or PWD parking privilege request was received and is awaiting review.
- WebPay does not display `Status temporarily unavailable` for the accepted pending-review response.
- WebPay leaves `Refresh status` available.
- WebPay leaves payment unavailable while the review is active.
- Stub log records `X-ExitPass-Service-Identity-Id = 9b000000-0000-0000-0000-000000000005`.
- Stub log records `X-ExitPass-Permissions = statutory-discounts.decision.submit.webpay` for submit and `statutory-discounts.decision.read` for readback.
- Browser route probe posts to `http://127.0.0.1:5173/v1/webpay/parking-session`.
- Browser route probe returns the deterministic parking session and correlation ID.
- Browser network does not show `OPTIONS http://127.0.0.1:5081/v1/webpay/parking-session`.
- Browser network does not show direct WebPay calls to port `5081`.

### Missing Configuration

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario MissingConfiguration -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario MissingConfiguration -ProbeOnly
```

Expected:

- WebPay receives `WEBPAY_STATUTORY_SERVICE_UNAVAILABLE`.
- Browser shows `Parking-privilege requests are temporarily unavailable. Please try again later or ask a parking attendant for assistance.`
- No Central PMS request is needed because Payment Orchestrator fails closed before sending statutory calls.

### Rejected Identity

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario RejectedIdentity -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario RejectedIdentity -ProbeOnly
```

Expected:

- Stub returns raw upstream `401`.
- WebPay receives `WEBPAY_STATUTORY_SERVICE_UNAVAILABLE`.
- Browser does not show the service identity GUID or raw authentication text.

### Permission Denied

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario PermissionDenied -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario PermissionDenied -ProbeOnly
```

Expected:

- Stub returns raw upstream `403`.
- WebPay receives `WEBPAY_STATUTORY_SERVICE_UNAVAILABLE`.
- Browser does not show `CentralPmsStatutoryDiscountDecisionSubmit` or `statutory-discounts.decision.submit.webpay`.

### Timeout

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Timeout -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Timeout -ProbeOnly
```

Expected:

- Payment Orchestrator times out its Central PMS call.
- WebPay receives `WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE`.
- Retryable posture is true.
- Browser does not show raw timeout or `HttpClient` exception text.

### Unavailable

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Unavailable -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Unavailable -BrowserRouteProbe
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Unavailable -ProbeOnly
```

Expected:

- Parking-session lookup remains healthy through `POST http://127.0.0.1:5173/v1/webpay/parking-session`.
- Browser route probe returns HTTP `200`, deterministic parking session `20000000-0000-4000-8000-000000000001`, and amount `PHP 137.50`.
- The Central PMS stub keeps `/v1/vendor-parking/resolve` healthy and returns statutory-route-specific HTTP `503` only for `/v1/statutory-discounts/decisions`.
- WebPay receives `WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE`.
- WebPay receives HTTP `503` with `retryable = true`.
- Browser sends `POST /v1/webpay/statutory-discounts/decisions`.
- Browser does not show connection-refused text or backend URL.
- Browser does not show `Another page may be submitting this statutory discount request` after opening the reset URL unless the current scenario creates a new in-flight request.

### Validation Failure

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario ValidationFailure -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario ValidationFailure -ProbeOnly
```

Expected:

- WebPay receives `WEBPAY_STATUTORY_DISCOUNT_REQUEST_INVALID`.
- Browser keeps specific safe field guidance.
- The result is not mapped to service unavailable.

### Conflict

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Conflict -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario Conflict -ProbeOnly
```

Expected:

- WebPay receives `STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT`.
- Browser shows safe conflict wording.
- Raw Central PMS response body is not exposed.

### Idempotent Replay

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario IdempotentReplay -Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario IdempotentReplay -ProbeOnly
```

Expected:

- The probe submits the same semantic request twice with the same idempotency key.
- Both responses converge on `aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa`.
- The browser does not display duplicate customer-visible submissions.

## Browser Steps

1. Open `http://127.0.0.1:5173/?ticketReference=WEBPAY-STAT-SERVICE-AUTH-001`.
2. Resolve the ticket.
3. Select `Request statutory discount`.
4. Use:
   - Entitlement: Senior Citizen
   - Document type: OSCA ID
   - Issuing authority: Local validation fixture
   - Masked ID reference: `SC-****-1234`
   - Attestation checked
5. Submit the request.
6. Observe the expected scenario result.
7. Open browser developer tools and inspect network requests from the browser to Payment Orchestrator.

Browser request inspection must confirm:

- Request URLs use `http://127.0.0.1:5173/v1/webpay/...`.
- Request URLs do not use direct browser calls to `http://127.0.0.1:5081/v1/webpay/...`.
- `OPTIONS 405` is absent.
- No `X-ExitPass-Service-Identity-Id` header.
- No `X-ExitPass-Permissions` header.
- No bearer token or server credential.
- No Central PMS internal URL.
- No policy name.
- No permission identifier.
- No raw upstream response.
- No stack trace.
- Safe correlation reference remains visible where intended.

The service headers may appear only in the harness stub request log for the Payment Orchestrator-to-stub call:

`.local/webpay-statutory-service-auth/logs/central-pms-statutory-stub.requests.jsonl`

## Correlation Lookup

Read the browser response correlation ID, then inspect:

```powershell
Select-String -Path .\.local\webpay-statutory-service-auth\logs\*.log -Pattern "<correlation-id>"
Select-String -Path .\.local\webpay-statutory-service-auth\logs\central-pms-statutory-stub.requests.jsonl -Pattern "<correlation-id>"
```

Do not capture passwords, tokens, service credentials, or raw browser storage.

## Stop and Cleanup

Stop only harness-owned processes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Stop
```

Remove harness logs and local state:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Cleanup
```

No normal developer database is created, reset, or deleted by this harness.

## Manual Acceptance Template

```text
Valid authenticated request: PASSED / FAILED
Browser did not construct service identity: PASSED / FAILED
Missing auth configuration safe message: PASSED / FAILED
Rejected service identity safe message: PASSED / FAILED
Permission denial safe message: PASSED / FAILED
Timeout safe retry message: PASSED / FAILED
Unavailable service safe retry message: PASSED / FAILED
Validation message preserved: PASSED / FAILED
Conflict behavior preserved: PASSED / FAILED
Internal auth wording absent: PASSED / FAILED
Policy and permission names absent: PASSED / FAILED
Raw backend response absent: PASSED / FAILED
Credentials absent from browser tools: PASSED / FAILED
Correlation traceable: PASSED / FAILED
Idempotent replay preserved: PASSED / FAILED
Manual walkthrough overall: PASSED / FAILED
```

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Port is occupied | Stop the occupying local process or rerun with explicit free ports. |
| Payment Orchestrator does not start | Inspect `.local/webpay-statutory-service-auth/logs/payment-orchestrator.err.log`. |
| WebPay does not start | Inspect `.local/webpay-statutory-service-auth/logs/webpay-ui.err.log` and confirm `npm.cmd ci` was run. |
| Stub is not ready | Inspect `.local/webpay-statutory-service-auth/logs/central-pms-statutory-stub.err.log`. |
| Browser shows vendor configuration error | Confirm the harness-started WebPay process was used and has `VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID`. |
| Internal auth phrase appears in browser | Fail the scenario and keep the logs for review. |

## Authorization Status

WebPay statutory service-authentication implementation is authorized.

WebPay statutory controlled UAT is not authorized.

Production rollout is not authorized.
