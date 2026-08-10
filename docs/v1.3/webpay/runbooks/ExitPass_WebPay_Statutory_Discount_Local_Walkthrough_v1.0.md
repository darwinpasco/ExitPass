# ExitPass WebPay Statutory-Discount Local Walkthrough v1.0

## 1. Purpose

This runbook coordinates one local-development validation journey across WebPay, Payment Orchestrator, Central PMS, private statutory-evidence storage and scanning, Operator Console human review, payable-basis application, and payment handoff.

It is deliberately separate from `ExitPass_WebPay_Local_Integration_Walkthrough_v1.0.md`: that walkthrough proves the ordinary payment path, while this one proves the evidence-required statutory path and its human-review boundary.

This is local-development validation. It is:

- not Controlled UAT;
- not compliance certification;
- not BIR evidence;
- not production validation;
- not production rollout authorization.

The static harness does not execute the walkthrough. Runtime execution, screenshots, and operator observations must be performed in a separately authorized validation task after review and merge.

## 2. Explicit exclusions

This walkthrough does not authorize or claim:

- use of a real Senior Citizen or PWD identity document;
- fiscal receipt issuance or BIR certification;
- production credentials, data, providers, storage, or rollout;
- external payment-provider settlement;
- Controlled UAT evidence;
- customer identity verification beyond the synthetic local fixture;
- a decision by WebPay or Operator Console to override canonical jurisdiction or ordinance policy.

Fiscal-payload and statutory fiscal-linkage behavior remains covered by the existing focused Central PMS and POS integration tests. This local journey stops at the current payment handoff/provider-session boundary unless a later authorized execution proves an actual fiscal linkage.

## 3. Authority boundaries

| Concern | Authority |
|---|---|
| WebPay statutory operation | Configured Central PMS service identity used by Payment Orchestrator |
| Human reviewer identity | Central PMS human session issued after local username/password authentication |
| Permissions | Canonical `identity.user_roles -> identity.role_permissions -> identity.permissions` resolution |
| Site and Site Group | Canonical active scoped grants evaluated by Central PMS |
| Jurisdiction and ordinance | Central PMS canonical jurisdiction assignment and active policy version |
| Evidence lifecycle | Central PMS metadata, private object storage, validation, and malware scan worker |
| Review decision | Authenticated Operator Console reviewer; WebPay cannot self-approve |
| Payable basis | Central PMS decision/application authority |
| Payment handoff | Payment Orchestrator using the applied tariff/payable basis |

Browser-provided user IDs, roles, permissions, Site IDs, Site Group IDs, or fixture identity headers are not authorization authorities. In Production hosting, `X-ExitPass-User-Id` and `X-Operator-User-Id` must be rejected. There is no implicit `GLOBAL` scope. The browser's selected Site and Site Group are context only; Central PMS reevaluates canonical scope.

The required review permission bundle is:

- `statutory-discounts.review.queue.read`
- `statutory-discounts.review.detail.read`
- `statutory-discounts.decision.review`
- `statutory-discounts.decision.approve`
- `statutory-discounts.decision.reject`
- `statutory-discounts.evidence.review.view`

The tracked Management Platform RBAC fixture is the reviewed source for these exact permission definitions and `OPERATIONS_SUPERVISOR` bindings. Its database-name guard does not allow this walkthrough database, so the walkthrough seed promotes only that six-permission subset under its own stricter disposable-name guard; it does not bypass or execute the tracked fixture. The seed then gives the synthetic reviewer an active role assignment plus explicit Site and Site Group grants. It seeds no `GLOBAL` grant.

## 4. Architecture and current routes

Default loopback services:

| Component | URL |
|---|---|
| Central PMS | `http://127.0.0.1:8080` |
| Payment Orchestrator | `http://127.0.0.1:8082` |
| Local payment provider | `http://127.0.0.1:8084` |
| WebPay | `http://127.0.0.1:5174` |
| Operator Console | `http://127.0.0.1:5175` |
| Private MinIO API | `http://127.0.0.1:19000` |
| ClamAV-compatible scanner | TCP `127.0.0.1:13310` |

Current browser-safe WebPay routes:

- `POST /v1/webpay/statutory-discounts/availability`
- `POST /v1/webpay/statutory-discounts/pending-lifecycle/rediscover`
- `POST /v1/webpay/statutory-discounts/decisions`
- `GET /v1/webpay/statutory-discounts/decisions/{decisionCommandId}`
- `POST /v1/webpay/statutory-discounts/decisions/{decisionCommandId}/apply-payable-basis`
- `POST /v1/webpay/statutory-discounts/evidence/bootstrap`
- `GET /v1/webpay/statutory-discounts/evidence/status`
- `POST /v1/webpay/statutory-discounts/evidence/upload-sessions`
- `PUT /v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference}`
- `POST /v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference}/finalize`
- `POST /v1/webpay/payment-intents`

Current Operator Console and human-session routes:

- `POST /v1/human-authentication/login`
- `GET /v1/human-authentication/session`
- `POST /v1/human-authentication/session/continue`
- `POST /v1/human-authentication/logout`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/pending`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{decisionCommandId}`
- `POST /v1/ops/operator-console/statutory-discounts/reviews/{decisionCommandId}/decision`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{decisionCommandId}/evidence`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{decisionCommandId}/evidence/{evidenceItemReference}/preview`

The preview route streams supported JPEG/PNG bytes through Central PMS. It never gives the browser a storage URL, bucket, object key, checksum, signing material, or permanent download authority.

## 5. Prerequisites

1. Windows PowerShell 5.1 or PowerShell 7, Docker, .NET SDK, Node/npm, and `git` are available.
2. `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` is current.
3. Shared local containers exist with the default names:
   - `exitpass-postgres`
   - `exitpass-rabbitmq`
   - `exitpass-mock-payment-provider`
4. PostgreSQL is reachable on loopback port `5433` and the shared container accepts the configured local DB user.
5. Ports `8080`, `8082`, `5174`, `5175`, `19000`, `19001`, and `13310` are free.
6. The six walkthrough assets pass the static harness.
7. Only synthetic data and generated synthetic PNG/JPEG evidence are used.

Run the static validation first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Test-WebPayStatutoryDiscountWalkthroughHarness.ps1
```

Expected: `WebPay statutory-discount walkthrough static validation passed.` No service, database, upload, or external request is made.

## 6. Secret and configuration preparation

Open a dedicated local-development PowerShell shell for this walkthrough. Retrieve each non-production value from an approved local secret source, then enter it only at the matching masked prompt below. Do not paste secret values into a command line, transcript, issue, screenshot, committed file, or any command that prints or dumps the environment.

The following block is executable in Windows PowerShell 5.1 and PowerShell 7. It temporarily converts each `SecureString` to a managed plaintext string only long enough to populate the current process environment. It does not echo the value or pass it as a process argument. The unmanaged plaintext buffer is zeroed and the temporary references are cleared after every assignment.

```powershell
function Set-ExitPassProcessSecret {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string] $Prompt
    )

    $secureValue = Read-Host -Prompt $Prompt -AsSecureString
    $plainTextPointer = [IntPtr]::Zero
    $plainTextValue = $null

    try {
        $plainTextPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
        $plainTextValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($plainTextPointer)
        [Environment]::SetEnvironmentVariable(
            $Name,
            $plainTextValue,
            [EnvironmentVariableTarget]::Process)
    }
    finally {
        $plainTextValue = $null
        if ($plainTextPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($plainTextPointer)
        }
        if ($null -ne $secureValue) {
            $secureValue.Dispose()
        }
    }
}

Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD' -Prompt 'Disposable PostgreSQL password'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD' -Prompt 'Synthetic reviewer password'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_MINIO_ACCESS_KEY' -Prompt 'Local MinIO access key'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_MINIO_SECRET_KEY' -Prompt 'Local MinIO secret key'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY_BASE64' -Prompt 'Local TOTP protection key (32 bytes, Base64)'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_SECRET_KEY' -Prompt 'Local mock-provider secret key'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_PUBLIC_KEY' -Prompt 'Local mock-provider public key'
Set-ExitPassProcessSecret -Name 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_WEBHOOK_SECRET' -Prompt 'Local mock-provider webhook secret'

Remove-Item Function:\Set-ExitPassProcessSecret -ErrorAction SilentlyContinue
```

Process-scoped environment variables are not a secret vault. Their plaintext values remain readable to the current PowerShell process and are inherited by child processes started by the walkthrough. Keep this dedicated shell private, do not enable transcript capture, do not dump its environment, and close it after running the explicit cleanup in Section 21. The startup script continues to reject missing or empty required values.

The startup script supplies these current values without logging them:

- `VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID`
- `VITE_WEBPAY_DEFAULT_SITE_ID`
- `VITE_WEBPAY_DEFAULT_SITE_GROUP_ID`
- `Integrations:CentralPms:StatutoryDiscounts:WebPayServiceIdentityId` (environment form uses double underscores)
- protected object-storage and scan-worker settings.

## 7. Startup

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1
```

The script:

1. validates exact source paths and tools;
2. refuses a database name outside `exitpass_webpay_local_walkthrough_statutory...`;
3. rebuilds only the guarded disposable database from current canonical DDL;
4. applies current payment-routing compatibility assets and tracked local fixtures;
5. seeds prerequisites, not workflow outcomes;
6. creates the synthetic reviewer's credential through the real activation runtime;
7. starts a walkthrough-owned private MinIO and ClamAV-compatible scanner;
8. generates one synthetic PNG under a restricted temporary directory;
9. starts Central PMS and Payment Orchestrator with `ASPNETCORE_ENVIRONMENT=Production`;
10. starts the real WebPay and Operator Console browser consumers;
11. probes readiness and confirms a fixture identity header is rejected;
12. records bounded process/container identity metadata for safe shutdown.

Expected terminal output includes the WebPay URL, Operator Console URL, reviewer username, and synthetic evidence path. It does not print the reviewer password or infrastructure secrets.

## 8. Readiness verification

Before beginning the journey:

1. Open `http://127.0.0.1:5174/?ticketReference=E2E-231-SESSION-001`.
2. Open `http://127.0.0.1:5175` in a separate browser profile or private window.
3. Confirm the WebPay session resolves the synthetic Site, Site Group, and original tariff.
4. Confirm the Operator Console shows a login screen, not a fixture-authenticated workspace.
5. Inspect browser network responses. No response may contain storage endpoint, bucket, object key, checksum, signed query, provider header, scanner endpoint, credential, or connection string.

The positive fixture is:

| Field | Value |
|---|---|
| Ticket | `E2E-231-SESSION-001` |
| Entitlement | `SENIOR_CITIZEN` |
| Required evidence | `SENIOR_CITIZEN_ID` |
| Reviewer | `sandbox-oc-sd-pilot-reviewer` |
| Site | `SANDBOX_OC_SD_PILOT_SITE` |
| Site Group | `SANDBOX_OC_SD_PILOT_GROUP` |

## 9. Availability and ordinance-policy gate

Perform these checks before submitting a decision.

### 9.1 Supported jurisdiction and active policy

1. Load `E2E-231-SESSION-001` in WebPay.
2. Select Senior Citizen where the UI requests an entitlement.
3. Observe `POST /v1/webpay/statutory-discounts/availability`.

Expected:

- supported jurisdiction and entitlement;
- active governing policy and current version;
- evidence required as `SENIOR_CITIZEN_ID`;
- statutory request control visible;
- ordinary payment remains available but does not apply a statutory basis.

### 9.2 Unsupported entitlement

Use browser developer tools to replay only the availability request with a syntactically valid but unsupported entitlement for the selected policy. Do not submit a decision.

Expected: fail-closed availability classification, no statutory submission authority, and ordinary-payment fallback remains available.

### 9.3 Missing jurisdiction

Load ticket `WEBPAY-STAT-MISSING-JURISDICTION`.

Expected: missing jurisdiction classification, statutory request hiding/denial, and ordinary-payment fallback.

### 9.4 Ambiguous jurisdiction

Load ticket `WEBPAY-STAT-AMBIGUOUS-JURISDICTION`.

Expected: ambiguous jurisdiction classification, no client-side selection of a jurisdiction, no statutory submission, and ordinary-payment fallback.

### 9.5 No applicable ordinance or unavailable policy

Load ticket `WEBPAY-STAT-NO-POLICY`.

Expected: no applicable ordinance/policy classification, statutory request unavailable, and ordinary-payment fallback.

### 9.6 Manipulated direct submission

For one negative ticket, use the browser network console to reproduce the current decision request shape sent by WebPay to `POST /v1/webpay/statutory-discounts/decisions`. Preserve the negative ticket's real session identifiers and attempt to claim the supported entitlement.

Expected: Central PMS independently reevaluates jurisdiction and policy and rejects the manipulated direct submission. No decision command may become approved or applied. Client-authored policy, Site, or authority values do not override canonical state.

### 9.7 Request hiding

Reload each negative ticket and open a second tab.

Expected: the statutory request remains hidden or disabled according to the authoritative availability result. Local/browser state must not restore it.

## 10. Submit the statutory request

Return to `E2E-231-SESSION-001`.

1. Submit the Senior Citizen statutory request through WebPay.
2. Observe `POST /v1/webpay/statutory-discounts/decisions`.
3. Record only the opaque decision command ID and correlation reference in the evidence checklist.

Expected:

- `PENDING_OPERATOR_REVIEW`/pending review behavior;
- `AWAITING_REVIEW` command/recovery posture where exposed;
- WebPay does not self-approve;
- payable basis is not yet applied;
- payment handoff does not use a discounted tariff yet.

Refresh WebPay and open a second tab. The UI must call `POST /v1/webpay/statutory-discounts/pending-lifecycle/rediscover` and/or read the canonical decision. It must not create a second semantic decision.

Repeat the original submission action with the same idempotency identity if the UI exposes retry. Expected: idempotent replay/readback, not a duplicate authoritative command.

## 11. Evidence upload and sequencing

1. Allow WebPay to call `POST /v1/webpay/statutory-discounts/evidence/bootstrap`.
2. Refresh once and confirm `GET /v1/webpay/statutory-discounts/evidence/status` rediscovers the same evidence set.
3. Select the generated synthetic PNG printed by the startup script. Never use a real identity document.
4. Observe upload-session issuance at `POST /v1/webpay/statutory-discounts/evidence/upload-sessions`.
5. Confirm the browser receives only an opaque upload-session reference, method, expiry, accepted content type, and limits. It must not receive a provider URL.
6. Upload through the `PUT` streaming route.
7. Finalize through the `/finalize` route.
8. Poll status until structural validation and malware scan complete.

Expected evidence sequencing:

- before finalization: not reviewable;
- while validation or scan is pending: not reviewable;
- after clean supported-image validation: `REVIEWABLE` and ready for review;
- no browser response contains object-storage internals;
- refresh and second-tab status readback preserve the authoritative lifecycle;
- upload/finalization replay follows the current idempotent or governed conflict contract.

If validation fails or the scanner/storage is unavailable, stop. Record only safe error and correlation references. Do not force lifecycle state through SQL.

## 12. Operator Console login and review

1. In the Operator Console browser, sign in with username `sandbox-oc-sd-pilot-reviewer` and the caller-owned synthetic password.
2. Observe `POST /v1/human-authentication/login`, then `GET /v1/human-authentication/session`.
3. Confirm the session is audience-bound to Operator Console and permissions/scopes are server-derived.
4. Open the pending queue (`GET /v1/ops/operator-console/statutory-discounts/reviews/pending`).
5. Open the matching decision detail.
6. Read evidence metadata.
7. Open the authorized preview.

Expected:

- the review actor is the authenticated Central PMS human user;
- queue/detail/evidence access is constrained by current Site and Site Group grants;
- preview is inline, `no-store`, and mediated by Central PMS;
- no raw customer identity data or storage internals are exposed;
- preview access creates privacy-safe evidence access events;
- viewing evidence does not itself approve, reject, apply payable basis, or alter replacement permission.

### Unauthorized evidence preview

In a separate unauthenticated browser profile, request the copied preview path without the Operator Console session. Then test with a reviewer session outside the selected Site/Site Group if an approved negative user fixture is available.

Expected: unauthenticated and cross-scope requests fail closed/anti-enumerate. Do not add a fixture header or browser-authored permission to make the request pass.

### Operator Console non-override

Open a negative or unavailable-policy request if one is visible through an approved scoped fixture.

Expected: Operator Console cannot override missing/ambiguous jurisdiction, unavailable ordinance, or unsupported entitlement. Approval is unavailable or the server rejects it.

## 13. Approval and rejection paths

### Approval path

1. On the positive request, choose the current approval action.
2. Submit through `POST /v1/ops/operator-console/statutory-discounts/reviews/{decisionCommandId}/decision`.
3. Record the correlation reference.

Expected: approved decision, authenticated reviewer attribution on the statutory validation/audit records, and no payable-basis application merely from preview.

### Rejection path

Rejection is destructive to the selected workflow and cannot be followed by approval. Prove it in a separately rebuilt disposable run:

1. stop and explicitly clean the first walkthrough;
2. start a fresh guarded database;
3. repeat submission/evidence/review;
4. reject with a safe synthetic reason;
5. refresh WebPay.

Expected: rejection is authoritative, attributed to the reviewer, and WebPay cannot apply payable basis. Do not approve and reject the same fixture by direct database mutation.

## 14. WebPay rediscovery and payable-basis application

After approval:

1. Return to WebPay and refresh.
2. Open a second tab with the same ticket.
3. Confirm authoritative approval rediscovery; no client-authored approval state is used.
4. Trigger the current application intent. Observe `POST /v1/webpay/statutory-discounts/decisions/{decisionCommandId}/apply-payable-basis`.
5. Record the application command reference/correlation reference.

Expected:

- approved-decision rediscovery;
- application succeeds only against the approved canonical decision and current policy authority;
- applied payable basis references a new applied tariff snapshot;
- decision moves to applied payable-basis posture;
- refresh and second tab read the same applied result;
- retry with the same idempotency identity returns the canonical result and does not create unintended duplicates;
- policy, evidence, storage, or Central PMS failure remains safe and retry-classified.

## 15. Payment handoff

Continue through the ordinary tracked local-integration mechanics after the applied tariff is visible:

1. create the payment intent through `POST /v1/webpay/payment-intents`;
2. confirm the payment attempt references the applied tariff snapshot and discounted amount;
3. continue to the local provider session;
4. verify the configured current payment-routing compatibility path;
5. do not use a live provider credential or endpoint;
6. record only safe payment attempt/provider-session references and correlation IDs.

Expected:

- one payment attempt for the idempotency identity;
- provider session uses the applied amount and current local routing;
- linkage remains traceable to the parking session, approved decision, statutory validation, payable-basis application, and applied tariff;
- retry is idempotent;
- payment handoff does not expose provider credentials or raw provider metadata.

This walkthrough does not claim fiscal issuance unless a separately authorized run observes and verifies the current fiscal linkage. Existing statutory fiscal-payload tests remain the referenced proof when fiscal issuance is excluded.

## 16. Refresh, second-tab, replay, and restart checks

At each major state (pending review, reviewable evidence, approved, applied, payment attempt):

1. refresh the active tab;
2. open a second tab from the original ticket URL;
3. compare opaque command/reference IDs;
4. repeat the safe UI action where retry is available;
5. confirm no duplicate authoritative command is created.

For restart recovery:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1 -RestartServicesOnly
```

The default stop preserves the disposable database, private dependency containers, generated synthetic evidence, logs, and state needed for restart. Expected after restart:

- Production fixture-header rejection still passes;
- the reviewer can log in through a new real human session;
- decision/evidence/application/payment state is rediscovered from Central PMS/PostgreSQL;
- object storage remains private;
- no client-local state becomes authority.

## 17. Read-only verification SQL

After the journey, run the verifier against the guarded disposable database:

```powershell
Get-Content .\scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql -Raw |
  docker exec -i exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d exitpass_webpay_local_walkthrough_statutory
```

Review every section. Expected significant results:

- reviewer has exactly the required active permission bundle;
- explicit Site and Site Group grants exist and no `GLOBAL` grant exists;
- active policy and evidence requirement resolve for the positive Site;
- one semantic decision identity reaches pending review, approval, and applied posture;
- evidence is uploaded, structurally valid, scan clean, and reviewable;
- evidence access/review events contain the human reviewer actor;
- payable-basis application references the applied tariff snapshot;
- payment attempt/provider session link to that tariff and amount;
- replay counters show no unintended duplicate authoritative commands;
- only safe audit classifications and opaque references are output;
- schema privacy checks report no evidence byte/Base64 columns.

The verifier is `BEGIN TRANSACTION READ ONLY` and ends with `ROLLBACK`. It deliberately does not return internal storage locators, checksums, checkout URLs, credentials, or raw evidence.

## 18. Evidence checklist

Retain only privacy-safe local observations until explicit cleanup:

- startup readiness result;
- Production fixture-header rejection status;
- positive and negative availability classifications;
- opaque decision command and correlation references;
- pending-review and rediscovery observations;
- opaque evidence set/item/session references;
- validation, scan, and reviewability classifications;
- preview response security headers, without bytes;
- reviewer user reference and scoped-authority proof;
- approval/rejection result and audit correlation;
- payable-basis application and applied tariff references;
- payment attempt/provider-session status and correlation;
- refresh, second-tab, replay, and restart observations;
- verifier output after checking that it contains no sensitive value.

Do not retain screenshots containing evidence pixels unless separately approved. Do not paste evidence bytes, Base64, object keys, checksums, credentials, cookies, or signed parameters into evidence notes.

## 19. Troubleshooting

| Symptom | Safe action |
|---|---|
| Startup reports a missing shared container | Prepare the current ordinary local-integration dependencies; do not edit container names blindly. |
| Database-name guard rejects the name | Use `exitpass_webpay_local_walkthrough_statutory` or a permitted suffixed disposable name. |
| Reviewer activation fails | Rebuild the disposable database; do not edit credential tables after workflow state exists. |
| Login is rejected | Verify the caller-owned password and allowed Origin; never use a fixture identity header. |
| Review queue is denied | Verify active role/permission bindings and explicit Site/Site Group grants with read-only SQL. |
| Evidence upload is denied | Verify service identity, channel scope, media type, length, and current lifecycle. Do not bypass the opaque route. |
| Evidence remains pending | Inspect privacy-safe scan attempt/event classifications and ClamAV readiness; do not force `REVIEWABLE`. |
| Preview is denied | Confirm current clean/reviewable lifecycle and reviewer scope; do not fetch from MinIO directly. |
| Application is denied | Re-read the canonical decision and policy authority; do not force payable-basis rows. |
| Payment handoff fails | Use current local routing diagnostics and correlation reference; do not switch to live credentials. |
| Restart fails | Preserve state and logs, verify recorded process/container ownership, then diagnose before cleanup. |

## 20. Safe shutdown

Stop only recorded walkthrough-owned listeners. Preserve proof state by default:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1
```

The script revalidates PID start time, executable path, and command markers. It refuses to terminate a process if ownership cannot be confirmed. It does not stop shared PostgreSQL, RabbitMQ, or mock-provider containers.

## 21. Explicit cleanup

After all observations and read-only verification are complete:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1 `
  -StopWalkthroughContainers `
  -RemoveDisposableDatabase `
  -RemoveGeneratedState
```

Cleanup is guarded by exact ownership labels, exact temporary paths, and the disposable database-name pattern. It preserves unrelated services and developer data. Do not use broad recursive deletion or `git clean`.

After cleanup, clear caller-owned environment values from the current shell:

```powershell
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_MINIO_ACCESS_KEY -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_MINIO_SECRET_KEY -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY_BASE64 -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_PAYMONGO_SECRET_KEY -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_PAYMONGO_PUBLIC_KEY -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_PAYMONGO_WEBHOOK_SECRET -ErrorAction SilentlyContinue
```

## 22. Residual risk and handoff

This package has static validation only until a later task executes it. The first authorized runtime run must validate container image compatibility, loopback Secure-cookie behavior, current local payment-provider mappings, actual scan completion, browser preview headers, process ownership checks, and full cleanup. Any runtime contract mismatch belongs to its owning implementation and must not be hidden by weakening this walkthrough.

Successful local execution still does not authorize Controlled UAT, compliance claims, production data, or production rollout.
