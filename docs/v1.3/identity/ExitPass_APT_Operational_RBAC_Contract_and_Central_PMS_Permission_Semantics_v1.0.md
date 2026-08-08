# ExitPass APT Operational RBAC Contract and Central PMS Permission Semantics

## 1. Decision

J-008 validation exposed an incorrect authorization mapping: `terminal-cash.payable-basis.read` was being treated as general APT human authority. Central PMS defines that permission only for the side-effect-free payable-basis resolve/revalidate facade. It cannot authorize application entry, shift or custody operation, cash acceptance, fiscal mutation, or `CASH_RECEIVED`.

The v1.3 MVP APT human permission catalog is now frozen:

| Permission | Purpose | Does not authorize |
|---|---|---|
| `apt.access` | Enter/use APT with a current APT session, trusted device binding, and Site/Site Group scope. | Shift, custody, cash acceptance, or `CASH_RECEIVED`. |
| `cashier-shifts.operate` | Open/resume/close the authenticated cashier's own shift. | Another cashier's shift, custody, cash receipt, or handover. |
| `cash-custody.operate` | Open/resume/close the authenticated cashier's own custody. | Another cashier's custody, cash receipt, or handover. |
| `terminal-cash.receive` | Human-permission dimension for cash acceptance and immediate pre-`CASH_RECEIVED` authorization. | It is not sufficient without every current session, scope, shift, custody, payable-basis, POS/fiscal, and cash-readiness check. |
| `terminal-cash.payable-basis.read` | Read-only payable-basis resolution/revalidation. | Every operational permission above, fiscal/payment mutation, handover, and `CASH_RECEIVED`. |

The authoritative application literals live in `AptHumanPermissionCatalog`. Existing named policy `TerminalCashPayableBasisRead` remains mapped only to the read-only permission. No new operation endpoint policy is registered by I-021A because Central PMS does not yet expose the corresponding APT shift/custody/cash mutation endpoints in this slice.

## 2. Role and scope contract

`SITE_OPERATOR` is the intended baseline cashier role and will receive the four operational permissions through the canonical I-021B database change. I-021A does not alter database source or seed data.

`OPERATIONS_SUPERVISOR` receives no cashier permission solely because it is a supervisor role. A supervisor who performs cashier duties must separately hold an effective `SITE_OPERATOR` assignment with applicable scope. Role names never become runtime authority.

Each operational permission requires all of:

- current online Central PMS human session and active account;
- APT audience and trusted device/service-identity binding;
- current canonical role-permission binding and effectivity;
- current Site or Site Group scope covering the terminal Site;
- operation-specific resource binding, including own shift/custody where applicable.

GLOBAL APT operation is prohibited. Missing Site fields never imply global access. Frontend, SQLite, request headers, and cached permission arrays are not authorization authority.

## 3. CASH_RECEIVED boundary

Immediately before the irreversible `CASH_RECEIVED` boundary, Central PMS and the consuming APT runtime must re-evaluate `terminal-cash.receive` together with current human session, account state, device binding, Site/Site Group scope, own shift, own custody, authoritative payable-basis revalidation, POS/fiscal readiness, and every existing terminal-cash readiness dimension.

`terminal-cash.payable-basis.read` can return readiness facts but cannot satisfy the human cash-receipt dimension. `apt.access`, shift operation, custody operation, upload completion, or any other individual prerequisite likewise cannot authorize `CASH_RECEIVED` alone.

## 4. Deferred handover

No supervisor-handover permission is defined in I-021A. DR-08/DR-09 remain unresolved, so `OPERATIONS_SUPERVISOR`, `cash-custody.operate`, `terminal-cash.receive`, or any role-name check cannot authorize custody handover/takeover. A later approved contract must define the permission, actor/freshness requirements, reconciliation evidence, and audit semantics before implementation.

## 5. Runtime compatibility

I-020 resolves live permissions from `identity.user_roles -> identity.role_permissions -> identity.permissions` and returns them through the current human-session contract. It does not restrict permission codes to a hard-coded application allowlist and therefore can surface the four new literals after I-021B creates their canonical rows and bindings. Site/Site Group grants remain assignment-scoped and effective-dated; session device and audience binding remain independent checks.

## 6. Follow-up boundaries

I-021B must add the four permission catalog rows and bind them to `SITE_OPERATOR`, without adding an automatic `OPERATIONS_SUPERVISOR` binding or any GLOBAL grant. It must preserve `terminal-cash.payable-basis.read` as a distinct read-only permission.

J-008 must replace every broad operational use of `terminal-cash.payable-basis.read` with the operation-specific permission at the corresponding boundary. It must re-evaluate `terminal-cash.receive` immediately before `CASH_RECEIVED`, preserve own-shift/own-custody checks, consume only Central PMS effective permissions, and leave supervisor handover disabled.

## 7. Validation

Focused tests verify the exact catalog, separation from payable-basis read, absence of role-name/GLOBAL/handover shortcuts, the unchanged read-only named policy, and live I-020 readback of canonical role-permission bindings. Existing payable-basis API tests continue proving no payment, fiscal, decision, or `CASH_RECEIVED` side effect.

Validation completed on PostgreSQL 16 with a uniquely named disposable database and container:

- Release Central PMS API build: passed with zero warnings and zero errors on the final no-change build;
- focused RBAC catalog: 44 passed;
- focused I-020 human authentication/session unit tests: 31 passed;
- I-020 API/repository integration tests, including live APT permission readback: 10 passed;
- payable-basis machine contract: 3 passed;
- payable-basis API: 11 passed;
- APT ordinance service: 10 passed;
- combined APT ordinance/payable-basis integration: 46 passed;
- broad Central PMS unit suite: 1,640 passed and 48 unrelated fiscal Controlled-UAT/PayMongo tests failed; pristine `origin/dev` produced the same 48 failure identities and causes with 1,638 passed, while I-021A adds two passing catalog tests;
- `git diff --check`, JSON parsing, source-literal, role-name, GLOBAL/handover, secret, and changed-file scans: passed.

The disposable database, PostgreSQL container, copied canonical SQL, and loopback listener were removed after validation.

Controlled UAT and production rollout remain unauthorized.
