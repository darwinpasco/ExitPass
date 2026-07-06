# ExitPass FEQ Semantic Hash Backfill Internal API Security Hardening Implementation Note v1.0

## Scope

This slice hardens the internal semantic hash backfill request API:

- `POST /internal/v1/fiscal-exception-queue/semantic-hash-backfill-requests`

It does not add public UI, public access, batch backfill, retry execution, retry worker, retry endpoint, POS Server POST, fiscal-gated ExitAuthorization behavior, payment finality mutation, gate behavior, fiscal number editing, or manual fiscal document creation.

## Hardening Added

The internal API remains disabled by default through `FiscalExceptionSemanticHashBackfillInternalApiOptions.Enabled`.

Controlled mutation intent now has a separate API-side guard:

- `FiscalExceptionSemanticHashBackfillInternalApiOptions.AllowControlledMutationIntent`

This is also disabled by default. A request with `ExecuteControlledMutation = true` is rejected before workflow invocation unless the API-side intent guard is explicitly enabled. The existing workflow invocation guard remains separate and must also be enabled before guarded mutation can be invoked.

## Validation Rules

The API handler rejects:

- batch-shaped requests through `FiscalIssuanceReferenceIds`
- missing/default fiscal issuance reference id
- missing/default recalculation preview audit id
- missing/default mutation-prep audit id
- empty/default correlation id when supplied
- blank, oversized, or unsafe reason code
- blank, oversized, control-character-bearing, or obvious secret/payload-bearing justification
- execute-controlled-mutation intent when API-side mutation intent is disabled
- mismatched preview or mutation-prep audit basis

Actor/service identity, explicit approval reference, and required dual-control reference continue to flow through the existing operator workflow service when enough FEQ/audit context exists. That preserves the existing workflow audit behavior for governed denials.

## Authorization / Internal Access Posture

The endpoint remains mapped with `RequireInternalServiceMtls()` and is verified by integration tests to carry `InternalServiceEndpointMetadata`.

No weak caller-identity mechanism was invented. Full authenticated service identity/claims binding is still not present in the current internal endpoint convention. The endpoint therefore remains disabled by default and must not be operationally enabled without an environment-level mTLS policy plus a future explicit service-identity authorization policy.

## Audit / Error Behavior

Responses return only safe status, block reason, mutation posture, guarded mutation basis when applicable, retry-execution availability, and safe summary. They do not echo raw request bodies, canonical source text, POS Server payloads, payment payloads, secrets, customer PII, statutory evidence, stack traces, or connection/config details.

Denied requests that can safely reach the operator workflow are persisted through the existing workflow audit. Shape/config denials before FEQ context resolution are not persisted because the existing workflow audit requires a known FEQ detail/basis and this slice deliberately avoids adding a broad denial-audit schema.

## Remaining Security Gaps

Before operational enablement, a follow-up slice should add or wire:

- authenticated internal service identity binding that cannot be spoofed by request body fields
- endpoint-specific authorization policy for the semantic hash backfill workflow
- operational idempotency/rate-limit policy for repeated internal requests
- optional denial audit for pre-context shape/config denials, if required by operations

Retry execution remains unavailable.

