# Operator Console Schema Extension Design

Status: schema design proposal for ExitPass v1.2

References:
- ExitPass Operator Console BRD v1.0
- ExitPass Database Design v1.2
- ExitPass Full Database Creation DDL v1.2
- ExitPass API Contract Pack v1.2
- `docs/operator-console/statutory-validation-and-access-contract.md`

This is a documentation-only schema design. It proposes future controlled DDL/migration work and does not implement runtime behavior, migrations, or API contract source changes.

Review-only DDL proposal: `docs/operator-console/proposals/operator-console-ddl-proposal.sql`.

## Design Goals

- Preserve ExitPass as the operational access authority while using HR/Timekeeping as the shift schedule/import source.
- Support auditable Operator Console access decisions across user role, registered device/browser binding, site assignment, active shift, revocation, and takeover state.
- Support browser key binding first for MVP device trust and mTLS later for managed devices once certificate lifecycle operations are ready.
- Persist statutory entitlement fingerprints without storing raw statutory ID secrets.
- Preserve site-configurable image evidence and existing `discounts.discount_evidence_references` behavior.
- Preserve the strict non-payment boundary.

## Existing v1.2 DDL Anchors

The proposed schema extensions should reuse these existing v1.2 domains where appropriate:

- `identity.users` for ExitPass operator users.
- `identity.roles`, `identity.user_roles`, and `identity.role_permissions` for operator role/permission context.
- `identity.service_identities` for non-human principals and future mTLS-backed device/gateway identities.
- `sites.site_groups`, `sites.sites`, and `sites.device_assignments` for site/device assignment context.
- `operations.operator_action_logs` and `audit.audit_events` for audit trails.
- `discounts.statutory_discount_validations` for statutory validation lifecycle state.
- `discounts.discount_evidence_references` for evidence references.

Do not overload payment, exit authorization, gate consume, coupon, or settlement tables for Operator Console access state.

## Proposed Schema Ownership

Use a dedicated `operator_console` schema for Operator Console-specific access and workflow support.

Rationale:
- It keeps operator access concepts separate from existing `identity`, `sites`, `operations`, and `discounts` domains.
- It avoids overloading gate-device tables for browser/device trust.
- It allows later migrations to be reviewed as one controlled Operator Console schema extension.

## HR/Timekeeping Identity Mapping

### Proposed Table

`operator_console.hr_timekeeping_identity_mappings`

Purpose: map an ExitPass operator user to one or more HR/Timekeeping identities.

Proposed fields:

| Field | Notes |
| --- | --- |
| `hr_timekeeping_identity_mapping_id` | Primary key. |
| `user_id` | FK to `identity.users(user_id)`. |
| `hr_provider_code` | Controlled code for the HR/Timekeeping source. |
| `external_person_id_hash` | Hash of the external HR person ID. |
| `external_person_id_masked` | Masked display value if needed for operations. |
| `external_employee_number_hash` | Optional hash of employee number. |
| `external_employee_number_masked` | Optional masked employee number. |
| `mapping_status` | Proposed enum or controlled code. |
| `effective_from` | Start of mapping validity. |
| `effective_to` | End of mapping validity. |
| `revoked_at` | Revocation timestamp. |
| `revocation_reason_code` | Controlled reason. |
| `correlation_id` | Cross-service trace. |
| audit columns | `created_at`, `created_by_user_id`, `created_by_service_identity_id`, `updated_at`, `updated_by_*`, `row_version`. |

### Proposed Status Values

Proposed `operator_console.hr_identity_mapping_status_enum`:

- `ACTIVE`
- `SUSPENDED`
- `REVOKED`
- `EXPIRED`
- `SUPERSEDED`

### Uniqueness

Recommended constraints:

- One active mapping per `(hr_provider_code, external_person_id_hash)`.
- Allow one user to have historical mappings, but only one active mapping per `(user_id, hr_provider_code)` unless HR provider policy requires multiple concurrent identities.

### Audit Requirements

Log mapping create, update, suspension, revocation, and supersession to `audit.audit_events`. Mapping changes affect access authority and must include actor user/service identity, reason code, external provider code, and correlation ID.

## Imported Operator Shifts

### Proposed Tables

Use two tables:

- `operator_console.imported_operator_shifts`
- `operator_console.imported_operator_shift_versions`

Purpose: keep immutable import history while exposing a current operational shift record for access evaluation.

### Current Shift Table

`operator_console.imported_operator_shifts`

Proposed fields:

| Field | Notes |
| --- | --- |
| `operator_shift_id` | Primary key used by Operator Console. |
| `hr_provider_code` | Source provider. |
| `external_shift_id_hash` | Hash of external shift ID. |
| `external_shift_id_masked` | Optional masked display reference. |
| `hr_timekeeping_identity_mapping_id` | FK to identity mapping. |
| `operator_user_id` | FK to `identity.users(user_id)` for denormalized access checks. |
| `site_id` | FK to `sites.sites(site_id)`. |
| `site_group_id` | FK to `sites.site_groups(site_group_id)`. |
| `scheduled_start_at` | Imported scheduled start. |
| `scheduled_end_at` | Imported scheduled end. |
| `source_imported_at` | Timestamp of latest import. |
| `import_status_code` | ExitPass-controlled normalized import status. |
| `source_system_code` | Raw source/provider system code for reporting and traceability. |
| `source_status_code` | Raw provider status code from HR/Timekeeping. |
| `source_status_description` | Raw provider status description, if supplied. |
| `operational_status` | ExitPass access status. |
| `active_from` | Operational access start, usually scheduled start or override. |
| `active_to` | Operational access end, usually scheduled end or override. |
| `revoked_at` | Revocation timestamp. |
| `revoked_by_user_id` | User who revoked, if manual. |
| `revocation_reason_code` | Controlled reason. |
| `current_takeover_id` | FK to approved takeover if active. |
| `correlation_id` | Cross-service trace. |
| audit columns | Standard created/updated/row version fields. |

### Import History Table

`operator_console.imported_operator_shift_versions`

Proposed fields:

| Field | Notes |
| --- | --- |
| `operator_shift_version_id` | Primary key. |
| `operator_shift_id` | FK to current shift. |
| `hr_provider_code` | Source provider. |
| `external_shift_id_hash` | Hash of source shift ID. |
| `source_payload_hash` | Hash of normalized imported payload. |
| `source_payload_ref` | Controlled reference to retained import payload, if any. |
| `import_status_code` | ExitPass-controlled normalized import status at import time. |
| `source_system_code` | Raw source/provider system code at import time. |
| `source_status_code` | Raw provider status code at import time. |
| `source_status_description` | Raw provider status description at import time, if supplied. |
| `scheduled_start_at` | Imported scheduled start. |
| `scheduled_end_at` | Imported scheduled end. |
| `site_id` | Site from import or mapping. |
| `operator_user_id` | Resolved ExitPass user at import time. |
| `imported_at` | Import timestamp. |
| `imported_by_service_identity_id` | Importing service. |
| `correlation_id` | Cross-service trace. |

### Proposed Status Values

Proposed `operator_console.operator_shift_operational_status_enum`:

- `SCHEDULED`
- `ACTIVE`
- `ENDED`
- `SUSPENDED`
- `REVOKED`
- `TAKEN_OVER`
- `CANCELLED`
- `IMPORT_CONFLICT`

Source HR statuses use two layers: ExitPass-controlled normalized import status plus raw source/provider status fields. ExitPass should not model every HR provider status as a PostgreSQL enum because provider vocabularies are not stable across providers.

### Access Semantics

- Active shift access requires `operational_status = ACTIVE`, current time inside the active window, matching site, non-revoked state, and no unresolved import conflict.
- `REVOKED`, `SUSPENDED`, `ENDED`, `CANCELLED`, and `IMPORT_CONFLICT` deny controlled actions.
- `TAKEN_OVER` denies the original operator and allows only the approved takeover operator while the takeover is active.

## Shift Revocation and Controlled Takeover

### Proposed Tables

- `operator_console.operator_shift_revocations`
- `operator_console.operator_shift_takeovers`

### Shift Revocation

`operator_console.operator_shift_revocations`

Proposed fields:

| Field | Notes |
| --- | --- |
| `operator_shift_revocation_id` | Primary key. |
| `operator_shift_id` | FK to imported shift. |
| `revocation_status` | Proposed enum. |
| `reason_code` | Required controlled reason. |
| `reason_note` | Controlled note, no sensitive evidence. |
| `requested_by_user_id` | Requesting operator/supervisor. |
| `approved_by_user_id` | Approver if policy requires approval. |
| `revoked_operator_user_id` | Operator whose access was revoked. |
| `site_id` | Site context. |
| `requested_at` | Request timestamp. |
| `approved_at` | Approval timestamp. |
| `effective_at` | Revocation effective timestamp. |
| `correlation_id` | Cross-service trace. |
| audit columns | Standard created/updated fields. |

Proposed `operator_console.shift_revocation_status_enum`:

- `REQUESTED`
- `APPROVED`
- `REJECTED`
- `CANCELLED`
- `EFFECTIVE`
- `EXPIRED`

### Shift Takeover

`operator_console.operator_shift_takeovers`

Proposed fields:

| Field | Notes |
| --- | --- |
| `operator_shift_takeover_id` | Primary key. |
| `operator_shift_id` | FK to imported shift. |
| `original_operator_user_id` | User assigned by HR/Timekeeping. |
| `takeover_operator_user_id` | User taking over the shift. |
| `takeover_status` | Proposed enum. |
| `reason_code` | Required controlled reason. |
| `reason_note` | Controlled note, no sensitive evidence. |
| `requested_by_user_id` | Requesting actor. |
| `approved_by_user_id` | Supervisor/authorized approver where required. |
| `site_id` | Site context. |
| `requested_at` | Request timestamp. |
| `approved_at` | Approval timestamp. |
| `active_from` | Takeover start. |
| `active_to` | Takeover end. |
| `ended_at` | Actual end. |
| `correlation_id` | Cross-service trace. |
| audit columns | Standard created/updated fields. |

Proposed `operator_console.shift_takeover_status_enum`:

- `REQUESTED`
- `PENDING_APPROVAL`
- `APPROVED`
- `REJECTED`
- `ACTIVE`
- `ENDED`
- `CANCELLED`
- `EXPIRED`

### Audit Requirements

Revocation and takeover actions must emit `audit.audit_events` and may also create `operations.operator_action_logs` rows for user-facing action history. Required audit context includes shift ID, HR provider, external shift hash, original operator, takeover operator, site, approver, reason code, timestamps, and correlation ID.

## Operator Console Device and Browser Binding

### Proposed Table

`operator_console.operator_device_bindings`

Purpose: represent Operator Console browser/device trust independent of physical gate devices.

Proposed fields:

| Field | Notes |
| --- | --- |
| `operator_device_binding_id` | Primary key. |
| `device_binding_code` | Stable internal display/reference code. |
| `device_name` | Human-readable device/browser name. |
| `site_id` | FK to `sites.sites(site_id)`. |
| `site_group_id` | FK to `sites.site_groups(site_group_id)`. |
| `service_identity_id` | Optional FK to `identity.service_identities` for managed device or mTLS principal. |
| `browser_key_thumbprint` | Public key thumbprint for browser key binding. |
| `browser_public_key_ref` | Controlled reference to public key material if stored externally. |
| `mtls_certificate_thumbprint` | Certificate thumbprint for managed devices. |
| `mtls_certificate_subject` | Optional certificate subject display. |
| `mtls_certificate_expires_at` | Expiry timestamp. |
| `device_status` | Proposed enum. |
| `trust_level` | Proposed enum or controlled code. |
| `binding_source` | Manual enrollment, managed deployment, import, etc. |
| `last_seen_at` | Last successful trusted use. |
| `revoked_at` | Revocation timestamp. |
| `revocation_reason_code` | Controlled reason. |
| `lost_reported_at` | Lost-device timestamp. |
| `correlation_id` | Cross-service trace. |
| audit columns | Standard created/updated/row version fields. |

### Proposed Status Values

Proposed `operator_console.operator_device_binding_status_enum`:

- `PENDING`
- `ACTIVE`
- `SUSPENDED`
- `REVOKED`
- `LOST`
- `EXPIRED`
- `RETIRED`

Proposed `operator_console.operator_device_trust_level_enum`:

- `BROWSER_KEY_ONLY`
- `MTLS_ONLY`
- `BROWSER_KEY_AND_MTLS`
- `UNVERIFIED`

### Relationship to Existing Identity

Managed devices may also have `identity.service_identities` rows with `identity_type = DEVICE` or `GATEWAY` and `credential_type = MTLS_CERTIFICATE_REFERENCE`. Browser-only bindings do not require service identities unless the implementation chooses to model each browser-bound device as a non-human principal.

Do not reuse `gates.gate_devices` for Operator Console browser bindings. Gate devices represent gate/lane equipment and gate integration responsibilities.

### Site Assignment

Use `operator_device_bindings.site_id` for current Operator Console access checks and include `operator_console.operator_device_assignment_history` in the first migration proposal. Device/site assignment affects authorization and must be reconstructable for audit. Do not overload `sites.device_assignments`.

## Operator Access Evaluation Evidence

### Persistence Recommendation

For MVP, persist access evaluations only for:

- denied access
- statutory workflow start
- statutory decision submission
- evidence capture/view
- supervisor override attempts
- shift takeover requests/approvals
- device trust failures

Do not persist every page load, tab switch, harmless read, or purely navigational event.

### Proposed Table

`operator_console.operator_access_evaluations`

Proposed fields:

| Field | Notes |
| --- | --- |
| `operator_access_evaluation_id` | Primary key. |
| `correlation_id` | Cross-service trace. |
| `requested_action` | Controlled action code. |
| `evaluation_status` | Proposed enum. |
| `operator_user_id` | FK to `identity.users`. |
| `hr_timekeeping_identity_mapping_id` | FK to HR mapping. |
| `operator_device_binding_id` | FK to device binding. |
| `operator_shift_id` | FK to imported shift. |
| `operator_shift_takeover_id` | FK when takeover is active. |
| `site_id` | Site context. |
| `site_group_id` | Site group context. |
| `target_entity_type` | Entity being acted on. |
| `target_entity_id` | Target entity ID. |
| `evaluated_at` | Evaluation timestamp. |
| `decision_snapshot_json` | Minimal normalized snapshot of evaluated facts. |
| `audit_event_id` | FK/reference to audit event if materialized. |
| audit columns | Standard created fields. |

Proposed `operator_console.access_evaluation_status_enum`:

- `ALLOWED`
- `DENIED`

### Access Evaluation Reason Strategy

Use normalized child rows in `operator_console.operator_access_evaluation_reasons`, not `text[]` storage. Reason rows should support controlled reason codes, message/source context, auditability, and reporting without frequent enum migrations.

Initial controlled reason codes:

- `USER_NOT_ACTIVE`
- `USER_ROLE_NOT_ALLOWED`
- `DEVICE_NOT_REGISTERED`
- `DEVICE_PENDING`
- `DEVICE_SUSPENDED`
- `DEVICE_REVOKED`
- `DEVICE_LOST`
- `SITE_NOT_ASSIGNED`
- `SITE_MISMATCH`
- `SHIFT_NOT_FOUND`
- `SHIFT_NOT_ACTIVE`
- `SHIFT_SUSPENDED`
- `SHIFT_REVOKED`
- `SHIFT_TAKEOVER_NOT_APPROVED`
- `SESSION_NOT_PROVIDED`
- `SESSION_NOT_ACTIVE`
- `SESSION_SITE_MISMATCH`
- `ACTION_NOT_SUPPORTED`

## Entitlement Fingerprint Storage

### Recommendation

Add a related table rather than only adding columns to `discounts.statutory_discount_validations`.

Rationale:
- Fingerprints may evolve by algorithm/version.
- Duplicate detection may need multiple fingerprints per validation over time.
- Retention and redaction policy can differ from the validation lifecycle.
- A related table avoids widening the main validation row with sensitive-adjacent metadata.

### Proposed Table

`discounts.statutory_entitlement_fingerprints`

Proposed fields:

| Field | Notes |
| --- | --- |
| `statutory_entitlement_fingerprint_id` | Primary key. |
| `statutory_discount_validation_id` | FK to `discounts.statutory_discount_validations`. |
| `entitlement_type` | Reuse `discounts.statutory_entitlement_type_enum`. |
| `fingerprint_hash` | Stable hash used for duplicate detection. |
| `fingerprint_algorithm` | Controlled algorithm code. |
| `fingerprint_algorithm_version` | Algorithm version. |
| `salt_reference` | Reference to salt/pepper material, never the secret. |
| `source_metadata_level` | Controlled code for fields used. |
| `duplicate_detection_scope` | Controlled-code/reference-data value, not a hard PostgreSQL enum. Initial recommended code family: `OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE`. |
| `matched_existing_fingerprint_id` | Optional self-reference for duplicate detection. |
| `fingerprint_status` | Proposed enum. |
| `generated_at` | Generation timestamp. |
| `generated_by_service_identity_id` | Generating service. |
| `retention_policy_code` | Retention policy. |
| `purged_at` | Purge timestamp. |
| `correlation_id` | Cross-service trace. |
| audit columns | Standard created/updated fields. |

Proposed `discounts.entitlement_fingerprint_status_enum`:

- `ACTIVE`
- `SUPERSEDED`
- `REDACTED`
- `PURGED`
- `HASH_ONLY`

### Duplicate Detection Requirements

Recommended indexes:

- `(entitlement_type, duplicate_detection_scope, fingerprint_hash)` for duplicate checks.
- `(statutory_discount_validation_id)` for validation lookup.
- Partial index for active fingerprints only if the selected database pattern supports it.

Do not store raw statutory ID numbers, birth dates, or unmasked identity values in the fingerprint table. Store only hashes, algorithm metadata, and secret references.

Initial duplicate detection scope values may include:

- `SAME_SESSION_ONLY`
- `SAME_SITE_ACTIVE_DAY`
- `SAME_SITE_GROUP_ACTIVE_DAY`
- `GLOBAL_ACTIVE_DAY`
- `CONFIGURED_POLICY_WINDOW`

## Evidence Storage Ownership and Retention

Use existing `discounts.discount_evidence_references` for statutory evidence references.

Evidence ownership is locked as a split-responsibility model:

- Audit/Event Service owns evidence metadata governance, evidence access audit, evidence retrieval authorization, and evidence lifecycle audit.
- Actual encrypted images and documents live in an external evidence vault or object store.
- Central PMS and Operator Console write or consume evidence references and validation results only. They do not own raw evidence storage.
- Discount validation tables and evidence reference tables store references, hashes, classifications, and retention metadata, not raw images, raw document bytes, or raw sensitive payloads.

Evidence paths:

- Structured metadata default path: store structured ID metadata in controlled validation request fields or a future structured metadata table, with fingerprint stored in `discounts.statutory_entitlement_fingerprints`.
- Cropped image path: when site policy requires image capture, create `discounts.discount_evidence_references` with `evidence_type = SENIOR_CITIZEN_ID` or `PWD_ID`.
- Hash-only path: use `evidence_type = HASH_ONLY_REFERENCE`, `evidence_storage_type = HASH_ONLY`, and `evidence_capture_status = HASH_ONLY` when only a hash/fingerprint reference is retained.
- Image capture remains configurable by site. The default evidence path remains structured ID metadata plus entitlement fingerprint. Cropped image evidence is required only when site policy or regulation enables it.

Object storage requirements:

- Store only storage URI/reference, object hash, hash algorithm, retention expiry, access classification, capture metadata, and lifecycle metadata in ExitPass tables.
- Do not store raw sensitive image bytes, raw document bytes, or raw statutory ID payloads in PostgreSQL.
- Evidence retrieval must go through the Audit/Event-owned authorization and audit flow, not direct Operator Console or Central PMS storage access.

Access control:

- Operators may view structured evidence only during the active validation workflow.
- Operators must not retrieve stored ID images after submission.
- Supervisors and compliance users may access stored evidence only through controlled, audited flows.

Retention hooks:

- Use `discounts.discount_evidence_references.retention_policy_code`.
- Use `retention_expires_at`, `redaction_status`, `purged_at`, and purge actor fields for lifecycle enforcement.
- Use `discounts.discount_policy_references.requires_evidence_capture` for site-configurable image capture behavior.
- Retention should be configurable by evidence type and site policy.
- Evidence deletion or purge must leave audit-safe traces where legally allowed.

## Payable-Basis Update Storage

Do not add payable-basis update persistence in this schema extension unless a later workflow requires it.

Existing approved discount materialization remains backend-owned through:

- `discounts.statutory_discount_validations.tariff_snapshot_id`
- `discounts.statutory_discount_validations.statutory_discount_amount`
- `discounts.statutory_discount_validations.net_amount_after_discount`
- `core.tariff_snapshots.statutory_discount_validation_id`

Response labels such as `queued`, `applied`, and `failed` remain workflow response labels unless a later controlled design adds a persisted update table.

## Non-Payment Boundary

None of these proposed schema extensions may create or mutate:

- `core.payment_attempts`
- `core.payment_confirmations`
- `payments.provider_outcomes`
- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `coupons.coupon_applications`
- settlement truth records
- provider routing or provider payment state

The Operator Console may display payment status only as read-only context. It must not accept payments, confirm payments, refund payments, manually mark payments as paid, issue exit authorization, control gates, validate coupons, or affect payment finality.

## Controlled-Code and Enum Additions Requiring Approval

Proposed enum types:

- `operator_console.hr_identity_mapping_status_enum`
- `operator_console.operator_shift_operational_status_enum`
- `operator_console.shift_revocation_status_enum`
- `operator_console.shift_takeover_status_enum`
- `operator_console.operator_device_binding_status_enum`
- `operator_console.operator_device_trust_level_enum`
- `operator_console.access_evaluation_status_enum`
- `discounts.entitlement_fingerprint_status_enum`

Proposed controlled code sets:

- HR/Timekeeping provider codes.
- Operator access requested action codes.
- Operator access denial reason codes.
- Shift revocation reason codes.
- Shift takeover reason codes.
- Device binding source codes.
- Device revocation/lost/suspension reason codes.
- Fingerprint algorithm codes and metadata-level codes.
- Duplicate detection scope codes.

These additions must be approved in a later controlled schema/API slice before implementation.

## Implementation Slices After Approval

Recommended follow-up order:

1. DDL migration proposal for `operator_console` schema, HR identity mapping, imported shifts, revocation, takeover, device binding, and access evaluations.
2. DDL migration proposal for `discounts.statutory_entitlement_fingerprints`.
3. Reference data proposal for controlled codes and initial Operator Console permissions.
4. API contract update for access evaluation and shift/device read models.
5. Backend implementation for HR/Timekeeping import and identity mapping.
6. Backend implementation for browser/device binding MVP.
7. Backend implementation for access evaluation persistence and audit.
8. Backend implementation for statutory validation fingerprint generation.
9. Operator Console frontend integration against finalized contracts.

