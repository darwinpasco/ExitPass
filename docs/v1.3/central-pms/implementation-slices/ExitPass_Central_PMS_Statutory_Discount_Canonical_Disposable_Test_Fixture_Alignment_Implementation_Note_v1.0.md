# ExitPass Central PMS Statutory Discount Canonical Disposable Test Fixture Alignment Implementation Note v1.0

## Purpose

This bounded test-infrastructure slice aligns proof-grade Central PMS statutory-discount integration tests to disposable PostgreSQL databases built from the current canonical database generated SQL.

No statutory-discount runtime behavior, public API route, DTO, statutory calculation, VAT treatment, payment finality, fiscal issuance, ExitAuthorization, gate behavior, WebPay, APT, Operator Console UI, POS Server, or Management Platform behavior changed.

## Governing Canonical Database Source

The fixture uses the current canonical database repository as read-only source:

- Repository: `D:\SourceCodes\exitpassdb_v1.2`
- Branch: `develop`
- Generated SQL: `build/generated/exitpass-full-object.generated.sql`
- Central PMS validator: `scripts/validation/Validate-V13CentralPmsAlignment.sql`

The fixture does not use `ExitPass_Full_Database_Creation_DDL_v1.2.sql`, `D:\SourceCodes\ExitPass_DBv1.2`, retired statutory app-local SQL patches, or `exitpass_v12_dev` as schema authority.

## Disposable Fixture Lifecycle

`StatutoryDiscountCanonicalDatabaseFixture` creates one uniquely named PostgreSQL database for the statutory test collection using the prefix `exitpass_statutory_fixture_`.

Setup phases are explicit:

- configuration
- disposable database creation
- canonical generated SQL apply
- canonical Central PMS alignment validation
- statutory canonical schema prerequisite verification
- test connection-string publication

Cleanup forcibly drops only the fixture-owned disposable database and restores previous test connection-string environment variables. Protected database names include `postgres`, `template0`, `template1`, and `exitpass_v12_dev`.

## Configuration

The fixture supports test-only overrides:

- `EXITPASS_STATUTORY_CANONICAL_DB_REPO`
- `EXITPASS_STATUTORY_CANONICAL_GENERATED_SQL`
- `EXITPASS_STATUTORY_CANONICAL_ALIGNMENT_VALIDATOR`
- `EXITPASS_STATUTORY_DB_FIXTURE_ADMIN_CONNECTION`
- `EXITPASS_STATUTORY_DB_FIXTURE_PREFIX`
- `EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_CONTAINER`
- `EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_USER`

Secrets are not printed. Failure messages identify the lifecycle phase without dumping full connection strings or SQL payloads.

## Isolation Model

The existing `OperatorConsoleManualFixture` xUnit collection now owns the canonical disposable fixture and remains `DisableParallelization = true`.

This keeps statutory tests serialized inside the affected collection without disabling parallelism for the whole integration-test assembly. The model prevents concurrent canonical SQL application, collection teardown while another statutory class is still running, and process-wide connection-string races inside the statutory proof group.

## Tests Migrated

The canonical disposable fixture now covers the staged statutory repository tests, decision façade repository tests, service-channel review repository tests, service-channel review API tests, service-channel post-approval application-intent tests, and retained Operator Console statutory API tests that participate in the statutory review/apply flow.

The fixture runs `StatutoryDiscountCanonicalSchemaPrerequisite` after canonical SQL and validator execution. Missing canonical schema fails setup clearly and does not trigger fallback patch application.

## Retired Patch Prohibition

The six promoted statutory application-local patches and the already-retired payable-basis statutory patches are not executed by active statutory fixtures.

`Validate_RetiredStatutoryDiscountCanonicalPatches.ps1` now also verifies that the canonical disposable fixture uses the canonical generated SQL and Central PMS validator, protects `exitpass_v12_dev`, and does not reference retired statutory patch paths.

## Repeatability and Cleanup Posture

Each fixture run creates a fresh disposable database and drops it during fixture disposal. Successful validation must show no database matching `exitpass_statutory_fixture_%` remains after the run.

The shared accumulated `exitpass_v12_dev` database is not mutated by the migrated statutory proof path.

## Deferred Tests and Cleanup

The stale standalone DDL references in non-statutory Vendor Session unit-test helpers remain inventoried for a broader test-baseline cleanup task. This slice does not redesign the full Central PMS integration-test platform, Docker lifecycle, connection-string ownership, per-test database creation, or unrelated payment fixture architecture.

## Remaining Work

- Perform the broad statutory-discount database-source documentation correction.
- Run canonical-only runtime and readback revalidation after this fixture alignment.
- Resume channel-safe application readback hardening only after canonical-only revalidation permits it.

## Authorization

This test-infrastructure slice does not authorize WebPay or APT integration.

WebPay integration: not authorized yet
APT integration: not authorized yet
