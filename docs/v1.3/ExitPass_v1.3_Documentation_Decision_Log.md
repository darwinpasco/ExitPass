# ExitPass v1.3 Documentation Decision Log

Version: v1.3 planning artifact  
Status: Draft for documentation planning only  
Generated: 2026-07-01

## Purpose

This log records approved documentation decisions for the ExitPass v1.3 stream before drafting the v1.3 BRD, System Design, companion documents, database delta, API Contract Pack, or Engineering Pack.

ExitPass v1.3 is a controlled minor-version documentation update. It preserves the v1.2 authority model and clarifies site, vendor connector, payment, fiscal issuance, continuity, and reporting planning boundaries.

## Authority Model Decisions

| ID | Decision | Status | Documentation impact |
| --- | --- | --- | --- |
| V13-D001 | ExitPass v1.3 is a minor version update, not a v2.0 redesign. | Approved | v1.3 documents must update and clarify the v1.2 baseline instead of replacing the product architecture. |
| V13-D002 | The core authority model remains unchanged from v1.2. | Approved | All v1.3 documents must preserve Central PMS, Vendor PMS, Payment Orchestrator, WebPay, POS Server, and gate authority boundaries. |
| V13-D003 | Vendor PMS remains authority for raw parking session lifecycle and tariff computation in normal mode. | Approved | Normal resolve mode must use live Vendor PMS/HCP lifecycle and fee calculation. |
| V13-D004 | Central PMS remains authority for payment-linked platform control state. | Approved | Central PMS owns platform payment finality and downstream control state. |
| V13-D005 | Central PMS owns ParkingSession projection, TariffSnapshot, PaymentAttempt, PaymentConfirmation, and ExitAuthorization. | Approved | Database, API, and engineering documents must preserve these ownership boundaries. |
| V13-D006 | Payment Orchestrator performs provider interaction and reports verified provider outcomes. | Approved | Payment Orchestrator must not declare platform payment finality. |
| V13-D007 | WebPay must not declare payment finality. | Approved | WebPay is a payment channel and customer experience surface, not payment finality authority. |
| V13-D008 | POS Server must not issue ExitAuthorization. | Approved | POS Server owns fiscal issuance only; Central PMS remains ExitAuthorization authority. |
| V13-D009 | Gate and exit execution must not bypass Central PMS authorization. | Approved | Gate consumers must use Central PMS-issued ExitAuthorization. |

## Product and Site Model Decisions

| ID | Decision | Status | Documentation impact |
| --- | --- | --- | --- |
| V13-D010 | Central PMS is centralized. | Approved | Core BRD and System Design must describe Central PMS as platform control authority across Sites and Site Groups. |
| V13-D011 | WebPay is centralized with site-specific public URLs. | Approved | WebPay URL planning must distinguish centralized WebPay service from site-specific entry points. |
| V13-D012 | Site Group means customer lookup and payment scope. | Approved | BRD terminology must clarify Site Group as lookup/payment scope, not necessarily one physical lot. |
| V13-D013 | Site means reporting, contract, Vendor PMS mapping, POS Server, and operational boundary. | Approved | BRD, System Design, database delta, API, reporting, and POS docs must use Site as the operating and integration boundary. |
| V13-D014 | Default case: one Site Group has one Site. | Approved | Documents should state this as the ordinary configuration. |
| V13-D015 | Special case: one Site Group may contain multiple Sites. | Approved | Documents must allow shared customer lookup/payment scope across multiple operational Sites where configured. |
| V13-D016 | Physical parking lots are not automatically ExitPass Sites. | Approved | Physical lot/cluster references should not create first-class Site semantics unless they match the operational/vendor boundary. |
| V13-D017 | Vendor PMS/HCP boundary determines ExitPass Site modeling. | Approved | Site modeling must follow the Vendor PMS/HCP authority and mapping boundary. |

## Vendor PMS and Connector Decisions

| ID | Decision | Status | Documentation impact |
| --- | --- | --- | --- |
| V13-D018 | VendorSystem represents a Vendor PMS/HCP instance. | Approved | Database and design documents should model vendor system instances separately from Sites. |
| V13-D019 | AdapterMapping connects an ExitPass Site to a vendor-side parking object. | Approved | Database/API planning must preserve explicit mapping between ExitPass Site and vendor object. |
| V13-D020 | HCP ParkingLotIndexCode is not an ExitPass `site_id`. | Approved | HikCentral connector profile must prevent direct reuse of vendor lot codes as ExitPass Site identifiers. |
| V13-D021 | Runtime vendor object key is `vendorSystemId + vendorObjectType + vendorObjectRef`. | Approved | Connector and integration contracts should use the compound runtime vendor object identity. |
| V13-D022 | Adapter codebase and connector instance are separate concepts. | Approved | System Design and Engineering Pack must separate reusable adapter implementation from deployed connector instances. |
| V13-D023 | Each Vendor PMS/HCP instance should have a connector instance. | Approved | Deployment and operations docs must plan per-vendor-instance connector registration, monitoring, and failure handling. |
| V13-D024 | HCP connector should poll passageway records every minute. | Approved | HikCentral connector profile must include one-minute polling as the planning baseline. |
| V13-D025 | Polling feed is an operational projection, not financial truth. | Approved | Projection data may support session awareness and operations, but must not override Vendor PMS fee truth or Central PMS payment finality. |

## Resolve Mode Decisions

| ID | Decision | Status | Documentation impact |
| --- | --- | --- | --- |
| V13-D026 | Normal mode uses live Vendor PMS/HCP fee calculation. | Approved | BRD and System Design must treat live vendor fee calculation as the ordinary resolve path. |
| V13-D027 | Degraded mode may use Central PMS session projection only under explicit controls. | Approved | Continuity and System Design documents must define controls, freshness thresholds, activation authority, and audit expectations before use. |

## POS, Fiscal Issuance, and Payment Channel Decisions

| ID | Decision | Status | Documentation impact |
| --- | --- | --- | --- |
| V13-D028 | POS/Invoicing is platform-wide. | Approved | POS/Invoicing must apply beyond AutoPay Machines. |
| V13-D029 | One Site or parking operation boundary should have one Site-level POS Server. | Approved | Site resolution determines fiscal POS Server routing. |
| V13-D030 | WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels route fiscal issuance through the resolved Site POS Server. | Approved | Payment channel docs must treat channels/terminals as children of the Site POS Server. |
| V13-D031 | Fiscal issuance must succeed before Central PMS issues ExitAuthorization. | Approved | Payment-to-exit flow must require verified payment finality, fiscal issuance, fiscal reference recording, then ExitAuthorization. |
| V13-D032 | Cashier POS and EC Device should be modeled as one Assisted Payment Terminal app family with different operating modes. | Approved | Companion BRD and technical design should avoid separate product families unless future requirements justify it. |
| V13-D036 | Cashier-Assisted Terminal mode supports statutory discount validation as part of the assisted payment workflow. Assisted Payment Terminal captures and submits validation data, but Central PMS / Discount workflow owns policy resolution, validation persistence, and payable-basis update. Operator Console and Assisted Payment Terminal remain separate modules/apps. | Approved | BRD v1.3 must document cashier-facing capture, backend validation authority, non-payment Operator Console boundary, and separate permission boundaries. Companion documents must not make the terminal an independent statutory discount policy engine. |

## Platform Module Decisions

| ID | Decision | Status | Documentation impact |
| --- | --- | --- | --- |
| V13-D033 | Operator Console is a formal platform module. | Approved | Operator Console BRD update must be part of the v1.3 companion business document set. |
| V13-D034 | ExitPass Continuity is a formal platform capability. | Approved | Continuity BRD and System Design must be planned as companion documents. |
| V13-D035 | Management Dashboard and Reporting should be planned as its own companion BRD. | Approved | Reporting requirements should be isolated enough to prevent overloading the core BRD. |

## Writing Order Control

| Order | Documentation step | Status |
| --- | --- | --- |
| 1 | v1.3 planning artifacts | Current task |
| 2 | ExitPass BRD v1.3 | Next after planning acceptance |
| 3 | Companion BRDs | After core BRD direction is accepted |
| 4 | ExitPass System Design v1.3 | After BRD baseline |
| 5 | Companion technical designs | After relevant BRD and core design alignment |
| 6 | Database/API/Engineering Pack v1.3 | Last planning-to-implementation documentation layer |

## Non-Decisions

| ID | Non-decision | Note |
| --- | --- | --- |
| V13-ND001 | No source code changes are proposed by this planning task. | Documentation planning only. |
| V13-ND002 | No database schema changes are proposed by this planning task. | Database delta is a later document. |
| V13-ND003 | No API endpoints, DTOs, or contracts are finalized here. | API impacts are only identified for later API Contract Pack work. |
| V13-ND004 | No DOCX files are created here. | Markdown planning artifacts only. |
| V13-ND005 | No final BRD or System Design prose is drafted here. | This task creates the planning layer only. |
| V13-ND006 | No diagrams are created here. | Diagrams may be planned later only where needed. |
