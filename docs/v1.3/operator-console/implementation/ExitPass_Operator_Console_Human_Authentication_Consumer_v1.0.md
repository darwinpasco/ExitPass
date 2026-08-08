# ExitPass Operator Console Human Authentication Consumer v1.0

## Purpose

H-008 replaces the Operator Console production fixture/header identity with the Central PMS I-020 human session. The browser consumes authentication and safe session presentation facts; Central PMS remains authoritative for credentials, session validity, actor identity, permissions, scope, device and shift constraints, and protected operations.

## I-020 integration

The Operator Console uses same-origin requests with `credentials: same-origin` and `cache: no-store`:

- `POST /v1/human-authentication/login`
- `GET /v1/human-authentication/session`
- `POST /v1/human-authentication/logout`

Login sends username, password, and the `OPERATOR_CONSOLE` audience. The v1.3 Operator Console flow has no TOTP input. A server response requiring MFA fails closed as unsupported assurance; it does not create a browser-owned MFA rule.

Central PMS issues the secure HttpOnly session cookie. The UI never reads, copies, refreshes, or persists the session secret. Startup and browser refresh always rediscover the current session through the session endpoint.

## CSRF and logout

The client retains the I-020 `X-CSRF-Token` response value only in the authentication client instance. Logout sends that value to the server, waits for revocation, clears bounded in-memory state, unmounts the protected workspace, and returns to sign in. The token is cleared on logout, authentication loss, and shell unmount.

## Actor and authorization boundary

The production API client does not emit fixture authority headers, browser permission arrays, actor IDs, or reviewer IDs. Statutory decision bodies contain workflow and resource facts only. Central PMS resolves the reviewer/operator from the authenticated session claim; compatibility actor input that conflicts with the claim is rejected by `OperatorConsoleIdentityContext`.

Current-session permissions and Site/Site Group counts are used only for presentation. Every backend request revalidates current permission and scope. Device, shift, Site, Site Group, resource, and row state remain server-owned access decisions. A `401` locks the workspace and is never replayed automatically. A `403` keeps the human session while presenting a safe denial.

## Security and persistence

The implementation does not store sessions, credentials, CSRF values, permissions, Site/Site Group grants, passwords, or TOTP material in localStorage, sessionStorage, IndexedDB, Cache Storage, URLs, or application telemetry. Password state is cleared after every login attempt. Existing secure evidence preview remains memory-bounded and continues to revoke temporary object URLs.

The former Vite-configured user and permission authority has been removed from the production HTTP client. Synthetic identity remains only in explicit unit and browser fixtures. Production Central PMS independently rejects fixture human-authority headers.

## Validation

Automated coverage includes:

- ordinary username/password login with no MFA prompt;
- invalid-credential and throttling-safe errors;
- current-session startup and refresh rediscovery;
- CSRF-bound server logout;
- expiry and revocation lockout, including revocation during evidence refresh;
- `403` behavior without false logout;
- session-fed permission and scope presentation;
- request scans for fixture headers, reviewer IDs, legacy apply routes, and browser storage;
- existing statutory approval/rejection and secure evidence-review regression;
- desktop and 390 x 844 keyboard/browser coverage.

Run:

```powershell
cd D:\wt\H008\src\Services\OperatorConsoleUi
npm.cmd ci
npm.cmd run proof:authentication
npm.cmd test
npm.cmd run build
npm.cmd run test:browser-smoke
```

Relevant Central PMS proof remains in `HumanAuthenticationApiIntegrationTests`, `HumanAuthenticationServiceTests`, `ProductionFixtureIdentityHeaderGuardMiddlewareTests`, statutory decision API tests, and statutory evidence-review API tests.

## Exclusions and readiness

H-008 does not add user, role, scope, MFA, or session administration. It does not add payable-application authority, evidence upload/download, or a legacy Operator Console apply route. Controlled UAT and production rollout require separately approved runtime configuration and operational validation.
