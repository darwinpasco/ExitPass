# HikCentral Sandbox Validation Evidence - 2026-06-01

This evidence record covers GitHub issues #226 and #226B. It is intentionally sanitized and contains no HikCentral credentials, validation keys, raw signatures, raw request bodies, raw response bodies, or secret-bearing headers.

## Status

- Result: BLOCKED / NOT EXECUTED.
- Branch: `feature/hikcentral-sandbox-manual-execution-evidence`.
- Date/time: 2026-06-01, local workspace session.
- Live HikCentral call made: No.
- Physical gate/door/barrier action attempted: No.
- Audit row produced: No, because no vendor request was attempted.
- Runtime defaults changed: No.
- Code changes made: No.

## Blocker

The controlled manual validation could not be executed because the required external sandbox configuration and safety confirmations were not present in the local session. Per the hard-stop rules, no positive validation request was sent.

Missing external prerequisites:

- HikCentral Professional sandbox/test server URL was not supplied outside Git.
- HikCentral sandbox AppKey was not supplied outside Git.
- HikCentral sandbox AppSecret was not supplied outside Git.
- HikCentral sandbox UserId/operator identity was not supplied outside Git.
- Approved non-production `doorIndexCode` was not supplied.
- Sandbox validation access key was not supplied outside Git.
- Allowed service identity was not supplied outside Git.
- Operator physically present confirmation was not supplied.
- Emergency stop/manual override ready confirmation was not supplied.
- Safe test window approval confirmation was not supplied.
- Non-production/isolated target confirmation was not supplied.

## Pre-Run Inspection Completed

Inspected and confirmed:

- Evidence template target path: this file.
- Runbook: `docs/hikcentral-sandbox-validation-runbook.md`.
- Harness documentation: `docs/hikcentral-sandbox-validation-harness.md`.
- Environment template: `docs/hikcentral-sandbox-validation.env.example`.
- Bruno/manual pack: `bruno/hikcentral-sandbox-validation/*`.
- Endpoint: `POST /v1/internal/hikcentral/sandbox/validate-gate-action`.
- Endpoint access control:
  - `X-Correlation-Id`
  - `X-Service-Identity-Id`
  - `X-HikCentral-Sandbox-Validation-Key`
  - service identity allowlist
  - configured validation key
- Harness request shape:
  - `doorIndexCode`
  - `controlType = Open`
  - `controlDirection = Exit`
  - `validationReason`
  - `requestedBy`
  - `correlationId`
  - `confirmLiveAction = true`
- Live/sandbox hard gates:
  - `GateActionAdapter:Mode = HikCentralLive`
  - `GateIntegrations:HikCentral:LiveTransportEnabled = true`
  - `GateIntegrations:HikCentral:SandboxValidationEnabled = true`
- Audit verification SQL: `docs/sql/Verify_HikCentralSandboxValidationAudit.sql`.

## Safe Defaults Confirmed

Committed `appsettings.json` remains safe:

- `GateActionAdapter:Mode = NoOp`.
- `GateIntegrations:HikCentral:TransportMode = Fake`.
- `GateIntegrations:HikCentral:LiveTransportEnabled = false`.
- `GateIntegrations:HikCentral:SandboxValidationEnabled = false`.
- `GateIntegrations:HikCentral:SandboxValidationAccess:Enabled = false`.
- `GateIntegrations:HikCentral:SandboxValidationAccess:AllowedServiceIdentityIds = []`.
- `GateIntegrations:HikCentral:SandboxValidationAccess:RequiredApiKey = ""`.
- No HikCentral AppKey, AppSecret, or BaseUrl is committed in appsettings.

## Required External Configuration For Future Execution

Supply values only through environment variables, user secrets, or a secure local secret store:

```text
GateActionAdapter__Mode=HikCentralLive
GateIntegrations__HikCentral__TransportMode=Live
GateIntegrations__HikCentral__LiveTransportEnabled=true
GateIntegrations__HikCentral__SandboxValidationEnabled=true
GateIntegrations__HikCentral__SandboxValidationAccess__Enabled=true
GateIntegrations__HikCentral__SandboxValidationAccess__AllowedServiceIdentityIds__0=<authorized-service-identity-guid>
GateIntegrations__HikCentral__SandboxValidationAccess__RequiredApiKey=<sandbox-validation-access-key>
GateIntegrations__HikCentral__BaseUrl=https://<hikcentral-sandbox-host>
GateIntegrations__HikCentral__AppKey=<sandbox-app-key>
GateIntegrations__HikCentral__AppSecret=<sandbox-app-secret>
GateIntegrations__HikCentral__UserId=<sandbox-user-id>
GateIntegrations__HikCentral__RequestTimeoutSeconds=10
```

Manual request placeholders also need to be supplied outside Git:

```text
HIKCENTRAL_SANDBOX_DOOR_INDEX_CODE=<approved-sandbox-door-index-code>
HIKCENTRAL_SANDBOX_REQUESTED_BY=<operator-id-or-email>
HIKCENTRAL_SANDBOX_VALIDATION_REASON=<approved-validation-reason>
HIKCENTRAL_SANDBOX_SERVICE_IDENTITY_ID=<authorized-service-identity-guid>
HIKCENTRAL_SANDBOX_VALIDATION_ACCESS_KEY=<sandbox-validation-access-key>
HIKCENTRAL_SANDBOX_OPERATOR_PRESENT=true
HIKCENTRAL_SANDBOX_EMERGENCY_OVERRIDE_READY=true
HIKCENTRAL_SANDBOX_TEST_WINDOW_APPROVED=true
HIKCENTRAL_SANDBOX_NON_PRODUCTION_TARGET_CONFIRMED=true
```

Do not commit real values. Do not paste real values into this file, PRs, tickets, screenshots, logs, or chat.

## Access-Control Negative Checks

Not run in this session because the service was not started with external sandbox configuration. These remain the required first execution checks:

| Check | Expected HTTP | Expected resultCode | Vendor call expected |
| --- | ---: | --- | --- |
| Missing `X-Service-Identity-Id` | 401 | `SERVICE_IDENTITY_REQUIRED` | No |
| Unauthorized `X-Service-Identity-Id` | 403 | `SERVICE_IDENTITY_NOT_ALLOWED` | No |
| Missing `X-HikCentral-Sandbox-Validation-Key` | 401 | `SANDBOX_VALIDATION_KEY_REQUIRED` | No |
| Invalid `X-HikCentral-Sandbox-Validation-Key` | 401 | `SANDBOX_VALIDATION_KEY_INVALID` | No |
| Access control disabled/misconfigured | 401 | `HIKCENTRAL_SANDBOX_ACCESS_DISABLED` | No |
| Sandbox harness disabled after valid access | 409 | `HIKCENTRAL_SANDBOX_VALIDATION_DISABLED` | No |

Rejected access-control requests should have `executed = false` and `auditId = null`.

## Positive Validation

Not run.

No request was sent to:

```http
POST /v1/internal/hikcentral/sandbox/validate-gate-action
```

No HikCentral vendor response was received, and no operator-safe validation report was produced.

## Physical Equipment Behavior

No physical equipment behavior was observed because no HikCentral request was made.

## Audit Verification

Not run. No audit row was expected because no vendor request was attempted.

For a future positive validation, use `docs/sql/Verify_HikCentralSandboxValidationAudit.sql` and confirm:

- Audit row exists.
- `audit_id` matches the validation report.
- `request_correlation_id` matches the request/report correlation ID.
- `source_processing_id` matches `validationAttemptId`.
- `request_body_sha256` is populated and is a 64-character SHA-256 hex string.
- `request_path = /artemis/api/acs/v1/door/doControl`.
- `signed_headers_list` stores header names only.
- Response metadata is populated when returned by HikCentral.
- `outcome_category`, `retryable`, and `terminal_failure` match the report.
- Linked validation-only gate command exists.
- No raw request body, raw response body, AppSecret, raw `X-Ca-Signature`, validation key, or secret-bearing header values are stored.

## Disable And Cleanup

No live/sandbox configuration was enabled in this session. No cleanup of live runtime state was required.

Future executions must disable immediately after validation:

```powershell
$env:GateIntegrations__HikCentral__SandboxValidationEnabled = "false"
$env:GateIntegrations__HikCentral__LiveTransportEnabled = "false"
$env:GateIntegrations__HikCentral__SandboxValidationAccess__Enabled = "false"
$env:GateActionAdapter__Mode = "NoOp"
Remove-Item Env:\GateIntegrations__HikCentral__SandboxValidationAccess__RequiredApiKey -ErrorAction SilentlyContinue
Remove-Item Env:\GateIntegrations__HikCentral__AppSecret -ErrorAction SilentlyContinue
Remove-Item Env:\GateIntegrations__HikCentral__AppKey -ErrorAction SilentlyContinue
Remove-Item Env:\GateIntegrations__HikCentral__BaseUrl -ErrorAction SilentlyContinue
```

Restart or stop the service if configuration was loaded at startup.

## Secret Redaction Confirmation

This file contains placeholders only. It does not contain:

- AppSecret.
- Raw AK/SK pair.
- Raw `X-Ca-Signature`.
- Real `X-HikCentral-Sandbox-Validation-Key`.
- Full signed headers.
- Raw request body sent to HikCentral.
- Raw response body from HikCentral.
- Production server URL.
- Production door/gate identifiers.
- Screenshots showing credentials, secret headers, or raw signatures.

## Recommended Next Action

Run the controlled validation only after all hard-stop prerequisites are available outside Git and the operator confirms the target equipment is safe. Execute one positive validation request, verify the audit row, disable live/sandbox config immediately, and update this evidence file with sanitized PASS/FAIL details.
