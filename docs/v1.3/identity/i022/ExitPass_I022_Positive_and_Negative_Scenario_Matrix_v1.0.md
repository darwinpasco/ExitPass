# ExitPass I-022 Positive and Negative Scenario Matrix v1.0

| Scenario | Expected | Proof surface |
|---|---|---|
| MP ordinary login/current session/logout | Authenticated session; server permissions/scopes; CSRF; logout invalidates | Visible I-022 Production browser run |
| MP privileged login | Password plus TOTP; replay rejected | I-020 DB/unit regressions |
| MP administration and revocation | Session-derived actor; User Administration reads real API; freshness and ceiling enforced | Visible I-022 read plus I-021 Production/boundary tests |
| OC login/current session/logout | Password session; no mandatory MFA | Visible I-022 Production browser run and H-008 tests |
| OC reviewer attribution | Session actor; approval/rejection only; evidence permission required | H-008/I-017 regressions |
| APT login | Trusted service/device plus password, Site and APT audience | I-022 Production API and actual Windows host proof |
| APT own shift/custody | Operation-specific permission and ownership | J-008 host tests |
| APT pre-cash | Current session/device/scope/shift/custody plus `terminal-cash.receive` and readiness | J-008 and Central PMS readiness regressions |
| Wrong password/unknown user | Same safe rejection | I-020 tests |
| Unauthenticated/expired/revoked | No authority | API and consumer tests |
| Cross-audience session | Rejected or anti-enumerated | I-022 Production API tests |
| Wrong device/cashier/Site/Site Group | Denied | I-022 API, J-008, H-008 tests |
| Permission/scope revoked while open | Next read/operation sees current canonical state | I-022 live grant convergence test |
| Targeted revoke/revoke-all | Selected/all durable sessions cease authorizing; logout-all invalidates MP, OC, and APT | I-021 and DB-backed I-022 tests |
| Central PMS outage | No local authority or password replay | Consumer tests and APT host tests |
| Fixture/development headers in Production | `400 FIXTURE_IDENTITY_HEADER_PROHIBITED` | I-022 Production hosted request and API test |
| Browser/SQLite restored values | Presentation-only; cannot create authority | Consumer source scans and tests |
| Missing GLOBAL | No global access | DB-backed I-022 assertions |
| Payable-basis read used as cash permission | Denied | I-021A/J-008 regressions |
