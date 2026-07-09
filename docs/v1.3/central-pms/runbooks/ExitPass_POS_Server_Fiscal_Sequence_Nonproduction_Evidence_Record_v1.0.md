# ExitPass POS Server Fiscal Sequence Nonproduction Evidence Record v1.0

## 1. Purpose

This record documents the minimal DR-13 non-production assertion used to resolve the POS Server fiscal sequence non-production blocker from the controlled UAT dry-run checklist result.

Darwin Pasco confirms this is a non-production controlled UAT environment. The assigned fiscal identity, fiscal sequence policy, and fiscal sequence state are non-production values for controlled UAT only and must not allocate production fiscal numbers.

This record does not execute UAT and does not authorize UAT execution.

## 2. Source Blocker

| Field | Value |
| --- | --- |
| Source check | `DR-13` |
| Source result classification | `dry_run_checklist_blocked`, resolved to `dry_run_checklist_passed` after Darwin Pasco's assertion |
| Source blocker | POS Server fiscal sequence non-production classification was not verified before Darwin Pasco's assertion. |
| Required closure posture | Resolved for this non-production local controlled UAT run. |

## 3. Assigned POS Server Owner

| Role | Assigned owner |
| --- | --- |
| POS Server owner | Calvin Garcia |

For this non-production local run, Calvin Garcia or POS Server owner evidence is not required to resolve DR-13 because Darwin Pasco provided the non-production assertion.

## 4. Required Assigned References

| Reference type | Assigned value |
| --- | --- |
| Site | `DEV-SITE-ATC-001` |
| Site POS Server | `DEV-POS-SERVER-ATC-001` |
| Fiscal identity | `DEV-FISCAL-IDENTITY-ATC-001` |
| Fiscal sequence policy | `DEV-SI-SEQUENCE-POLICY-ATC-001` |
| Fiscal sequence state | `DEV-SI-SEQUENCE-STATE-ATC-001` |

If any assigned reference changes, update the Controlled UAT Data Assignment Record and rerun the relevant readiness review before using this evidence record.

## 5. Evidence Required

Darwin Pasco's assertion resolves DR-13 for this non-production local run by confirming that:

- fiscal identity `DEV-FISCAL-IDENTITY-ATC-001` exists;
- fiscal identity `DEV-FISCAL-IDENTITY-ATC-001` is non-production;
- fiscal identity `DEV-FISCAL-IDENTITY-ATC-001` is active/effective;
- fiscal sequence policy `DEV-SI-SEQUENCE-POLICY-ATC-001` exists;
- fiscal sequence policy `DEV-SI-SEQUENCE-POLICY-ATC-001` is non-production;
- fiscal sequence policy `DEV-SI-SEQUENCE-POLICY-ATC-001` is active/effective;
- fiscal sequence state `DEV-SI-SEQUENCE-STATE-ATC-001` exists;
- fiscal sequence state `DEV-SI-SEQUENCE-STATE-ATC-001` is non-production;
- fiscal sequence state `DEV-SI-SEQUENCE-STATE-ATC-001` is configured for Site POS Server `DEV-POS-SERVER-ATC-001`;
- the selected sequence cannot allocate production fiscal numbers.

## 6. Acceptable Evidence Forms

At least one acceptable evidence form must be attached for each required evidence item:

- POS Server owner signed statement;
- screenshot reference with secrets redacted;
- read-only DB/config query output with secrets redacted;
- config file reference with secrets redacted.

Evidence must not include secrets, database passwords, connection strings, certificates, private keys, payment provider payloads, customer PII, raw fiscal request payloads, raw POS Server request/response bodies, raw statutory evidence payloads, stack traces, or local environment dumps.

## 7. Suggested Read-Only Query Placeholders

These are placeholders only. They were not executed when this record was created.

The POS Server owner must adapt table names, column names, schemas, and environment markers to the actual approved POS Server database/config structure before use. Every query must be reviewed as read-only before execution.

### 7.1 Fiscal Identity Lookup Placeholder

```sql
-- Placeholder only. Not executed by this evidence record.
-- Purpose: prove the assigned fiscal identity exists, is active/effective,
-- and is classified as non-production.
SELECT fiscal_identity_ref,
       site_ref,
       site_pos_server_ref,
       environment_name,
       is_production_identity,
       status,
       effective_from,
       effective_to
FROM <pos_schema>.<fiscal_identity_table>
WHERE fiscal_identity_ref = 'DEV-FISCAL-IDENTITY-ATC-001'
  AND site_ref = 'DEV-SITE-ATC-001'
  AND site_pos_server_ref = 'DEV-POS-SERVER-ATC-001';
```

Expected evidence posture:

- exactly one assigned fiscal identity is returned;
- status is active/effective for the controlled UAT window;
- `environment_name` or equivalent marker is non-production;
- `is_production_identity` or equivalent marker is false.

### 7.2 Fiscal Sequence Policy Lookup Placeholder

```sql
-- Placeholder only. Not executed by this evidence record.
-- Purpose: prove the assigned fiscal sequence policy exists, is active/effective,
-- and is classified as non-production.
SELECT fiscal_sequence_policy_ref,
       fiscal_identity_ref,
       document_type,
       environment_name,
       is_production_sequence_policy,
       status,
       effective_from,
       effective_to,
       prefix_text,
       suffix_text
FROM <pos_schema>.<fiscal_sequence_policy_table>
WHERE fiscal_sequence_policy_ref = 'DEV-SI-SEQUENCE-POLICY-ATC-001'
  AND fiscal_identity_ref = 'DEV-FISCAL-IDENTITY-ATC-001';
```

Expected evidence posture:

- exactly one assigned fiscal sequence policy is returned;
- status is active/effective for the controlled UAT window;
- `environment_name` or equivalent marker is non-production;
- `is_production_sequence_policy` or equivalent marker is false;
- prefix/suffix/series values are clearly non-production or otherwise approved as disposable controlled UAT numbering.

### 7.3 Fiscal Sequence State Lookup Placeholder

```sql
-- Placeholder only. Not executed by this evidence record.
-- Purpose: prove the assigned fiscal sequence state exists, is configured
-- for the selected Site POS Server, and is classified as non-production.
SELECT fiscal_sequence_state_ref,
       fiscal_sequence_policy_ref,
       site_pos_server_ref,
       environment_name,
       is_production_sequence_state,
       status,
       current_sequence_value,
       next_sequence_value,
       effective_from,
       effective_to
FROM <pos_schema>.<fiscal_sequence_state_table>
WHERE fiscal_sequence_state_ref = 'DEV-SI-SEQUENCE-STATE-ATC-001'
  AND fiscal_sequence_policy_ref = 'DEV-SI-SEQUENCE-POLICY-ATC-001'
  AND site_pos_server_ref = 'DEV-POS-SERVER-ATC-001';
```

Expected evidence posture:

- exactly one assigned fiscal sequence state is returned;
- state is configured for `DEV-POS-SERVER-ATC-001`;
- status is active/effective for the controlled UAT window;
- `environment_name` or equivalent marker is non-production;
- `is_production_sequence_state` or equivalent marker is false.

### 7.4 Production/Non-Production Classification Lookup Placeholder

```sql
-- Placeholder only. Not executed by this evidence record.
-- Purpose: prove the identity, policy, and state cannot allocate production
-- fiscal numbers.
SELECT identity.fiscal_identity_ref,
       policy.fiscal_sequence_policy_ref,
       state.fiscal_sequence_state_ref,
       identity.environment_name AS identity_environment,
       policy.environment_name AS policy_environment,
       state.environment_name AS state_environment,
       identity.is_production_identity,
       policy.is_production_sequence_policy,
       state.is_production_sequence_state,
       policy.prefix_text,
       policy.suffix_text
FROM <pos_schema>.<fiscal_identity_table> identity
JOIN <pos_schema>.<fiscal_sequence_policy_table> policy
  ON policy.fiscal_identity_ref = identity.fiscal_identity_ref
JOIN <pos_schema>.<fiscal_sequence_state_table> state
  ON state.fiscal_sequence_policy_ref = policy.fiscal_sequence_policy_ref
WHERE identity.fiscal_identity_ref = 'DEV-FISCAL-IDENTITY-ATC-001'
  AND policy.fiscal_sequence_policy_ref = 'DEV-SI-SEQUENCE-POLICY-ATC-001'
  AND state.fiscal_sequence_state_ref = 'DEV-SI-SEQUENCE-STATE-ATC-001'
  AND state.site_pos_server_ref = 'DEV-POS-SERVER-ATC-001';
```

Expected evidence posture:

- identity, policy, and state all have non-production environment markers;
- all production flags or equivalent controls are false;
- numbering series/prefix/suffix cannot be confused with production fiscal numbers;
- the POS Server owner explicitly confirms that the selected sequence cannot allocate production fiscal numbers.

## 8. Evidence Table

| Evidence item | Owner | Source of truth | Evidence reference | Result | Reviewer | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Fiscal identity exists | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Confirmed for controlled UAT local run | Darwin Pasco | Resolved |
| Fiscal identity is non-production | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Non-production controlled UAT value only | Darwin Pasco | Resolved |
| Fiscal identity is active/effective | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Confirmed for controlled UAT local run | Darwin Pasco | Resolved |
| Fiscal sequence policy exists | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Confirmed for controlled UAT local run | Darwin Pasco | Resolved |
| Fiscal sequence policy is non-production | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Non-production controlled UAT value only | Darwin Pasco | Resolved |
| Fiscal sequence policy is active/effective | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Confirmed for controlled UAT local run | Darwin Pasco | Resolved |
| Fiscal sequence state exists | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Confirmed for controlled UAT local run | Darwin Pasco | Resolved |
| Fiscal sequence state is non-production | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Non-production controlled UAT value only | Darwin Pasco | Resolved |
| Fiscal sequence state is configured for `DEV-POS-SERVER-ATC-001` | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Confirmed for controlled UAT local run | Darwin Pasco | Resolved |
| Sequence cannot allocate production fiscal numbers | Darwin Pasco | Non-production controlled UAT assertion | Darwin Pasco assertion | Must not allocate production fiscal numbers | Darwin Pasco | Resolved |

## 9. Owner Attestation

Complete this section only after evidence is attached.

| Field | Value |
| --- | --- |
| Assertion owner name | Darwin Pasco |
| Evidence package/reference | Darwin Pasco non-production assertion |
| Owner attestation | This is a non-production controlled UAT environment. The assigned fiscal identity, fiscal sequence policy, and fiscal sequence state are non-production values for controlled UAT only and must not allocate production fiscal numbers. |
| Attestation date/time | 2026-07-09 |
| Reviewer name | Darwin Pasco |
| Reviewer decision | DR-13 resolved for this non-production local controlled UAT run |
| Review date/time | 2026-07-09 |

Suggested owner attestation:

```text
I confirm that Site DEV-SITE-ATC-001, Site POS Server DEV-POS-SERVER-ATC-001,
Fiscal identity DEV-FISCAL-IDENTITY-ATC-001, Fiscal sequence policy
DEV-SI-SEQUENCE-POLICY-ATC-001, and Fiscal sequence state
DEV-SI-SEQUENCE-STATE-ATC-001 are non-production, active/effective for the
controlled UAT review window, and cannot allocate production fiscal numbers.
```

## 10. Closure Decision

Current decision: `ready_for_dry_run_recheck`

Closure note:

- DR-13 is resolved by Darwin Pasco's non-production assertion for this local controlled UAT run;
- this record does not set `ready_for_execution`.

This record closes only the DR-13 non-production classification gap for this local controlled UAT run.

## 11. Explicit Non-Goals

This record does not:

- execute UAT;
- create fiscal issuance;
- mutate POS Server;
- call runtime mutation endpoints;
- confirm payment;
- trigger ExitAuthorization;
- trigger gate behavior;
- create refund/reversal;
- generate PDF, HTML, or QR artifacts;
- define final BIR statutory wording.

## 12. Recommended Next Step

Use the updated dry-run checklist result as the current DR-13 status source. UAT execution remains blocked until separately authorized.

## 13. Validation

`git diff --check` result: passed.
