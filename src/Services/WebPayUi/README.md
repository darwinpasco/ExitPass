# ExitPass WebPay UI

Vite/React UI for testing WebPay payment intent creation from a desktop browser or a real phone.

## Local development

```powershell
cd D:\SourceCodes\ExitPass
docker compose -f .\infra\docker\docker-compose.yml up -d

cd .\src\Services\WebPayUi
$env:VITE_WEBPAY_API_PROXY_TARGET = "http://localhost:8082"
$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = "<site-group-guid>"
$env:VITE_WEBPAY_DEFAULT_SITE_ID = "<site-guid>"
$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "HIKCENTRAL"
npm run dev
```

The UI runs on `http://localhost:5174`. During Vite development, the UI should call `POST /v1/webpay/payment-intents` as a same-origin request. Vite proxies `/v1` to `VITE_WEBPAY_API_PROXY_TARGET`, which defaults to `http://localhost:8082` to match the local Payment Orchestrator compose port.

`VITE_WEBPAY_API_BASE_URL` is still supported for cases where the browser should call an explicit API origin. Leave it unset for ngrok phone testing so the phone only talks to the ngrok HTTPS origin and the laptop-side Vite proxy talks to the backend.

For local manual ticket or plate testing, set the default site context variables above. The UI sends these values in the payment intent request but does not show them as normal parker input fields. A scanned QR or URL payload may provide explicit `siteGroupId`, `siteId`, or `vendorSystemId`; otherwise the `VITE_WEBPAY_DEFAULT_*` values are used.

## Phone testing through ngrok

1. Start the backend containers from the repo root:

```powershell
docker compose -f .\infra\docker\docker-compose.yml up -d
```

2. Start the WebPay UI on all interfaces:

```powershell
cd D:\SourceCodes\ExitPass\src\Services\WebPayUi
$env:VITE_WEBPAY_API_PROXY_TARGET = "http://localhost:8082"
$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = "<site-group-guid>"
$env:VITE_WEBPAY_DEFAULT_SITE_ID = "<site-guid>"
$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "HIKCENTRAL"
npm run dev
```

The `dev` script runs `vite --host 0.0.0.0` so ngrok can reach the local Vite server.

3. Start ngrok for the Vite port:

```powershell
ngrok http 5174
```

4. Open the ngrok HTTPS URL on the phone, for example:

```text
https://<ngrok-host>.ngrok-free.dev
```

5. Allow camera permissions when the browser prompts.

6. Test the manual `ticketReference` entry first. Confirm the browser submits `POST /v1/webpay/payment-intents` successfully through the Vite proxy before using the scanner.

7. Test QR scan after the manual API path works.

## Environment variables

- `VITE_WEBPAY_API_PROXY_TARGET`: Vite dev proxy target for `/v1`. Defaults to `http://localhost:8082`.
- `VITE_WEBPAY_API_BASE_URL`: Optional browser-side API base URL. Prefer leaving this unset for ngrok phone testing so requests stay same-origin.
- `VITE_WEBPAY_DEFAULT_SITE_GROUP_ID`: Optional default site group GUID sent with local WebPay payment intent requests.
- `VITE_WEBPAY_DEFAULT_SITE_ID`: Optional default site GUID sent with local WebPay payment intent requests.
- `VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID`: Required for local manual testing unless the scanned QR/URL provides `vendorSystemId`. For HikCentral-backed local tests, use `HIKCENTRAL`.

## Troubleshooting

- ngrok requires an authtoken before it will create tunnels. Run `ngrok config add-authtoken <token>` if ngrok reports an authentication error.
- Vite blocks unknown hosts unless they are allowed. This project allows `.ngrok-free.app` and `.ngrok-free.dev` in `server.allowedHosts`.
- Camera access on phones requires HTTPS. Use the ngrok HTTPS URL, not plain HTTP.
- A phone cannot use the laptop's `localhost`. Keep `VITE_WEBPAY_API_BASE_URL` unset for phone testing and let Vite proxy `/v1` from the laptop.
- The backend must be reachable from the laptop running Vite at `VITE_WEBPAY_API_PROXY_TARGET`. Check that Payment Orchestrator is listening on the configured port.
- If submit fails before calling the API with a WebPay vendor configuration message, set `VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID` or scan a QR/URL payload that includes `vendorSystemId`.
- CORS should not be needed for ngrok testing when the UI calls same-origin `/v1` and Vite proxies the request.

## Asset reference

Static assets live under `public/assets`.

```text
public/assets/
  logo/
  payment-methods/
  icons/
  illustrations/
  brand/
```

For production, replace raster payment logos with official brand-owner vector/source files when available.
