# Site-specific POS Server routing

## Decision

ExitPass deploys one POS Server appliance and database per Site. Central PMS must route fiscal create, read, presentation, and void calls using the immutable `site_pos_server_id` and `site_pos_server_ref` stored with the fiscal issuance reference. A global POS Server URL is not a supported runtime route.

Historical v1.3 controlled-UAT records that describe `PosServerBaseUrl` remain evidence of those runs. Their single-endpoint configuration guidance is superseded by this document.

## Configuration

Endpoint locations are non-secret deployment configuration. API keys are mounted files and are never stored in Central PMS configuration or PostgreSQL.

```powershell
$env:FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall = 'true'
$env:FiscalIssuance__PosServerIntegration__TimeoutSeconds = '10'

$env:FiscalIssuance__PosServerIntegration__Endpoints__0__SitePosServerId = '<SITE-A-POS-SERVER-ID>'
$env:FiscalIssuance__PosServerIntegration__Endpoints__0__SitePosServerRef = '<SITE-A-POS-SERVER-REF>'
$env:FiscalIssuance__PosServerIntegration__Endpoints__0__BaseUrl = 'https://site-a-pos.internal'
$env:FiscalIssuance__PosServerIntegration__Endpoints__0__ApiKeyFile = 'C:\run\secrets\site-a-pos-api-key'
$env:FiscalIssuance__PosServerIntegration__Endpoints__0__Environment = 'Production'
$env:FiscalIssuance__PosServerIntegration__Endpoints__0__Enabled = 'true'

$env:FiscalIssuance__PosServerIntegration__Endpoints__1__SitePosServerId = '<SITE-B-POS-SERVER-ID>'
$env:FiscalIssuance__PosServerIntegration__Endpoints__1__SitePosServerRef = '<SITE-B-POS-SERVER-REF>'
$env:FiscalIssuance__PosServerIntegration__Endpoints__1__BaseUrl = 'https://site-b-pos.internal'
$env:FiscalIssuance__PosServerIntegration__Endpoints__1__ApiKeyFile = 'C:\run\secrets\site-b-pos-api-key'
$env:FiscalIssuance__PosServerIntegration__Endpoints__1__Environment = 'Production'
$env:FiscalIssuance__PosServerIntegration__Endpoints__1__Enabled = 'true'
```

Each endpoint must have a unique ID and reference, match the Central PMS runtime environment, and use an origin-only URL. Production endpoints require HTTPS. HTTP is accepted only by local development and test environments.

## Failure behavior

When fiscal integration is enabled, invalid endpoint configuration fails application startup. At request time, Central PMS requires an exact ID and reference match and reads the API key from the configured mounted file. Missing, ambiguous, disabled, environment-mismatched, insecure, or unavailable-secret bindings fail before a network request.

Retries and readback use the routing identity persisted with the original fiscal reference. They cannot switch Site or POS Server. Central PMS sends only the operation permission required by the request and does not log or persist the API key.
