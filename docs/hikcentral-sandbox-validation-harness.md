# HikCentral Sandbox Validation Harness

This harness is for controlled manual validation of HikCentral Professional OpenAPI door control from the Gate Integration Service. It is disabled by default and must not be enabled in production/default runtime configuration.

## What It Does

- Executes `POST /artemis/api/acs/v1/door/doControl` through the existing HikCentral signed request, live transport, adapter, classifier, and audit path.
- Requires an explicit operator request with a door index code, safe operation, reason, requester, correlation ID, and live-action confirmation.
- Writes a HikCentral audit row for every actual vendor attempt.
- Returns an operator-safe report with status, vendor response metadata, classification, retry/terminal flags, timing, correlation ID, and audit ID.

## What It Does Not Do

- It does not enable HikCentral as the default runtime adapter.
- It does not call HikCentral unless both live transport and sandbox validation are explicitly enabled.
- It does not store or print `AppSecret`, raw signatures, secret-bearing header values, raw request bodies, or raw response bodies.
- It does not mutate Central PMS payment, tariff, statutory discount, exit authorization, or normal gate consume behavior.
- It does not call payment providers, AUB, coupon logic, or reconciliation logic.

## Required Configuration

Keep committed configuration disabled:

```json
{
  "GateActionAdapter": {
    "Mode": "NoOp"
  },
  "GateIntegrations": {
    "HikCentral": {
      "LiveTransportEnabled": false,
      "SandboxValidationEnabled": false
    }
  }
}
```

For a controlled sandbox run, set these values through environment variables, user secrets, or another uncommitted local configuration source:

```powershell
$env:GateActionAdapter__Mode = "HikCentralLive"
$env:GateIntegrations__HikCentral__LiveTransportEnabled = "true"
$env:GateIntegrations__HikCentral__SandboxValidationEnabled = "true"
$env:GateIntegrations__HikCentral__BaseUrl = "https://sandbox-hikcentral.example"
$env:GateIntegrations__HikCentral__AppKey = "<sandbox-app-key>"
$env:GateIntegrations__HikCentral__AppSecret = "<sandbox-app-secret>"
$env:GateIntegrations__HikCentral__UserId = "exitpass-gate-integration"
$env:GateIntegrations__HikCentral__RequestTimeoutSeconds = "10"
```

Do not commit real `AppKey`, `AppSecret`, or sandbox endpoint values.

## Request

Endpoint:

```http
POST /v1/internal/hikcentral/sandbox/validate-gate-action
```

Example body:

```json
{
  "doorIndexCode": "sandbox-door-01",
  "controlType": "Open",
  "controlDirection": "Exit",
  "validationReason": "Controlled HikCentral sandbox validation",
  "requestedBy": "operator@example.test",
  "correlationId": "00000000-0000-0000-0000-000000000001",
  "confirmLiveAction": true
}
```

The harness currently allows only `controlType = Open` and `controlDirection = Exit`. `confirmLiveAction` must be `true`. HikCentral does not document a dry-run mode for this door-control operation in the inspected V3.1.0 guide, so the harness does not fake a dry run as a live call.

## Report

The response includes:

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

The report intentionally excludes secrets, raw signature values, signed header values, raw request bodies, and raw response bodies.

## Audit Lookup

Use the returned `auditId` to inspect safe metadata:

```sql
SELECT
    audit_id,
    gate_command_id,
    request_correlation_id,
    door_index_code,
    request_method,
    request_path,
    request_body_sha256,
    signed_headers_list,
    http_status_code,
    vendor_response_code,
    vendor_response_message,
    outcome_category,
    retryable,
    terminal_failure,
    duration_ms,
    timeout_occurred,
    vendor_unavailable,
    created_at
FROM gates.hikcentral_gate_action_audits
WHERE audit_id = '<audit-id>';
```

The audit row links to a validation-only command row in `gates.gate_commands` with `command_type = 'HikCentralSandboxValidation'`.

## Safety Warning

When enabled with real sandbox HikCentral credentials and a real HikCentral endpoint, this harness can trigger a real HikCentral door-control action for the supplied door index code. Use only with sandbox/test doors and explicit operator authorization.
