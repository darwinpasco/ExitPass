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
