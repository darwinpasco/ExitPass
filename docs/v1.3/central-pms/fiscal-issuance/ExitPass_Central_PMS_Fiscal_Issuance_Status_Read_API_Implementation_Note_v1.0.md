# ExitPass Central PMS Fiscal Issuance Status Read API Implementation Note v1.0

## Scope

This slice adds a small read-only Central PMS API for fiscal issuance status already recorded in Central PMS.

It does not change fiscal issuance mutation behavior and does not call POS Server from the status endpoint.

## Endpoint Added

- `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}`

Behavior:

- Returns HTTP `200` with the recorded fiscal issuance status when the reference exists.
- Returns HTTP `404` with safe error code `FISCAL_ISSUANCE_REFERENCE_NOT_FOUND` when no active reference exists.

## Fields Returned

The response returns safe recorded fields only:

- fiscal issuance reference identity;
- fiscal issuance state;
- result classification and fiscal evidence status;
- upstream finality reference;
- payment confirmation, payment attempt, and parking session identifiers;
- site and site POS Server context where available;
- POS Server fiscal document id and fiscal document number where recorded;
- fiscal identity, sequence policy, sequence value, fiscal series, prefix, suffix, and assignment timestamp where recorded;
- semantic request hash value, version, status, algorithm, and fact count where recorded;
- latest safe error code, error posture, and exception reason;
- first recorded timestamp, last updated timestamp, and correlation id.

The response does not expose raw request payloads, canonical source text, secrets, stack traces, internal configuration, payment provider payloads, customer PII, or statutory evidence payloads.

## State Behavior

- Recorded fiscal issuance returns fiscal document id and fiscal document number when available.
- Same-key/same-hash replay returns `FISCAL_ISSUANCE_REPLAYED` and `IDEMPOTENT_REPLAY` while preserving the recorded fiscal document details.
- Same-key/different-hash conflict returns `FISCAL_ISSUANCE_CONFLICT`, safe error code `fiscal_document_idempotency_conflict`, and posture `DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE` without inventing a fiscal document id or number.
- Failed service/configuration/request states return safe error code/posture only.

## Intentionally Not Implemented

- No fiscal issuance mutation.
- No POS Server live call or readback call.
- No fiscal number allocation.
- No retry execution.
- No FEQ behavior.
- No PDF/HTML/QR generation.
- No BIR final statutory wording.
- No Gate/ExitAuthorization behavior.
- No refund/reversal behavior.
- No scheduler or batch retry behavior.

## Tests And Validation

Unit coverage was added for:

- recorded fiscal issuance status;
- replayed fiscal issuance status;
- conflict fiscal issuance status;
- failed-service fiscal issuance status;
- missing reference behavior;
- read service non-mutation posture;
- endpoint source remaining GET-only and not wiring POS Server or retry execution.

## Recommended Next Step

Use this read-only status surface for operator/support visibility and integration diagnostics. Keep mutation, retry, document rendering, and gate behavior in separate explicitly governed slices.
