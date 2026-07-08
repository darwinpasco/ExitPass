# ExitPass Central PMS to POS Server Fiscal Issuance Readiness Checkpoint v1.0

## Scope

This checkpoint covers the normal Central PMS to POS Server fiscal issuance integration path and POS Server idempotency behavior for fiscal document creation.

This is an evidence-backed UAT readiness checkpoint. It is not a production certification.

## Passing Proof Set

The current proof set contains three completed validations:

- First issuance: passed.
- Same-key/same-hash replay: passed.
- Same-key/different-hash conflict: passed.

## Evidence References

- `docs/v1.3/central-pms/evidence/pos-server-normal-fiscal-issuance-clean-rerun-controlled-codes-20260708/`
- `docs/v1.3/central-pms/evidence/pos-server-normal-fiscal-issuance-same-key-same-hash-replay-20260708/`
- `docs/v1.3/central-pms/evidence/pos-server-normal-fiscal-issuance-same-key-different-hash-conflict-20260708/`

## Key Outcomes

- First issuance created fiscal document `deac11e4-fc31-4c40-9a44-da690b9730ef`.
- Fiscal document number `SI-00000001-UAT` was assigned.
- Central PMS recorded `FISCAL_ISSUANCE_RECORDED`.
- Same-key/same-hash replay kept fiscal document count `1 -> 1`.
- Same-key/same-hash replay kept idempotency count `1 -> 1`.
- Same-key/same-hash replay kept fiscal sequence value `1 -> 1`.
- Central PMS recorded `FISCAL_ISSUANCE_REPLAYED` / `IDEMPOTENT_REPLAY`.
- Same-key/different-hash conflict returned POS Server HTTP `409`.
- Conflict kept fiscal document count `1 -> 1`.
- Conflict kept idempotency count `1 -> 1`.
- Conflict kept fiscal sequence value `1 -> 1`.
- Central PMS recorded `FISCAL_ISSUANCE_CONFLICT` with `fiscal_document_idempotency_conflict` and `DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE`.
- Forbidden side-effect counts were zero across the validation set.

## Environment Notes

The passing result required aligned disposable POS Server controlled-code seed data and a clean disposable Central PMS database for each fresh run.

The proof set used disposable/local UAT data only. It did not rely on production identifiers or production databases.

## Readiness Decision

Central PMS to POS Server fiscal issuance is ready at UAT evidence level for the core normal fiscal issuance and idempotency integration path.

This is not a production certification. Production readiness still requires environment-specific deployment validation, security configuration validation, operational runbook validation, and final compliance/BIR review where applicable.

## Explicitly Not Covered

- Production load/performance.
- Production security hardening.
- mTLS/service identity enforcement.
- Real live site data.
- Final BIR statutory receipt wording.
- PDF/HTML/QR generation.
- Annex E, X/Z, and reporting flows.
- Gate/ExitAuthorization behavior.
- Refund/reversal flows.
- FEQ batch retry scheduler behavior.

## Recommended Next Action

Keep this proof set closed. Move to deployment/UAT runbook hardening or the next product slice.

Do not keep adding idempotency validation unless a new risk is identified.
