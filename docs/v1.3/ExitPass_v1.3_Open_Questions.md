# ExitPass v1.3 Open Questions

Version: v1.3 planning artifact  
Status: Draft for documentation planning only  
Generated: 2026-07-01

## Purpose

This file captures unresolved or confirmation-needed items for the ExitPass v1.3 documentation stream. Already-decided authority, Site, connector, POS/Invoicing, Operator Console, Continuity, and Reporting direction is not repeated as a blocker.

## Open Questions

| ID | Question | Current planning posture | Blocks |
| --- | --- | --- | --- |
| V13-Q001 | What is the exact public WebPay URL slug registry structure? | Open. WebPay is centralized with site-specific URLs, but the registry shape is not finalized. | WebPay BRD wording, database delta, API contract, operations runbook. |
| V13-Q002 | Do WebPay URL slugs resolve to Site Group, Site, or both? | Open. Site Group is lookup/payment scope and Site is operational boundary; exact URL resolution target needs confirmation. | WebPay flow, Site resolution, POS Server routing, reporting attribution. |
| V13-Q003 | Do physical parking lots or clusters need first-class tables in v1.3, or do they remain operational metadata? | Open. Physical lots are not automatically ExitPass Sites. | Database delta, reporting model, vendor mapping model. |
| V13-Q004 | What is the exact degraded tariff freshness threshold? | Open. Degraded mode may use Central PMS projection only under explicit controls. | Continuity BRD, Continuity System Design, UAT acceptance criteria. |
| V13-Q005 | What is the exact POS Server deployment and registration model? | Open. Site-level POS Server is approved, but deployment registration details remain unresolved. | POS Server System Design, database delta, API Contract Pack, operations runbook. |
| V13-Q006 | What is the exact POS Server API ownership and service boundary? | Open. POS Server owns fiscal issuance; Central PMS owns payment finality and ExitAuthorization. API ownership still needs service boundary confirmation. | POS Server API Contract, core API Contract Pack v1.3, Engineering Pack. |
| V13-Q007 | Is POS Server a module under Central PMS or a separate service? | Open. Fiscal authority is separate from Central PMS control authority, but deployment/module packaging is not finalized. | System Design, deployment architecture, ownership, operations. |
| V13-Q008 | Who has exact BCP activation authority for Continuity Terminal? | Open. Continuity is a formal platform capability, but activation authority and approval workflow need confirmation. | Continuity BRD, Operator Console update, operations runbook, audit requirements. |
| V13-Q009 | In each deployment topology, does the HCP connector push to Central PMS or does Central PMS pull from a connector endpoint? | Open. Connector instance per Vendor PMS/HCP instance is approved; topology-specific data direction remains unresolved. | Vendor PMS Connector System Design, HikCentral profile, security model, operations runbook. |
| V13-Q010 | Is vendor payment acknowledgment synchronous or queued/retried per Site? | Open. Vendor PMS remains lifecycle/tariff authority in normal mode; acknowledgment mechanics are not finalized. | System Design, connector design, API Contract Pack, failure handling. |
| V13-Q011 | How should HCP connector health and projection freshness be modeled? | Open. One-minute polling is approved, but health states, freshness metrics, alerts, and stale behavior need definition. | HikCentral connector profile, Operator Console update, Engineering Pack, runbook. |
| V13-Q012 | Should Site Group be user-facing as Payment Scope or Lookup Scope while retaining `site_group` in the database? | Open. Business/user-facing terminology needs confirmation without changing the database concept prematurely. | BRD wording, WebPay UX copy, Operator Console, reporting labels. |
| V13-Q013 | Should dashboard/reporting requirements belong in the core BRD or only in a companion BRD? | Open. Management Dashboard and Reporting is planned as its own companion BRD, but the core BRD may need a concise anchor section. | Core BRD outline, companion BRD scope. |
| V13-Q014 | Should POS Server technical documents continue now or pause until core v1.3 planning is accepted? | Open process question. Existing POS artifacts are present, but the locked order says planning and core BRD alignment should control downstream work. | Documentation sequencing and review plan. |

## Not Open Blockers

| Topic | Current decision |
| --- | --- |
| v1.3 version posture | v1.3 is a minor version update, not v2.0. |
| Core authority model | Preserved from v1.2. |
| Central PMS payment finality | Central PMS remains authority for payment-linked platform control state. |
| Payment Orchestrator finality | Payment Orchestrator reports verified provider outcomes but does not declare platform payment finality. |
| WebPay finality | WebPay does not declare payment finality. |
| POS Server ExitAuthorization | POS Server does not issue ExitAuthorization. |
| Gate execution | Gate/exit execution must not bypass Central PMS authorization. |
| Site Group meaning | Customer lookup/payment scope. |
| Site meaning | Reporting, contract, Vendor PMS mapping, POS Server, and operational boundary. |
| Physical parking lot meaning | Not automatically an ExitPass Site. |
| HCP ParkingLotIndexCode | Not an ExitPass `site_id`. |
| HCP polling interval | One-minute passageway polling baseline. |
| Polling feed authority | Operational projection, not financial truth. |
| Fiscal issuance sequence | Fiscal issuance must succeed before Central PMS issues ExitAuthorization. |
