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
- endpoint outcome: failed, empty, records with matching ticket identifier, or records with no matching ticket identifier
- first 3 sanitized record samples when records are returned

Samples are flattened to ticket/card/session-like fields and useful parking diagnostics such as plate, passageway, lane, and parking time fields. App secrets and signatures are not printed.

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
