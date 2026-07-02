# ExitPass Central PMS Fiscal Issuance Reference and Exception-State Implementation Planning Note v1.0 Review

## 1. Review Summary

This review covers the creation of `ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`.

The planning note translates the POS Server runtime behavior, POS Server API Contract, and Central PMS to POS Server Fiscal Issuance Integration Contract into Central PMS implementation planning items. It does not implement source code, SQL, migrations, generated artifacts, API specs, or runtime behavior.

## 2. Repositories Inspected

| Repository | Purpose | Branch inspected |
| --- | --- | --- |
| `D:\SourceCodes\ExitPass` | Documentation repository modified by this task. | `docs/v1.3-central-pms-fiscal-issuance-implementation-planning` |
| `D:\SourceCodes\ExitPass-PoSServer` | POS Server runtime repository inspected read-only. | `dev` |

## 3. Documentation References Inspected

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Alignment_Review.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

## 4. Runtime References Inspected

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 5. Files Created

- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0_Review.md`

## 6. Fiscal Reference Persistence Planning Summary

The planning note identifies Central PMS persistence needs for:

- POS Server fiscal document id
- fiscal identity id
- fiscal sequence policy id
- fiscal sequence value
- fiscal document number
- fiscal series
- fiscal number prefix/suffix text
- fiscal number assigned timestamp and actor reference
- fiscal document status code id
- result classification
- fiscal issuance evidence status
- fiscal number assignment state
- upstream finality reference
- Central PMS payment/session references
- Site and Site POS Server references
- request/correlation id
- request hash or semantic hash reference if available
- POS Server response timestamp
- retry/replay/conflict history
- current fiscal issuance integration state
- exception state/reason

These are explicitly marked as planning requirements, not DDL.

## 7. State Model Planning Summary

The note proposes candidate Central PMS fiscal issuance states for later Engineering Pack confirmation:

- `not_required`
- `pending_fiscal_issuance`
- `fiscal_issuance_requested`
- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`
- `fiscal_issuance_conflict`
- `fiscal_issuance_failed_request`
- `fiscal_issuance_failed_configuration`
- `fiscal_issuance_failed_service`
- `fiscal_issuance_unknown`
- `fiscal_issuance_manual_review`
- `fiscal_issuance_exception_released`
- `fiscal_issuance_reconciled`

The note states that final names, enum values, API statuses, and transition rules remain deferred.

## 8. Exception-State Planning Summary

The note plans exception buckets for:

- idempotency conflict
- request construction error
- unapproved discount reference
- sensitive payload rejection
- fiscal identity missing/ambiguous/not effective
- fiscal sequence policy missing/ambiguous/not effective
- fiscal sequence state missing/not effective
- allocation/format failure
- persistence unavailable
- incomplete numbering evidence
- unknown POST outcome
- GET readback inconclusive
- Central PMS fiscal reference mismatch
- POS Server readback mismatch
- manual release request after fiscal issuance failure

## 9. Retry/Replay/Conflict Planning Summary

The note documents:

- retry same semantic request with the same `payableBasis.upstreamFinalityRef` after uncertain outcome
- idempotent replay is success only when fiscal evidence and assignment state are complete
- conflict is fail-closed and requires review
- Central PMS must not use a new upstream finality reference to bypass conflict
- retry scheduler details remain deferred

## 10. Unknown Outcome Planning Summary

The note plans handling for:

- POST timeout before response
- POST `503` with fiscal document id
- POST `503` without fiscal document id
- network disconnect after POS Server may have committed
- Central PMS payment finality recorded while POS Server is unreachable
- successful GET readback
- failed GET readback
- later replay success
- fiscal reference recording failure after POS Server success

Unknown outcome is explicitly not treated as fiscal success.

## 11. ExitAuthorization Gating Planning Summary

The note preserves the gating rule that normal ExitAuthorization remains blocked until:

1. payment finality is verified by Central PMS
2. POS Server fiscal issuance succeeds or replays successfully
3. POS Server returns `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`
4. POS Server returns `fiscalNumberAssignmentState = assigned`
5. Central PMS durably records fiscal issuance reference

Manual release/exception release remains separately approved, auditable, incident-tagged, and reconciliation-tagged.

## 12. Operator Console Review Queue Planning Summary

The note plans Operator Console queue/review views for:

- fiscal issuance pending
- fiscal issuance retry needed
- fiscal configuration correction required
- idempotency conflict
- unknown outcome
- incomplete numbering evidence
- fiscal reference mismatch
- manual release requested after fiscal failure
- reconciled/closed exceptions

Operator Console remains non-payment and non-gate-authority.

## 13. Management Dashboard Visibility Planning Summary

The note plans dashboard visibility for:

- fiscal issuance success rate
- fiscal issuance failures by category
- replay count
- conflict count
- unknown outcome count
- pending exception count
- manual release count tied to fiscal issuance exception
- average time from payment finality to fiscal reference recording
- Site/Site POS Server breakdown
- open fiscal exception age
- retry backlog
- reconciliation backlog

Management Dashboard remains visibility/reporting only.

## 14. API/Service/Database/Event/Job/Test Planning Summary

The note identifies future work areas for:

- Central PMS fiscal issuance orchestration service
- POS Server client boundary
- request construction and response interpretation
- fiscal reference recording service
- fiscal issuance state transition service
- ExitAuthorization gating check update
- retry/reconciliation jobs
- GET readback job
- Operator Console queue APIs
- Dashboard reporting projections
- audit events and correlation propagation
- persistence model, attempt/history records, exception state fields, and dashboard/reporting projection source
- Test/UAT scenarios covering success, replay, conflict, request/config errors, service errors, incomplete numbering, unknown outcome, reference recording failure, ExitAuthorization blocking, manual release exception, Operator Console queue visibility, and dashboard metrics

All implementation details remain deferred.

## 15. Security/Access Control Planning Summary

The note plans:

- Central PMS service identity as the only caller for POS Server fiscal issuance path
- role-scoped Operator Console fiscal exception review
- role/scoped Management Dashboard fiscal visibility
- exclusion of secrets, PAN/CVV, tokens, raw provider callbacks, raw entitlement evidence, and unmanaged sensitive payloads from logs
- controlled audit access
- attributable retry/manual review actions

## 16. Authority Boundaries Preserved

The planning note preserves:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console supports review/governance only and must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.
- Management Dashboard is visibility/reporting only.

## 17. Deferred Items Preserved

The note does not implement or define detailed contracts for:

- Digital SI
- printable SI
- QR presentation
- X-read
- Z-read
- BIR Sales Summary
- Annex E
- EJ
- POSLog
- reprints
- adjustments
- reset/Z-counter/GTA mechanics
- recovery automation
- gate integration endpoint
- POS Server-side Central PMS callbacks
- final SQL DDL
- final API endpoint paths
- final DTOs
- final event payloads
- final queue names
- final UAT scripts

## 18. Issues or Mismatches

No blockers or source contradictions were found for this planning scope.

No source code, SQL, migrations, generated artifacts, DOCX files, or runtime repository files were modified.

## 19. Recommended Next Task

Recommended next task:

> Draft the Central PMS Fiscal Issuance Engineering Pack outline covering persistence deltas, service/API changes, event/job contracts, audit fields, Operator Console queue contracts, Management Dashboard projection inputs, and Test/UAT coverage.
