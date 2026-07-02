# ExitPass POS Server API Contract v1.0 Alignment Review

## 1. Review Summary

This review aligns `ExitPass_POS_Server_API_Contract_v1.0.md` with the current implemented POS Server runtime in `D:\SourceCodes\ExitPass-PoSServer`.

The prior API contract was broader and described many provisional API families. The aligned contract now documents the currently implemented fiscal document create/read endpoints, the response/status hardening semantics merged to `dev`, and clearly marks the remaining API families as deferred.

## 2. Runtime Repository Inspected

| Item | Value |
| --- | --- |
| Runtime repository | `D:\SourceCodes\ExitPass-PoSServer` |
| Runtime branch inspected | `dev` |
| Runtime slice reflected | `runtime/fiscal-issuance-response-status-hardening`, merged to `dev` |
| Documentation repository | `D:\SourceCodes\ExitPass` |
| Documentation branch | `docs/v1.3-pos-server-api-response-status-update` |

## 3. Files Inspected

Runtime source inspected:

- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentEndpointRouteBuilderExtensions.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentCreationEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentReadEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentRequest.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/GetFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentCreationService.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalIssuanceIdempotencyResolver.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentSemanticRequestHasher.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentDraft.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/IFiscalDocumentRepository.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentRepository.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentSql.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentReader.cs`

Runtime notes inspected:

- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Runtime_Fiscal_Numbering_and_Idempotency_Implementation_Plan.md`

Documentation references inspected:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_Repo_Inspection_Report.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

## 4. Files Created or Updated

Updated:

- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Alignment_Review.md`

## 5. Current Implemented Endpoints

Confirmed implemented runtime endpoints:

| Method | Route | Runtime source |
| --- | --- | --- |
| `POST` | `/v1/fiscal-documents/` | `FiscalDocumentEndpointRouteBuilderExtensions.cs` |
| `GET` | `/v1/fiscal-documents/{fiscalDocumentId:guid}` | `FiscalDocumentEndpointRouteBuilderExtensions.cs` |

No other POS Server API endpoints were confirmed as runtime-supported in this alignment pass.

## 6. Runtime Facts Confirmed

Confirmed current behavior:

- `POST /v1/fiscal-documents/` exists.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` exists.
- POST creates fiscal document header and child fiscal facts transactionally.
- POST validates upstream payable-basis and finality references.
- POST rejects statutory discount treatment without an approved upstream discount reference.
- POST rejects raw sensitive evidence/payment/credential/token/secret/card data markers.
- POST computes a deterministic semantic request hash server-side.
- POST derives idempotency scope from operation, Site POS Server id, and fiscal document type id.
- POST derives idempotency key from upstream finality reference.
- POST inserts or locks the `pos.idempotency_records` row.
- Same key and same semantic hash replays the original fiscal document result.
- Same key and different semantic hash fails as idempotency conflict.
- POST resolves fiscal identity server-side.
- POST resolves fiscal sequence policy server-side.
- POST locks the selected `pos.fiscal_sequence_states` row using `FOR UPDATE`.
- POST allocates the next fiscal sequence value.
- POST formats and persists fiscal document number fields.
- POST returns fiscal numbering fields after durable commit.
- POST success includes `resultClassification`, `fiscalIssuanceEvidenceStatus`, `fiscalNumberAssignmentState`, and `fiscalDocumentStatusCodeId`.
- POST first-time success returns `resultClassification = newly_created`.
- POST same-key/same-hash replay returns `resultClassification = idempotent_replay`.
- POST fail-closed incomplete fiscal numbering evidence returns `fiscal_number_assignment_incomplete`.
- POST supported failures expose conservative `errorPosture` values.
- GET returns persisted fiscal numbering fields.
- GET derives `fiscalIssuanceEvidenceStatus`, `fiscalNumberAssignmentState`, and `fiscalDocumentStatusCodeId` from the persisted read model.

Current runtime-specific response/status posture:

- Create and replay both return code `accepted` and HTTP `202 Accepted`.
- Replay is explicitly distinguished by `resultClassification = idempotent_replay`.
- First-time creation is explicitly distinguished by `resultClassification = newly_created`.

## 7. Docs Aligned

The API Contract was aligned to:

- current implemented endpoints only
- current DTO fields
- current HTTP status codes
- current response envelope behavior
- current response/status hardening behavior
- current idempotency source/key/hash posture
- current conflict/replay behavior
- current fiscal identity and policy resolution behavior
- current sequence allocation behavior
- current Central PMS integration posture
- current deferred API surface

The old broad route-family structure was replaced with a runtime-aligned contract and explicit deferred API list.

## 8. Deferred Items Confirmed

The following are not documented as current runtime-supported APIs:

- Digital SI URL creation
- printable Sales Invoice rendering
- QR generation/presentation contract
- X-read
- Z-read
- BIR Sales Summary / Annex E-1
- Annex E-2 to E-5
- Electronic Journal runtime generation
- POSLog generation/export
- fiscal export packaging
- reprints
- void/refund/cancel/return fiscal adjustment APIs
- reset counter runtime mechanics
- Z-counter runtime mechanics
- Grand Total Amount runtime mechanics
- fiscal recovery/failover runtime behavior
- statutory discount entitlement validation
- payment finality ownership
- `ExitAuthorization`
- gate opening
- continuity activation
- manual release approval

## 9. Authority Boundaries Preserved

The aligned contract preserves:

- Central PMS owns payment finality.
- Central PMS calls POS Server only after verified payment finality and payable-basis readiness.
- POS Server response is fiscal issuance evidence, not payment confirmation.
- POS Server does not issue `ExitAuthorization`.
- POS Server does not open gates.
- POS Server does not approve entitlement.
- POS Server does not mutate Central PMS payable basis.
- POS Server does not activate continuity.
- POS Server does not approve manual release.
- Channels and terminals are not independent POS systems.
- Site POS Server remains the resolved Site fiscal issuance authority.

## 10. Mismatches Found

Mismatches resolved:

- The previous API contract still used obsolete primary terminal terminology that has been replaced by `Cashier-Assisted Terminal` and `Continuity Terminal`.
- The previous API contract described many provisional API families in a way that could be read as current contract surface.
- The previous API contract treated `Idempotency-Key` as the required current idempotency source, while the runtime currently derives idempotency from request-body upstream finality reference.
- The previous API contract did not reflect implemented server-side fiscal identity resolution, sequence policy resolution, idempotency conflict/replay, sequence-state row locking, and fiscal number return behavior.
- The previous API contract still described replay as distinguishable only by message; current runtime now exposes `resultClassification = idempotent_replay`.
- The previous API contract did not document `errorPosture`, `fiscal_number_assignment_incomplete`, or GET-derived fiscal evidence/assignment status.

No runtime contradiction was found for the v1.3 authority model.

## 11. Response/Status Hardening Update

Runtime branch/slice inspected:

- Runtime repository: `D:\SourceCodes\ExitPass-PoSServer`
- Runtime branch inspected: `dev`
- Runtime slice reflected: `runtime/fiscal-issuance-response-status-hardening`, merged to `dev`
- Runtime note inspected: `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

New fields documented:

- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `fiscalDocumentStatusCodeId`
- `errorPosture`

Replay distinction now aligned:

- First-time success is documented as `resultClassification = newly_created`.
- Same-key/same-hash replay is documented as `resultClassification = idempotent_replay`.
- Both may still use HTTP `202 Accepted` and code `accepted`.
- Replay returns the original fiscal document id and numbering fields and does not allocate another fiscal number.

Error posture documented:

- `do_not_retry_without_request_change`
- `retry_after_configuration_correction`
- `retry_after_service_recovery`

Fiscal numbering completeness fail-closed behavior documented:

- If a successful persistence result lacks complete fiscal numbering evidence, the API fails closed with `fiscal_number_assignment_incomplete`.
- Central PMS must not record fiscal issuance evidence from `fiscal_number_assignment_incomplete`.

Central PMS interpretation updated:

- `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned` is POS Server fiscal issuance evidence only.
- It does not mean payment finality, `ExitAuthorization`, gate permission, entitlement approval, manual release approval, continuity activation, BIR report finality, X/Z finality, Annex E finality, Digital SI issuance, or recovery completion.

Files updated:

- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Alignment_Review.md`

Authority boundaries preserved:

- POS Server remains fiscal issuance authority only.
- Central PMS remains payment finality, fiscal reference recording, and `ExitAuthorization` authority.
- POS Server response/status fields remain fiscal evidence/status fields, not payment, exit, entitlement, gate, continuity, or manual-release authority.

## 12. Risks and Open Questions

Risks:

- Authentication and authorization remain placeholders in this API Contract.
- Idempotency currently depends on upstream finality reference rather than an explicit HTTP header; Central PMS must treat that reference as stable and non-reusable for semantically different fiscal issuance.
- Durable post-commit sequence gap and recovery handling remains deferred.
- Deferred API families must not be consumed until separate runtime contracts are produced.

Open questions:

- Should a future `Idempotency-Key` header be added or should upstream finality reference remain canonical?
- What is the final service-to-service authentication and authorization model?
- What Central PMS fiscal reference recording callback or reconciliation endpoint is needed, if any?
- What is the final durable post-commit gap/recovery policy?
- What is the final cross-API error envelope standard?

## 13. Recommended Next API / Documentation Task

Recommended next task:

> Draft a Central PMS to POS Server Fiscal Issuance Integration Contract addendum that defines Central PMS caller responsibilities, retry behavior, fiscal reference recording, handling of `202 accepted`, replay, `409` idempotency conflict, `400` fiscal identity/policy/sequence failures, and `503` persistence/configuration failures.

Do not draft deferred API families until the corresponding runtime features exist.
