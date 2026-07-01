# Assisted Payment Terminal System Design Input Pack 02: Terminal Workflow and State

Status: Specialist input pack only  
Date: 2026-07-01  
Owner: Codex v1.3 Specialist Task - Terminal Workflow and State  
Target design: Future Assisted Payment Terminal System Design v1.0

## 1. Purpose

This input pack provides companion-design workflow and conceptual state guidance for the Assisted Payment Terminal System Design. It covers Cashier-Assisted Terminal and Continuity Terminal workflows, state ownership, exception paths, statutory discount capture, payment/fiscal/exit status display, supervisor escalation, and fail-closed behavior.

This pack does not draft the final Assisted Payment Terminal System Design. It does not define endpoint paths, DTOs, database tables, event payload schemas, queue names, retry counts, implementation classes, UI wireframes, final screen names, printer command formats, or device SDK calls.

The terminal posture used throughout this pack is:

- The terminal is a controlled capture, coordination, and display surface.
- Central PMS owns payment-linked platform state, TariffSnapshot/payable-basis recording, payment finality, fiscal reference recording, degraded resolve decisions under approved policy, and ExitAuthorization.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, evidence reference governance, and payable-basis effect.
- Vendor PMS / HikCentral Professional remains normal authority for raw parking session lifecycle and tariff computation where the vendor capability is confirmed.
- Payment Orchestrator or approved payment integration reports verified provider outcomes but does not declare platform payment finality.
- Resolved Site POS Server owns Sales Invoice issuance and fiscal status.
- Operator Console or an approved operations workflow owns non-payment governance, supervisor review, continuity governance, fiscal exception review, and manual release governance.
- Gate/exit execution consumes Central PMS ExitAuthorization. The terminal must not directly open gates.

## 2. Source Documents Reviewed

Primary sources reviewed:

- `docs/v1.3/assisted-payment-terminal/system-design/ExitPass_Assisted_Payment_Terminal_System_Design_Orchestration_Plan.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`

Key source conclusions:

- Assisted Payment Terminal is a payment-capable app family with Cashier-Assisted Terminal mode and Continuity Terminal mode.
- Cashier-Assisted Terminal is the normal staffed assisted payment mode.
- Continuity Terminal is disabled by default and may be activated only under approved degraded/BCP controls.
- Site Group is the customer lookup/payment scope. Site is the reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary.
- Current terminal lookup posture should remain ticket-first / ticket-only where vendor support permits, while not overclaiming HCP ticket support before the `cardNum` / physical ticket mapping question is resolved.
- Normal payable basis depends on Central PMS coordination with Vendor PMS/HCP live session and fee calculation where capability and identifier policy are confirmed.
- Projection is operational visibility and controlled degraded support only. Projection is not normal tariff authority, payment finality, fiscal truth, discount approval, or exit authority.
- Fiscal issuance must succeed, and Central PMS must record fiscal reference, before normal ExitAuthorization is issued unless a separately approved exception/manual-release policy applies.

## 3. Terminal Role in Normal Mode

In normal Cashier-Assisted Terminal mode, the terminal should:

- Require authenticated cashier use.
- Establish trusted terminal/device identity before payment workflow access.
- Bind operation to an assigned Site/Site Group context.
- Require active cashier shift/session accountability where policy requires.
- Accept ticket/card/manual lookup input according to approved Site and vendor capability.
- Prefer ticket-first / ticket-only lookup where vendor support permits.
- Preserve the HCP `cardNum` / ticket mapping uncertainty as an open vendor/deployment question.
- Request session resolution through Central PMS-approved backend workflow.
- Display session, payable-basis, discount validation, payment, fiscal, and ExitAuthorization statuses returned by backend authorities.
- Capture statutory discount validation inputs and evidence references where required by policy.
- Submit statutory discount inputs to Central PMS / Discount workflow.
- Initiate payment only after payable basis is established or refreshed by backend authority.
- Display provider outcome and Central PMS payment finality as distinct concepts.
- Display POS Server fiscal issuance status and Central PMS fiscal reference recording status as backend-owned status.
- Display Central PMS ExitAuthorization status.
- Reset to a next-customer-ready state after transaction completion or governed cancellation.

In normal mode, the terminal must not:

- Approve statutory entitlement or calculate policy outcomes locally.
- Mutate payable basis directly.
- Declare platform payment finality.
- Issue or independently generate Sales Invoices.
- Record fiscal reference as an authority action.
- Issue ExitAuthorization.
- Directly open gates.
- Treat vendor payment acknowledgment as ExitPass payment finality.

## 4. Terminal Role in Continuity Mode

Continuity Terminal mode is a restricted degraded/BCP operating mode of the same Assisted Payment Terminal app family. It should remain disabled by default.

When activated under approved degraded/BCP controls, the terminal should:

- Display that operation is under approved continuity scope without implying normal operation.
- Require trusted terminal/device identity, cashier identity, Site/Site Group scope, active shift/session context, incident/BCP reference linkage, and audit/reconciliation tagging.
- Permit only workflows allowed by the active continuity policy.
- Use projection-based or approved continuity lookup context only after Central PMS determines degraded resolve eligibility.
- Display projection freshness, ambiguity, and restriction context where returned by backend authority.
- Restrict statutory discount handling under approved degraded-mode policy.
- Fail closed or route to supervisor/manual review when entitlement, policy basis, evidence requirement, projection freshness, or payable-basis recalculation cannot be safely validated.
- Permit payment, fiscal routing, and exit handling only where approved continuity policy and backend/fiscal prerequisites allow.
- Route manual release requests to Operator Console or an approved operations workflow; the terminal should display governance handoff status only.
- Preserve post-restoration review and reconciliation tagging for continuity-origin activity.

Continuity Terminal must not silently replace normal Vendor PMS/Central PMS authority, create unmanaged offline discounts, approve unmanaged offline fiscal issuance, or become a bypass path around fiscal and exit controls.

## 5. Workflow Summary Table

| Workflow | Normal mode posture | Continuity/degraded posture | Owning authority or state source |
| --- | --- | --- | --- |
| Cashier login and terminal trust | Required before payment activity | Required before any continuity workflow | Identity/platform controls with terminal trust posture |
| Shift/session activation | Required where policy applies; ties cashier, device, Site/Site Group, and activity | Required and additionally incident/audit/reconciliation tagged | Cashier session/shift governance with Central PMS/audit correlation |
| Site/Site Group binding | Terminal must be bound to authorized Site/Site Group | Activation scope must match affected Site/Site Group | Central configuration and platform controls |
| Ticket/session lookup | Ticket-first / ticket-only where vendor support permits; Central PMS resolves through vendor connector | Projection or approved continuity source only where policy allows | Central PMS and Vendor PMS/HCP connector |
| Live tariff/payable basis | Vendor PMS/HCP live fee where confirmed; Central PMS records payable basis | Approved degraded basis only after Central PMS policy decision | Vendor PMS/HCP for normal tariff; Central PMS for TariffSnapshot/payable basis |
| Statutory discount capture | Terminal captures inputs, evidence references, and cashier attestation | Restricted; fail closed or route to review if unsafe | Central PMS / Discount workflow |
| Payment initiation | Terminal initiates approved payment flow after payable basis | Only where continuity policy allows | Central PMS and Payment Orchestrator/approved payment channel |
| Provider outcome display | Display pending, failed, cancelled, completed, or unknown as returned | Same conservative display; unknown does not imply finality | Payment Orchestrator reports outcome; Central PMS owns finality |
| Payment finality display | Display Central PMS finality after verified acceptance | Same; no finality during uncertainty | Central PMS |
| Fiscal issuance display | Display POS Server Sales Invoice status/reference context where allowed | Only where continuity fiscal policy allows | Resolved Site POS Server and Central PMS fiscal reference |
| ExitAuthorization display | Display Central PMS authorization or pending/blocked state | Display Central PMS authorization or manual-governance handoff state | Central PMS |
| Fiscal failure/pending | Show payment received but fiscal/exit pending; block exit-authorized messaging | Same, with continuity/manual-release governance if approved | POS Server, Central PMS, Operator Console/governance workflow |
| Manual release handoff | Handoff to Operator Console or approved operations workflow | Last-resort continuity governance only | Operator Console / approved operations workflow |
| Post-transaction reset | Clear customer/session-specific display and prepare for next lookup | Clear only after required tags/status capture | Terminal UI state, audit/reconciliation correlation |

## 6. Cashier Authentication / Terminal Assignment / Shift Context Workflow

Conceptual flow:

1. Cashier starts terminal use.
2. Terminal validates terminal/device trust posture through approved identity/platform controls.
3. Cashier authenticates.
4. Backend/platform controls establish cashier identity, role, permitted Site/Site Group scope, and whether the device is authorized for the terminal mode.
5. Terminal displays assigned Site/Site Group context for cashier confirmation.
6. Cashier opens or resumes an active shift/session where policy requires.
7. Terminal blocks payment workflow if any required identity, trust, Site/Site Group, or active shift/session condition is invalid.

Recommended companion-design notes:

- Terminal trust should be evaluated before cashier payment workflow access.
- Cashier identity and device identity should both be present in audit context.
- Site/Site Group binding should be explicit in the terminal workflow to reduce wrong-site processing.
- A trusted terminal should not imply cashier authorization. A valid cashier should not imply device trust.
- Shift/session context should bind cashier, device, Site/Site Group, and transaction activity for reconciliation.
- Continuity mode should require additional activation scope and incident/BCP context before restricted workflows are available.

Exception posture:

- Invalid cashier login: deny workflow and log failed attempt where policy requires.
- Untrusted terminal/device: deny payment workflow or route to support according to policy.
- Unauthorized Site/Site Group: block lookup/payment and require authorized correction.
- No active shift/session where required: deny shift-scoped workflow.
- Mode not authorized for terminal: block the mode and surface support/supervisor guidance.

## 7. Ticket / Session Lookup Workflow

Conceptual flow:

1. Cashier enters or scans the customer reference.
2. Terminal classifies the input only at a user-action level, such as ticket/card/manual lookup, without asserting vendor field semantics.
3. Terminal submits lookup context through Central PMS-approved backend workflow with cashier/device/shift/Site/Site Group context.
4. Central PMS resolves lookup/payment scope and resolved Site.
5. Central PMS uses the configured Vendor PMS connector and AdapterMapping for normal session resolution.
6. Vendor PMS/HCP provides live session and fee facts where capability and identifier policy are confirmed.
7. Central PMS evaluates the result, records or refreshes the payable basis, and returns display context to the terminal.
8. Terminal displays session/payable-basis status, ambiguity, not-found, or approved escalation guidance.

Ticket-first posture:

- The current target should remain ticket-first / ticket-only where vendor support permits.
- The terminal should not require plate lookup as the default normal workflow unless later Site/vendor policy approves it.
- Plate and card lookup may remain capability areas, but the current companion design should avoid making them required for v1.0 terminal workflow.

HCP identifier caution:

- HCP `cardNum` appears in passageway and fee contexts, but its exact business meaning remains unresolved.
- Local evidence does not prove that a physical printed ticket number maps to HCP `cardNum`.
- Ticket-only fee calculation for HCP should remain unconfirmed until the target deployment validates the lookup key and barcode/QR payload behavior.
- If HCP `plateLicense` is returned as `Unknown`, terminal display must not treat it as a real plate identity.

Exception posture:

- Session not found: display clear cashier/customer message and approved escalation path.
- Ambiguous session: fail closed or route to supervisor/manual review.
- Vendor unavailable or timeout: do not invent a payable basis; use only approved degraded workflow where active.
- Missing or ambiguous AdapterMapping: do not choose Site/vendor object by heuristic; route to approved review.
- Ticket identifier unsupported by vendor: display unsupported/needs-assistance posture without claiming session truth.

## 8. Normal Cashier-Assisted Payment Workflow

Conceptual flow:

1. Cashier logs into a trusted, assigned terminal with active shift/session context.
2. Cashier scans or manually enters ticket/card reference, with ticket-first posture where supported.
3. Central PMS resolves the session through the appropriate Vendor PMS/HCP connector and resolved Site mapping.
4. Vendor PMS/HCP supplies normal live session and tariff facts where confirmed.
5. Central PMS records or refreshes the payable basis.
6. Terminal displays payable-basis details returned by backend authority.
7. If the customer requests statutory discount handling, cashier initiates validation capture before payment.
8. Central PMS / Discount workflow resolves validation status and payable-basis effect.
9. Terminal displays approved payable-basis refresh or rejected/pending validation status.
10. Cashier initiates payment only after payable basis is established and any discount effect is approved or explicitly not applied.
11. Payment Orchestrator or approved payment integration handles provider interaction.
12. Terminal displays provider outcome status without declaring platform finality.
13. Central PMS records payment finality after verified outcome.
14. Central PMS requests fiscal issuance from the resolved Site POS Server.
15. POS Server issues Sales Invoice and returns fiscal status/identity to Central PMS.
16. Central PMS records fiscal issuance reference.
17. Central PMS issues ExitAuthorization if eligible.
18. Terminal displays customer instruction, fiscal status, and ExitAuthorization status.
19. Terminal clears customer/session-specific context and returns to next-customer readiness after completion or governed closure.

Guardrails:

- Payment must not proceed using an unapproved discounted payable basis.
- Provider success must be displayed separately from Central PMS platform payment finality.
- Fiscal pending/failure status must prevent exit-authorized messaging.
- ExitAuthorization display must come from Central PMS, not inferred from payment or fiscal display alone.

## 9. Statutory Discount Validation Capture Workflow

Conceptual flow:

1. Cashier resolves a valid session and payable basis.
2. Customer requests statutory discount handling.
3. Terminal presents capture workflow allowed by cashier role, Site/Site Group policy, and current operating mode.
4. Cashier captures required entitlement details, evidence reference inputs where policy requires, privacy acknowledgment/notice posture where required, and cashier attestation.
5. Terminal submits captured inputs to Central PMS / Discount workflow.
6. Central PMS / Discount workflow performs policy resolution, validation persistence, evidence reference governance, and payable-basis effect.
7. Terminal displays validation status returned by backend authority.
8. If approved, Central PMS refreshes payable basis before payment.
9. If rejected, failed, expired, or pending review, terminal does not apply the discount as payable basis.
10. If supervisor review is required, terminal routes or hands off to Operator Console / approved operations workflow.

State and evidence posture:

- Terminal may capture evidence reference inputs but should not retain unmanaged entitlement evidence.
- Evidence access and review remain privacy-controlled and auditable.
- Operator Console may review APT-captured discount cases where role and policy allow.
- Continuity-mode statutory discount handling must carry incident, audit, reconciliation, and post-restoration review context.

Fail-closed conditions:

- Entitlement cannot be safely validated.
- Policy basis is missing, ambiguous, or not applicable to the Site/jurisdiction.
- Required evidence cannot be captured or referenced under approved policy.
- Projection freshness is insufficient in continuity mode.
- Payable-basis recalculation cannot be safely completed.

## 10. Payment Initiation and Provider Outcome Display Workflow

Conceptual flow:

1. Terminal receives payable basis from backend authority.
2. Cashier confirms payment initiation under assigned terminal and active shift/session context.
3. Central PMS/payment workflow creates or controls the payment attempt concept.
4. Payment Orchestrator or approved payment integration interacts with the provider.
5. Terminal displays provider-facing status as returned through approved backend workflow.
6. Central PMS records platform payment finality only after verified outcome and required validation.
7. Terminal displays Central PMS payment finality as a separate status when returned.

Display concepts:

- Payment initiation accepted.
- Provider pending.
- Provider failed.
- Provider cancelled.
- Provider outcome unknown.
- Provider completed but platform finality pending.
- Central PMS payment finality recorded.

Guardrails:

- Unknown provider outcome must not be shown as paid/final.
- Provider success must not be treated as platform finality until Central PMS records finality.
- Duplicate cashier submissions should be blocked or shown as already in progress where backend state indicates an existing active attempt.
- Cashier/device/shift/Site/session/payment attempt correlation must be auditable.

## 11. Fiscal Issuance Status Display Workflow

Conceptual flow:

1. Central PMS has recorded verified payment finality.
2. Central PMS routes fiscal issuance to the resolved Site POS Server.
3. POS Server issues Sales Invoice or returns pending/failed/timeout status.
4. Central PMS records fiscal issuance reference when fiscal issuance succeeds or records exception status as applicable.
5. Terminal displays fiscal issuance status returned through approved backend workflow.

Display concepts:

- Fiscal issuance pending.
- Sales Invoice issued.
- Fiscal reference recorded by Central PMS.
- Fiscal issuance failed.
- Fiscal issuance status unknown.
- Fiscal exception routed for review.

Guardrails:

- Terminal may present or print fiscal output where allowed, but presentation does not make the terminal fiscal authority.
- Resolved Site determines POS Server routing.
- Fiscal issuance failure or timeout must not be hidden behind payment success.
- Terminal must not imply that exit is authorized before fiscal prerequisite and Central PMS ExitAuthorization status are satisfied.
- Exact reprint/display behavior remains open for POS Server policy and technical design.

## 12. ExitAuthorization Status Display Workflow

Conceptual flow:

1. Central PMS evaluates eligibility after payment finality, fiscal issuance success, fiscal reference recording, and other required control conditions.
2. Central PMS issues ExitAuthorization if eligible.
3. Terminal receives and displays ExitAuthorization status from approved backend workflow.
4. Cashier provides customer instruction based on returned status only.

Display concepts:

- ExitAuthorization issued.
- ExitAuthorization pending.
- ExitAuthorization blocked because fiscal issuance is pending/failed.
- ExitAuthorization blocked because payment finality is not recorded.
- ExitAuthorization blocked because session/lookup state is unresolved.
- ExitAuthorization governed by manual release workflow where formally approved.

Guardrails:

- Terminal must not issue ExitAuthorization.
- Terminal must not infer exit eligibility from provider success, fiscal display, vendor paid state, or cashier judgment.
- Terminal must not directly open gates.
- Gate/exit execution must consume Central PMS authorization or follow a separately approved manual emergency process outside terminal gate control.

## 13. Fiscal Issuance Failure / Pending Exit Workflow

Conceptual flow:

1. Payment finality is recorded by Central PMS.
2. Fiscal issuance request to POS Server fails, times out, or remains pending.
3. Central PMS does not issue normal ExitAuthorization yet.
4. Terminal displays that payment was received but fiscal issuance and/or exit authorization is pending.
5. Case enters controlled fiscal exception/retry/review workflow.
6. Operator Console or approved operations workflow supports fiscal exception review and escalation.
7. If later fiscal issuance succeeds, Central PMS records fiscal reference and evaluates ExitAuthorization.
8. If a formally approved manual release policy applies, manual release governance is handled separately with supervisor approval, incident/audit/reconciliation tags, reason, attribution, and post-review.

Display posture:

- Use clear pending/exception language.
- Avoid "exit allowed" or equivalent messaging until Central PMS ExitAuthorization is returned.
- Distinguish "payment received" from "fiscal issuance complete" and "exit authorized".
- Distinguish manual release governance from normal ExitAuthorization.

Guardrails:

- Fiscal issuance failure after payment finality must not automatically reverse payment.
- Fiscal issuance failure must not automatically authorize exit.
- Retry/status confirmation must avoid duplicate fiscal documents; exact mechanics are deferred.
- Manual release, if allowed, remains last-resort governance and must be reconciliation-tagged.

## 14. Continuity Terminal Activation and Restricted Operation Workflow

Conceptual flow:

1. A degraded/BCP condition is recognized for a defined Site/Site Group, dependency, or operating scope.
2. Approved authority or policy condition activates continuity controls.
3. Activation records affected Site/Site Group, dependency, incident/BCP reference, activation reason, approval actor where required, allowed workflows, restricted workflows, audit tags, and reconciliation tags.
4. Continuity Terminal mode becomes available only for authorized terminals, cashiers, Sites/Site Groups, shifts, and activation scope.
5. Cashier authenticates and confirms restricted operating scope.
6. Terminal permits only allowed continuity lookup, payment, discount, fiscal, and handoff workflows.
7. Central PMS determines degraded resolve eligibility and payable-basis availability under approved continuity policy.
8. Terminal displays restricted operation, projection freshness, degraded basis status, pending/blocked states, and approved escalation guidance.
9. Deactivation disables continuity-only workflows and sends continuity-origin activity into post-restoration review where applicable.

Restricted-operation guardrails:

- Continuity Terminal is disabled by default.
- Activation authority and exact approval workflow remain open.
- Vendor PMS/HCP outage does not automatically permit payment or exit.
- WebPay/APM outage does not authorize bypassing payment or fiscal controls.
- Projection data may be used only under approved degraded controls and freshness policy.
- Payment collection may proceed only where continuity policy allows.
- Fiscal handling may proceed only where POS Server and continuity fiscal policy allow.
- Offline fiscal issuance is not approved unless later BIR/accounting/POS Server design approves it.

## 15. Degraded Resolve / Projection-Based Context Workflow

Conceptual flow:

1. Normal live vendor resolve or fee calculation is unavailable, degraded, or unsafe.
2. Central PMS evaluates whether continuity policy is active for the affected Site/Site Group and workflow.
3. Central PMS checks projection freshness, ambiguity, mapping status, and approved degraded tariff basis.
4. If projection is fresh, unambiguous, mapped, and allowed, Central PMS may provide degraded context/payable basis under approved policy.
5. Terminal displays degraded/projection-based context with source/freshness/restriction labels returned by backend authority.
6. If projection is stale, ambiguous, insufficient, or outside approved policy, terminal fails closed or routes to supervisor/manual review.

Projection guardrails:

- Projection is operational visibility and controlled degraded support only.
- Projection is not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority.
- The terminal should not invent tariffs from passageway/projection records.
- Degraded tariff basis belongs to Central PMS under approved continuity policy using approved tariff configuration or approved continuity basis.
- HCP one-minute passageway polling is a planning baseline, not a final freshness threshold or permission to proceed.

## 16. Manual Release Governance Handoff Workflow

Conceptual flow:

1. Terminal detects or receives a state where normal payment-to-fiscal-to-exit flow cannot complete.
2. Terminal displays pending/blocked condition and approved escalation guidance.
3. Cashier requests supervisor assistance or follows approved operations handoff.
4. Operator Console or approved operations workflow handles manual release governance where policy allows.
5. Governance workflow captures supervisor/operator identity, reason, incident tag, audit tag, reconciliation tag, Site/Site Group, device/session context, and post-review requirement.
6. Terminal displays handoff status or instruction returned by governance/backend workflow without opening the gate or converting the case into normal ExitAuthorization.

Guardrails:

- Manual release is not normal ExitAuthorization.
- Manual release must not silently become payment finality.
- Operator Console remains non-payment and non-gate except where a future approved System Design explicitly changes a manual emergency process boundary.
- Gate or physical release execution remains outside the terminal workflow unless separately approved.
- Manual release records must remain distinguishable from normal payment-to-exit records.

## 17. Terminal State Ownership Notes

| Conceptual state | Terminal relationship | Owning authority / state source | Notes for final design |
| --- | --- | --- | --- |
| Terminal UI state | Owns local presentation, current workflow step, transient customer display, and next-customer readiness | Assisted Payment Terminal | Local UI state must clear customer/session-specific context after completion/cancellation. |
| Cashier session / shift state | Displays and submits context; may block workflow when missing | Identity/shift governance with Central PMS/audit correlation | Must tie cashier, terminal, Site/Site Group, and activity for accountability. |
| Device trust state | Displays trusted/untrusted/unsupported posture and blocks workflow when invalid | Identity/platform device trust controls | Trust does not grant fiscal, payment finality, discount, or exit authority. |
| Site/Site Group binding state | Displays assigned scope and submits scope context | Central configuration/platform controls | Wrong-site processing must block or require authorized correction. |
| Lookup/session display state | Displays resolved, not found, ambiguous, degraded, or exception context | Central PMS using Vendor PMS/HCP connector or approved continuity source | Terminal must not choose among ambiguous sessions by heuristic. |
| Payable-basis display state | Displays backend-approved payable basis and refreshes after approved discount | Central PMS with Vendor PMS/HCP normal tariff or approved degraded basis | Terminal must not calculate or mutate payable basis locally. |
| Statutory validation capture state | Captures required inputs, evidence references, cashier attestation, and displays status | Central PMS / Discount workflow | Pending/rejected/failed validation must not become discounted payable basis. |
| Payment initiation/display state | Initiates approved payment flow and displays provider/backend status | Central PMS plus Payment Orchestrator/approved payment integration | Provider outcome and Central PMS finality must remain distinct. |
| Central PMS payment finality state | Displays finality when recorded | Central PMS | Terminal must not declare finality. |
| POS Server fiscal issuance state | Displays Sales Invoice/fiscal status where returned | Resolved Site POS Server | Terminal may present fiscal output where allowed but is not fiscal authority. |
| Central PMS fiscal reference state | Displays whether Central PMS recorded fiscal linkage | Central PMS | Recording reference is not POS issuance; POS Server remains fiscal issuer. |
| Central PMS ExitAuthorization state | Displays issued, pending, blocked, or exception status | Central PMS | Terminal must not issue authorization or open gates. |
| Continuity activation state | Displays active/restricted/deactivated posture | Approved continuity/governance workflow with Central PMS coordination | Continuity Terminal disabled by default; activation authority remains open. |
| Manual release governance state | Displays handoff or decision status only | Operator Console / approved operations workflow | Manual release is auditable exception governance, not normal ExitAuthorization. |
| Audit/reconciliation state | Supplies context and displays relevant tags/status where allowed | Audit/reconciliation workflows across Central PMS, POS Server, Operator Console, connector, and gate records | Must correlate cashier, device, shift, Site/Site Group, session, payment, fiscal, exit, continuity, and manual release facts. |

## 18. Retry / Idempotency / Duplicate Submission Concepts

This pack intentionally does not define retry counts, endpoint contracts, event payloads, queue names, or implementation algorithms. The final design should carry these concepts forward at architecture level:

- Repeated terminal submissions for lookup, discount capture, payment initiation, fiscal status refresh, and exit status refresh should be correlated to existing backend state where applicable.
- The terminal should show "in progress", "pending", "already submitted", or equivalent backend-owned state rather than creating duplicate cashier actions.
- Unknown provider outcomes should remain pending/exception until verified through approved payment workflow.
- Fiscal issuance retry/status confirmation must avoid duplicate fiscal documents.
- Vendor acknowledgment retry/status confirmation must avoid duplicate vendor-side payment effects.
- Repeated vendor responses must not duplicate Central PMS payment finality, payable-basis records, fiscal references, or ExitAuthorization.
- Projection polling retries must preserve freshness/staleness context and must not present stale data as current.
- Cashier actions that create high-risk workflow transitions should be attributable to cashier, terminal, Site/Site Group, shift/session, and operating mode.

## 19. Open Workflow Questions

Open questions to carry into Lead synthesis or downstream design:

- What is the exact Continuity Terminal activation authority and approval workflow?
- What exact degraded tariff/projection freshness threshold applies before projection can support degraded resolve?
- What exact degraded tariff configuration owner, rounding posture, and grace rules apply?
- What exact offline payment policy, if any, is allowed for Continuity Terminal?
- What exact offline fiscal issuance policy, if any, is allowed after BIR/accounting/POS Server design review?
- What exact fiscal issuance exception release policy applies when payment is final but fiscal issuance is pending or failed?
- What exact manual release policy and emergency override boundary apply?
- What exact relationship should the terminal use for supervisor escalation to Operator Console versus another approved operations workflow?
- What exact permission matrix applies across cashier, supervisor, support, administrator, auditor, and operations roles?
- Is cash payment supported in Cashier-Assisted Terminal v1.0?
- Are card/eWallet/QR payments hosted checkout only or terminal-integrated?
- What exact fiscal reprint/display behavior is allowed from the terminal?
- What handoff to POS Server for X-read/Z-read or cashier shift reports, if any, is required?
- Is a fixed cashier station browser/PWA or desktop-compatible variant allowed in v1.0, and what trust restrictions apply?
- What is the exact terminal certificate/key storage and device trust model?
- What offline evidence capture behavior, if any, is allowed?
- What is the exact HCP `cardNum` meaning, and does it map to a printed ticket number, card identifier, internal credential, or another vendor-side identifier?
- What is the correct HCP ticket-only lookup key, and does physical ticket barcode/QR payload differ from the visible printed ticket number?
- Is HCP vendor payment acknowledgment required before exit in any deployment, and what vendor state does it change?
- Is vendor payment acknowledgment synchronous, asynchronous, queued/retried, exit-blocking, or Site/vendor-profile dependent?
- How should unknown vendor acknowledgment outcomes be confirmed without duplicate vendor-side effects?
- What exact connector health states, freshness labels, stale thresholds, and alert rules are approved?
- What exact Site Group user-facing terminology should be shown in terminal UI, if any, while retaining Site Group as the architecture concept?
- What exact endpoint paths, DTOs, database changes, event payloads, and queue names are needed? These remain deferred to downstream API/database/engineering artifacts.

## 20. Summary for Lead

This input pack recommends that the Assisted Payment Terminal System Design model terminal workflows as controlled state presentation, input capture, and workflow coordination only.

Normal Cashier-Assisted Terminal mode should start with terminal trust, cashier identity, Site/Site Group binding, and active shift/session context. It should then support ticket-first lookup where vendor capability permits, Central PMS-mediated session resolve, live Vendor PMS/HCP tariff/payable-basis retrieval, statutory discount capture routed to Central PMS / Discount workflow, payment initiation through approved payment flow, provider outcome display, Central PMS payment finality display, POS Server fiscal issuance display, Central PMS fiscal reference display, and Central PMS ExitAuthorization display. Post-transaction reset should clear customer/session-specific terminal UI state and prepare the terminal for the next customer.

Continuity Terminal mode should remain disabled by default and activated only under approved degraded/BCP controls. It should permit restricted workflows only within activation scope, with incident/audit/reconciliation tagging, projection freshness visibility, fail-closed degraded resolve behavior, restricted statutory discount handling, and post-restoration review. Projection-based context must remain operational visibility and controlled degraded support only.

Fiscal issuance pending or failed after payment finality must create a blocked/pending exit posture. The terminal should clearly display that payment may be received while fiscal issuance and/or ExitAuthorization remains pending, and should route fiscal exception/manual release situations to Operator Console or an approved operations workflow. The terminal must not directly open gates or convert manual release into normal ExitAuthorization.

Lead synthesis should preserve the unresolved workflow questions rather than filling them with implementation details. In particular, HCP ticket-only support and `cardNum` semantics remain open; Continuity Terminal activation authority remains open; fiscal exception/manual release policy remains open; and retry/idempotency mechanics remain deferred to later API/database/engineering design.
