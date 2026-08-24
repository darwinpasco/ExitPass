# ExitPass Operator Console Server-Owned Operating Context v1.0

## Purpose and provenance

This focused correction addresses the blocking result from `OPCON-MVP-ACCEPT-20260824T064400Z-cpms`. H-006 authentication succeeded in that run, but the canonical statutory-review queue correctly returned `403` because the authenticated session had no trusted device binding or active operator shift. The failed acceptance evidence remains authoritative as failure provenance and is not rewritten or deleted.

This implementation record uses only current `docs/v1.3/` authority. It records a targeted correction and targeted runtime proof; it does not establish whole-console acceptance.

## Baseline and recovery

- Baseline and current `origin/dev` when work resumed: `1fb614bfd12f64db68728259d6b37555d222658e`.
- Branch: `fix/operator-console-server-owned-operating-context`.
- Worktree: `D:\wt\OperatorConsoleServerOwnedOperatingContext`.
- Merge base with `origin/dev`: `1fb614bfd12f64db68728259d6b37555d222658e`; left/right count `0/0` at recovery.
- Final remote observation after a fresh fetch: `origin/dev` advanced to `a224505ab0fafda355a3327ceb18876987f99c87`; the merge base remains `1fb614bfd12f64db68728259d6b37555d222658e` and left/right count is `0/4`. The four intervening commits are two merge commits and two POS fiscal-profile corrections outside this change set. This uncommitted correction was not merged or rebased because submission and merge are outside this task.
- Baseline merge parents: `13feb97174f47d27eb355fadd29e037b77ac1cce` and `84c192e4ef5b03d781554331f135368845ae39c7`.
- The existing worktree and branch were clean and had no staged, unstaged, untracked, or committed partial implementation. No worktree was deleted, reset, overwritten, or recreated.
- The selected baseline contains the canonical Central PMS statutory-review integration, Management Platform head-office statutory-benefit review, canonical-review runtime hardening, PHP-only Operator Console corrections, and current H-006 authentication/session implementation.

## Architecture decision

Central PMS remains the exclusive Operator Console authority. The correction reuses the locked v1.2 canonical records for H-006 sessions, users, roles, permissions, Site/Site Group grants, trusted Operator Console devices, device assignment history, and operator shifts.

The existing `browser_key_thumbprint` is used as the server-verifiable device proof. A provisioned opaque proof can be exchanged once, after same-origin validation, for a random 48-byte server-generated credential. Central PMS atomically replaces the canonical thumbprint and emits the replacement only as the `__Host-ExitPass-Operator-Device` cookie with `Secure`, `HttpOnly`, `SameSite=Strict`, host-only, and root-path controls. JavaScript receives no credential, canonical device ID, shift ID, Site, Site Group, permission, role, or authorization version.

After H-006 password authentication, Central PMS resolves exactly one active trusted device and exactly one compatible active shift for the authenticated user. It verifies the canonical Site-to-Site-Group relationship, current device assignment, user grants, session status, idle/absolute expiry, authorization epoch, and credential version. Ambiguous device proof, duplicate active assignments, and multiple compatible active shifts fail closed.

A narrowly scoped additive v1.3 table, `operator_console.operator_session_contexts`, links one H-006 session to its validated canonical user, device, shift, Site, and Site Group. It stores authorization and credential version snapshots plus lifecycle/audit timestamps and a correlation identifier. It stores no browser proof, session token, password, TOTP material, role, permission, reviewer input, statutory evidence, or statutory ID. The locked v1.2 DDL is unchanged.

Every protected `/v1/operator-console/` or `/v1/ops/operator-console/` H-006 request revalidates this context from canonical data before the existing RBAC and `OperatorConsoleAccessEvaluationService`/readiness path executes. Server-only claims are refreshed from validated context. Browser-authored Operator Console identity, device, shift, Site, or Site Group headers are rejected for H-006 principals and cannot restore authority. The active UI emits none of those fields or legacy authority environment values. The readiness preflight ignores any body-authored versions for H-006 and resolves its context from the validated principal; the server environment also overrides any browser development-fallback assertion.

Management Platform, unrelated H-006 audiences, APT sessions, service identities, and internal mTLS callers do not acquire an Operator Console device/shift prerequisite. A GLOBAL Management Platform reviewer continues to use the head-office statutory-benefit route without an Operator Console context.

## Controlled outcomes and telemetry

The correction returns non-leaking classifications for required, invalid, revoked, and expired device proof; unauthorized device Site; missing, conflicting, closed/expired, incompatible, and out-of-scope shifts; stale authorization; and expired/revoked sessions.

Structured Central PMS logs record binding success, credential rotation, controlled binding failure, readiness denial, and correlation IDs without raw proofs or credentials. The server-owned context row durably records successful binding, last validation, invalidation classification, lifecycle timestamps, row version, and correlation ID.

## Validation status

Completed focused validation on the uncommitted working tree includes:

- Final Central PMS API Release build: succeeded with 170 existing repository warnings and zero errors.
- H-006, operating-context, access-evaluation, and readiness unit tests: 89 passed, 0 failed, 0 skipped.
- Statutory-review queue/detail/evidence/decision service tests: 61 passed, 0 failed, 0 skipped.
- Canonical migration-order, H-006 persistence, and hosted cross-application integration: 20 passed, 0 failed, 0 skipped.
- Canonical statutory queue/detail/evidence/decision, RBAC, concurrency, and repository integration: 57 passed, 0 failed, 0 skipped.
- Final targeted hosted correction proof: passed, including actual H-006 password login, opaque device-proof exchange, HttpOnly cookie, persisted device/shift context, queue/detail/evidence access, WebPay-originated approval, APT-originated rejection with mandatory reason, stored server-owned reviewer/device/shift/timestamp attribution, browser non-disclosure, forged-header denial, forged readiness-body values ignored in favor of canonical session context, live shift closure denial, session rebind, live device revocation denial, and GLOBAL Management Platform queue access without Operator Console device/shift.
- Existing access-evaluation/readiness integration: 12 production cases passed. Six legacy local-fallback assertions are obsolete against the isolated database with canonical readiness tables and are reported separately; they are not counted as successful canonical coverage and the production readiness service was not weakened to satisfy them.
- Operator Console typecheck and production build: passed.
- Complete active Vitest suite: 123 passed in 9 files with one worker under the task memory controls; 28 explicitly skipped obsolete assertions are reported separately. An earlier multi-file run produced one unrelated policy-import timing flake that passed both focused and one-worker reruns.
- Complete active Chromium suite: 12 passed with one headless Chromium worker; 16 explicitly skipped obsolete draft-path cases are reported separately.
- Dependency audit: zero known vulnerabilities after the existing Vite/PostCSS semver range was lock-resolved from `nanoid` 3.3.17 to 3.3.18; declared dependencies are unchanged.
- The shared PostgreSQL test harness now confirms a real authenticated loopback `SELECT 1` after container-local `pg_isready`, eliminating an observed Docker port-mapping startup race without altering product authorization behavior.

The external evidence bundle records the remaining static validation, resource observations, cleanup, and exact commands/results.

## Scope boundary and acceptance posture

This correction adds no device-administration UI, shift scheduling, attendance/payroll, continuity workflow, incident/BCP linkage, manual release, fiscal mutation, export/report expansion, direct client integration, provider integration, or multi-currency behavior. PHP remains the only supported currency.

Review posture: `SELF-REVIEWED`.

Independent review: `NOT_PERFORMED`.

The correction is implemented and receives targeted runtime proof only. Whole-console integrated runtime and visual acceptance remains pending. After merge, the next task is **Operator Console MVP Whole-Console Integrated Runtime and Visual Acceptance Rerun**.
