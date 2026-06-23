# WebPay Local E2E Smoke

## Purpose

This runbook verifies the local WebPay initiation path across:

- WebPay UI
- Payment Orchestrator WebPay endpoints
- Central PMS parking session, tariff snapshot, and payment attempt paths
- PayMongo Checkout Session handoff through a local stub or approved sandbox configuration

This smoke proves payment initiation and handoff only. It does not prove provider settlement, callback finality, exit authorization, gate opening, BIR invoicing, or vendor payment acknowledgment.

## Authority Boundaries

- WebPay may initiate payment, but must not declare payment finality.
- Payment Orchestrator may create provider checkout/payment handoff and report verified provider outcomes through approved paths.
- Central PMS owns PaymentAttempt state, PaymentConfirmation, platform payment finality, and ExitAuthorization issuance.
- Provider evidence is not ExitPass finality until Central PMS accepts it.
- WebPay must not issue ExitAuthorization.
- Gate Integration consumes valid ExitAuthorization only.
- HikCentral projection/read-model data must not create PaymentAttempt, PaymentConfirmation, payment finality, tariff finality, or ExitAuthorization.

## Local Ports

Use the port set that matches how the services are started.

| Component | Docker compose default | Direct local example |
| --- | ---: | ---: |
| Central PMS API | `http://localhost:8080` | `http://localhost:56065` |
| Payment Orchestrator API | `http://localhost:8082` | `http://localhost:56063` |
| WebPay Vite UI | `http://localhost:5174` | `http://localhost:5174` |
| Mock payment provider WireMock | `http://localhost:8084` | `http://localhost:8084` |
| PostgreSQL | `localhost:5433` | environment-specific |

The actual direct `dotnet run` smoke used Payment Orchestrator on `http://localhost:56063`; the same process also listened on `https://localhost:56062`. Point the WebPay Vite proxy at the HTTP URL:

```powershell
$env:VITE_WEBPAY_API_PROXY_TARGET = "http://localhost:56063"
```

## Prerequisites

- The target database has the current ExitPass schema.
- Central PMS can resolve the selected ticket/card into a parking session and tariff snapshot.
- Payment Orchestrator can reach Central PMS.
- WebPay can reach Payment Orchestrator through the Vite proxy.
- A PayMongo-compatible checkout session path is available through either approved sandbox credentials or the local WireMock stub below.
- No real PayMongo API key, webhook secret, database password, or provider secret is written into this repository or screenshots.

## Required Configuration

### Central PMS

When running directly, supply the same database and vendor configuration used by the local Central PMS smoke environment.

```powershell
$env:ASPNETCORE_URLS = "http://localhost:56065"
$env:ConnectionStrings__MainDatabase = "Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=<user>;Password=<password>"
dotnet run --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-launch-profile
```

When using Docker compose, Central PMS is exposed on `http://localhost:8080` and uses the compose database connection.

### Payment Orchestrator

Payment Orchestrator requires Central PMS and PayMongo provider configuration at startup. Use placeholders or local-only fake values for keys when pointing to a local stub; do not use production secrets.

```powershell
$env:ASPNETCORE_URLS = "http://localhost:56063;https://localhost:56062"
$env:ConnectionStrings__MainDatabase = "Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=<user>;Password=<password>"
$env:Integrations__CentralPms__BaseUrl = "http://localhost:56065"
$env:Payments__Providers__PayMongo__BaseUrl = "https://api.paymongo.com"
$env:Payments__Providers__PayMongo__SecretKey = "sk_test_local_placeholder"
$env:Payments__Providers__PayMongo__PublicKey = "pk_test_local_placeholder"
$env:Payments__Providers__PayMongo__WebhookSecretKey = "whsec_local_placeholder"
$env:Payments__Providers__PayMongo__IsLiveMode = "false"
$env:Payments__Providers__PayMongo__AllowedPaymentMethodTypes__0 = "gcash"
$env:Payments__Providers__PayMongo__AllowedPaymentMethodTypes__1 = "paymaya"
$env:Payments__Providers__PayMongo__AllowedPaymentMethodTypes__2 = "card"
$env:Payments__Providers__PayMongo__AllowedPaymentMethodTypes__3 = "qrph"
$env:WEBPAY_PUBLIC_BASE_URL = "http://localhost:5174"
$env:WEBPAY_PAYMENT_SUCCESS_PATH = "/webpay/payment-return"
$env:WEBPAY_PAYMENT_CANCEL_PATH = "/webpay/payment-cancelled"
dotnet run --project src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj --no-launch-profile
```

`ConnectionStrings__MainDatabase` is required. Without it, `POST /v1/webpay/parking-session` fails when `PaymentProviderRoutingPolicyResolver` tries to load provider routing policy.

Use `Payments__Providers__PayMongo__WebhookSecretKey`. The application does not use `Payments__Providers__PayMongo__WebhookSecret`.

`Payments__Providers__PayMongo__BaseUrl=https://api.paymongo.com` can satisfy startup validation with test-shaped placeholder keys, but fake keys cannot create a real PayMongo checkout. Do not proceed to live provider checkout with fake keys. Real PayMongo test checkout requires real PayMongo test keys and the actual webhook secret through the approved secret channel. Local mock checkout requires WireMock or an equivalent PayMongo-compatible provider stub.

For Docker compose, use `Integrations__CentralPms__BaseUrl=http://central-pms:8080` inside the container network. If using the bundled WireMock payment provider as a PayMongo checkout stub, set:

```powershell
$env:PAYMONGO_BASE_URL = "http://mock-payment-provider:8080"
$env:PAYMONGO_SECRET_KEY = "sk_test_local_placeholder"
$env:PAYMONGO_PUBLIC_KEY = "pk_test_local_placeholder"
$env:PAYMONGO_WEBHOOK_SECRET_KEY = "whsec_local_placeholder"
$env:PAYMONGO_IS_LIVE_MODE = "false"
```

For direct local smoke against the bundled WireMock stub instead of the real PayMongo API, set:

```powershell
$env:Payments__Providers__PayMongo__BaseUrl = "http://localhost:8084"
```

### WebPay UI

Use the WebPay-specific proxy variable. Leave `VITE_WEBPAY_API_BASE_URL` unset for normal local Vite proxy behavior.

```powershell
cd src\Services\WebPayUi
$env:VITE_WEBPAY_API_PROXY_TARGET = "http://localhost:56063"
$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = "<site_group_id>"
$env:VITE_WEBPAY_DEFAULT_SITE_ID = "<site_id>"
$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "<vendor_system_id>"
Remove-Item Env:VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue
npm run dev
```

Open:

```text
http://localhost:5174
```

## Optional Local PayMongo Checkout Stub

The bundled `mock-payment-provider` contains a legacy `/api/provider/payments` mapping. Current WebPay PayMongo checkout creation calls:

```text
POST /v1/checkout_sessions
```

For local smoke without real PayMongo credentials, register a temporary WireMock mapping after the mock provider is running:

```powershell
$mapping = @'
{
  "request": {
    "method": "POST",
    "urlPath": "/v1/checkout_sessions"
  },
  "response": {
    "status": 200,
    "headers": {
      "Content-Type": "application/json"
    },
    "body": "{\"data\":{\"id\":\"cs_webpay_local_{{randomValue length=16 type='ALPHANUMERIC'}}\",\"type\":\"checkout_session\",\"attributes\":{\"checkout_url\":\"https://paymongo.local.test/checkout/cs_webpay_local\",\"checkout_url_expires_at\":\"2026-06-23T12:00:00Z\"}}}",
    "transformers": ["response-template"]
  }
}
'@

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:8084/__admin/mappings" `
  -ContentType "application/json" `
  -Body $mapping
```

This stub is local-only. It does not settle a payment, verify a callback, create PaymentConfirmation, or issue ExitAuthorization.

## Start Services

Docker compose option:

```powershell
docker compose -f .\infra\docker\docker-compose.yml up -d postgres rabbitmq mock-payment-provider central-pms payment-orchestrator
```

Direct local option:

1. Start PostgreSQL and any required local dependencies.
2. Start Central PMS with the environment values above.
3. Start Payment Orchestrator with the environment values above.
4. Start WebPay UI with `npm run dev`.

Avoid rebuilding a running API output directory. Stop the API first or build to an `artifacts\verify` directory if a DLL is locked.

## Reachability Checks

```powershell
Invoke-WebRequest -Uri "http://localhost:56063/swagger/v1/swagger.json" -UseBasicParsing
Invoke-WebRequest -Uri "http://localhost:8084/__admin/mappings" -UseBasicParsing
```

If Central PMS is running directly on `56065`:

```powershell
Invoke-WebRequest -Uri "http://localhost:56065/swagger/v1/swagger.json" -UseBasicParsing
```

If Central PMS is running through Docker compose:

```powershell
Invoke-WebRequest -Uri "http://localhost:8080/swagger/v1/swagger.json" -UseBasicParsing
```

## Smoke Flow

1. Open `http://localhost:5174`.
2. Enter or scan a test ticket/card that Central PMS can resolve.
3. Confirm the parking session resolves and shows a payable amount.
4. Select a supported method: `QRPH`, `GCASH`, `MAYA`, or `CARD`.
5. Start payment.
6. Confirm WebPay calls:
   - `POST /v1/webpay/parking-session`
   - `POST /v1/webpay/payment-intents`
7. Confirm the payment intent response includes a `paymentAttemptId`, `parkingSessionId`, `tariffSnapshotId`, amount/currency, selected provider, and handoff data.
8. Confirm WebPay does not show a final paid state from initiation alone.
9. Confirm no exit authorization is shown or created by WebPay initiation alone.

The current known-good local fixture from the actual smoke was `3519278781100`. It existed in `sessions.vendor_session_projections`, resolved successfully in WebPay, and displayed PHP 3,700.00 during that run. This fixture is environment-specific; recheck it before future smoke runs.

Do not use `3519281044100` as the default current local smoke fixture. During the actual smoke, it had no `sessions.vendor_session_projections` row and failed with `VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_128` / malformed vendor parking response.

## Actual Smoke Findings

The actual local smoke confirmed:

- WebPay UI loaded.
- Vite proxy reached Payment Orchestrator through `VITE_WEBPAY_API_PROXY_TARGET=http://localhost:56063`.
- Payment Orchestrator reached Central PMS after `ConnectionStrings__MainDatabase` was configured.
- Ticket/session lookup succeeded for `3519278781100`.
- Payable basis and amount due displayed; the amount observed in this run was PHP 3,700.00.
- Payment methods displayed.
- Payment intent request reached `POST /v1/webpay/payment-intents`.
- WebPay did not create payment finality by session lookup alone.
- WebPay did not issue ExitAuthorization by session lookup alone.

Provider checkout with fake PayMongo test-shaped keys is expected to fail later because the keys are not real PayMongo test credentials.

## Failed Intent Retry Recovery

Before #319:

- A failed payment intent could consume the tariff snapshot.
- The related payment attempt became `FAILED`.
- Retry reused the consumed tariff snapshot.
- Retry failed with generic `TARIFF_SNAPSHOT_INVALID`.

After #319:

- WebPay, Payment Orchestrator, and Central PMS no longer remain trapped on an earlier consumed failed-attempt snapshot.
- Central PMS can signal a refresh-required / re-resolve path for consumed snapshots tied only to failed attempts.
- Payment Orchestrator can recover by re-resolving payable basis and retrying with a fresh tariff snapshot.
- Failed attempts remain `FAILED`.
- Consumed snapshots remain `CONSUMED`.
- Confirmed or successful consumed snapshots remain protected and are not payable again.

This recovery does not prove real provider checkout success. If fake PayMongo keys are used against the real PayMongo API, provider handoff failure is still expected.

## Browser DevTools Checks

In the Network tab:

- Requests should go to the WebPay origin and be proxied through Vite.
- `X-Correlation-Id` should be present.
- The request body should include `correlationId`.
- The selected payment method should be one of the supported explicit options.
- No PayMongo secret key, webhook secret, database password, or raw credential should appear.
- Payment intent creation should return a provider handoff or a deterministic provider configuration error.

Provider configuration errors are valid smoke findings if the local PayMongo stub or sandbox configuration is not configured. They must not become client-side payment finality.

## Database Verification Snippets

Replace placeholders before running.

Check the known-good projection fixture:

```sql
SELECT
    vendor_record_guid,
    card_num,
    plate_license,
    parking_lot_index_code,
    parking_lot_name,
    enter_time,
    exit_time,
    projection_status,
    last_refreshed_at
FROM sessions.vendor_session_projections
WHERE card_num = '<ticket_reference>'
ORDER BY last_refreshed_at DESC;
```

Check tariff snapshots for the ticket:

```sql
SELECT
    ts.tariff_snapshot_id,
    ts.parking_session_id,
    ps.ticket_number_masked,
    ps.plate_number_masked,
    ts.gross_amount,
    ts.net_amount,
    ts.currency_code,
    ts.calculated_at,
    ts.expires_at,
    ts.snapshot_status,
    ts.created_at
FROM core.tariff_snapshots ts
JOIN core.parking_sessions ps
    ON ps.parking_session_id = ts.parking_session_id
WHERE ps.ticket_number_masked = '<ticket_reference>'
ORDER BY ts.created_at DESC;
```

Check attempts and consumed snapshots:

```sql
SELECT
    ts.tariff_snapshot_id,
    ts.snapshot_status,
    ts.gross_amount,
    ts.net_amount,
    ts.created_at,
    pa.payment_attempt_id,
    pa.attempt_status,
    pa.requested_at,
    pa.updated_at,
    pa.correlation_id
FROM core.tariff_snapshots ts
LEFT JOIN core.payment_attempts pa
    ON pa.tariff_snapshot_id = ts.tariff_snapshot_id
WHERE ts.parking_session_id = '<parking_session_id>'::uuid
ORDER BY ts.created_at DESC, pa.requested_at DESC;
```

Find provider session handoff rows:

```sql
select
    provider_session_id,
    payment_attempt_id,
    provider_code,
    provider_product_code,
    provider_session_ref,
    session_status,
    created_at,
    updated_at
from payments.provider_sessions
where payment_attempt_id = '<payment_attempt_id>'
order by created_at desc;
```

Confirm WebPay initiation alone did not create platform confirmation:

```sql
SELECT
    payment_confirmation_id,
    payment_attempt_id,
    provider_reference,
    confirmation_status,
    confirmed_at
FROM core.payment_confirmations
WHERE payment_attempt_id IN (
    SELECT payment_attempt_id
    FROM core.payment_attempts
    WHERE parking_session_id = '<parking_session_id>'::uuid
)
ORDER BY confirmed_at DESC;
```

Confirm WebPay initiation alone did not issue exit authorization:

```sql
SELECT
    exit_authorization_id,
    parking_session_id,
    payment_attempt_id,
    authorization_status,
    issued_at,
    expires_at
FROM core.exit_authorizations
WHERE parking_session_id = '<parking_session_id>'::uuid
ORDER BY issued_at DESC;
```

If the local smoke only reaches provider handoff, the confirmation and exit authorization queries should return no rows.

## What This Smoke Proves

- WebPay can call Payment Orchestrator locally.
- Payment Orchestrator can call Central PMS locally.
- WebPay can locally resolve a known ticket through Payment Orchestrator and Central PMS.
- Payable basis and amount are displayed.
- WebPay payment intent request reaches the backend.
- Correlation ID propagation is visible.
- Explicit payment method selection reaches the backend request path.
- Retry after a failed payment intent no longer remains trapped on the earlier consumed failed-attempt snapshot after #319.
- WebPay does not declare platform payment finality.
- WebPay does not issue ExitAuthorization.

## What This Smoke Does Not Prove

- Real PayMongo settlement.
- Real PayMongo checkout with valid test keys, unless those keys are supplied through the approved secret channel.
- Production PayMongo webhook verification.
- PayMongo runtime status query or polling.
- Central PMS payment finality after a verified provider outcome.
- PaymentConfirmation recording after finality.
- ExitAuthorization issuance after confirmed payment.
- Gate Integration consume/open.
- BIR invoicing.
- Vendor PMS payment acknowledgment.
- Production TLS, mTLS, firewall, or secret-store behavior.

Use the Central PMS payment-to-exit integration smokes for finality, confirmation, exit authorization, and gate-consume coverage.

## Troubleshooting

### Payment Orchestrator cannot reach Central PMS

Check `Integrations__CentralPms__BaseUrl`. Use `http://central-pms:8080` inside Docker compose and a localhost URL only for direct local runs.

### Payment Orchestrator starts, but parking-session returns 500

Check `ConnectionStrings__MainDatabase`. Payment Orchestrator needs the database connection for provider routing policy lookup:

```powershell
$env:ConnectionStrings__MainDatabase = "Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=<user>;Password=<password>"
```

### WebPay calls return 404

Check `VITE_WEBPAY_API_PROXY_TARGET`. For local WebPay to Payment Orchestrator, use:

```powershell
$env:VITE_WEBPAY_API_PROXY_TARGET = "http://localhost:56063"
```

Do not confuse this with `VITE_WEBPAY_API_BASE_URL`. Leave `VITE_WEBPAY_API_BASE_URL` unset unless intentionally bypassing the Vite proxy.

If the browser Network tab shows requests to `localhost:5174`, that is expected when Vite proxies `/v1` requests. Verify the proxy target points to the Payment Orchestrator HTTP port.

### Browser CORS errors appear

Use the Vite proxy path first. Direct browser calls to a different API origin may require CORS behavior that is not part of this smoke.

### Payment Orchestrator fails at startup

It validates `Integrations:CentralPms:BaseUrl`, `Payments:Providers:PayMongo:SecretKey`, `Payments:Providers:PayMongo:PublicKey`, `Payments:Providers:PayMongo:BaseUrl`, `Payments:Providers:PayMongo:WebhookSecretKey`, and `Payments:Providers:PayMongo:AllowedPaymentMethodTypes`.

For invalid PayMongo settings:

- Confirm `BaseUrl` is an absolute HTTPS URL unless loopback is explicitly allowed.
- Confirm `AllowedPaymentMethodTypes` contains the intended values.
- Confirm `WebhookSignatureToleranceSeconds` is inside the configured validation range.
- Use `WebhookSecretKey`, not `WebhookSecret`.

### Provider handoff fails

Confirm that `Payments__Providers__PayMongo__BaseUrl` points to a PayMongo-compatible endpoint. The bundled mock provider needs the temporary `/v1/checkout_sessions` WireMock mapping above for this smoke.

If fake PayMongo keys are used against `https://api.paymongo.com`, provider handoff failure is expected. Use real PayMongo test keys through the approved secret channel or a local PayMongo-compatible mock.

### Ticket returns vendor malformed

Check `sessions.vendor_session_projections` for the ticket/card. Use the current known-good fixture `3519278781100` or another current active ticket. During the actual smoke, `3519281044100` had no projection row and failed with a malformed vendor response.

### Consumed tariff snapshot trap

#319 should recover when the consumed snapshot is tied only to failed attempts. Confirmed or successful consumed snapshots remain protected and should still reject new payment initiation.

### Raw provider credentials appear in DevTools or logs

Stop the smoke and treat it as a defect. WebPay must not receive provider secret keys or database credentials.

### Reused fixture causes payment-attempt conflict

Use a fresh test ticket/card fixture or clean up only the smoke-created rows through approved test helpers. Do not delete broad production-like data.

### DLL locked during build

Stop the running API process or build to a separate output directory, for example:

```powershell
dotnet build src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj --no-restore -o artifacts\verify\PaymentOrchestrator.Api
```

### Confirmation or exit authorization appears after WebPay initiation alone

Stop and investigate. WebPay initiation must not create PaymentConfirmation or ExitAuthorization without the approved Central PMS finality and explicit exit authorization paths.
