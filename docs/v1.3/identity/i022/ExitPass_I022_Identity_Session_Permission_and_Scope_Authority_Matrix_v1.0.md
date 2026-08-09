# ExitPass I-022 Identity, Session, Permission, and Scope Authority Matrix v1.0

| Concern | Authoritative source | Management Platform | Operator Console | APT | Prohibited authority |
|---|---|---|---|---|---|
| Human identity | Central PMS and `identity.users` | Session user | Session reviewer | Session cashier | Body/header actor, Windows account |
| Credential | `identity.local_credentials` / protected TOTP record | Password; privileged TOTP policy | Password | Native one-shot password after device trust | Browser/SQLite credential cache |
| Session | `identity.human_sessions` | Secure host-only cookie | Secure host-only cookie | Opaque device-bound host token | localStorage/sessionStorage/SQLite session authority |
| Permission | Canonical role binding | Current server response and per-operation check | Current server response and per-operation check | `apt.access`, `cashier-shifts.operate`, `cash-custody.operate`, `terminal-cash.receive` | Client arrays, role names, payable-basis read as cash authority |
| Scope | Role-scoped Site/Site Group grant | Server evaluated | Server evaluated | Server evaluated plus device Site | Selector/header/null inference |
| GLOBAL | Explicit approved grant | Only when returned by Central PMS | No implicit grant | Prohibited for APT operations | Missing Site fields |
| Shift/custody | Canonical APT operational records | N/A | N/A | Own active records only | Human session or another cashier's records |
| Audit actor | Authenticated session user | Administrator | Reviewer | Cashier/supervisor | Client-authored identifier |

Service and device identity establish a channel boundary but never become human identity or human MFA.

