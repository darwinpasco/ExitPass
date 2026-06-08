# Operator Console Statutory Discount Pilot Readiness Sign-Off v1.0

## 1. Title And Purpose

This document is the pilot readiness sign-off package for Operator Console statutory discount validation.

It is based on the controlled sandbox validation run for the Operator Console statutory discount backend/API flow, including the completed #233 validation run, #234A feedback and triage artifacts, and #235A sandbox fixture validation support.

## 2. Scope

In scope:

- Operator Console statutory discount validation backend/API flow.
- Session lookup.
- Policy resolution.
- Draft creation.
- Evidence gating.
- Metadata-only evidence capture.
- Approval decision.
- Apply-payable-basis.
- Final read model verification.
- Boundary mutation check.

Out of scope:

- WebPay UI changes.
- Payment provider routing.
- AUB selection, configuration, routing, or invocation.
- Payment attempts and payment confirmations.
- Exit authorization and gate opening.
- Coupons.
- Reconciliation.
- HikCentral integration.
- Raw evidence storage.
- OCR.
- Automated ID validation.
- Production personal data.

## 3. Source Artifacts

Referenced source artifacts:

- [OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md](OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md)
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Feedback_Log_Template.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md`
- `scripts/operator-console/Seed-StatutoryDiscountPilotFixture.sql`
- `scripts/operator-console/Verify-StatutoryDiscountPilotFixture.sql`
- `scripts/operator-console/README.md`

`OperatorConsole_Statutory_Discount_Pilot_Workbook_v1.xlsx` is not present under `docs/operator-console` in this branch. Treat the pilot workbook as externally maintained manual validation evidence unless it is committed separately.

## 4. Environment And Fixture Summary

| Item | Value |
| --- | --- |
| API base URL used | `http://localhost:5080` |
| PostgreSQL local DB | `exitpass_v12_dev` on `localhost:5433` |
| Environment type | Local sandbox validation |
| Fixture type | Deterministic sandbox-only test fixture |

Deterministic sandbox fixture values:

| Fixture value | Test-only value |
| --- | --- |
| Site ID | `77000000-0000-0000-0000-000000000002` |
| Site Group ID | `77000000-0000-0000-0000-000000000001` |
| Operator User ID | `77000000-0000-0000-0000-000000000010` |
| Operator Device Binding ID | `77000000-0000-0000-0000-000000000030` |
| Operator Shift ID | `77000000-0000-0000-0000-000000000050` |
| Ticket Reference | `E2E-231-SESSION-001` |
| Parking Session ID | `23100000-0000-0000-0000-000000000003` |
| Original Tariff Snapshot ID | `23100000-0000-0000-0000-000000000004` |
| Entitlement Type | `SENIOR_CITIZEN` |
| Required Evidence Type | `SENIOR_CITIZEN_ID` |
| Capture Method | `OPERATOR_CONFIRMED` |

The fixture is sandbox-only validation support. It is not production seed data and must not be added to baseline DDL, migrations, or production reference data.

## 5. Validation Checklist Summary

Main validation run correlation ID: `52883917-a776-4656-8d0a-b87087d646b1`

Second negative-control run correlation ID: `9ad98e0f-2ba8-4dc1-b68e-ba7466c68e80`

| Step | Workflow step | Expected control | Actual result | Pass/Fail | Correlation ID |
| --- | --- | --- | --- | --- | --- |
| 1 | Start validation session | Sandbox fixture, operator context, and no payment/provider/gate path in scope. | Validation session started with deterministic sandbox values. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 2 | Session lookup | Active parking session resolves for the seeded site and site group. | Session lookup passed for `E2E-231-SESSION-001`. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 3 | Policy resolution | `SENIOR_CITIZEN` policy resolves and requires evidence. | Policy resolution passed. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 4 | Draft creation | Draft persists with evidence required. | Draft creation passed; draft ID `b84541dc-4929-4f53-bdcc-22b145dd7c41`. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 5 | Approval before evidence | Approval is blocked before required evidence is captured. | Blocked with `EVIDENCE_REQUIRED_NOT_CAPTURED`. | Pass | `9ad98e0f-2ba8-4dc1-b68e-ba7466c68e80` |
| 6 | Wrong evidence type | `PWD_ID` must not satisfy a `SENIOR_CITIZEN` evidence requirement. | Rejected with `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST`. | Pass | `9ad98e0f-2ba8-4dc1-b68e-ba7466c68e80` |
| 7 | Evidence capture | Metadata-only `SENIOR_CITIZEN_ID` evidence capture succeeds. | Evidence capture passed; evidence reference ID `47ac2014-b933-4932-851c-1b8884a1f95a`. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 8 | Evidence list/read model | Evidence list and draft read model show evidence satisfied. | Evidence list/read model passed. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 9 | Apply before approval | Apply-payable-basis is blocked until the validation is approved. | Blocked with `STATUTORY_DISCOUNT_NOT_APPROVED`. | Pass | `9ad98e0f-2ba8-4dc1-b68e-ba7466c68e80` |
| 10 | Approval decision | Approval succeeds after required evidence is captured. | Approval after evidence passed. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 11 | Apply-payable-basis | Approved validation applies statutory discount payable basis. | Apply payable-basis passed; applied tariff snapshot ID `b6dc81d9-c8c3-485c-a2cd-97e5c69c2477`. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |
| 12 | Final verification | Final read model reflects approved validation, evidence satisfied, and applied payable basis. | Final verification passed. | Pass | `52883917-a776-4656-8d0a-b87087d646b1` |

## 6. Negative Control Evidence

| Negative control | Expected result | Actual result | Pass/Fail |
| --- | --- | --- | --- |
| Approval before evidence | Approval must be blocked until required evidence is captured. | Blocked with `EVIDENCE_REQUIRED_NOT_CAPTURED`. | Pass |
| Wrong evidence type | `PWD_ID` must be rejected for `SENIOR_CITIZEN` evidence requirement. | Rejected with `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST`. | Pass |
| Apply before approval | Apply-payable-basis must be blocked before approval. | Blocked with `STATUTORY_DISCOUNT_NOT_APPROVED`. | Pass |

## 7. Payable-Basis Result

| Payable-basis field | Result |
| --- | --- |
| Original amount | `12500` minor units |
| VAT amount | `1339` minor units |
| VAT-exclusive amount | `11161` minor units |
| Statutory discount | `2232` minor units |
| Final payable amount | `8929` minor units |
| Currency | `PHP` |
| Applied tariff snapshot ID | `b6dc81d9-c8c3-485c-a2cd-97e5c69c2477` |

## 8. Boundary Mutation Confirmation

Boundary SQL result:

| Boundary table | Count |
| --- | ---: |
| `core.payment_attempts` | 0 |
| `core.payment_confirmations` | 0 |
| `core.exit_authorizations` | 0 |
| `coupons.coupon_applications` | 0 |

The Operator Console statutory discount flow did not create payment attempts, payment confirmations, provider records, exit authorizations, gate records, coupon applications, or reconciliation records during the controlled validation run.

## 9. Privacy And Evidence Handling Confirmation

Privacy and evidence handling controls passed:

- Metadata-only evidence capture was used.
- No raw ID number was returned.
- No raw evidence bytes were uploaded.
- No OCR was performed.
- No automated ID validation was performed.
- Operator-confirmed evidence used `storageReference=operator-confirmed`.
- Masked ID reference handling was required and validated.
- No production personal data was added to this sign-off package.

## 10. Triage Conclusion

Triage conclusion:

- No accepted implementation defects were identified from the pilot validation run.
- The #235 defect implementation batch should not proceed.
- All required positive controls and negative controls passed.
- The observed draft conflict in the second run was setup-related and resolved by reseeding the sandbox fixture.
- The remaining item is operational sign-off, not implementation defect repair.

## 11. Go/No-Go Recommendation

Recommendation:

- GO for pilot-readiness sign-off for the Operator Console statutory discount validation backend/API flow.
- NO-GO for production rollout until production identity/device/shift enrollment, production policy registry configuration, full operator UX, deployment hardening, and operational support processes are completed or separately approved.

## 12. Remaining Operational Assumptions And Risks

Remaining assumptions and risks:

- Sandbox validation used deterministic fixture data.
- `operator_console.*` tables were not present in the local baseline during #235A, so fixture support used available identity, site, core, and discounts structures with conditional operator-console support.
- Production must validate real identity/device/shift binding once production operator-console access tables or equivalent identity controls are finalized.
- Production statutory discount policies must not be auto-applied until official ordinance copies are reviewed and configured.
- The pilot workbook should be retained as manual evidence if it is not committed.

## 13. Sign-Off

| Role | Date | Decision | Signature/initials |
| --- | --- | --- | --- |
| Product Owner |  |  |  |
| Backend/Architecture |  |  |  |
| QA |  |  |  |
| Operations |  |  |  |
| Compliance/Privacy |  |  |  |

## 14. Final Recommendation

The Operator Console statutory discount validation backend/API flow is pilot-ready.

Proceed to operational pilot-readiness sign-off.

Do not proceed with the #235 defect implementation batch unless new accepted pilot defects are logged later.
