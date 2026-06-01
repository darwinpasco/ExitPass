# HikCentral Sandbox Validation Evidence - 2026-06-01

This evidence record covers GitHub issues #226 and #226B. It is intentionally sanitized and contains no HikCentral credentials, validation keys, raw signatures, raw request bodies, raw response bodies, or secret-bearing headers.

## Status

* Result: BLOCKED / NOT EXECUTED.
* Branch: `feature/hikcentral-sandbox-manual-execution-evidence`.
* Date/time: 2026-06-01, local workspace session.
* Live HikCentral call made: No.
* Physical gate/door/barrier action attempted: No.
* Physical gate/door/barrier action observed: No.
* Audit row produced: No, because no vendor request was attempted.
* Runtime defaults changed: No.
* Code changes made: No.

## Blocker

The controlled manual validation could not be executed because the required external sandbox configuration and safety confirmations were not present in the local session. Per the hard-stop rules, no positive validation request was sent.

Missing external prerequisites:

* HikCentral Professional sandbox/test server URL was not supplied outside Git.
* HikCentral sandbox AppKey was not supplied outside Git.
* HikCentral sandbox AppSecret was not supplied outside Git.
* HikCentral sandbox UserId/operator identity was not supplied outside Git.
* Approved non-production `doorIndexCode` was not supplied.
* Sandbox validation access key was not supplied outside Git.
* Allowed service identity was not supplied outside Git.
* Operator physically present confirmation was not supplied.
* Emergency stop/manual override ready confirmation was not supplied.
* Safe test window approval confirmation was not supplied.
* Non-production/isolated target confirmation was not supplied.

Because hard-stop prerequisites were missing:

* No Gate Integration Service live sandbox run was started.
* No access-control negative checks were executed.
* No positive HikCentral validation request was sent.
* No HikCentral live call was made.
* No physical gate/door/barrier action occurred.
* No audit row was expected or created.

## Pre-Run Inspection Completed

Inspected and confirmed:

* Evidence template target path: this file.
* Runbook: `docs/hikcentral-sandbox-validation-runbook.md`.
* Harness documentation: `docs/hikcentral-sandbox-validation-harness.md`.
* Environment template: `docs/hikcentral-sandbox-validation.env.example`.
* Bruno/manual pack: `bruno/hikcentral-sandbox-validation/*`.
* Endpoint: `POST /v1/internal/hikcentral/sandbox/validate-gate-action`.
* Audit verification script: `docs/sql/Verify_HikCentralSandboxValidationAudit.sql`.

Endpoint access control:

* `X-Correlation-Id`
* `X-Service-Identity-Id`
* `X-HikCentral-Sandbox-Validation-Key`
* service identity allowlist
* configured validation key

Harness request shape:

* `doorIndexCode`
* `controlType = Open`
* `controlDirection = Exit`
* `validationReason`
* `requestedBy`
* `correlationId`
* `confirmLiveAction = true`

Live/sandbox hard gates:

* `GateActionAdapter:Mode = HikCentralLive`
* `GateIntegrations:HikCentral:LiveTransportEnabled = true`
* `GateIntegrations:HikCentral:SandboxValidationEnabled = true`

Access-control config:

* `GateIntegrations:HikCentral:SandboxValidationAccess:Enabled`
* `GateIntegrations:HikCentral:SandboxValidationAccess:AllowedServiceIdentityIds`
* `GateIntegrations:HikCentral:SandboxValidationAccess:RequiredApiKey`

## Safe Defaults Confirmed

Committed `appsettings.json` remains safe:

* `GateActionAdapter:Mode = NoOp`.
* `GateIntegrations:HikCentral:TransportMode = Fake`.
* `GateIntegrations:HikCentral:LiveTransportEnabled = false`.
* `GateIntegrations:HikCentral:SandboxValidationEnabled = false`.
* `GateIntegrations:HikCentral:SandboxValidationAccess:Enabled = false`.
* `GateIntegrations:HikCentral:SandboxValidationAccess:AllowedServiceIdentityIds = []`.
* `GateIntegrations:HikCentral:SandboxValidationAccess:RequiredApiKey = ""`.
* No HikCentral AppKey, AppSecret, or BaseUrl is committed in appsettings.

## Pre-Run Checklist

Complete every item before a real sandbox/manual validation:

* HikCentral Professional test/sandbox server is available.
* AK/SK credentials are available outside Git.
* Sandbox/test `UserId` is configured outside Git.
* Test `doorIndexCode` is identified.
* Target door/gate is non-production or isolated.
* No real parker/customer traffic can be affected.
* Operator is physically present.
* Emergency stop/manual override is available.
* Safe test window is approved.
* Gate Integration Service is running in a controlled test environment.
* PostgreSQL audit table `gates.hikcentral_gate_action_audits` is deployed.
* Sandbox validation endpoint access key is configured outside Git.
* Service identity allowlist is configured outside Git.
* Evidence capture rules are understood.

## Sanitized Configuration Required

Set these values through environment variables, user secrets, or a secure local secret store only:

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

## Access-Control Negative Checks To Run

Not run in this session because the service was not started with external sandbox configuration. These remain the required first execution checks.

Each rejected response must have `executed = false` and `auditId = null`.

| Check                                         | Expected HTTP | Expected resultCode                      | Vendor call expected |
| --------------------------------------------- | ------------: | ---------------------------------------- | -------------------- |
| Missing `X-Service-Identity-Id`               |           401 | `SERVICE_IDENTITY_REQUIRED`              | No                   |
| Unauthorized `X-Service-Identity-Id`          |           403 | `SERVICE_IDENTITY_NOT_ALLOWED`           | No                   |
| Missing `X-HikCentral-Sandbox-Validation-Key` |           401 | `SANDBOX_VALIDATION_KEY_REQUIRED`        | No                   |
| Invalid `X-HikCentral-Sandbox-Validation-Key` |           401 | `SANDBOX_VALIDATION_KEY_INVALID`         | No                   |
| Access control disabled/misconfigured         |           401 | `HIKCENTRAL_SANDBOX_ACCESS_DISABLED`     | No                   |
| Sandbox harness disabled after valid access   |           409 | `HIKCENTRAL_SANDBOX_VALIDATION_DISABLED` | No                   |

## Positive Validation

Not run.

No request was sent to:

```http
POST /v1/internal/hikcentral/sandbox/validate-gate-action
```

No HikCentral vendor response was received, and no operator-safe validation report was produced.

## Positive Validation Request Template

Use only after all pre-run checks are complete and the operator confirms the target equipment is safe.

```http
POST /v1/internal/hikcentral/sandbox/validate-gate-action
X-Correlation-Id: <new-guid>
X-Service-Identity-Id: <authorized-service-identity-guid>
X-HikCentral-Sandbox-Validation-Key: <sandbox-validation-access-key>
Content-Type: application/json
```

```json
{
  "doorIndexCode": "<approved-sandbox-door-index-code>",
  "controlType": "Open",
  "controlDirection": "Exit",
  "validationReason": "Approved controlled HikCentral sandbox validation",
  "requestedBy": "<operator-id-or-email>",
  "correlationId": "<same-or-recorded-guid>",
  "confirmLiveAction": true
}
```

## Physical Equipment Behavior

No physical equipment behavior was observed because no HikCentral request was made.

For a future completed validation, record:

* whether HikCentral accepted or rejected the command
* whether the configured test gate/door/barrier acted
* observed delay
* whether manual intervention was required
* whether emergency/manual override was used
* whether observed behavior matched the intended test

## Evidence Fields To Capture

Capture only operator-safe values:

* Date/time and approved test window.
* Environment type.
* Operator role or sanitized operator identifier.
* Sanitized test equipment description.
* Masked or approved `doorIndexCode`.
* Branch/commit.
* Request correlation ID.
* Validation attempt ID.
* Audit ID.
* Sanitized response summary:

  * `executed`
  * `succeeded`
  * `resultCode`
  * `diagnosticMessage`
  * `httpStatusCode`
  * `vendorResponseCode`
  * `vendorResponseMessage`
  * `outcomeCategory`
  * `retryable`
  * `terminalFailure`
  * `durationMs`
* Physical equipment behavior:

  * command accepted or rejected by HikCentral
  * gate/door/barrier acted or did not act
  * observed delay
  * manual intervention required or not required
* Audit verification result.
* Disable/rollback confirmation.

## Audit Verification

Not run. No audit row was expected because no vendor request was attempted.

After a positive validation attempt, run `docs/sql/Verify_HikCentralSandboxValidationAudit.sql` with placeholders replaced locally. Confirm:

* Audit row exists.
* `audit_id` matches the validation report.
* `request_correlation_id` matches the request/report correlation ID.
* `source_processing_id` matches `validationAttemptId`.
* `request_body_sha256` is populated and is a 64-character SHA-256 hex string.
* `request_path = /artemis/api/acs/v1/door/doControl`.
* `signed_headers_list` stores header names only.
* Response metadata is populated when returned by HikCentral.
* `outcome_category`, `retryable`, and `terminal_failure` match the report.
* Linked validation-only gate command exists.
* No raw request body, raw response body, AppSecret, raw `X-Ca-Signature`, validation key, or secret-bearing header values are stored.

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

* AppSecret.
* Raw AK/SK pair.
* Raw `X-Ca-Signature`.
* Real `X-HikCentral-Sandbox-Validation-Key`.
* Full signed headers.
* Raw request body sent to HikCentral.
* Raw response body from HikCentral.
* Production server URL.
* Production door/gate identifiers.
* Screenshots showing credentials, secret headers, or raw signatures.

## Forbidden Evidence

Do not capture or commit:

* AppSecret.
* Raw AK/SK pair.
* Raw `X-Ca-Signature`.
* `X-HikCentral-Sandbox-Validation-Key`.
* Full signed headers.
* Raw request body sent to HikCentral.
* Raw response body from HikCentral.
* Production server URL if sensitive.
* Production door/gate identifiers.
* Screenshots showing credentials, secret headers, or raw signatures.

## Completion Placeholder

Fill this section only after a real operator-run sandbox validation:

* Validation completed at: `<timestamp>`
* Environment type: `<sandbox/test>`
* Operator: `<sanitized>`
* Test equipment: `<sanitized>`
* Door/gate resource: `<masked-or-approved-id>`
* Branch/commit: `<branch-and-commit>`
* Access negative checks: `<pass/fail summary>`
* Positive validation result: `<pass/fail>`
* Physical behavior observed: `<summary>`
* Validation attempt ID: `<guid>`
* Correlation ID: `<guid>`
* Audit ID: `<guid>`
* Audit verification: `<pass/fail summary>`
* Secrets redacted: `<yes/no>`
* Live/sandbox disabled after test: `<yes/no>`
* Issues found: `<summary>`

## Recommended Next Action

Run the controlled validation only after all hard-stop prerequisites are available outside Git and the operator confirms the target equipment is safe. Execute one positive validation request, verify the audit row, disable live/sandbox config immediately, and update this evidence file with sanitized PASS/FAIL details.
