# HikCentral Projection And Resolve UAT Results

Date: 2026-06-22

This document records the completed HikCentral projection and resolve UAT results for the TEST SITE UAT parking lot. It covers projection sync, normal live vendor resolve, guarded degraded projection fallback, authority boundaries, and the defects fixed during UAT.

No secrets, AppKey, AppSecret, local credentials, or live connection strings are included.

## Purpose

The purpose of this UAT was to prove that ExitPass can:

- Pull HikCentral passageway records for a site-scoped parking lot.
- Persist latest-known vendor session projection rows for card/ticket lookup and continuity visibility.
- Resolve a normal live HikCentral parking session and tariff through the vendor adapter.
- Return a guarded degraded projection fallback only when live HikCentral lookup is unavailable.
- Preserve ExitPass authority boundaries for payment, tariff, parking-session truth, and exit authorization.

## UAT Scope

The completed UAT covered:

1. HikCentral projection sync.
2. Normal HikCentral live resolve.
3. Guarded projection fallback resolve.
4. Authority boundary verification.
5. No payment or exit side effects.

The UAT did not include payment creation, payment finality, exit authorization, gate open, or any HikCentral fee confirmation mutation.

## Environment And Non-Secret Identifiers

| Field | Value |
| --- | --- |
| UAT date | `2026-06-22` |
| Parking lot | `TEST SITE` |
| `parking_lot_index_code` | `1` |
| `projection_sync_target_id` | `abe7da56-1198-4d51-901f-87e8fb7cd40d` |
| `site_id` | `c9000000-0000-0000-0000-000000000001` |
| `site_group_id` | `ce000000-0000-0000-0000-000000000001` |
| `vendor_system_id` | `31bde78a-5dfc-45c3-a1f3-e48abaf90927` |
| `vendor_code` | `HIKCENTRAL` |
| `environment_code` | `UAT` |
| Test ticket/card | `3519278781100` |

## Preconditions

- Current database contains the projection schema objects:
  - `sessions.vendor_session_projections`
  - `sessions.vendor_session_projection_sync_targets`
- If the database predates the projection schema, `docs/sql/HikCentralProjectionSchemaPatch.sql` must be applied before UAT.
- The HikCentral vendor system exists in `integration.vendor_systems`.
- The site and site group identifiers are valid for the UAT site.
- The parking lot index code is confirmed from HikCentral:

```http
POST /artemis/api/vehicle/v1/parkinglot/list
```

- HikCentral secrets are supplied only through local environment, user secrets, or an approved secret store.
- Degraded fallback remains disabled unless explicitly enabled for a guarded fallback test.

## Projection Sync Result

Projection sync passed.

| Metric | Result |
| --- | --- |
| HikCentral passageway API item count | `19` |
| Records processed | `19` |
| `records_seen` | `19` |
| `records_projected` | `19` |
| `records_skipped` | `0` |
| `records_upserted` | `19` |
| Scheduler `targets_succeeded` | `1` |
| Scheduler `targets_failed` | `0` |

Confirmed outcomes:

- `sessions.vendor_session_projections` contains projected rows.
- `personInfo.cardNum` values are available for ticket/card lookup where present.
- `plateLicense = Unknown` values are stored as `NULL`.
- HikCentral `+08:00` timestamps are persisted as equivalent UTC `timestamptz` values.
- Sync target health is `HEALTHY`.
- Sync target `failure_count = 0`.
- Sync target `last_error_code = NULL`.
- Sync target `last_error_message = NULL`.
- Sync target was restored to `enabled_flag = false` after UAT.

## Normal Live Resolve Result

Normal live resolve passed.

| Field | Result |
| --- | --- |
| Endpoint | `POST /v1/vendor-parking/resolve` |
| Result | `resolved` |
| Parking session created | `3e9cdb52-b025-4f6e-a249-d0051893881a` |
| Tariff snapshot created | `cb5ce0fc-a186-478a-804b-94b954ac7607` |
| Ticket/card | `3519278781100` |
| Amount | `PHP 3,190.00` |
| Payment status | `Not Started` |
| DB parking session status | `ACTIVE` |
| API parking status | `PaymentRequired` |

Confirmed outcomes:

- Live HikCentral lookup returned vendor-authoritative parking-session and tariff data.
- Central PMS persisted a parking session and tariff snapshot for the normal resolve path.
- No `PaymentAttempt` was created.
- No `ExitAuthorization` was issued.
- Vendor PMS remained parking-session authority.
- Vendor PMS remained tariff authority.
- ExitPass remained payment authority.

## Guarded Degraded Projection Fallback Result

Guarded degraded projection fallback passed.

Fallback test conditions:

- HikCentral unavailability was simulated using a bad `BaseUrl`.
- `DegradedResolveFallbackEnabled` was temporarily set to `true`.
- `SchedulerEnabled` was set to `false` during the fallback test.
- `MaxProjectionAgeMinutes` was temporarily widened to `10080` for guarded UAT.

| Field | Result |
| --- | --- |
| Endpoint | `POST /v1/vendor-parking/resolve` |
| HTTP result | `503 Service Unavailable` |
| `errorCode` | `VENDOR_UNAVAILABLE_PROJECTION_SNAPSHOT_AVAILABLE` |
| `details.source` | `projection_snapshot` |
| `details.projection_based` | `true` |
| `details.non_authoritative` | `true` |
| `details.card_num` | `3519278781100` |
| `details.projection_status` | `Active` |
| `details.parking_session_authority` | `Vendor PMS` |
| `details.tariff_authority` | `Vendor PMS` |
| `details.payment_authority` | `ExitPass` |

The fallback response confirmed that projection data is for continuity visibility only and must not be used as tariff finality, payment finality, parking-session authority, or exit authorization.

Post-test restoration:

- `DegradedResolveFallbackEnabled` restored to `false`.
- HikCentral `BaseUrl` restored to `http://127.0.0.1:9019`.
- Sync target remained disabled after UAT.

## Authority Boundary Confirmations

Confirmed:

- HikCentral / Vendor PMS remains parking-session operational authority.
- HikCentral / Vendor PMS remains tariff authority when reachable.
- ExitPass remains payment authority.
- Projection data is a latest-known continuity snapshot only.
- Projection data is non-authoritative.
- Projection fallback does not calculate tariff.
- Projection fallback does not create `PaymentAttempt`.
- Projection fallback does not record payment finality.
- Projection fallback does not issue `ExitAuthorization`.
- Projection fallback does not mark tickets as paid.
- Normal live resolve persisted a parking session and tariff snapshot but did not create payment or exit side effects.

## Issues Found And Fixes Made

### #295 Fix HikCentral Passageway Projection Response Mapping

Problem:

- Projection sync reached HikCentral but skipped records.
- Actual HikCentral response used fields such as:
  - `parkingLotInfo.parkingLotIndexCode`
  - `parkingLotInfo.parkingLotName`
  - `passagewayInfo.passagewayIndexCode`
  - `passagewayInfo.passagewayName`
  - `laneInfo.laneIndexCode`
  - `laneInfo.laneName`
  - `carInfo.ImageUrl`
  - `carInfo.EnterTime`
  - `carInfo.ExitTime`

Fix:

- DTO and normalizer mapping were updated for actual HikCentral field names.
- Empty `ExitTime`, `ImageUrl`, and `cardNum` are normalized safely.
- `plateLicense = Unknown` is normalized to `NULL`.
- `guid` remains the stable identity when present.

### #296 Fix HikCentral Projection UTC Timestamp Persistence

Problem:

- Projection upsert failed because HikCentral timestamps carried `+08:00` offset and Npgsql requires UTC offset `0` for PostgreSQL `timestamptz`.

Fix:

- Projection persistence now converts `DateTimeOffset` values to UTC before Npgsql binding.

### #297 Fix Vendor Parking Resolve UTC Timestamp Persistence

Problem:

- Normal live resolve failed when persisting parking session and tariff timestamps with `+08:00` offset.

Fix:

- Vendor parking resolve persistence now converts `entry_at`, `calculated_at`, and `expires_at` timestamps to UTC before Npgsql binding.

## Final Safe Operating State

After UAT:

- Projection sync target `abe7da56-1198-4d51-901f-87e8fb7cd40d` is disabled.
- Sync target health remains `HEALTHY`.
- Sync target `failure_count = 0`.
- Sync target error fields are `NULL`.
- `DegradedResolveFallbackEnabled = false`.
- HikCentral `BaseUrl` restored to `http://127.0.0.1:9019`.
- No payment attempts were created during fallback verification.
- No exit authorizations were issued during fallback verification.

## Remaining Risks And Recommended Next Steps

- Keep degraded fallback disabled by default until an operations runbook defines approval, monitoring, and stale-data thresholds.
- Add dashboard or alert visibility for projection sync target health and freshness.
- Add a controlled UAT case for stale projection behavior after operations approves the stale-data threshold.
- Confirm production parking lot mappings from HikCentral parking lot list before enabling any production target.
- Review retention and cleanup policy for projection snapshots after production volume is known.
- Keep documenting live HikCentral payload variations as vendor firmware/API behavior changes.

## Appendix A: Verification SQL Snippets

Use these read-only snippets for verification. Replace identifiers only when running against another approved UAT target.

### Sync Target State

```sql
SELECT
    projection_sync_target_id,
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    parking_lot_name,
    enabled_flag,
    health_status,
    failure_count,
    last_success_at,
    last_failure_at,
    last_attempt_at,
    last_error_code,
    last_error_message
FROM sessions.vendor_session_projection_sync_targets
WHERE projection_sync_target_id = 'abe7da56-1198-4d51-901f-87e8fb7cd40d';
```

### Projection Rows For Parking Lot

```sql
SELECT
    vendor_session_projection_id,
    vendor_record_guid,
    parking_lot_index_code,
    card_num,
    plate_license,
    enter_time,
    exit_time,
    projection_status,
    stable_identity_type,
    stable_identity_key,
    first_seen_at,
    last_seen_at,
    last_refreshed_at
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
ORDER BY last_refreshed_at DESC;
```

### Ticket/Card Projection Lookup

```sql
SELECT
    vendor_session_projection_id,
    vendor_record_guid,
    card_num,
    plate_license,
    enter_time,
    exit_time,
    projection_status,
    last_refreshed_at
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
  AND card_num = '3519278781100'
ORDER BY
    CASE projection_status
        WHEN 'ACTIVE' THEN 0
        WHEN 'UNKNOWN' THEN 1
        WHEN 'STALE' THEN 2
        WHEN 'EXITED' THEN 3
        ELSE 4
    END,
    last_refreshed_at DESC,
    enter_time DESC NULLS LAST;
```

### Confirm Unknown Plate Normalization

```sql
SELECT
    COUNT(*) FILTER (WHERE plate_license IS NULL) AS null_plate_count,
    COUNT(*) FILTER (WHERE plate_license ILIKE 'unknown') AS unknown_plate_count
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1';
```

### Confirm UTC Timestamptz Storage View

```sql
SELECT
    vendor_session_projection_id,
    enter_time AT TIME ZONE 'UTC' AS enter_time_utc,
    exit_time AT TIME ZONE 'UTC' AS exit_time_utc,
    last_refreshed_at AT TIME ZONE 'UTC' AS last_refreshed_at_utc
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
ORDER BY last_refreshed_at DESC
LIMIT 20;
```

### Normal Resolve Persistence Check

```sql
SELECT
    ps.parking_session_id,
    ps.vendor_session_ref,
    ps.ticket_number_masked,
    ps.entry_at,
    ps.session_status,
    ts.tariff_snapshot_id,
    ts.net_amount,
    ts.currency_code,
    ts.calculated_at,
    ts.expires_at,
    ts.snapshot_status
FROM core.parking_sessions AS ps
LEFT JOIN core.tariff_snapshots AS ts
    ON ts.parking_session_id = ps.parking_session_id
WHERE ps.parking_session_id = '3e9cdb52-b025-4f6e-a249-d0051893881a'
ORDER BY ts.created_at DESC;
```

### Payment And Exit Side Effect Check

```sql
SELECT
    (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = '3e9cdb52-b025-4f6e-a249-d0051893881a') AS payment_attempt_count,
    (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = '3e9cdb52-b025-4f6e-a249-d0051893881a') AS exit_authorization_count;
```

## Appendix B: PlantUML Diagrams

Diagram sources:

- `docs/diagrams/hikcentral-normal-resolve-flow.puml`
- `docs/diagrams/hikcentral-degraded-projection-fallback-flow.puml`

