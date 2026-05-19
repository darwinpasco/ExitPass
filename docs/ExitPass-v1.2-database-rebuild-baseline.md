# ExitPass v1.2 Database Rebuild Baseline

The ExitPass v1.2 development database baseline is established through `ExitPass_Full_Database_Creation_DDL_v1.2.sql`. This script is the authoritative clean rebuild DDL baseline for v1.2 development and validation environments.

The validation and preflight scripts are used after rebuild to confirm that required schemas, enums, tables, constraints, indexes, reference constraints, and payment-chain routines exist as expected. The focused payment-chain validation currently uses `infra/db/Validate-FullDdlPaymentChain.ps1` to rebuild a validation database, verify the typed v1.2 routines, point Central PMS at the rebuilt database, and run the focused payment-chain integration guard.

This v1.2 rebuild baseline is intended for clean rebuilds. It is not an in-place upgrade path from the legacy v1.0 database. Production migration planning will be handled separately when a real production cutover path is required.

Future schema changes must be made through controlled DDL updates or migrations. When a change affects contracts, physical names, constraints, indexes, or documented data definitions, update the corresponding ExitPass v1.2 artifacts:

- Database Design
- API Contract Pack, if API behavior or physical names change
- Engineering Pack
- Data Dictionary
- Constraint Matrix and Index Inventory, where affected

The engineering stance is rebuild baseline first, migration strategy later when production migration planning begins.

## ExitPass v1.2 Reference Data Seed

After a clean rebuild, apply `infra/db/seed/ExitPass_Reference_Data_v1.2.sql` to install the deterministic v1.2 reference-data baseline for local development and integration testing. The seed script is idempotent and may be re-run after the full DDL has created the database structure.

Example Docker PostgreSQL command:

```powershell
Get-Content .\infra\db\seed\ExitPass_Reference_Data_v1.2.sql -Raw |
  docker exec -i exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d <database-name>
```

The reference-data seed is intended for clean v1.2 development databases. It is not an upgrade script for legacy v1.0 databases and does not carry production secrets or credential material.

## One-Command Local Bootstrap

Use `infra/db/scripts/Reset-ExitPassV12Database.ps1` when a local Docker PostgreSQL database must be rebuilt from the v1.2 full DDL, seeded with the v1.2 reference data baseline, replayed to prove seed idempotency, and checked with the repository validation/preflight script where available. The script writes step-specific logs under `logs/db` by default.

The script refuses to drop or recreate a database unless `-ForceRecreate` is supplied.

Rebuild the default development database:

```powershell
.\infra\db\scripts\Reset-ExitPassV12Database.ps1 -ForceRecreate
```

Rebuild a test database:

```powershell
.\infra\db\scripts\Reset-ExitPassV12Database.ps1 -DatabaseName exitpass_v12_seed_test -ForceRecreate
```

Skip validation/preflight:

```powershell
.\infra\db\scripts\Reset-ExitPassV12Database.ps1 -ForceRecreate -SkipValidation
```

Skip seed replay:

```powershell
.\infra\db\scripts\Reset-ExitPassV12Database.ps1 -ForceRecreate -SkipSeedReplay
```
