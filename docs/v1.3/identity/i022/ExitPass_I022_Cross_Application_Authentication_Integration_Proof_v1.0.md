# ExitPass I-022 Cross-Application Authentication Integration Proof v1.0

## Scope

I-022 closes the human-authentication integration proof across Central PMS, Management Platform, Operator Console, APT, and the canonical database. It adds no product capability. Central PMS remains the only human credential, session, permission, and scope authority.

## Added proof

- `CrossApplicationHumanAuthenticationIntegrationTests` hosts the real ASP.NET Core pipeline in Production composition against a fresh canonical PostgreSQL database.
- It proves web audience isolation, APT device/audience binding, canonical operation-specific APT permissions, no payable-basis permission conflation, live role/scope convergence, explicit no-GLOBAL posture, logout-all, and Production fixture-header rejection.
- `Invoke-I022CrossApplicationAuthenticationProof.ps1` aggregates Central PMS regressions, consumer builds/tests, source privacy scans, the actual APT WebView2 host check, and cleanup of temporary external-repository copies.
- `ExitPass.I022.ProofSeed` creates only synthetic hosted-browser users in an `exitpass_i022_` disposable database and never prints credentials or verifiers.
- `I022HostedBrowserProof.mjs` drives visible Chromium against the real Management Platform and Operator Console consumers and the Production Central PMS API. It verifies login, current-session scope, User Administration readback, cross-audience rejection, and logout.

## Executed validation

The disposable proof used PostgreSQL 16, a fresh canonical full-object rebuild, Production Central PMS over runtime-only TLS, loopback Vite proxies, and synthetic identity records.

| Gate | Result |
|---|---|
| Central PMS Release API build | Passed, 0 warnings and 0 errors |
| Focused authentication/administration/readiness unit suite | 142 passed |
| Combined DB-backed API/repository suite | 26 passed, including 3 I-022 cross-application tests |
| Management Platform authentication/administration consumer | 70 passed; Production build passed |
| Operator Console authentication consumer | 22 passed; Production build passed |
| APT desktop human-session/host boundary | 47 passed |
| APT presentation boundary | 18 passed; Production build and actual packaged WebView2 smoke passed |
| Statutory/payable-basis/fiscal/receipt/print/reconciliation slice | 536 passed |
| Visible hosted browser proof | Management Platform and Operator Console passed against Production Central PMS |
| Production fixture-header request | `400 FIXTURE_IDENTITY_HEADER_PROHIBITED` |
| Broad Central PMS unit command | I-022 and pristine `origin/dev` both 48 failed, 1,640 passed, total 1,688 |
| Disposable cleanup | Passed; no I-022 containers, databases, listeners, processes, temporary roots, credentials, logs, or test/build output remain |

The 48 broad failures are the same pre-existing fiscal Controlled-UAT and PayMongo diagnostic family. A shared metrics test appeared only in one later parallel TRX rerun; it passed in isolation on both I-022 and pristine `origin/dev`, and the exact broad commands had identical totals.

## Security result

The web clients retain only in-memory CSRF state and use server cookies. The APT web layer retains no password or session authority; credentials cross the native one-shot prompt. No consumer emits human identity, permission, role, Site, or Site Group authority headers. Production log scans found no runtime password, certificate password, TOTP protection key, connection string, session authorization header, or permission authority header. Canonical session rows contained 64-byte hashes, audit/security/attempt rows contained no runtime credential marker, and no raw session or credential material was returned to a consumer.

## Business boundary result

Operator Console review does not regain payable-application authority. APT `terminal-cash.payable-basis.read` remains read-only and cannot satisfy application entry, shift, custody, cash acceptance, or `CASH_RECEIVED`. Statutory approval/application, fiscal readiness, receipts, printing, and reconciliation remain separate authorities.

## Controlled UAT decision

The human-authentication workstream is technically ready for Controlled UAT reassessment after this proof merges. I-022 does not authorize Controlled UAT or production rollout; the deferred DR-05/08/09/10/11 decisions and other product workstreams retain their own gates.
