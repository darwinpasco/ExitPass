# HikCentral Ticket-Only Read-Only Validation

This document covers the local HikCentral Professional V3.1.0 ticket-only discovery path. It is read-only and must not be used to confirm payment or open gates.

## Confirmed Local Endpoint

- Base URL: `http://127.0.0.1:9019`
- Version endpoint: `POST /artemis/api/common/v1/version`
- Confirmed version response:
  - `produceName = HikCentral Professional`
  - `softVersion = V3.1.0.0`
- Parking lot endpoint: `POST /artemis/api/vehicle/v1/parkinglot/list`
- Confirmed parking lot:
  - `parkingLotIndexCode = 1`
  - `parkingLotName = TEST SITE`

## Signing Pattern

The local OpenDataServer profile uses:

- Method: `POST`
- `Accept: */*`
- `Content-Type: application/json`
- Signed headers: `x-ca-key,x-ca-timestamp`
- No `Content-MD5`
- No `Date` header

Canonical string:

```text
POST
*/*
application/json
x-ca-key:<AppKey>
x-ca-timestamp:<timestamp>
<path>
```

The signature is HMAC-SHA256 over the canonical string using `HIKCENTRAL_APP_SECRET`, then Base64 encoded.

## Required Environment

Set these locally only. Do not commit them.

```powershell
$env:HIKCENTRAL_BASE_URL = "http://127.0.0.1:9019"
$env:HIKCENTRAL_APP_KEY = "<local only>"
$env:HIKCENTRAL_APP_SECRET = "<local only>"
$env:HIKCENTRAL_TEST_PARKING_LOT_INDEX_CODE = "1"
$env:HIKCENTRAL_CONFIRM_PAYMENT_ENABLED = "false"
$env:HIKCENTRAL_GATE_OPEN_ALLOWED = "false"
```

Optional discovery window:

```powershell
$env:HIKCENTRAL_TICKET_DISCOVERY_BEGIN_TIME = "2026-06-11T00:00:00+08:00"
$env:HIKCENTRAL_TICKET_DISCOVERY_END_TIME = "2026-06-11T23:59:59+08:00"
```

Optional cross-records camera:

```powershell
$env:HIKCENTRAL_TEST_CAMERA_INDEX_CODE = "<local camera index code>"
```

Optional parking floor for current parking-space status:

```powershell
$env:HIKCENTRAL_TEST_FLOOR_INDEX_CODE = "<local floor index code>"
```

If the optional window variables are not set, the live runner uses the current local day:
`00:00:00` through `23:59:59` in the machine's local offset. The runner prints the resolved
window before making ticket discovery calls. All timestamps sent to HikCentral are formatted as
`yyyy-MM-ddTHH:mm:sszzz`, for example `2026-06-11T15:00:00+08:00`, with no fractional seconds.

## Run Version And Parking Lot Checks

The live discovery runner performs version and parking lot list calls before ticket discovery:

```powershell
.\scripts\hikcentral-ticket-only-readonly-discovery.ps1
```

If local PowerShell execution policy blocks direct script execution, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\hikcentral-ticket-only-readonly-discovery.ps1
```

With an explicit time window:

```powershell
.\scripts\hikcentral-ticket-only-readonly-discovery.ps1 `
  -BeginTime "2026-06-11T00:00:00+08:00" `
  -EndTime "2026-06-11T23:59:59+08:00"
```

With a camera index for cross-record discovery:

```powershell
.\scripts\hikcentral-ticket-only-readonly-discovery.ps1 `
  -BeginTime "2026-06-11T15:00:00+08:00" `
  -EndTime "2026-06-11T18:00:00+08:00" `
  -CameraIndexCode "<local camera index code>"
```

With a floor index for parking-space status discovery:

```powershell
.\scripts\hikcentral-ticket-only-readonly-discovery.ps1 `
  -BeginTime "2026-06-11T15:00:00+08:00" `
  -EndTime "2026-06-11T18:00:00+08:00" `
  -FloorIndexCode "<local floor index code>"
```

## Ticket-Only Discovery

The runner tests these physical printed ticket numbers:

- `3518855073102`
- `3518855085105`

Discovery flow:

1. Calls `POST /artemis/api/vehicle/v1/parkingfee/calculate` with `{ "cardNum": "<printedTicketNumber>" }`.
2. If HikCentral returns `code = 0`, the printed ticket maps to `cardNum` and fee fields are reported.
3. If HikCentral returns `code = 128` or another not-found result, discovery does not retry random formats.
4. It calls read-only record endpoints using the configured `parkingLotIndexCode` and optional time window:
   - `POST /artemis/api/vehicle/v1/parkinglot/passageway/record`
   - `POST /artemis/api/vehicle/v1/parkingspace/record`
   - `POST /artemis/api/pms/v1/crossRecords/page`, only when `HIKCENTRAL_TEST_CAMERA_INDEX_CODE` is set
   - `POST /artemis/api/vehicle/v1/floor/parkingspace/status`, only when `HIKCENTRAL_TEST_FLOOR_INDEX_CODE` is set
5. It searches returned records for ticket/card/session-like fields:
   - `cardNum`
   - `ticketNo`
   - `ticketNumber`
   - `serialNo`
   - `parkingSerial`
   - `parkingSpaceSerial`
   - `crossRecordSyscode`
   - `guid`
   - `recordId`
   - barcode or QR payload fields

The output reports the ticket number, endpoint, HikCentral code/message, whether `cardNum` was accepted, fee fields, candidate identifier field, and conclusion. For each read-only discovery endpoint, it also prints:

- endpoint path
- HTTP status
- HikCentral code and message
- item count
- endpoint outcome: failed, empty, records with matching current ticket identifier, records with only non-ticket lookup values observed, or records with no matching ticket identifier
- `ticketMatched`, `matchedTicketValue`, and `matchedTicketField` scoped only to the current printed ticket being evaluated
- `observedOtherLookupValues` for control/discovered values found in records that do not match the current printed ticket
- first 3 sanitized record samples when records are returned

Samples are flattened to ticket/card/session-like fields and useful parking diagnostics such as plate, passageway, lane, and parking time fields. App secrets and signatures are not printed.

Printed-ticket matching is evaluated independently for each printed ticket. For example, a historical passageway value such as `personInfo.cardNum=3518835144105` is useful evidence, but it does not prove that printed ticket `3518855073102` or `3518855085105` matched. Such values are reported as `observedOtherLookupValues`, not as `ticketMatched=true`.

Parking record requests use the documented shape:

```json
{
  "pageIndex": 1,
  "pageSize": 50,
  "queryInfo": {
    "parkingLotIndexCode": "1",
    "beginTime": "2026-06-11T15:00:00+08:00",
    "endTime": "2026-06-11T18:00:00+08:00"
  }
}
```

Cross-record requests are skipped unless `HIKCENTRAL_TEST_CAMERA_INDEX_CODE` is configured. When configured, they use:

```json
{
  "cameraIndexCode": "<HIKCENTRAL_TEST_CAMERA_INDEX_CODE>",
  "startTime": "2026-06-11T15:00:00+08:00",
  "endTime": "2026-06-11T18:00:00+08:00",
  "pageNo": 1,
  "pageSize": 50
}
```

Parking-space status requests are skipped unless `HIKCENTRAL_TEST_FLOOR_INDEX_CODE` is configured. When configured, they use:

```json
{
  "floorIndexCode": "<HIKCENTRAL_TEST_FLOOR_INDEX_CODE>"
}
```

## Local Reference Search Notes

Searched local references:

- `D:\Hikvision\hikcentral_openapi_for_bruno.yaml`
- `D:\Hikvision\HikCentral Professional OpenAPI_Developer Guide_V3.1.0_20260130.pdf`

The YAML was text-searchable. The PDF was searchable for outline/title text in this environment; no full PDF text extraction utility was available locally, so request/response details were not copied from the PDF. The PDF outline showed an additional read-only-looking parking status endpoint that was missing from the YAML: `POST /artemis/api/vehicle/v1/floor/parkingspace/status`.

Search terms included `parkingfee`, `calculate`, `confirm`, `cardNum`, `ticket`, `ticketNo`, `ticketNumber`, `parkingSerial`, `parkingSpaceSerial`, `guid`, `recordId`, `vehicle is not exist`, `current vehicle`, `active vehicle`, `parked vehicle`, `parking record`, `in parking lot`, `temporary vehicle`, `temporary car`, `unpaid`, `bill`, `order`, `receivable`, `pms`, `crossRecords`, `cameraIndexCode`, `passageway`, `parkingspace`, `vehicle/v1`, and `pms/v1`.

Current interpretation:

- `parkingfee/calculate` is read-only, but live tests show it expects an active vehicle/session lookup key. Printed ticket numbers and historical `personInfo.cardNum` from passageway records returned `code = 128`.
- `parkinglot/passageway/record` and `parkingspace/record` are read-only history/record searches. They can expose useful fields such as nested `carInfo.*`, `personInfo.*`, `parkingLotInfo.*`, `passagewayInfo.*`, `laneInfo.*`, `guid`, timing fields, and card/serial/order-like fields, but a historical passageway card is not sufficient for `parkingfee/calculate`.
- Observed card numbers from historical records are diagnostics only. They must not be interpreted as printed-ticket matches unless they equal, contain, or clearly match the current printed ticket being tested.
- `pms/v1/crossRecords/page` is read-only PMS passage history and requires `cameraIndexCode`; it is skipped unless configured.
- `floor/parkingspace/status` appears to be the best next read-only candidate for current inside/active parking state because the PDF labels it as parking-space status, not historical passage. It requires a floor index before we can test it safely.
- The PDF outline also listed parking-guidance get endpoints. They were not added to live diagnostics because the body/schema was not extractable locally and the names suggest parking-guidance resource lookup rather than active fee/ticket lookup.
- No reference endpoint found so far explicitly says physical printed ticket, barcode, QR ticket, unpaid bill, receivable order, or active parked vehicle lookup by ticket.

Unresolved questions for HikCentral admin/vendor:

- Which API returns currently parked vehicles or unpaid active parking sessions for a parking lot?
- Which field from a physical printed ticket maps to the active session lookup key accepted by `parkingfee/calculate`?
- Is there a barcode/QR payload field on printed tickets that differs from visible printed ticket number?
- What is the correct floor index for TEST SITE, and does `floor/parkingspace/status` include active ticket/card/session identifiers?
- Is the local site configured for ticket-based fee calculation, or only plate/session-based calculation?

## Reference Endpoint Inventory

| Endpoint | Method | Description | Requires | Appears read-only? | Safe for live diagnostic runner? | Reason included or excluded |
| --- | --- | --- | --- | --- | --- | --- |
| `/artemis/api/common/v1/version` | POST | Product version lookup | none | yes | yes | Confirms OpenDataServer connectivity |
| `/artemis/api/vehicle/v1/parkinglot/list` | POST | Parking lot list | none | yes | yes | Finds parking lot index/name |
| `/artemis/api/vehicle/v1/parkingfee/calculate` | POST | Parking fee calculation | active vehicle lookup key | yes | yes | Calculation only; does not confirm payment |
| `/artemis/api/vehicle/v1/parkinglot/passageway/record` | POST | Entry/exit passageway record search | parking lot and time window | yes | yes | Read-only historical record search |
| `/artemis/api/vehicle/v1/parkingspace/record` | POST | Parking-space record search | parking lot and time window | yes | yes | Read-only record search |
| `/artemis/api/pms/v1/crossRecords/page` | POST | PMS vehicle passage log search | camera index and time window | yes | yes, when camera index is configured | Read-only; skipped without `HIKCENTRAL_TEST_CAMERA_INDEX_CODE` |
| `/artemis/api/vehicle/v1/floor/parkingspace/status` | POST | Parking-space status under a floor | floor index | yes, based on PDF title | yes, when floor index is configured | Best current active-state candidate; skipped without `HIKCENTRAL_TEST_FLOOR_INDEX_CODE` |
| `/artemis/api/vehicle/v1/parkingguidance/parkingspace/get` | POST | Parking-guidance parking-space lookup | unknown from locally extractable text | likely yes | no | PDF outline only; request schema not locally extractable, and endpoint does not clearly target active ticket/fee lookup |
| `/artemis/api/vehicle/v1/parkingguidance/singleparkingspace/get` | POST | Single parking-guidance parking-space lookup | unknown from locally extractable text | likely yes | no | PDF outline only; request schema not locally extractable, and endpoint does not clearly target active ticket/fee lookup |
| `/artemis/api/vehicle/v1/parkingguidance/parkingspace/batchrelate` | POST | Parking-guidance parking-space relation update | unknown from locally extractable text | no | no | Name indicates relation/update behavior, so it is treated as mutating |
| `/artemis/api/vehicle/v1/vehicle/blocklist/add` | POST | Add vehicle blocklist entry | blocklist vehicle data | no | no | Mutating vehicle access/security state |
| `/artemis/api/vehicle/v1/vehicle/blocklist/modify` | POST | Modify vehicle blocklist entry | blocklist vehicle data | no | no | Mutating vehicle access/security state |
| `/artemis/api/vehicle/v1/vehicle/blocklist/delete` | POST | Delete vehicle blocklist entry | blocklist identifiers | no | no | Mutating vehicle access/security state |
| `/artemis/api/vehicle/v1/parkingfee/confirm` | POST | Confirm parking fee payment | payment/exit confirmation details | no | no | Mutates payment/exit state and may allow exit |

## Common HikCentral Codes

- `0`: success
- `68`: signature authentication failed
- `128`: vehicle/resource not found
- `1`: unknown/internal request error

## Hard Safety Boundary

Do not call:

```text
POST /artemis/api/vehicle/v1/parkingfee/confirm
```

This validation path must not confirm payment, open gates, trigger barrier movement, create ExitPass payment finality, create `ExitAuthorization`, mutate ExitPass payment/gate state, or change database schema.
