# ExitPass Vendor PMS Connector Orchestration Plan

Version: v1.0
Status: Orchestration workspace prepared
Date: 2026-07-01
Owner: Lead Connector Design agent

## 1. Purpose

This plan prepares the orchestration workspace for two companion technical design documents to be drafted later:

- Generic Vendor PMS Connector System Design.
- HikCentral Connector Profile.

This task does not draft either final design. It defines source inputs, authority guardrails, specialist input-pack ownership, review gates, and validation rules for the later Lead integration pass.

## 2. Target Documents

The final documents to be drafted later by the Lead are:

| Target document | Purpose | Current status |
| --- | --- | --- |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Generic connector design for Vendor PMS/HCP integration patterns, connector runtime boundaries, projection, vendor acknowledgment, security, observability, and operational controls. | Not drafted in this task. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HikCentral-specific connector profile covering HCP object mapping, ParkingLotIndexCode handling, passageway polling, live resolve behavior, projection freshness, and HCP-specific source constraints. | Not drafted in this task. |

## 3. Approved Baseline Inputs

The later connector designs shall use these approved v1.3 documents as primary inputs:

| Source | Use |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Business authority model, Site/Site Group semantics, VendorSystem and AdapterMapping business requirements, normal/degraded resolve boundaries. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Approved system-level architecture baseline, component responsibilities, connector posture, trust boundaries, topology diagrams, and deferred companion-design scope. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | BRD baseline approval posture for System Design and downstream companion designs. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Assisted and continuity terminal interaction boundaries, discount capture boundaries, payment/fiscal/exit authority exclusions. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Degraded operation, projection use, activation controls, manual release, reconciliation, and post-restoration review guardrails. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator visibility, connector health, projection freshness, fiscal exception review, and non-payment governance boundaries. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Reporting and dashboard distinction between operational projection visibility and financial truth. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Fiscal issuance boundary, POS Server authority, and fiscal issuance before normal ExitAuthorization. |

The later connector designs shall also use these planning artifacts:

| Source | Use |
| --- | --- |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved v1.3 connector, authority, Site, projection, normal/degraded mode, and fiscal sequencing decisions. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Downstream questions for connector topology, acknowledgment behavior, projection freshness, and degraded controls. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Source-to-target impact map for VendorSystem, AdapterMapping, HikCentral mapping, projection, degraded mode, API, database, engineering, and UAT impacts. |

Vendor/HikCentral source availability found during workspace setup:

| Source location | Availability |
| --- | --- |
| `docs/vendor/hikcentral/HikCentral Professional OpenAPI_Developer Guide_V3.1.0_20260130.pdf` | Found in repository. Use for HikCentral API discovery input pack. |
| `docs/hikcentral-*.md` and `docs/diagrams/hikcentral-*.puml` | Found in repository. Use as historical validation, runbook, smoke/UAT, and diagram-planning references only. |
| `D:\Docs\ExitPass\HikCentral` | Not found locally during setup. |
| `D:\Docs\ExitPass\Vendor PMS` | Not found locally during setup. |
| `D:\Docs\ExitPass\Parking` | Not found locally during setup. |

Source availability issue: if later specialists require vendor API collections beyond the local HikCentral OpenAPI Developer Guide, they must report the missing collection in their input pack instead of inventing API behavior.

## 4. Vendor PMS Connector Design Scope

The generic Vendor PMS Connector System Design shall cover, at companion technical-design level:

- VendorSystem as configured Vendor PMS/HCP instance.
- AdapterMapping between ExitPass Site and vendor-side parking object.
- Adapter codebase versus deployed connector instance separation.
- Connector instance per Vendor PMS/HCP instance unless later design approves an alternate model.
- Runtime vendor object key: `vendorSystemId + vendorObjectType + vendorObjectRef`.
- Normal live vendor session/fee resolve behavior.
- Vendor payment acknowledgment behavior as a design question requiring explicit queue/retry/synchronous decision.
- Projection and polling ingestion boundaries.
- Connector health, freshness, stale, unavailable, ambiguous, and degraded-state signaling.
- Security, credentials, network trust, and deployment topology options.
- Observability and operational controls for support, Operator Console, Management Dashboard, and Continuity workflows.
- Failure handling and reconciliation tagging where connector state affects degraded operation.

The generic design shall not define final database columns, final endpoint paths, final DTO payloads, or engineering implementation details before the appropriate Database/API/Engineering Pack phase.

## 5. HikCentral Connector Profile Scope

The HikCentral Connector Profile shall cover, at vendor-profile level:

- HCP-specific API source references and availability.
- HCP ParkingLotIndexCode handling as vendor-side identity only.
- Mapping from HCP parking object identity through AdapterMapping to ExitPass Site.
- One-minute HCP passageway polling planning baseline.
- HCP passageway records as operational projection input, not financial truth.
- HCP live fee calculation / parking fee resolve behavior where supported by confirmed API capability.
- HCP vendor payment acknowledgment behavior where supported by confirmed API capability.
- HCP connector health and projection freshness signals.
- HCP deployment topology options and source constraints.
- Known gaps in local vendor documentation or API collection availability.

The HikCentral profile shall not treat HCP ParkingLotIndexCode as ExitPass `site_id`, shall not make HCP projection financial truth, and shall not bypass Central PMS payment/fiscal/exit authority.

## 6. Authority Model Guardrails

All specialist input packs and later Lead documents must preserve these guardrails:

- Vendor PMS / HCP remains authority for raw parking session lifecycle and normal tariff computation.
- Central PMS remains authority for payment-linked platform state, TariffSnapshot recording, payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- Vendor connector does not declare payment finality.
- Vendor connector does not issue fiscal documents.
- Vendor connector does not issue ExitAuthorization.
- Vendor connector does not open gates directly unless a later approved gate profile explicitly assigns a controlled integration boundary.
- Projection/polling data is operational visibility and controlled degraded support only.
- HCP ParkingLotIndexCode is vendor-side identity and must not be treated as ExitPass `site_id`.
- VendorSystem, AdapterMapping, adapter codebase, and connector instance must remain distinct.
- Fiscal issuance must remain under resolved Site POS Server authority.
- Gate/exit execution must consume Central PMS authorization and must not bypass Central PMS authorization.

## 7. Specialist Input-Pack List

Specialist input-pack files to be created later:

| Input pack | Assigned focus | Expected output |
| --- | --- | --- |
| `docs/v1.3/vendor-pms-connector/input-packs/01_authority_scope_guard.md` | Authority boundaries, approved baseline references, source contradictions, non-authority list. | Guardrail matrix and contradiction log. |
| `docs/v1.3/vendor-pms-connector/input-packs/02_hikcentral_api_discovery.md` | HikCentral OpenAPI source discovery, confirmed API areas, missing collections, profile-specific unknowns. | Source inventory, API capability notes, unresolved HCP questions. |
| `docs/v1.3/vendor-pms-connector/input-packs/03_connector_workflow_and_state.md` | Normal resolve, projection ingestion, vendor acknowledgment, degraded handoff, retry/failure state. | Workflow/state recommendations without final endpoint or database design. |
| `docs/v1.3/vendor-pms-connector/input-packs/04_security_credentials_trust.md` | Credential handling, connector trust, deployment topology, network/security assumptions. | Security/trust requirements and open questions. |
| `docs/v1.3/vendor-pms-connector/input-packs/05_observability_projection_operations.md` | Health, freshness, polling status, stale/ambiguous projection, operator/dashboard visibility, runbook needs. | Observability model, alert candidates, operational handoff notes. |
| `docs/v1.3/vendor-pms-connector/input-packs/06_diagram_planning.md` | Diagram inventory and proposed BRD/System Design-level diagrams for final connector docs. | Diagram plan only; no final diagrams unless Lead later authorizes. |

## 8. File Ownership Rules

- Specialist agents may create only their assigned input-pack file.
- Specialist agents must not edit final documents.
- Specialist agents must not edit approved BRDs or System Design.
- Specialist agents must not create API/database/engineering implementation details.
- Specialist agents must not create DOCX files.
- Specialist agents must not modify source code, database schema, API contracts, diagrams, or existing approved documents.
- Lead integrates final documents only after all input packs exist.
- Any contradiction must be reported in the relevant input pack, not silently corrected in approved sources.

## 9. Lead Integration Rules

The Lead Connector Design agent shall:

- Review all specialist input packs before drafting final connector documents.
- Preserve approved v1.3 authority boundaries.
- Keep the generic Vendor PMS Connector System Design separate from the HikCentral Connector Profile.
- Keep generic design reusable across vendor PMS/HCP systems.
- Keep HikCentral-specific details in the HikCentral profile.
- Carry unresolved questions into the final documents rather than deciding them silently.
- Avoid final endpoint paths, DTOs, database columns, SQL routines, deployment scripts, and implementation internals unless already approved for the companion technical-design phase.
- Reference source availability issues explicitly.
- Stop and report if an approved source contradicts another approved source in a way that affects authority, fiscal, payment, exit, or degraded-operation control.

## 10. Out-of-Scope Items

This orchestration setup does not include:

- Final Vendor PMS Connector System Design.
- Final HikCentral Connector Profile.
- ExitPass System Design v1.3 changes.
- Approved BRD changes.
- Database Design / Database Delta.
- API Contract Pack v1.3.
- Engineering Pack v1.3.
- Test/UAT Pack.
- Operations Runbook Pack.
- Source code changes.
- Database/schema changes.
- API contract changes.
- DOCX output.
- Diagram creation or regeneration.
- Commit creation.

## 11. Review Gates

| Gate | Required condition |
| --- | --- |
| Gate 1: Workspace setup | Folders and orchestration plan exist; no final connector docs drafted. |
| Gate 2: Specialist input packs | All six specialist input-pack files exist and follow file ownership rules. |
| Gate 3: Source availability review | HikCentral API guide and any API collections are inventoried; missing sources are reported. |
| Gate 4: Authority review | Guardrails confirm no connector payment finality, fiscal issuance, ExitAuthorization, or gate bypass authority. |
| Gate 5: Lead synthesis | Lead reconciles specialist inputs and open questions before drafting final documents. |
| Gate 6: Final design readiness | Generic connector design and HikCentral profile can be drafted without changing approved BRDs or System Design. |

## 12. Validation Commands

Run the following after orchestration setup:

```powershell
git status --short --untracked-files=all
git diff --check
```

Expected setup result:

- Only Markdown orchestration files/folders under `docs/v1.3/vendor-pms-connector/` and `docs/v1.3/hikcentral-connector/` are added.
- No source code changes.
- No database/schema changes.
- No API contract changes.
- No DOCX files.
- No final connector design drafts.
- No diagram regeneration.

## 13. Next Step

Create the six specialist input-pack files in sequence or assign them to specialist agents. Each specialist must create only their assigned input-pack file and must report contradictions or missing sources in that file.

After all input packs exist, the Lead Connector Design agent may draft:

- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
