# Operator Console Sandbox Scripts

These scripts are sandbox-only helpers for local Operator Console statutory discount validation.

## Statutory Discount Pilot Fixture

Run against a local PostgreSQL validation database after confirming the database is not production:

```powershell
$env:EXITPASS_INTEGRATION_DB="Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me"
```

Apply `Seed-StatutoryDiscountPilotFixture.sql`, then run `Verify-StatutoryDiscountPilotFixture.sql`.

The seed creates deterministic test-only site, operator, policy, active parking session, and original active tariff snapshot rows for the #233 validation runbook. It does not create payment attempts, payment confirmations, provider sessions or outcomes, exit authorizations, gate records, coupon applications, reconciliation rows, production credentials, raw ID numbers, or evidence file references.

If the optional `operator_console` extension tables are present, the seed also inserts the deterministic HR mapping, operator device binding, device assignment, and active shift context. If those tables are not installed in the local v1.2 baseline database, the seed leaves them untouched and the verification script reports `operator_access_context_status = NOT_INSTALLED`.
