# HikCentral API Discovery Input Pack

Version: v1.0
Status: Specialist input pack
Date: 2026-07-01
Owner: HikCentral API Discovery specialist

## 1. Purpose

This input pack inventories confirmed HikCentral Professional OpenAPI areas and local ExitPass evidence needed by the later HikCentral Connector Profile. It does not draft the final Vendor PMS Connector System Design or HikCentral Connector Profile.

Evidence is labeled as follows:

- Vendor guide confirmed: confirmed from the local HikCentral Professional OpenAPI Developer Guide V3.1.0 PDF outline/title text.
- Historical/UAT evidence: found in existing ExitPass historical validation, runbook, smoke-test, or diagram files.
- Adapter evidence: found in existing local source code or tests; useful as implementation evidence, not vendor documentation.
- Unknown/vendor question: not confirmed by available local source.

## 2. Source Documents / Files Found

Primary ExitPass sources reviewed:

| Source | Use in this pack |
| --- | --- |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md` | Scope, file ownership, authority guardrails, HikCentral profile constraints. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Vendor PMS/HCP authority, Site/Site Group, AdapterMapping, projection, live resolve, acknowledgment guardrails. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Connector boundaries, projection/freshness, vendor acknowledgment, security, observability, deferred HCP details. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Degraded operation, projection fallback, vendor acknowledgment failure, reconciliation. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Connector health and projection freshness visibility; non-payment Operator Console boundary. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Reporting distinction between operational projection and financial truth. |

Confirmed vendor source found:

| Source | Availability | Notes |
| --- | --- | --- |
| `docs/vendor/hikcentral/HikCentral Professional OpenAPI_Developer Guide_V3.1.0_20260130.pdf` | Found | PDF outline/title text is searchable locally. Full request/response text extraction utilities were not available in this shell. |

Historical HikCentral references found and reviewed:

| Source | Relevant content |
| --- | --- |
| `docs/hikcentral-ticket-only-readonly-validation.md` | Local V3.1.0 ticket-only discovery, signing pattern, safe endpoint inventory, known response codes, ticket/card open questions. |
| `docs/hikcentral-projection-live-uat.md` | Live UAT projection setup, parking lot confirmation, page size/lookback planning, non-payment boundary. |
| `docs/hikcentral-projection-resolve-uat-results.md` | Projection and live resolve UAT outcomes, observed passageway fields, timestamp handling, no payment/exit side effects. |
| `docs/hikcentral-projection-production-controls.md` | Production projection controls, parking lot mapping confirmation, health/freshness, source constraints. |
| `docs/hikcentral-real-sync-target-deployment-handoff.md` | Deployment handoff, parking lot list sample, projection identity notes, source gap and support checks. |
| `docs/hikcentral-operator-console-projection-health-smoke.md` | Operator Console projection health smoke and safe display boundaries. |
| `docs/hikcentral-sandbox-validation-runbook.md` and `docs/hikcentral-sandbox-validation-harness.md` | AK/SK signing and door-control sandbox validation context, not parking-fee profile authority. |
| `docs/diagrams/hikcentral-normal-resolve-flow.puml` | Historical sequence for projection sync and normal live resolve. |
| `docs/diagrams/hikcentral-degraded-projection-fallback-flow.puml` | Historical sequence for degraded projection snapshot fallback. |

Supporting adapter evidence reviewed:

| Source | Relevant content |
| --- | --- |
| `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralRequestSigner.cs` | Existing AK/SK HMAC-SHA256 signature implementation for HCP V3.1.0. |
| `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralPassagewayRecordClient.cs` | Existing passageway record request/response mapping. |
| `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralParkingClient.cs` | Existing parking fee calculate and confirm adapter behavior. |
| `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralTicketDiscoveryClient.cs` | Existing read-only ticket discovery endpoint inventory and request shapes. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorSessions/HikCentralPassagewayProjectionNormalizer.cs` | Existing projection identity normalization and placeholder plate handling. |

## 3. Source Availability Gaps

- External documentation folders checked on 2026-07-01 and not found: `D:\Docs\ExitPass\HikCentral`, `D:\Docs\ExitPass\Vendor PMS`, and `D:\Docs\ExitPass\Parking`.
- No repository-local HikCentral OpenAPI YAML, Swagger, Postman collection, or parking API collection was found. The only repository-local vendor API source found is the PDF developer guide.
- A Bruno folder exists for `bruno/hikcentral-sandbox-validation`, but it relates to sandbox gate/door validation, not the parking fee or passageway APIs needed for this connector profile.
- The PDF was searchable for outline/title text, including endpoint paths, but no local `pdftotext`, Poppler, or Python runtime was available to extract full field-level tables from the PDF. Field-level details below therefore use historical/UAT and adapter evidence unless explicitly labeled vendor guide confirmed.
- A historical document references `D:\Hikvision\hikcentral_openapi_for_bruno.yaml`, but that file was not present in this repository and was not available through the checked local documentation folders.

## 4. HikCentral Authentication Posture

Vendor guide confirmed:

- The PDF contains section `3.2 Signature and Authentication`.
- The local source and historical docs consistently treat HikCentral as an AK/SK or AppKey/AppSecret signed OpenAPI integration.

Historical/UAT and adapter evidence:

- The local V3.1.0 signing pattern uses `POST`, `Accept: */*`, `Content-Type: application/json`, signed headers `x-ca-key,x-ca-timestamp`, and no `Content-MD5` or `Date` header in the tested profile.
- The canonical string in the local ticket-only validation doc is method, accept, content type, signed `x-ca-*` headers, and path.
- The adapter computes an HMAC-SHA256 signature with the secret key and Base64 encodes it.
- Headers in existing implementation include `X-Ca-Key`, `X-Ca-Timestamp`, `X-Ca-Signature-Headers`, and `X-Ca-Signature`; some parking calls also include `userId` and `X-Correlation-Id`.

Connector profile implication:

- AppKey/AppSecret/AK/SK values are secrets. They must be stored only in approved secret channels and must not be logged, displayed, committed, or included in design examples.
- Signature values and raw secret-bearing headers should not appear in evidence, audit exports, or dashboards.

## 5. Parking API Endpoint Inventory

Vendor guide confirmed from PDF outline/title text:

| Area | Method/path | Notes |
| --- | --- | --- |
| PMS image | `POST /artemis/api/pms/v1/image` | Listed in parking application section; not currently relevant to connector profile. |
| PMS cross records | `POST /artemis/api/pms/v1/crossRecords/page` | Passage/log search area; camera index required by local diagnostic evidence. |
| Parking lot list | `POST /artemis/api/vehicle/v1/parkinglot/list` | Required to discover/confirm HCP parking lot identity. |
| Floor list | `POST /artemis/api/vehicle/v1/floor/list` | Parking resource metadata; not currently in profile scope except possible floor status discovery. |
| Floor overview | `POST /artemis/api/vehicle/v1/floor/overview` | Parking resource/status area; not currently in profile scope. |
| Floor parking-space status | `POST /artemis/api/vehicle/v1/floor/parkingspace/status` | Status lookup candidate; requires floor index in historical diagnostics. |
| Parking lot passageway record | `POST /artemis/api/vehicle/v1/parkinglot/passageway/record` | Projection polling source. |
| Parking-space record | `POST /artemis/api/vehicle/v1/parkingspace/record` | Read-only record search candidate. |
| Parking fee calculate | `POST /artemis/api/vehicle/v1/parkingfee/calculate` | Live fee calculation area. |
| Parking fee confirm | `POST /artemis/api/vehicle/v1/parkingfee/confirm` | Mutating payment/exit acknowledgment area. |
| Vehicle blocklist add/get/modify/delete | `/artemis/api/vehicle/v1/vehicle/blocklist/*` | Out of current connector profile scope. |
| Parking guidance endpoints | `/artemis/api/vehicle/v1/parkingguidance/*` | Out of current profile scope unless later approved. |

Historical/UAT safe diagnostic classification:

| Endpoint | Current profile relevance | Current safety posture |
| --- | --- | --- |
| `/artemis/api/common/v1/version` | Connectivity/version check | Read-only diagnostic. |
| `/artemis/api/vehicle/v1/parkinglot/list` | Parking object mapping | Read-only. |
| `/artemis/api/vehicle/v1/parkingfee/calculate` | Normal live resolve/tariff source where supported | Read-only calculation, not payment confirmation. |
| `/artemis/api/vehicle/v1/parkinglot/passageway/record` | Projection polling | Read-only historical/operational record search. |
| `/artemis/api/vehicle/v1/parkingspace/record` | Discovery/supporting diagnostics | Read-only record search. |
| `/artemis/api/pms/v1/crossRecords/page` | Optional diagnostics | Read-only when camera index is configured. |
| `/artemis/api/vehicle/v1/floor/parkingspace/status` | Possible active-state discovery candidate | Read-only based on title, but request/response schema not fully extracted from PDF. |
| `/artemis/api/vehicle/v1/parkingfee/confirm` | Vendor payment acknowledgment candidate | Mutating; do not call in read-only diagnostics. |

## 6. Relevant Parking Lot / Passageway / Parking Fee APIs

Parking lot list:

- Vendor guide confirmed endpoint: `POST /artemis/api/vehicle/v1/parkinglot/list`.
- Historical evidence confirms local use to find `parkingLotIndexCode` and `parkingLotName`.
- Historical sample fields include `parkingLotIndexCode`, `parkingLotName`, `parentParkingLotIndexCode`, `totalSpaceNum`, and `totalPermanentSpaceNum`.

Passageway record:

- Vendor guide confirmed endpoint: `POST /artemis/api/vehicle/v1/parkinglot/passageway/record`.
- Historical and adapter evidence use this as the one-minute HCP projection planning source.
- Adapter request shape uses `pageIndex`, `pageSize`, and `queryInfo` containing `parkingLotIndexCode`, `beginTime`, `endTime`, `directionType`, `allowResult`, `sortField = EnterTime`, and `orderType = 1`.
- Historical evidence treats it as read-only history/record search, not active financial truth.

Parking fee calculate:

- Vendor guide confirmed endpoint: `POST /artemis/api/vehicle/v1/parkingfee/calculate`.
- Adapter evidence sends `plateLicense` and/or `cardNum`.
- Adapter evidence maps successful response data containing `plateLicense`, `cardNum`, `parkingInTime`, `parkingDuration`, `feeRuleType`, `feeRuleIndexCode`, `feeRuleName`, and `fee`.
- Historical ticket-only evidence says printed ticket numbers and historical `personInfo.cardNum` values returned `code = 128` in local tests, so ticket-only support is not confirmed for the tested flow.

Parking fee confirm:

- Vendor guide confirmed endpoint: `POST /artemis/api/vehicle/v1/parkingfee/confirm`.
- Historical and adapter evidence treat it as mutating payment/exit state and disabled by default for diagnostics.
- Adapter evidence sends `plateLicense` and/or `cardNum`, `immediatelyLeave`, and `fee`, and maps response fields `fee` and `feeTime`.
- The exact deployment requirement for whether confirmation must occur before exit remains unknown.

## 7. Known Request / Response Concepts

Confirmed or strongly evidenced concepts:

| Concept | Evidence type | Notes |
| --- | --- | --- |
| Envelope `code` | Historical/UAT and adapter | `code = 0` treated as success. Nonzero codes are not success. |
| Envelope `msg` | Historical/UAT and adapter | Used for vendor message/error metadata. |
| Envelope `data` | Historical/UAT and adapter | Can be object or array depending on endpoint/response. |
| `pageIndex` / `pageSize` | Historical/UAT and adapter | Used by parking lot passageway and parking-space record requests. |
| `pageNo` / `pageSize` | Historical/UAT and adapter | Used by parking lot list and PMS cross-record diagnostics. |
| `total` / `totalCount` | Adapter evidence | Passageway response mapper accepts either. |
| `parkingLotInfo` | Historical/UAT and adapter | Nested object containing parking lot identity fields. |
| `passagewayInfo` | Historical/UAT and adapter | Nested object containing passageway identity fields. |
| `laneInfo` | Historical/UAT and adapter | Nested object containing lane identity/direction fields. |
| `personInfo.cardNum` | Historical/UAT and adapter | Useful lookup/projection field where present; exact business meaning is unclear. |
| `carInfo.plateLicense` | Historical/UAT and adapter | Vehicle plate; `Unknown` placeholder is normalized to null in projection. |
| `carInfo.EnterTime` / `carInfo.ExitTime` | Historical/UAT and adapter | Observed actual field casing in local response samples. |
| `enterTime` / `exitTime` | Adapter evidence | Also supported by mapper as alternate top-level names. |
| `fee`, `parkingInTime`, `parkingDuration` | Adapter evidence | Fee calculate response concepts. |
| `feeTime` | Adapter evidence | Fee confirm response concept. |

Unknown:

- Full vendor-documented request/response field descriptions, constraints, and enum meanings were not extractable from the PDF in this environment.
- Exact date-range maximums, retention windows, and pagination limits are deployment/vendor questions.

## 8. Vendor Object Identity Concepts

- HCP `parkingLotIndexCode` is vendor-side identity only.
- HCP `parkingLotIndexCode` must not be treated as ExitPass `site_id`.
- ExitPass Site mapping must go through AdapterMapping from ExitPass Site to vendor-side parking object.
- Runtime vendor object identity should follow the orchestration baseline: `vendorSystemId + vendorObjectType + vendorObjectRef`.
- For HCP parking lots, `vendorObjectType` should be a parking-lot-like type and `vendorObjectRef` should carry the HCP `parkingLotIndexCode`; final naming is for later design.
- Historical projection identity uses HCP record `guid` when present; fallback identity combines parking lot, `cardNum` or plate, and enter time.

## 9. Ticket / Card / Plate Identifier Notes

Ticket/card:

- `cardNum` appears in fee calculation and passageway contexts.
- Historical local ticket-only validation tested `parkingfee/calculate` with `{ "cardNum": "<printedTicketNumber>" }`.
- In that local validation, printed ticket numbers and historical `personInfo.cardNum` values returned `code = 128`, so ticket-only fee calculation is not confirmed for the current tested deployment.
- `personInfo.cardNum` values in passageway records are useful diagnostics and projection lookup candidates where present, but historical records do not prove the field equals the printed ticket number.
- Exact `cardNum` meaning is unclear and must remain a vendor question.

Plate:

- `plateLicense` is supported by historical/adapter evidence in fee calculation, passageway diagnostics, and projection records.
- `plateLicense = Unknown` must be treated as a placeholder, not a real plate identity.
- Plate support is out of current ticket-only profile scope unless later approved for ExitPass flow.

Printed ticket:

- No available local vendor source explicitly confirms a physical printed ticket number, barcode, QR payload, unpaid bill, receivable order, or active parked vehicle lookup field.
- Ticket-only support should not be claimed until the deployment/vendor confirms which field from the physical ticket maps to the active session lookup key accepted by `parkingfee/calculate`.

## 10. Projection and Passageway Record Notes

- Passageway records are projection/operational visibility input, not financial truth.
- Projection data must not replace live Vendor PMS/HCP tariff calculation in normal mode.
- Projection must not establish payment finality.
- Projection must not authorize exit.
- One-minute HCP passageway polling is the v1.3 planning baseline from the orchestration plan and BRD.
- Historical production controls include page size, lookback window, max pages per run, freshness, health buckets, and scheduler ownership controls, but those operational values are not vendor API guarantees.
- Historical UAT observed nested response fields: `parkingLotInfo.parkingLotIndexCode`, `personInfo.cardNum`, `carInfo.EnterTime`, and `carInfo.ExitTime`.
- Timestamps with `+08:00` offset were observed historically and must be converted to UTC before persistence where stored in UTC database types.

## 11. Fee Calculation Notes

Confirmed API area:

- Vendor guide confirmed endpoint: `POST /artemis/api/vehicle/v1/parkingfee/calculate`.

Historical/adapter evidence:

- Fee calculation request can include `cardNum` and/or `plateLicense`.
- Existing adapter validates that at least one of `cardNum` or `plateLicense` is supplied and each is within local length constraints.
- Existing adapter maps successful calculate data into a vendor-authoritative session and tariff quote in normal mode.
- Existing adapter treats multiple returned records as ambiguous rather than guessing a session.
- Existing adapter treats missing fee or missing parking-in time as adapter error.
- Historical UAT confirmed normal live resolve returned vendor-authoritative parking-session and tariff data and did not create payment, exit, or gate side effects.

Open:

- Which identifier is correct for ticket-only fee calculation in the target deployment.
- Whether `cardNum` means printed ticket number, card identifier, internal credential, or another value.
- Whether plate-based calculation is enabled and intended for the current ExitPass flow.
- Exact fee currency/rounding/cutoff semantics from the vendor guide were not extracted from the PDF.

## 12. Payment Acknowledgment / Parking Fee Confirmation Notes

Confirmed API area:

- Vendor guide confirmed endpoint: `POST /artemis/api/vehicle/v1/parkingfee/confirm`.
- The PDF outline labels this area as confirming parking fee payment and allowing exit.

Historical/adapter evidence:

- Local diagnostics explicitly disable confirmation by default and warn not to call `parkingfee/confirm` in read-only validation.
- Existing adapter treats confirmation as a mutating operation guarded by a local enable flag.
- Existing adapter confirmation request includes `plateLicense`, `cardNum`, `immediatelyLeave`, and `fee`.
- Existing adapter confirmation response mapping expects `fee` and `feeTime`.

ExitPass authority constraints:

- Vendor payment acknowledgment is downstream of Central PMS payment finality and fiscal handling.
- HCP confirmation must not create ExitPass payment finality.
- HCP confirmation must not issue fiscal documents.
- HCP confirmation must not issue ExitAuthorization.
- Whether acknowledgment is synchronous, queued, retried, or exit-blocking remains a later design question.

## 13. Response Success / Error / Pagination / Date Range Notes

Success and errors:

- Historical and adapter evidence treat `code = 0` as success.
- Historical ticket-only validation lists common codes: `0` success, `68` signature authentication failed, `128` vehicle/resource not found, and `1` unknown/internal request error.
- Existing adapter maps nonzero HikCentral codes to adapter error categories, preserving the vendor code in a sanitized internal error code.
- Existing adapter treats HTTP 5xx and request timeout as retryable unavailable for live resolve/confirmation.
- Existing adapter treats ambiguous fee calculation results as ambiguous, not as a guessed session.

Pagination:

- Passageway record evidence uses `pageIndex` and `pageSize`.
- Local projection controls bound page size to 1 through 500 in the existing client.
- Parking lot list diagnostics use `pageNo` and `pageSize`.
- Cross-record diagnostics use `pageNo` and `pageSize`.
- Exact vendor pagination limits and ordering behavior should be confirmed from full vendor documentation or vendor support.

Date range:

- Passageway and parking-space record diagnostics use `beginTime` and `endTime` under `queryInfo`.
- Cross-record diagnostics use `startTime` and `endTime`.
- Local evidence formats timestamps as `yyyy-MM-ddTHH:mm:sszzz`, for example with `+08:00`.
- Expected retention, maximum date range, and timezone requirements are not confirmed by available extracted vendor text.

## 14. API Availability / License / Deployment Constraints

- HCP parking APIs may depend on HikCentral deployment modules, OpenAPI exposure, application permissions, and license/module enablement.
- Historical production controls require confirming the target HikCentral base URL scheme, port, host reachability, AppKey/AppSecret permission, and parking APIs before enabling production polling.
- AppKey permissions must cover only required functions: parking lot list, passageway records, fee calculation, and fee confirmation if approved/required.
- The connector profile should include an API availability checklist before production enablement.
- Confirmation/payment acknowledgment must be disabled by default until the deployment requirement, safety behavior, and retry/exit-block policy are approved.

## 15. Gaps / Unknowns / Vendor Questions

Vendor questions:

- What exactly is `cardNum` in parking fee and passageway contexts?
- Is `cardNum` the printed ticket number, card identifier, internal credential, or another value?
- Which API should be used for ticket-only fee calculation?
- Which field from a physical printed ticket maps to the active session lookup key accepted by `parkingfee/calculate`?
- Is there a barcode or QR payload field on printed tickets that differs from the visible printed ticket number?
- Which API confirms payment back to HikCentral?
- Is `parkingfee/confirm` required before exit in this deployment?
- Does `parkingfee/confirm` itself allow exit, mark the ticket paid, both, or only create a vendor-side acknowledgment?
- Are the parking APIs licensed and enabled in the deployed HikCentral instance?
- What HikCentral app permissions are required for parking lot list, passageway records, fee calculation, and fee confirmation?
- What is the expected retention and maximum date range for passageway record queries?
- What identifiers are stable across entry, fee calculation, payment acknowledgment, and exit?
- What fields should be treated as immutable vendor references?
- What error codes indicate no ticket, already paid, exited, expired, invalid payment confirmation, duplicate confirmation, or amount mismatch?
- What are the maximum supported `pageSize`, date range, and request rate for production polling?
- Are timestamps always returned with offset, and what timezone should requests use for this deployment?
- Is plate-based fee calculation enabled, and is it approved for the current ExitPass flow?

Source gaps:

- Full PDF field tables were not extractable locally.
- No repository-local OpenAPI YAML/API collection for parking APIs was found.
- No external local documentation folders were found at the checked paths.

## 16. Recommended HikCentral Connector Profile Sections

The later HikCentral Connector Profile should include these HCP-specific sections:

- Source authority and availability matrix.
- Authentication and signature posture, with no secrets or raw signatures.
- HCP deployment prerequisites, modules, licenses, permissions, base URL, port, and network checks.
- HCP parking lot identity and AdapterMapping, explicitly stating `parkingLotIndexCode` is not ExitPass `site_id`.
- Parking lot discovery using `parkinglot/list`.
- Passageway polling/projection using `parkinglot/passageway/record`.
- Projection field mapping and identity strategy, including `guid`, `parkingLotIndexCode`, `cardNum`, plate, enter time, passageway, and lane fields.
- Projection freshness and one-minute planning baseline.
- Live fee calculation using `parkingfee/calculate`, limited to confirmed identifiers.
- Ticket/card/plate identifier policy and unresolved `cardNum` meaning.
- Payment acknowledgment using `parkingfee/confirm` only if approved and confirmed for deployment.
- Success/error/pagination/date-range behavior.
- API availability and operational validation checklist.
- Vendor questions and deployment sign-off checklist.
- Explicit exclusions: projection financial truth, direct gate authority, fiscal issuance, and ExitAuthorization.

## 17. Summary for Lead

- The local HikCentral Professional OpenAPI V3.1.0 PDF confirms the relevant parking API areas by outline/title: `parkinglot/list`, `parkinglot/passageway/record`, `parkingspace/record`, `parkingfee/calculate`, and `parkingfee/confirm`.
- Local extraction could not recover full PDF field tables; field-level notes are based on historical UAT/runbooks and existing adapter evidence and should be treated accordingly.
- `parkingLotIndexCode` is a vendor identity only and must map through AdapterMapping; it is not ExitPass `site_id`.
- Passageway records are suitable for operational projection and degraded visibility only. They are not tariff, payment, fiscal, or exit truth.
- `parkingfee/calculate` is the confirmed live fee calculation API area, but ticket-only support is not confirmed because local tests did not prove printed ticket numbers map to `cardNum`.
- `parkingfee/confirm` is the confirmed vendor payment acknowledgment/fee confirmation API area, but it is mutating and must stay disabled unless the deployment and ExitPass design explicitly approve its use.
- Highest-priority vendor questions are `cardNum` meaning, correct ticket-only lookup key, whether confirmation is required before exit, available error codes, and license/permission enablement in the deployed HikCentral instance.
- No secrets are included in this input pack.
