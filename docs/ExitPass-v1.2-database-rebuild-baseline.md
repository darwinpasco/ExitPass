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
