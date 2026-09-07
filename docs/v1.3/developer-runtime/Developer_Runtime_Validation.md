# Developer Runtime Validation

**DEVELOPER SYNTHETIC ENVIRONMENT**

**NOT REAL-SITE IST ACCEPTANCE**

## Boundary

- Profile: `DEVELOPER`
- Network: `exitpass-dev-synthetic`
- Database: `exitpass-dev-synthetic-db` / `exitpass-dev-synthetic-data`
- Site Group: `DEV` (`12000000-0000-0000-0000-000000000301`)
- Site: `ExitPass Developer Parking` (`12000000-0000-0000-0000-000000000302`)
- Vendor: `HIKCENTRAL_DEV` (`de100000-0000-0000-0000-000000000003`)
- Site Adapter: `exitpass-dev-site-adapter` (`de100000-0000-0000-0000-000000000004`)
- HikCentral boundary: `exitpass-dev-hikcentral` WireMock
- Payment environment: PayMongo TEST

No Developer component uses the persistent PITX Testing database, volume,
network, Site, or Site Adapter.

## Contract Proof

WireMock exposes the HikCentral V3.1-compatible passageway, fee calculation,
and fee confirmation paths. Successful mappings require the current signed
headers and operation-specific JSON body. Malformed fee calculation and fee
confirmation requests receive HTTP 400.

The five deterministic tickets resolve through Central PMS, the Developer Site
Adapter, and WireMock. Each resolve creates one Developer parking session and
one authoritative tariff snapshot:

| Ticket | Plate | Fee | Method |
| --- | --- | ---: | --- |
| `DEV-QRPH-001` | `DEVQRPH1` | PHP 25.00 | QRPH |
| `DEV-GCASH-001` | `DEVGCASH1` | PHP 30.00 | GCash |
| `DEV-MAYA-001` | `DEVMAYA1` | PHP 35.00 | Maya |
| `DEV-CARD-001` | `DEVCARD1` | PHP 40.00 | Card |
| `DEV-APT-CASH-001` | `DEVAPT01` | PHP 45.00 | APT cash fixture |

Projection synchronization persists five non-authoritative records and returns
the Developer target to `HEALTHY`. Live payable-basis resolution still uses
HikCentral fee calculation and does not use projection fallback.

QRPH, GCash, Maya, and Card each create a normal pending PaymentAttempt and a
PayMongo TEST redirect handoff. Provider completion is intentionally not
fabricated. The Site Adapter fee-confirmation path signs and sends the matching
HikCentral request to WireMock and maps its successful response normally.

## Cross-Repository Boundaries

Developer fiscal completion remains blocked until ExitPass-PoSServer adopts the
Developer profile. The Testing POS database must not be used for Developer
transactions. APT Developer profile adoption remains a separate repository
task.
