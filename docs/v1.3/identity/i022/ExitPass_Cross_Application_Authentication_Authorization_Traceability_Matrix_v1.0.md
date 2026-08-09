# ExitPass Cross-Application Authentication and Authorization Traceability Matrix v1.0

## Authority chain

| Requirement | Canonical implementation | Consumer evidence | I-022 proof |
|---|---|---|---|
| Human identity and credentials | `identity.users`, `identity.local_credentials`; `HumanAuthenticationService` | All staff clients submit credentials only to Central PMS | DB-backed Production login and consumer tests |
| Human session | `identity.human_sessions`; opaque secret hash; `HumanSessionAuthenticationHandler` | Web host-only cookie; APT native-host token | Audience isolation, logout-all, expiry/revocation, restart tests |
| Effective permission | `user_roles -> role_permissions -> permissions` | Current-session DTO is presentation input only | Live permission removal is visible without relogin |
| Site and Site Group scope | `user_role_scope_grants` attached to `user_roles` | Client selector/context never creates scope | Live Site revocation, retained Site Group, wrong-scope tests |
| GLOBAL | Explicit `GLOBAL` grant only | No client null/default inference | I-022 fixtures contain no GLOBAL grant |
| Management Platform TOTP | Privileged-role state plus active protected TOTP authenticator | TOTP challenge only when Central PMS requires it | I-020 privileged login/replay regressions |
| Operator reviewer actor | Session `user_id` and internal human-session ID | Browser sends no actor or permission headers | H-008 tests and Production actor/audit integration |
| APT cashier authority | APT audience, device service identity, Site scope and four operation-specific permissions | Native host owns one-shot credential prompt; UI owns no token | DB-backed APT API test and actual WebView2 host proof |
| Revocation | Durable session state and current account/credential state | Clients clear presentation state and never retry passwords | Targeted revoke, logout-all, expiry and consumer regressions |
| Fixture authority | Production guard rejects identity/permission headers | Production consumers do not emit them | Production `400 FIXTURE_IDENTITY_HEADER_PROHIBITED` |

## Preserved business boundaries

Authentication proves a human. Authorization grants a capability. Scope limits where it applies. None of these replaces statutory approval/application, payable-basis revalidation, fiscal readiness, owned shift, owned custody, receipt, printing, or reconciliation authority.

`terminal-cash.payable-basis.read` remains read-only. APT cash finality additionally requires `terminal-cash.receive`, a current device-bound cashier session, current scope, owned shift/custody, payable-basis revalidation, and all terminal/POS/fiscal readiness dimensions.

