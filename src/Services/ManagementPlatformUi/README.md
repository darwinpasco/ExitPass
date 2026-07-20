# ExitPass Management Platform UI

Dedicated browser foundation for Management Platform administrative modules.

## Purpose

This project hosts the Management Platform browser shell. It is separate from WebPay and Operator Console. Future administrative modules register routes and navigation entries under `/management-platform` and call Central PMS browser-safe APIs only.

## Local prerequisites

- Node.js compatible with the existing repository Vite projects
- npm
- .NET SDK only when validating backend contracts alongside this UI

## Install

```powershell
npm ci
```

## Typecheck

```powershell
npm run typecheck
```

## Tests

```powershell
npm test
```

## Build

```powershell
npm run build
```

## Local startup

```powershell
npm run dev
```

The local Vite proxy forwards `/v1` to `VITE_MANAGEMENT_PLATFORM_API_PROXY_TARGET` or `http://localhost:8082` when unset.

## Authentication adapter posture

The shell uses a replaceable `ManagementPlatformAuthState` abstraction. The current development adapter exposes only safe user display data, permissions, and authorized Sites. Production integration must supply an authenticated Central PMS principal without exposing tokens, raw claims, cookies, API keys, or downstream configuration to the page.

## Site context posture

The Site selector uses the authorized Site list from the principal context. It does not accept free-form Site authority and does not store authoritative access rules in the browser.

## Environment configuration

Browser-safe configuration is limited to the app base path, relative Central PMS API base path, environment label, and feature flags. It must not contain POS Server URLs, POS Server API keys, database connections, or server-side secret references.

## Security boundaries

- Browser code calls only relative Central PMS routes under `/v1/management-platform`.
- The UI must not call downstream POS Server administration routes directly.
- The UI must not contain or render server-to-server API keys, authorization headers, or downstream base URLs.
- The UI must not persist credentials or statutory profile data in browser storage.
- Central PMS authorization remains authoritative; UI permission checks are presentation controls only.

## Future module registration

Future modules should add typed route descriptors, permission requirements, navigation metadata, and feature clients that use the shared Central PMS API client. Modules must keep authority in Central PMS/POS Server and avoid local authoritative persistence.

## Development manual-validation scenarios

The development build supports `mpScenario` URL query parameters only while `import.meta.env.DEV === true`. Production builds ignore these query parameters and do not expose a scenario selector.

Use these URLs with the local dev server:

- `http://127.0.0.1:5176/management-platform/?mpScenario=authenticated`
- `http://127.0.0.1:5176/management-platform/?mpScenario=unauthenticated`
- `http://127.0.0.1:5176/management-platform/?mpScenario=permission-denied`
- `http://127.0.0.1:5176/management-platform/?mpScenario=multi-site`
- `http://127.0.0.1:5176/management-platform/?mpScenario=no-sites`
- `http://127.0.0.1:5176/management-platform/?mpScenario=unavailable`
- `http://127.0.0.1:5176/management-platform/?mpScenario=not-found`

When `mpScenario` is absent or unknown, the development build falls back to the authenticated scenario. Scenarios are never stored in browser storage, cookies, IndexedDB, or server configuration.

## Sales Invoice Setup read-only module

Route:

```text
/management-platform/sales-invoice-profiles
```

Required permission:

```text
sales-invoice-profile.read
```

The module is read-only. It supports Site-scoped Sales Invoice Setup listing, setup detail, linked Registered Business detail, authoritative completeness validation, Sales Invoice readiness, and Issuance history visibility. It does not create or edit Registered Businesses, create or edit Sales Invoice Setups, activate, retire, create new versions, issue fiscal documents, print receipts, authorize exits, or operate gates.

The feature client uses only relative Central PMS routes under `/v1/management-platform`. Browser code must not call downstream administration routes directly and must not contain server-to-server credentials, downstream base URLs, or server-only headers.

### Sales Invoice Profile development scenarios

The development build supports `mpProfileScenario` URL query parameters only while `import.meta.env.DEV === true`. Production builds ignore these query parameters and do not expose a scenario selector.

Use these URLs with the local dev server:

- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=profiles`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=empty`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=incomplete`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=ready`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=no-effective-profile`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=expired`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=ambiguous`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=unsupported-version`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retired`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=unknown-readiness`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=usage`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=disabled`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=forbidden`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=unavailable`

Scenarios are never stored in browser storage, cookies, IndexedDB, or server configuration. They use controlled in-memory browser adapters and contain only obvious development data.

## Sales Invoice Setup Manage-only workflows

Required permission:

```text
sales-invoice-profile.manage
```

Manage-authorized users can create and update Registered Businesses and create or edit Sales Invoice Setups only while the setup is Draft. Read-only users continue to see the list, detail, validation, Sales Invoice readiness, and Issuance history surfaces without active mutation controls.

Registered Business forms send only:

- `registeredBusinessName`
- `registeredBusinessAddress`
- `tin`
- `taxpayerRegistrationPosture`

Draft Sales Invoice Setup forms send only governed setup fields, with Site and Site POS Server derived from the current authorized Site context. Template and presentation versions are controlled to:

- `digital-sales-invoice-json-v1`
- `digital-sales-invoice-presentation-json-v1`

The browser does not send actor-reference fields. Central PMS derives the actor from the authenticated Management Platform principal. The browser does not expose approval, retirement, delete, create-new-version, terminal ID, downstream credential, or POS Server administration fields.

Mutation requests are sent once and are not automatically retried. If a create or update times out or loses connectivity after send, the UI shows a Result uncertain posture and tells the user to refresh and verify authoritative state before retrying.

Unsaved form state remains only in component memory. Site switching while a form has unsaved changes requires confirmation before the form is discarded. No Registered Business, Sales Invoice Setup, statutory data, validation result, credential, or Site authorization data is stored in localStorage, sessionStorage, IndexedDB, or cookies.

## Sales Invoice Setup activation and retirement

Required permission:

```text
sales-invoice-profile.approve
```

Approve-authorized users can activate a complete Draft Sales Invoice Setup and retire an Active Sales Invoice Setup. The UI uses the customer-facing actions **Activate Sales Invoice Setup** and **Retire Sales Invoice Setup** while the internal backend route remains the approved Central PMS lifecycle API.

Activation requires the latest displayed authoritative validation result to be Complete for the selected setup. The server remains authoritative for the final activation decision. The browser never sends actor fields such as `approvedByRef` or `retiredByRef`; Central PMS derives actor identity from the authenticated principal.

Activation and retirement requests are sent once and are not automatically retried. Timeout or connection-loss outcomes show Result uncertain guidance with a safe support reference and instruct the user to refresh and verify authoritative status before retrying.

Retirement is not deletion. Historical Sales Invoices and their recorded setup details remain unchanged, and Issuance history remains visible after retirement.

### Activate/retire development scenarios

Use these URLs with the local dev server. They are active only while `import.meta.env.DEV === true` and are ignored by production builds.

- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-user`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=manage-without-approve`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-draft-complete`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-draft-incomplete`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-success`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-conflict`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-timeout`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retire-approved`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retire-success`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retire-conflict`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retire-timeout`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retired-history`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approve-forbidden`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retire-forbidden`

### Manage workflow development scenarios

Use these URLs with the local dev server. They are active only while `import.meta.env.DEV === true` and are ignored by production builds.

- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=manage`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=read-only`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=fiscal-identity-create-success`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=fiscal-identity-create-conflict`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=fiscal-identity-update-success`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=fiscal-identity-update-immutable`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=profile-create-success`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=profile-create-conflict`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=profile-create-timeout`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=draft-edit-success`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=draft-edit-conflict`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=approved-read-only`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=retired-read-only`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=forbidden-manage`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=disabled-manage`
- `http://127.0.0.1:5176/management-platform/sales-invoice-profiles?mpScenario=authenticated&mpProfileScenario=unavailable-manage`

## Playwright E2E validation

Install the Chromium browser managed by Playwright once per machine or agent image:

```powershell
npx playwright install chromium
```

Run the automated browser matrix:

```powershell
npm run test:e2e
```

Interactive variants:

```powershell
npm run test:e2e:headed
npm run test:e2e:debug
```

The Playwright suite starts and stops a controlled Vite server automatically on port `5177` by default. Override it with `MANAGEMENT_PLATFORM_E2E_PORT` when needed. It also serves a production bundle on port `5178` by default to prove production builds ignore `mpScenario` and `mpProfileScenario`.

Generated Playwright artifacts are written to:

- `test-results`
- `playwright-report`

These directories are generated evidence only and are ignored by Git.

## Complete lifecycle UI proof

Use this command for the complete Management Platform Sales Invoice Setup lifecycle validation:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Invoke-ManagementPlatformSalesInvoiceProfileApproveRetireUiE2eProof.ps1
```

The proof runs install, typecheck, unit tests, production build, Playwright E2E, foundation proof, read UI proof, manage UI proof, approve/retire UI proof, terminology scans, and static browser-boundary scans.

## Remaining manual smoke

After the automated Playwright proof passes, the remaining manual browser validation is a short visual smoke check:

1. Open an activation-eligible Draft Sales Invoice Setup at `1366x768`.
2. Open the Activate confirmation and confirm user-friendly wording with no overlap.
3. Open an Active Sales Invoice Setup.
4. Open the Retire confirmation and confirm historical-preservation wording with no overlap.
5. Confirm no `Mutation accepted`, `Fiscal Identity`, or `Header Profile` browser labels remain on the touched surfaces.
