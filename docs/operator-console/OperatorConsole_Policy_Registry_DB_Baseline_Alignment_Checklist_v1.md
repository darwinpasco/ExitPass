# Operator Console Policy Registry DB Baseline Alignment Checklist v1

## Position

- [ ] Production statutory discount auto-application remains NO-GO.
- [ ] DB baseline owner is confirmed as `D:\SourceCodes\ExitPass_DBv1.2`.
- [ ] Local DB drift is not promoted as baseline.
- [ ] App repo readiness scripts remain verification support only.

## Baseline Decision

- [ ] Confirm hybrid transition: add `discounts.statutory_discount_policy_registry` and temporarily retain `discounts.discount_policy_references`.
- [ ] Decide schema-only versus schema-plus-approved-reference-data baseline.
- [ ] Confirm whether national fallback rows for RA 9994 and RA 10754 are approved baseline reference data.
- [ ] Keep pilot/sample/sandbox rows separate from production-active rows.

## DB Repo Work For Next Slice

- [ ] Add/update schema artifacts for the dedicated registry.
- [ ] Add/update controlled values for verification, benefit, residency, and related policy fields.
- [ ] Add/update constraints, FKs, and indexes.
- [ ] Add jurisdiction/site/site-group scope support if approved.
- [ ] Add reference data only after Legal/Product/Compliance/Ops approval.
- [ ] Update validation/build notes and Atlas/state-based scripts if needed.
- [ ] Rebuild local DB from the DB repo baseline.
- [ ] Run Atlas/state-based compare and confirm expected drift result.

## App Follow-Up After DB Alignment

- [ ] Update readiness SQL to prefer the dedicated registry when present.
- [ ] Design resolver query path for the dedicated registry.
- [ ] Add dedicated registry fixtures and tests.
- [ ] Add audit/reporting display of verification/readiness status.
- [ ] Add Operator Console readiness indicators if required.

## Rollout Gate

- [ ] Run `Verify-ProductionPolicyRegistryReadiness.sql`.
- [ ] Run `Run-ProductionPolicyRegistryReadinessCheck.ps1`.
- [ ] Run Central PMS policy resolution tests.
- [ ] Run Operator Console statutory discount controlled validation.
- [ ] Approve controlled pilot only with manually verified site-approved policy evidence.
- [ ] Approve full production only after DB repo alignment and verified production policy rows are complete.

## Boundary Checks

- [ ] No backend behavior changes in the planning slice.
- [ ] No frontend behavior changes in the planning slice.
- [ ] No DB, DDL, migration, or seed mutations in the planning slice.
- [ ] No AUB, WebPay, payment routing, coupon, reconciliation, HikCentral, or gate changes.
- [ ] No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
