# ExitPass v1.3 Documentation Outline

Version: v1.3 planning artifact  
Status: Draft for documentation planning only  
Generated: 2026-07-01

## Purpose

This outline defines the planned ExitPass v1.3 documentation set and drafting sequence. ExitPass v1.3 is a controlled minor-version documentation update. It must preserve the v1.2 authority model while adding v1.3 clarifications for Site Group/Site semantics, centralized WebPay, vendor connector modeling, POS/Invoicing, Operator Console, Continuity, and Reporting.

## Locked Writing Order

All v1.3 documentation work must be monitored against this order:

| Order | Documentation layer | Drafting rule |
| --- | --- | --- |
| 1 | v1.3 planning artifacts | Create decision log, outline, open questions, and source impact map first. |
| 2 | ExitPass BRD v1.3 | Draft only after planning artifacts are accepted. |
| 3 | Companion BRDs | Draft after the core BRD establishes shared scope and terminology. |
| 4 | ExitPass System Design v1.3 | Draft after BRD v1.3 stabilizes the business baseline. |
| 5 | Companion technical designs | Draft after the relevant companion BRDs and core System Design direction are aligned. |
| 6 | Database/API/Engineering Pack v1.3 | Draft after business and system design impacts are accepted. |

The next document to draft after this planning layer is **ExitPass BRD v1.3**. Companion BRDs should follow only after the core BRD confirms the shared Site Group/Site, authority, payment, connector, POS, continuity, and reporting vocabulary.

## A. Core v1.3 Baseline Documents

| Document | Purpose | Primary source baseline | Draft timing |
| --- | --- | --- | --- |
| ExitPass BRD v1.3 | Controlled business baseline update for v1.3 scope, actors, authority, business flows, and module boundaries. | ExitPass BRD v1.2; Operator Console BRD v1.0; v1.3 planning artifacts. | First after planning acceptance. |
| ExitPass System Design v1.3 | Technical architecture update preserving v1.2 authority while clarifying centralized WebPay, Site/Site Group, connector, projection, degraded mode, POS, Continuity, and Reporting boundaries. | ExitPass System Design v1.2; v1.3 BRD; companion BRDs. | After core and companion BRD alignment. |
| ExitPass Database Design v1.3 | Database impact update and delta planning for v1.3 concepts. | ExitPass Database Design v1.2; DDL v1.2 Data Dictionary; Constraint Matrix and Index Inventory. | After System Design v1.3. |
| ExitPass API Contract Pack v1.3 | API impact update for core and companion service boundaries without violating authority ownership. | ExitPass API Contract Pack v1.2; System Design v1.3; companion technical designs. | After System Design and database delta direction. |
| ExitPass Engineering Pack v1.3 | Implementation planning, rollout, operations, observability, test, and engineering handoff guidance. | ExitPass Engineering Pack v1.2; approved v1.3 design and API/database documents. | Last core implementation support layer. |

## B. Companion Business Documents

| Document | Purpose | Draft timing |
| --- | --- | --- |
| ExitPass POS/Invoicing BRD v1.0 | Platform-wide fiscal issuance requirements for WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels routed through the Site POS Server. | After ExitPass BRD v1.3 establishes shared authority and Site terminology. |
| ExitPass Continuity BRD v1.0 | Business requirements for degraded mode, continuity operations, controlled projection use, activation authority, audit, and recovery. | After core BRD defines normal and degraded resolve policy. |
| ExitPass Operator Console BRD update | Update existing Operator Console requirements to align with v1.3 module positioning, POS/Invoicing, Continuity, and projection health. | After core BRD and Continuity/POS scope are clear. |
| ExitPass Assisted Payment Terminal BRD v1.0 | Business requirements for Cashier POS and EC Device as one app family with different operating modes. | After POS/Invoicing and Continuity business boundaries are agreed. |
| ExitPass Management Dashboard and Reporting BRD v1.0 | Business requirements for management dashboard, operational reporting, fiscal references, site/site-group views, and analytics scope. | After core BRD confirms reporting as a companion domain. |

## C. Companion Technical Documents

| Document | Purpose | Draft timing |
| --- | --- | --- |
| ExitPass POS Server System Design v1.0 | Technical design for Site-level POS Server fiscal authority, issuance sequence, counters, reports, EJ, POSLog, exports, audit, recovery, and integration boundaries. | After POS/Invoicing BRD approval and core System Design alignment. |
| ExitPass POS Server API Contract v1.0 | API contract for POS Server fiscal issuance, status, digital SI URL, reports, configuration, audit, and channel/terminal interactions. | After POS Server System Design. |
| ExitPass Vendor PMS Connector System Design v1.0 | Technical design for VendorSystem, AdapterMapping, connector instance, projection feed, health, retry, and vendor acknowledgment handling. | After core System Design defines connector model. |
| ExitPass HikCentral Connector Profile v1.0 | HCP-specific connector profile including ParkingLotIndexCode mapping, one-minute passageway polling, projection freshness, and HCP topology. | After Vendor PMS Connector System Design. |
| ExitPass Assisted Payment Terminal System Design v1.0 | Technical design for Cashier POS and EC Device operating modes, terminal identity, POS Server routing, and continuity behavior. | After Assisted Payment Terminal BRD. |
| ExitPass Continuity System Design v1.0 | Technical design for degraded mode activation, projection use, freshness controls, recovery, audit, and continuity terminal coordination. | After Continuity BRD and core System Design. |

## D. Database and Implementation Support Documents

| Document | Purpose | Draft timing |
| --- | --- | --- |
| ExitPass v1.3 Database Delta Design | Defines approved database deltas needed for v1.3 after BRD/System Design agreement. | After System Design v1.3 and companion technical designs identify accepted data impacts. |
| ExitPass v1.3 Data Dictionary / Constraint Matrix Refresh Plan | Plans updates to the v1.2 data dictionary, constraints, indexes, ownership notes, and projection/fiscal references. | After Database Delta Design. |
| ExitPass v1.3 Test and UAT Pack | Defines verification coverage for authority model, WebPay, Site/Site Group, connectors, POS issuance before exit, Continuity, Operator Console, and reporting. | After API/database/engineering impacts are known. |
| ExitPass v1.3 Operations Runbook Pack | Defines operational procedures for connector health, projection freshness, degraded mode, POS Server outages, fiscal issuance exceptions, and incident handling. | After System Design and Engineering Pack stabilize. |

## Source Reference Set

| Source folder | Reference documents |
| --- | --- |
| `D:\Docs\ExitPass\v1.2` | ExitPass BRD v1.2; System Design v1.2; Database Design v1.2; API Contract Pack v1.2; Engineering Pack v1.2; Operator Console BRD v1.0; DDL v1.2 Data Dictionary; DDL v1.2 Constraint Matrix and Index Inventory; Philippine Parking Statutory Discount Local Ordinances Detailed List. |
| `D:\Docs\ExitPass\POS` | BIR POS Accreditation Requirements; RMO 24-2023 and annexes; Hikvision autopay BIR gap/checklist materials; ARTS POSLog references; sample e-journal. |

## Draft-First Guidance

| Priority | Document | Reason |
| --- | --- | --- |
| 1 | ExitPass BRD v1.3 | It anchors minor-version scope, terminology, authority boundaries, and companion document split. |
| 2 | Companion BRDs | They prevent POS, Continuity, Operator Console, Assisted Payment Terminal, and Reporting detail from bloating the core BRD. |
| 3 | ExitPass System Design v1.3 | It converts accepted BRD scope into technical boundaries without finalizing database/API details too early. |
| 4 | Companion technical designs | They provide focused technical depth for POS Server, connectors, HikCentral, assisted terminals, and Continuity. |
| 5 | Database/API/Engineering Pack v1.3 | These should follow after authority and design boundaries are stable. |
