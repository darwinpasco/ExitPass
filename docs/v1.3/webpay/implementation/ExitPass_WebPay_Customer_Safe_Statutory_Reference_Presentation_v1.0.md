# ExitPass WebPay Customer-Safe Statutory Reference Presentation v1.0

## Scope

G-007 hardens customer-visible reference presentation across WebPay statutory request, evidence, recovery, ordinary-payment, handoff, and Sales Invoice states. It does not change Central PMS contracts, canonical identifiers, idempotency, recovery authority, evidence upload behavior, or payment behavior.

## Audited References

The audit covered statutory request references; decision and application command IDs; evidence set, item, and upload-session references; parking-session, Site, Site Group, vendor, service-identity, payment-attempt, support, and correlation IDs; continuation links; generic API errors; accessibility attributes; browser recovery state; browser-smoke fixtures; narrow-layout rendering; and the statutory ID-reference input, validation, payload, and storage paths.

Canonical identifiers remain present where required in TypeScript DTOs, request payloads, in-memory state, approved non-authoritative recovery hints, server logs, audit records, and deterministic fixtures. They are not rendered as customer support details.

## Customer-Visible Policy

- Human-readable lifecycle, evidence, payable-basis, and payment status remains visible.
- Full workflow, evidence, scope, service, payment-attempt, and correlation GUIDs are absent from customer markup and accessible names.
- Statutory request references are not displayed when they are internal GUIDs.
- At most one `Support reference` is shown for the active customer state.
- Missing or malformed correlation values produce no visible support reference.
- Unknown backend text is replaced by established safe fallback guidance rather than reflected to the browser.
- Existing opaque continuation links remain usable, but their underlying identity is not reformatted or used as a support reference.

## Support Reference

`formatCustomerSupportReference` accepts a GUID-shaped correlation/support value, normalizes its case, and applies 32-bit FNV-1a across the complete canonical value. The eight uppercase hexadecimal characters are displayed as `XXXX-XXXX`.

The output is deterministic, phone-readable, derived from the complete value rather than a prefix, and does not expose a complete UUID. It is presentation-only. WebPay never sends it to an API, stores it as authority, uses it for idempotency, or uses it for lifecycle recovery.

## Automatic Statutory ID Masking

The customer enters the statutory ID reference normally and never types masking characters. While the field is actively being edited, the raw value exists only in the input component's in-memory state. On blur, WebPay validates ASCII letters, digits, and hyphens; requires at least seven characters; replaces every character between the first two and last four with `*`; clears the raw component state; and places only the masked result in application state.

Examples:

- `SC12345678` becomes `SC****5678`.
- `PWD-123456789` becomes `PW*******6789`.
- `ABCD1234` becomes `AB**1234`.

Values shorter than seven characters are cleared and rejected because the visible prefix and suffix would overlap. Manual `*` input and unsupported Unicode are rejected with customer-safe guidance. A customer can use the `Change` action to discard the masked value and enter a replacement; WebPay never reveals the prior raw value.

The approved Payment Orchestrator and Central PMS contracts both accept `maskedIdReference` and reject raw full-ID values. Accordingly, the automatically masked value is the payload value. The raw value is never sent to the backend, used for idempotency or recovery, or written to localStorage, sessionStorage, IndexedDB, Cache Storage, URLs, logs, or telemetry. Existing backend-approved legacy masked shapes remain accepted by the low-level request builder for compatibility, but the customer UI always generates the first-2/middle-stars/last-4 form.

## Accessibility And Responsive Behavior

The visible text contains the complete label and short value, so screen readers announce one meaningful support reference without a duplicate ARIA label. The reference remains selectable and copyable as text. `overflow-wrap` and tabular hexadecimal figures keep it readable on narrow layouts. The ID-reference input has an explicit label, an automatic-masking description, safe inline validation, and a keyboard-accessible replacement action. Masked values remain readable at narrow widths without exposing the raw value in accessible names. Existing evidence, retry, refresh, replacement, and regular-payment controls are unchanged.

## Security And Privacy Validation

Unit coverage proves deterministic support formatting, distinct output for distinct canonical values, safe malformed-value handling, first-2/last-4 statutory masking, short and malformed ID rejection, paste/edit/replacement behavior, full-ID removal after blur, masked-only backend payloads, full-GUID absence from statutory and evidence markup, unchanged API identity, and safe unknown-error mapping. Chromium coverage inspects serialized runtime customer DOM for GUID-shaped values across pending review, evidence verification, replacement lock, approved, rejected, applied, narrow, and keyboard states, and verifies that raw statutory ID input becomes a masked-only backend payload. Browser requests and approved recovery storage continue to retain canonical workflow values only where technically required; statutory ID references are not recovery authority.

Source and fixture GUID matches are intentional contract/test data and are assessed separately from customer-visible runtime DOM. No full-GUID copy action or technical-details panel is introduced.

## Deterministic Manual Walkthrough

Run from `D:\wt\G007\src\Services\WebPayUi`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action SelfTest
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action Start -Scenario validation-pending
Start-Process msedge.exe -ArgumentList '--inprivate','http://127.0.0.1:5196/'
```

Use ticket `WEBPAY-EVIDENCE-G006`, submit the synthetic Senior Citizen request, and verify `Awaiting review`, `Pay regular amount`, evidence selection, and one `Support reference` in `XXXX-XXXX` form. Enter `SC12345678` without asterisks, move focus away, and verify the field displays `SC****5678`. Use `Change`, paste `PWD-123456789`, edit it, move focus away, and verify the first two and last four remain visible while every intervening character is `*`. Enter a value shorter than seven characters and verify it is cleared and rejected. Inspect the Elements, Accessibility, Network, localStorage, and sessionStorage panes and confirm no full GUID or full statutory ID remains after masking; confirm the statutory decision payload contains only `maskedIdReference` in the generated masked form.

For each scenario below, close the prior InPrivate window, reset the fixture, select the scenario, and repeat the ticket/request flow:

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:5196/__fixture/reset' -Method Post -ContentType 'application/json' -Body '{}'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action SetScenario -Scenario replacement-denied
```

Repeat `SetScenario` with `validation-pending`, `approved`, `rejected`, and `applied`. For the latter lifecycle scenarios, select a synthetic JPEG or PNG and complete the fixture upload to reach the configured state. Verify the human-readable status remains, regular payment remains available where allowed, and no full request, decision, application, evidence, parking-session, Site, Site Group, upload-session, or correlation GUID appears. At 390 x 844, confirm the short support reference wraps safely and keyboard focus remains visible.

Stop and remove only harness-owned state:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action Stop
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action Cleanup
```

## Remaining Visibility Boundary

Operator Console and support-tool reference visibility is outside G-007. Those channels may retain role-authorized canonical references under their own policy; WebPay does not add a technical-details view.
