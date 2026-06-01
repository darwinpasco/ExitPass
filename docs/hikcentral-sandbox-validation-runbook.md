# HikCentral Sandbox Validation Runbook

This runbook describes the controlled manual validation workflow for the HikCentral sandbox validation harness added in #223.

The harness can trigger a real HikCentral door-control action when explicitly enabled. Use it only against an approved sandbox/test HikCentral environment and a safe test door/gate.

## Purpose And Scope

Use this workflow to validate:

- HikCentral AK/SK request signing.
- Live HTTP transport behavior.
- `POST /artemis/api/acs/v1/door/doControl` request shape.
- HikCentral response parsing.
- Result classification.
- HikCentral request/response audit capture.

This workflow does not validate production rollout, does not enable production live behavior, and does not change the default Gate Integration Service adapter. Runtime default remains `NoOp`.

## Preconditions

- #223 controlled sandbox validation harness is deployed.
- Gate Integration Service is running in a controlled local/sandbox environment.
- The HikCentral sandbox/test environment is available.
- A safe test `doorIndexCode` has been selected and approved by site operations.
- Operator approval and an approved test window are recorded outside the service.
- `gates.hikcentral_gate_action_audits` has been deployed and validated.
- No production gate, production door, or live customer lane is targeted.

Do not continue if any precondition is missing.

## Required Configuration

Committed defaults must stay disabled:

```json
{
  "GateActionAdapter": {
    "Mode": "NoOp"
  },
  "GateIntegrations": {
    "HikCentral": {
      "TransportMode": "Fake",
      "LiveTransportEnabled": false,
      "SandboxValidationEnabled": false
    }
  }
}
```

For one controlled sandbox run, provide these values through environment variables, user secrets, or a secure uncommitted local configuration source:

```powershell
$env:GateActionAdapter__Mode = "HikCentralLive"
$env:GateIntegrations__HikCentral__TransportMode = "Live"
$env:GateIntegrations__HikCentral__LiveTransportEnabled = "true"
$env:GateIntegrations__HikCentral__SandboxValidationEnabled = "true"
$env:GateIntegrations__HikCentral__BaseUrl = "https://<hikcentral-sandbox-host>"
$env:GateIntegrations__HikCentral__AppKey = "<sandbox-app-key>"
$env:GateIntegrations__HikCentral__AppSecret = "<sandbox-app-secret>"
$env:GateIntegrations__HikCentral__UserId = "exitpass-gate-integration"
$env:GateIntegrations__HikCentral__RequestTimeoutSeconds = "10"
```

Do not commit these values. Do not paste real values into tickets, PRs, screenshots, Bruno files, logs, or chat.

## Secret Handling

- Store `AppKey` and `AppSecret` only in the local process environment, user secrets, or an approved secret store.
- Do not capture raw `X-Ca-Signature`.
- Do not capture raw secret-bearing headers.
- Do not capture full request or response bodies.
- Capture only sanitized report fields and safe audit metadata.
- Before sharing evidence, search it for `AppSecret`, app key, `X-Ca-Signature`, `X-Ca-Key`, and any raw header block.

## Safe Request Fields

Endpoint:

```http
POST /v1/internal/hikcentral/sandbox/validate-gate-action
```

Request body:

```json
{
  "doorIndexCode": "<approved-sandbox-door-index-code>",
  "controlType": "Open",
  "controlDirection": "Exit",
  "validationReason": "Approved controlled HikCentral sandbox validation",
  "requestedBy": "<operator-id-or-email>",
  "correlationId": "<new-guid>",
  "confirmLiveAction": true
}
```

The #223 harness only allows `controlType = Open` and `controlDirection = Exit`. HikCentral V3.1.0 does not document a dry-run mode for this door-control endpoint, so do not treat this as a dry run.

## Manual Execution Steps

1. Confirm the target is a sandbox/test HikCentral environment.
2. Confirm the selected `doorIndexCode` maps to a safe test door/gate.
3. Confirm the approved test window and operator approval.
4. Start Gate Integration Service with the required explicit configuration.
5. Confirm these effective settings before sending the request:
   - `GateActionAdapter:Mode = HikCentralLive`
   - `GateIntegrations:HikCentral:LiveTransportEnabled = true`
   - `GateIntegrations:HikCentral:SandboxValidationEnabled = true`
   - `GateIntegrations:HikCentral:BaseUrl` points to sandbox/test only
6. Send exactly one validation request.
7. Record the returned `validationAttemptId`, `correlationId`, `auditId`, timestamp, outcome, and vendor response code/message.
8. Run the read-only audit verification SQL.
9. Disable the live/sandbox flags immediately after the test.
10. Remove local secrets from the shell/session.

## Expected Successful Response

Successful reports should have:

- `executed = true`
- `succeeded = true`
- `resultCode = HIKCENTRAL_GATE_ACTION_SUCCEEDED`
- `outcomeCategory = Succeeded`
- `retryable = false`
- `terminalFailure = false`
- `auditId` populated
- `durationMs >= 0`
- `vendorResponseCode` and `vendorResponseMessage` populated from HikCentral when available

The report includes:

- `validationAttemptId`
- `correlationId`
- `timestampUtc`
- `doorIndexCode`
- `controlType`
- `controlDirection`
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
- `auditId`
- `durationMs`

## Expected Failure Responses

The harness returns deterministic operator-safe reports for common failure modes:

| Condition | Expected report |
| --- | --- |
| `SandboxValidationEnabled = false` | `executed = false`, `resultCode = HIKCENTRAL_SANDBOX_VALIDATION_DISABLED` |
| `GateActionAdapter:Mode != HikCentralLive` | `executed = false`, `resultCode = HIKCENTRAL_SANDBOX_VALIDATION_REQUIRES_LIVE_MODE` |
| Missing/invalid live options | `executed = false`, `resultCode = HIKCENTRAL_SANDBOX_VALIDATION_CONFIG_INVALID` |
| `confirmLiveAction = false` | `executed = false`, `resultCode = HIKCENTRAL_SANDBOX_VALIDATION_REQUEST_INVALID` |
| Unauthorized/signature failure | `executed = true`, `outcomeCategory = Unauthorized`, `terminalFailure = true` |
| Invalid door/resource | `executed = true`, `outcomeCategory = InvalidRequest`, `terminalFailure = true` |
| Timeout | `executed = true`, `outcomeCategory = Timeout`, `retryable = true` |
| Vendor/network unavailable | `executed = true`, `outcomeCategory = VendorUnavailable`, `retryable = true` |

Rejected requests do not write HikCentral vendor audit rows because no vendor request was made.

## Audit Verification

Use [Verify_HikCentralSandboxValidationAudit.sql](sql/Verify_HikCentralSandboxValidationAudit.sql) after a validation attempt.

Minimum checks:

- `audit_id` matches the report `auditId`.
- `request_correlation_id` matches the report/request `correlationId`.
- `source_processing_id` matches `validationAttemptId`.
- `request_body_sha256` is a 64-character lowercase SHA-256 hash.
- `request_path = /artemis/api/acs/v1/door/doControl`.
- `signed_headers_list` contains header names only.
- `outcome_category`, `retryable`, and `terminal_failure` match the report.
- The linked `gates.gate_commands` row has `command_type = HikCentralSandboxValidation`.
- No raw request body, raw response body, app secret, raw secret, or signature columns exist in the audit table.

## Evidence Capture Checklist

Capture:

- Test date/time and approved test window.
- Operator/requester identifier.
- Environment name, excluding secrets.
- Safe target summary: sandbox/test HikCentral, test `doorIndexCode`, and site/gate note.
- Sanitized request summary: endpoint, `doorIndexCode`, `controlType`, `controlDirection`, `correlationId`, `validationReason`, `requestedBy`, `confirmLiveAction`.
- Full operator-safe response/report JSON.
- `validationAttemptId`.
- `correlationId`.
- `auditId`.
- Read-only audit verification result.
- Disable/rollback confirmation.

Do not capture:

- `AppSecret`.
- Raw `X-Ca-Signature`.
- Raw `X-Ca-Key` if the key is treated as sensitive in the target environment.
- Full signed headers.
- Raw request body sent to HikCentral.
- Raw response body from HikCentral.
- Any production door/gate identifiers.

## Disable And Rollback

Immediately after the test:

```powershell
$env:GateIntegrations__HikCentral__SandboxValidationEnabled = "false"
$env:GateIntegrations__HikCentral__LiveTransportEnabled = "false"
$env:GateActionAdapter__Mode = "NoOp"
Remove-Item Env:\\GateIntegrations__HikCentral__AppSecret -ErrorAction SilentlyContinue
Remove-Item Env:\\GateIntegrations__HikCentral__AppKey -ErrorAction SilentlyContinue
Remove-Item Env:\\GateIntegrations__HikCentral__BaseUrl -ErrorAction SilentlyContinue
```

Restart the service if configuration was loaded at process startup.

## Risks And Controls

Risk: The harness can trigger a real HikCentral door/gate action.

Controls:

- Use only sandbox/test HikCentral.
- Use only an approved test `doorIndexCode`.
- Coordinate with parking/site operations.
- Run during an approved test window.
- Send one request at a time.
- Verify the audit row before any repeat attempt.
- Disable the harness immediately after validation.

## References

- Harness overview: [hikcentral-sandbox-validation-harness.md](hikcentral-sandbox-validation-harness.md)
- Audit verification SQL: [Verify_HikCentralSandboxValidationAudit.sql](sql/Verify_HikCentralSandboxValidationAudit.sql)
- Environment template: [hikcentral-sandbox-validation.env.example](hikcentral-sandbox-validation.env.example)
- Bruno manual pack: [../bruno/hikcentral-sandbox-validation/README.md](../bruno/hikcentral-sandbox-validation/README.md)
