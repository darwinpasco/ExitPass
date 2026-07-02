# ExitPass POS Server API Contract v1.0 Alignment Review

## 1. Review Summary

This review aligns `ExitPass_POS_Server_API_Contract_v1.0.md` with the current implemented POS Server runtime in `D:\SourceCodes\ExitPass-PoSServer`.

The prior API contract was broader and described many provisional API families. The aligned contract now documents the currently implemented fiscal document create/read endpoints and clearly marks the remaining API families as deferred.

## 2. Runtime Repository Inspected

| Item | Value |
| --- | --- |
| Runtime repository | `D:\SourceCodes\ExitPass-PoSServer` |
| Runtime branch at read-only inspection | `dev` |
| Runtime branch at final validation | `runtime/fiscal-issuance-response-status-hardening` |
| Documentation repository | `D:\SourceCodes\ExitPass` |
| Documentation branch | `docs/v1.3-pos-server-api-contract-alignment` |

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

Created:

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
- GET returns persisted fiscal numbering fields.

Current runtime-specific caveat:

- Create and replay both return code `accepted` and HTTP `202 Accepted`; replay is distinguished by message, not a distinct response code.

## 7. Docs Aligned

The API Contract was aligned to:

- current implemented endpoints only
- current DTO fields
- current HTTP status codes
- current response envelope behavior
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

No runtime contradiction was found for the v1.3 authority model.

Validation issue:

- During final validation, `D:\SourceCodes\ExitPass-PoSServer` was on branch `runtime/fiscal-issuance-response-status-hardening` and reported uncommitted changes in runtime source and test files. Those runtime changes were not made by this documentation task and were not reverted. This API contract is aligned to the runtime files inspected during the read-only pass before those final-validation unstaged changes appeared.

Runtime files reported modified during final validation:

- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentCreationEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentReadEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/GetFiscalDocumentResponse.cs`
- `tests/ExitPass.PosServer.Api.IntegrationTests/FiscalDocumentApiPostgresSmokeTests.cs`
- `tests/ExitPass.PosServer.Api.Tests/FiscalDocumentCreationEndpointTests.cs`
- `tests/ExitPass.PosServer.Api.Tests/FiscalDocumentReadEndpointTests.cs`

## 11. Risks and Open Questions

Risks:

- Current replay response uses the same code `accepted` as first-time creation, which may require caller guidance or later response differentiation.
- Authentication and authorization remain placeholders in this API Contract.
- Idempotency currently depends on upstream finality reference rather than an explicit HTTP header; Central PMS must treat that reference as stable and non-reusable for semantically different fiscal issuance.
- Durable post-commit sequence gap and recovery handling remains deferred.
- Deferred API families must not be consumed until separate runtime contracts are produced.
- The runtime repository must be returned to a clean or intentionally reviewed state before this API contract is treated as final against the runtime implementation branch.

Open questions:

- Should a future `Idempotency-Key` header be added or should upstream finality reference remain canonical?
- Should replay return a distinct `code` while preserving HTTP `202`?
- What is the final service-to-service authentication and authorization model?
- What Central PMS fiscal reference recording callback or reconciliation endpoint is needed, if any?
- What is the final durable post-commit gap/recovery policy?

## 12. Recommended Next API / Documentation Task

Recommended next task:

> Draft a Central PMS to POS Server Fiscal Issuance Integration Contract addendum that defines Central PMS caller responsibilities, retry behavior, fiscal reference recording, handling of `202 accepted`, replay, `409` idempotency conflict, `400` fiscal identity/policy/sequence failures, and `503` persistence/configuration failures.

Do not draft deferred API families until the corresponding runtime features exist.
