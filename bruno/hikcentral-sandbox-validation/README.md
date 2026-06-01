# HikCentral Sandbox Validation Manual Pack

This Bruno collection supports the controlled HikCentral sandbox validation runbook. It contains placeholders only and must not contain HikCentral credentials.

Read the runbook first:

```text
docs/hikcentral-sandbox-validation-runbook.md
```

## Scope

This collection targets:

```http
POST /v1/internal/hikcentral/sandbox/validate-gate-action
```

It does not configure HikCentral credentials. The service must be configured separately through environment variables, user secrets, or an approved local secret store.

## Required Service Configuration

For an actual sandbox validation attempt, the Gate Integration Service process must be started with:

- `GateActionAdapter:Mode = HikCentralLive`
- `GateIntegrations:HikCentral:TransportMode = Live`
- `GateIntegrations:HikCentral:LiveTransportEnabled = true`
- `GateIntegrations:HikCentral:SandboxValidationEnabled = true`
- `GateIntegrations:HikCentral:BaseUrl = <sandbox HikCentral URL>`
- `GateIntegrations:HikCentral:AppKey = <sandbox app key>`
- `GateIntegrations:HikCentral:AppSecret = <sandbox app secret>`
- `GateIntegrations:HikCentral:UserId = exitpass-gate-integration`
- `GateIntegrations:HikCentral:RequestTimeoutSeconds = 10`

Do not put these secrets in Bruno environment files.

## Bruno Environment

Use `environments/local.bru`.

Variables:

- `gateIntegrationBaseUrl`: Gate Integration Service base URL.
- `sandboxDoorIndexCode`: approved sandbox/test door only.
- `sandboxRequestedBy`: operator ID or email.
- `sandboxValidationReason`: approved validation reason.

The default `sandboxDoorIndexCode` is a placeholder. Replace it only in your local Bruno environment and do not commit real door identifiers if they are sensitive.

## Requests

| Request | Purpose | Expected result |
| --- | --- | --- |
| `01 Disabled harness rejected` | Proves the committed/default disabled posture rejects without vendor execution. | HTTP `409`, `executed = false`, `auditId = null` |
| `02 Missing confirmation rejected` | Proves explicit operator confirmation is required before config/vendor execution. | HTTP `400`, `executed = false`, `auditId = null` |
| `03 Sandbox validation request template` | Template for one approved sandbox validation attempt. | HTTP `200` only when the service is explicitly configured for live sandbox validation |

## Evidence To Capture

Capture only:

- Sanitized request summary.
- Response/report JSON.
- `validationAttemptId`.
- `correlationId`.
- `auditId`.
- Read-only audit verification result from `docs/sql/Verify_HikCentralSandboxValidationAudit.sql`.
- Test operator and approved test window.

Do not capture:

- `AppSecret`.
- Raw `X-Ca-Signature`.
- Raw `X-Ca-Key` if treated as sensitive.
- Raw signed headers.
- Raw request body sent to HikCentral.
- Raw response body from HikCentral.

## Disable After Test

After any live sandbox attempt, disable the harness:

```powershell
$env:GateIntegrations__HikCentral__SandboxValidationEnabled = "false"
$env:GateIntegrations__HikCentral__LiveTransportEnabled = "false"
$env:GateActionAdapter__Mode = "NoOp"
Remove-Item Env:\GateIntegrations__HikCentral__AppSecret -ErrorAction SilentlyContinue
Remove-Item Env:\GateIntegrations__HikCentral__AppKey -ErrorAction SilentlyContinue
Remove-Item Env:\GateIntegrations__HikCentral__BaseUrl -ErrorAction SilentlyContinue
```

Restart the service if configuration was read at startup.
