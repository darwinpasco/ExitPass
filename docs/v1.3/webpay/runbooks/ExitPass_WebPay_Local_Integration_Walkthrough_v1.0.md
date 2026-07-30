# ExitPass WebPay Local Integration Walkthrough

## Purpose

This runbook provides a deterministic local developer walkthrough for the ordinary WebPay payment path:

WebPay -> Payment Orchestrator -> Central PMS -> disposable canonical PostgreSQL database -> local mock payment provider.

This is a local developer walkthrough only. It is not controlled UAT and it is not production rollout evidence.

## Scope

Included:

- PostgreSQL using a disposable walkthrough database
- Central PMS
- Payment Orchestrator
- WebPay UI
- local mock payment provider
- one deterministic ordinary parking session
- one active tariff snapshot
- one durable payment attempt or idempotent replay

Excluded:

- entitlement or discount workflows
- ordinance policies
- secure ID evidence
- real PayMongo settlement
- POS Server fiscal issuance
- real Sales Invoice issuance
- ExitAuthorization proof
- HikCentral
- gate control
- APT

## Prerequisites

- Windows PowerShell
- Docker with access to the local Docker engine
- `dotnet`
- Node.js and `npm`
- Canonical database repository at `D:\SourceCodes\exitpassdb_v1.2`
- Generated canonical SQL baseline at `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- Standard local infrastructure containers available:
  - `exitpass-postgres`
  - `exitpass-rabbitmq`
  - `exitpass-mock-payment-provider`

Set the local database password as an environment variable before starting real services:

```powershell
$env:EXITPASS_WEBPAY_LOCAL_DB_PASSWORD = "<local postgres password>"
```

Do not commit local passwords or environment files.

## Startup

From `D:\SourceCodes\ExitPass-G-LocalHarness`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Start-WebPayLocalIntegrationWalkthrough.ps1 -DryRun -StartServices
```

Review the dry-run output. Then start the walkthrough:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Start-WebPayLocalIntegrationWalkthrough.ps1 -StartServices -AllowExistingPorts
```

The script:

- validates repository paths
- validates the canonical database baseline
- validates Docker and required tools
- starts existing standard infrastructure containers when needed
- creates only `exitpass_webpay_local_walkthrough`
- applies the canonical generated SQL baseline
- applies the existing repository payment-routing compatibility patch required by the current Payment Orchestrator runtime
- applies the existing repository PayMongo checkout-session rail reference-data patch required by provider-session persistence
- runs the ordinary WebPay fixture seed
- verifies the fixture
- discovers the Site Group ID, Site ID, Vendor System ID, parking session ID, and tariff snapshot ID from the disposable database by stable business keys
- exports the discovered browser-safe Site Group ID, Site ID, and Vendor System ID to the WebPay process
- starts Central PMS, Payment Orchestrator, and WebPay UI when `-StartServices` is supplied
- configures `WEBPAY_PUBLIC_BASE_URL` for local checkout return URLs
- verifies the local mock provider supports `POST /v1/checkout_sessions`

The script does not reset the normal developer database.

## URLs

- WebPay UI: `http://localhost:5173`
- Payment Orchestrator: `http://localhost:8082`
- Central PMS: `http://localhost:8080`
- Local mock payment provider: `http://localhost:8084`
- Browser walkthrough URL: `http://127.0.0.1:5173/?ticketReference=WEBPAY-LOCAL-ORDINARY-001`

## Deterministic Fixture

- Ticket reference: `WEBPAY-LOCAL-ORDINARY-001`
- Plate number: `LOCALPAY001`
- Site Group business key: `WEBPAY_LOCAL_GROUP`
- Site business key: `WEBPAY_LOCAL_SITE`
- Vendor code/environment: `WEBPAY_LOCAL_MOCK_PMS` / `LOCAL`
- Parking session ID: discovered from the disposable database after seeding
- Tariff snapshot ID: discovered from the disposable database after seeding
- Original amount: PHP 137.50
- Currency: PHP
- Payment method to use first: QRPh

The fixture is ordinary payment data only. It creates no discount decision, no review row, and no discount application row.

## Manual Walkthrough

1. Open WebPay at `http://127.0.0.1:5173/?ticketReference=WEBPAY-LOCAL-ORDINARY-001`.
2. Resolve the deterministic session using ticket `WEBPAY-LOCAL-ORDINARY-001` or plate `LOCALPAY001`.
3. Confirm the displayed site is the local walkthrough site.
4. Confirm the displayed amount is PHP 137.50.
5. Confirm discount or entitlement controls are outside the scope of this runbook and do not use them.
6. Select QRPh or another available mock-backed WebPay method.
7. Click the payment action once.
8. Confirm the browser is handed to the local mock provider flow or displays the local provider handoff.
9. Return to WebPay if the mock page provides a local return action.
10. Refresh the WebPay page.
11. Confirm refresh does not create another payment attempt.
12. Open a second WebPay tab with the same fixture reference.
13. Confirm the second tab observes the same active payment state or safe replay state.
14. Try a rapid double-click on the payment action only if the button is visible and enabled.
15. Confirm the UI does not submit two independent provider handoffs.

Do not complete real provider settlement. Do not claim fiscal issuance, Sales Invoice issuance, or ExitAuthorization from this walkthrough.

## Read-Only Database Verification

After session resolution and before payment:

```powershell
Get-Content scripts\v1.3\webpay\Verify-WebPayLocalIntegrationWalkthrough.sql -Raw |
  docker exec -i exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d exitpass_webpay_local_walkthrough
```

After payment submission, rerun the same verification. Expected:

- one active parking session
- one active tariff snapshot
- amount PHP 137.50
- zero discount decision rows for the fixture
- zero discount application rows for the fixture
- one durable `core.payment_attempts` row, or the same row returned by idempotent replay
- one provider session or provider handoff record when the local mock provider seam creates one

## Automated Provider-Handoff Proof

After the startup script reports all services ready, run the focused local proof:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Test-WebPayLocalIntegrationPaymentHandoff.ps1
```

Expected:

- `POST /v1/webpay/parking-session` resolves `WEBPAY-LOCAL-ORDINARY-001`
- `POST /v1/webpay/parking-session` also resolves plate `LOCALPAY001`
- resolved amount is PHP 137.50
- `POST /v1/webpay/payment-intents` succeeds
- replay of the same payment request returns the same payment attempt
- replay does not create a second mock provider request
- the mock provider request journal contains `POST /v1/checkout_sessions`
- read-only SQL shows one payment attempt and one provider session

The script discovers the fixture Site Group ID, Site ID, and Vendor System ID from `WEBPAY_LOCAL_GROUP`, `WEBPAY_LOCAL_SITE`, and `WEBPAY_LOCAL_MOCK_PMS` / `LOCAL` before calling the browser-facing WebPay routes.

To prove only the real browser-facing parking-session lookup before creating a payment attempt:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Test-WebPayLocalIntegrationPaymentHandoff.ps1 -ParkingSessionProbeOnly
```

## Evidence Checklist

Capture:

- branch and commit hash
- startup command used
- WebPay session resolution showing ticket or plate
- displayed amount PHP 137.50
- selected mock payment method
- payment attempt ID
- provider session reference or handoff URL when present
- read-only SQL output before payment
- read-only SQL output after payment
- evidence that refresh did not create another durable attempt
- evidence that a second tab converged on the same active payment state

Do not capture:

- local passwords
- bearer tokens
- API keys
- raw provider credentials
- database connection strings with secrets
- real customer data

## Double-Click Proof

The useful proof is database-backed. After attempting a rapid double-click, rerun the verification SQL and confirm there is still only one durable payment attempt for the discovered walkthrough parking session, or that later calls replay the same active attempt.

## Refresh Proof

Refresh after the first payment attempt is created. WebPay should not create another payment attempt merely because the page reloaded. The verification SQL should continue to show one durable attempt or idempotent replay.

## Second-Tab Proof

Open the fixture in a second tab. The second tab should observe the same payment state through the normal WebPay flow. Backend idempotency remains authoritative; the browser is not the source of truth.

## Troubleshooting

Docker unavailable:

- Run `docker ps`.
- If Docker cannot connect to the engine, start Docker Desktop or fix local permissions before running the walkthrough.

PostgreSQL port collision:

- The standard local PostgreSQL container uses host port `5433`.
- Stop the conflicting service or configure a matching local container before starting the walkthrough.

Required app port collision:

- Central PMS: `8080`
- Payment Orchestrator: `8082`
- WebPay UI: `5173`
- Rerun with `-AllowExistingPorts` only when the port is already occupied by the intended service.

Canonical baseline missing:

- Verify `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`.
- Do not use retired database artifacts as schema authority.

Seed failure:

- Confirm the target database name is `exitpass_webpay_local_walkthrough`.
- The seed intentionally refuses normal developer databases.

WebPay cannot resolve the fixture:

- Rerun the verification SQL.
- Confirm Central PMS and Payment Orchestrator are using the disposable database connection string.
- Confirm WebPay proxies to `http://localhost:8082`.
- Confirm the startup output printed discovered Site Group ID, Site ID, and Vendor System ID.

Payment gate rejects the request:

- Confirm the resolved tariff snapshot ID matches the discovered tariff snapshot ID printed by the startup script.
- Confirm the amount is PHP 137.50.
- Confirm there is no stale previous payment attempt from a different fixture run.

Mock provider does not open:

- Confirm `exitpass-mock-payment-provider` is running and reachable on `http://localhost:8084`.
- Confirm the startup script reports `Mock PayMongo checkout-session endpoint reachable`.
- A `WEBPAY_PUBLIC_BASE_URL` configuration failure means Payment Orchestrator could not build local checkout return URLs before provider handoff.
- This walkthrough does not require real payment completion.

## Shutdown

Stop walkthrough-started processes and preserve the disposable database:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1
```

Drop only the disposable database when inspection is complete:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1 -RemoveDisposableDatabase
```

Remove only generated harness state when inspection is complete:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1 -RemoveGeneratedState
```

Stop shared local infrastructure only when you intentionally want to stop the standard local containers:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1 -StopInfrastructure
```

## Known Limitations

- This walkthrough stops at local payment attempt or provider handoff.
- It does not prove provider settlement.
- It does not prove POS Server fiscal issuance.
- It does not prove Sales Invoice generation.
- It does not prove ExitAuthorization issuance.
- It does not prove any gate or HikCentral behavior.
- It is not controlled UAT.

## Authorization Status

WebPay integration implementation is authorized.

APT integration implementation is authorized.

APT cash acceptance is not authorized.

Controlled WebPay UAT is not authorized by this runbook.

Production rollout is not authorized by this runbook.
