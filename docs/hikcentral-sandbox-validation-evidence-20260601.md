# HikCentral Sandbox Validation Evidence - 2026-06-01

This evidence record is for GitHub issue #226. It is intentionally sanitized and contains no HikCentral credentials, validation keys, raw signatures, raw request bodies, raw response bodies, or secret-bearing headers.

## Status

- Result: Not executed by Codex in this workspace.
- Reason: Required sandbox/live configuration, HikCentral AK/SK credentials, approved test door/gate resource, and physical operator confirmation were not present in the local session.
- Live HikCentral call made: No.
- Physical gate/door/barrier action observed: No.
- Audit row produced: No, because no vendor attempt was made.
- Runtime defaults changed: No.

## Inspected Implementation

- Endpoint: `POST /v1/internal/hikcentral/sandbox/validate-gate-action`.
- Harness request fields:
  - `doorIndexCode`
  - `controlType = Open`
  - `controlDirection = Exit`
  - `validationReason`
  - `requestedBy`
  - `correlationId`
  - `confirmLiveAction = true`
- Required operational headers:
  - `X-Correlation-Id`
  - `X-Service-Identity-Id`
  - `X-HikCentral-Sandbox-Validation-Key`
- Access-control config:
  - `GateIntegrations:HikCentral:SandboxValidationAccess:Enabled`
  - `GateIntegrations:HikCentral:SandboxValidationAccess:AllowedServiceIdentityIds`
  - `GateIntegrations:HikCentral:SandboxValidationAccess:RequiredApiKey`
- Live/sandbox hard gates:
  - `GateActionAdapter:Mode = HikCentralLive`
  - `GateIntegrations:HikCentral:LiveTransportEnabled = true`
  - `GateIntegrations:HikCentral:SandboxValidationEnabled = true`
- Audit verification script: `docs/sql/Verify_HikCentralSandboxValidationAudit.sql`.

## Pre-Run Checklist

Complete every item before a real sandbox/manual validation:

- HikCentral Professional test/sandbox server is available.
- AK/SK credentials are available outside Git.
- Sandbox/test `UserId` is configured outside Git.
- Test `doorIndexCode` is identified.
- Target door/gate is non-production or isolated.
- No real parker/customer traffic can be affected.
- Operator is physically present.
- Emergency stop/manual override is available.
- Safe test window is approved.
- Gate Integration Service is running in a controlled test environment.
- PostgreSQL audit table `gates.hikcentral_gate_action_audits` is deployed.
- Sandbox validation endpoint access key is configured outside Git.
- Service identity allowlist is configured outside Git.
- Evidence capture rules are understood.

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

Do not commit real values. Do not paste real values into this file, PRs, tickets, screenshots, or chat.

## Access-Control Negative Checks To Run

Run these before any positive live validation. Each rejected response must have `executed = false` and `auditId = null`.

| Check | Expected HTTP | Expected resultCode | Vendor call expected |
| --- | ---: | --- | --- |
| Missing `X-Service-Identity-Id` | 401 | `SERVICE_IDENTITY_REQUIRED` | No |
| Unauthorized `X-Service-Identity-Id` | 403 | `SERVICE_IDENTITY_NOT_ALLOWED` | No |
| Missing `X-HikCentral-Sandbox-Validation-Key` | 401 | `SANDBOX_VALIDATION_KEY_REQUIRED` | No |
| Invalid `X-HikCentral-Sandbox-Validation-Key` | 401 | `SANDBOX_VALIDATION_KEY_INVALID` | No |
| Access control disabled/misconfigured | 401 | `HIKCENTRAL_SANDBOX_ACCESS_DISABLED` | No |
| Sandbox harness disabled after valid access | 409 | `HIKCENTRAL_SANDBOX_VALIDATION_DISABLED` | No |

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

## Evidence Fields To Capture

Capture only operator-safe values:

- Date/time and approved test window.
- Environment type.
- Operator role or sanitized operator identifier.
- Sanitized test equipment description.
- Masked or approved `doorIndexCode`.
- Branch/commit.
- Request correlation ID.
- Validation attempt ID.
- Audit ID.
- Sanitized response summary:
  - `executed`
  - `succeeded`
  - `resultCode`
  - `diagnosticMessage`
  - `httpStatusCode`
  - `vendorResponseCode`
  - `vendorResponseMessage`
  - `outcomeCategory`
  - `retryable`
  - `terminalFailure`
  - `durationMs`
- Physical equipment behavior:
  - command accepted or rejected by HikCentral
  - gate/door/barrier acted or did not act
  - observed delay
  - manual intervention required or not required
- Audit verification result.
- Disable/rollback confirmation.

## Audit Verification

After a positive validation attempt, run `docs/sql/Verify_HikCentralSandboxValidationAudit.sql` with placeholders replaced locally. Confirm:

- Audit row exists.
- `request_correlation_id` matches the report/request correlation ID.
- `source_processing_id` matches `validationAttemptId`.
- `request_body_sha256` is populated and is a 64-character SHA-256 hex string.
- `request_path = /artemis/api/acs/v1/door/doControl`.
- `signed_headers_list` stores header names only.
- Response metadata is populated when returned by HikCentral.
- `outcome_category`, `retryable`, and `terminal_failure` match the report.
- Linked validation-only gate command exists.
- No raw request body, raw response body, AppSecret, raw `X-Ca-Signature`, validation key, or secret-bearing header values are stored.

## Disable And Cleanup

Immediately after validation:

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

Restart the service if configuration was loaded at startup.

## Forbidden Evidence

Do not capture or commit:

- AppSecret.
- Raw AK/SK pair.
- Raw `X-Ca-Signature`.
- `X-HikCentral-Sandbox-Validation-Key`.
- Full signed headers.
- Raw request body sent to HikCentral.
- Raw response body from HikCentral.
- Production server URL if sensitive.
- Production door/gate identifiers.
- Screenshots showing credentials, secret headers, or raw signatures.

## Completion Placeholder

Fill this section only after a real operator-run sandbox validation:

- Validation completed at: `<timestamp>`
- Environment type: `<sandbox/test>`
- Operator: `<sanitized>`
- Test equipment: `<sanitized>`
- Door/gate resource: `<masked-or-approved-id>`
- Branch/commit: `<branch-and-commit>`
- Access negative checks: `<pass/fail summary>`
- Positive validation result: `<pass/fail>`
- Physical behavior observed: `<summary>`
- Validation attempt ID: `<guid>`
- Correlation ID: `<guid>`
- Audit ID: `<guid>`
- Audit verification: `<pass/fail summary>`
- Secrets redacted: `<yes/no>`
- Live/sandbox disabled after test: `<yes/no>`
- Issues found: `<summary>`
