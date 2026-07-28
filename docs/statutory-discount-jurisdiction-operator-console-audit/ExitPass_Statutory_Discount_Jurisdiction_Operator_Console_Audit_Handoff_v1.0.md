# ExitPass Statutory Discount Jurisdiction and Operator Console Audit Handoff v1.0

## Overall verdict

**NOT_READY_END_TO_END_ENFORCEMENT_GAP**

ExitPass has durable statutory-discount decision/application mechanics, but it does not yet prove the fail-closed sequence required for local-ordinance-based Senior Citizen and PWD parking discounts.

The immediate blocker is not payable-basis durability. It is legal-authority gating:

Parking session -> site -> city/municipality jurisdiction -> active applicable local ordinance -> covered entitlement type -> ordinance-specific requirements -> Operator Console review -> canonical decision -> payable-basis application.

## Critical gaps

| Gap | Impact |
| --- | --- |
| Shared WebPay/APT service-channel intake can create `AWAITING_REVIEW` before proving applicable local ordinance authority. | Blocks WebPay/APT statutory-discount controlled UAT and production. |
| WebPay renders the Senior Citizen/PWD request after session resolution without a server-owned ordinance availability result. | Blocks local walkthrough as successful compliance evidence. |
| Service-channel Operator Console approval can resolve a national fallback policy and is not proven to require local ordinance applicability. | Reviewers may appear able to approve without the required local parking ordinance. |

## High gaps

- Canonical decision persistence does not include full jurisdiction, ordinance number, policy version, effective window, covered-entitlement, and evidence-policy snapshot.
- Operator Console service-channel review detail is not proven to display governing ordinance and non-override eligibility facts.
- Management Platform source-of-truth for local ordinance configuration, publishing, suspension, retirement, and audit lifecycle is not proven.
- Application-v1 is not proven to reject an approved decision that lacks local ordinance authority.
- Automated tests do not prove no-ordinance, expired/suspended ordinance, unsupported entitlement, manipulated WebPay request, or no-sensitive-evidence fail-closed behavior for the service-channel path.

## Exact implementation prerequisites

1. Confirm or promote the canonical site-jurisdiction and local-ordinance policy source of truth.
2. Implement a Central PMS fail-closed eligibility resolver before service-channel decision creation.
3. Add shared channel-safe availability/readback so WebPay/APT can show only covered entitlement types and requirements.
4. Add WebPay visibility gating and backend manipulated-request rejection proof.
5. Add Operator Console service-channel ordinance display and approval enforcement.
6. Persist ordinance identity/version/effectivity/evidence-policy snapshot in the canonical decision or validation linkage.
7. Ensure application-v1 and payment/fiscal handoff require and preserve safe ordinance authority facts.

## Recommended first implementation task

**Implement Central PMS statutory-discount local-ordinance eligibility resolver and availability contract design, including canonical policy-source gap closure.**

The first task must decide whether the current `discounts.statutory_discount_policy_registry` and `sites.sites.lgu_code` model is sufficient, or whether a canonical jurisdiction-history object is required before runtime enforcement can be safe.

## Channel and rollout status

| Item | Status |
| --- | --- |
| WebPay implementation mechanics | Authorized with ordinance-gate remediation constraints. |
| APT implementation mechanics | Authorized with ordinance-gate remediation constraints. |
| WebPay statutory-discount controlled UAT | Not authorized. |
| APT statutory-discount controlled UAT | Not authorized. |
| Production rollout | Not authorized. |
| Current local WebPay statutory walkthrough as successful evidence | Blocked. |

## ID-evidence sequencing

Applicable ordinance must be resolved before sensitive ID evidence capture.

No ordinance means:

- no discount request
- no ID fields
- no image capture
- no Operator Console approvable request

Applicable ordinance means:

- collect only evidence required by the ordinance and approved evidence policy
- keep evidence metadata/reference-only until a later secure ID-image task is explicitly authorized

## Manual test posture

Significant manual testing required for audit merge: **No**.

Significant manual testing required after remediation: **Yes**.

Required later scenarios:

1. Active ordinance permits only covered entitlements and lets Operator Console approve under visible ordinance facts.
2. No ordinance hides the request, rejects manipulated submission, and leaves ordinary payment available.
3. Expired or suspended ordinance fails closed and cannot be overridden.
4. Unsupported entitlement is absent and rejected if manipulated.
5. Missing or ambiguous jurisdiction fails closed with safe remediation guidance.
