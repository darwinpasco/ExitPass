# ExitPass Central PMS to POS Server Controlled UAT Post-Run Review v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS to POS Server Controlled UAT Post-Run Review |
| Version | v1.0 |
| Date | 2026-07-03 |
| Scope | Post-run review and lessons learned for the first controlled Central PMS to POS Server fiscal issuance diagnostic run |
| Run ID | CPS-POS-UAT-20260703-DEV-ATC-001 |
| Approval reference | DEV-UAT-CPS-POS-001 |
| Evidence source | `D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001\controlled-posserver-fiscal-uat-CPS-POS-UAT-20260703-DEV-ATC-001-final-summary.md` |

## 2. Executive Summary

The first controlled Central PMS to POS Server fiscal issuance diagnostic run completed successfully after resolving Central PMS reference-preparation, controlled request-mapping, local fixture, POS Server runtime configuration, and disposable database drift blockers.

The final controlled run returned HTTP 200 with `accepted = true`, invoked the diagnostic seam, attempted the POS Server call, recorded fiscal evidence in Central PMS, and produced POS Server fiscal document `DEV-SI-00000001`.

The run remained inside the controlled UAT boundary. It did not mutate payment finality, issue ExitAuthorization, trigger gate behavior, enable fiscal gating enforcement, or write evidence files automatically.

## 3. Final Result

| Field | Value |
| --- | --- |
| Task result | passed |
| Final `/run` HTTP status | 200 |
| Final run status | newly_created_recorded |
| accepted | true |
| diagnosticInvoked | true |
| posServerCallAttempted | true |
| Fiscal document ID | 0f368ed4-0fd4-417b-bb73-164df423f147 |
| Fiscal document number | DEV-SI-00000001 |
| SHA-256 | 9F2D4BD1F3900919F23FBB275DE9A79353A33A446F4A6C1B3E61AA160354EBF4 |

## 4. Evidence Package

Evidence was manually saved under:

`D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001`

Package contents:

- Final request JSON.
- Final preflight response JSON.
- Final run response JSON.
- Final run SHA-256 hash file.
- Final summary file.
- Earlier failed responses and logs, preserved separately and not labeled as success evidence.

## 5. Timeline / Workstream Progression

| Stage | Outcome |
| --- | --- |
| Harness planning and evidence governance | Completed before runtime execution. |
| Application-level controlled UAT harness | Implemented with guard checks and safe result model. |
| Evidence exporter and manual-save procedure | Implemented without automatic file writing. |
| Data assignment and readiness refresh | Completed for development-only first run values. |
| Dry-run checklist | Identified no safe runtime invocation surface. |
| Controlled invocation surface | Added guarded internal preflight and run endpoints. |
| First execution attempts | Failed safely before successful fiscal issuance evidence was produced. |
| Final controlled run | Passed and produced evidence package. |

## 6. Blockers Encountered

- No safe runtime invocation surface existed initially.
- Central PMS fiscal reference state transition failed because the placeholder reference ID had no backing row.
- Central PMS fiscal reference preparation failed due to nullable Npgsql parameter handling and missing local dependency fixture rows.
- Controlled UAT request mapping initially missed POS Server-required local development schema IDs.
- POS Server local runtime needed disposable database persistence configuration.
- POS Server disposable database schema had drift and was missing fiscal numbering columns compared with repository DDL.
- Local development secrets appeared in environment output during troubleshooting and should be rotated before any shared UAT use or log sharing.

## 7. Root Causes

The final blocker was not a single issue. It was a chain of Central PMS reference-preparation, request-mapping, local fixture, POS Server runtime configuration, and disposable database drift issues.

Root causes:

- The controlled run path needed a real Central PMS fiscal issuance reference row prepared through the application path before state transitions could succeed.
- Nullable PostgreSQL parameters in Central PMS fiscal reference lookup and preparation were bound without explicit Npgsql types.
- The controlled invocation mapper did not provide POS Server-required development schema IDs for local code-backed fiscal request fields.
- The local POS Server process was not initially configured with persistence for `posserver_validation_local`.
- The disposable POS Server database did not fully match the checked fiscal numbering DDL.

## 8. Fixes Applied

Source-controlled Central PMS fixes:

- Added the controlled invocation surface for the guarded UAT diagnostic path.
- Added fiscal reference state preparation before the live diagnostic orchestration path.
- Fixed typed nullable Npgsql parameter binding in the fiscal issuance reference repository.
- Added deterministic DEV-only controlled-UAT defaults for POS Server-required local schema IDs.

Local runtime-only fixes:

- Seeded Central PMS local fixture rows required by the controlled run path.
- Seeded POS Server local fiscal fixture rows required by the fiscal issuance path.
- Started POS Server with disposable DB persistence configuration for `posserver_validation_local`.
- Repaired the local disposable POS Server DB schema to include fiscal numbering columns expected by the checked DDL.

The local runtime-only repairs were not promoted to source control and should not be treated as production changes.

## 9. Safety Verification

| Safety marker | Final value |
| --- | --- |
| paymentFinalityChanged | false |
| exitAuthorizationIssued | false |
| gateBehaviorTriggered | false |
| fiscalGatingEnforcementEnabled | false |
| evidenceFileWritten | false |
| sensitiveDataExcluded | true |

The final run response passed the sensitive marker scan. Evidence was returned by the controlled endpoint and manually saved; no application evidence writer was introduced.

## 10. Post-Run Central PMS Verification

- One active fiscal issuance reference exists for upstream finality reference `CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`.
- Recorded state: `FISCAL_ISSUANCE_RECORDED`.
- POS Server fiscal document ID recorded: `0f368ed4-0fd4-417b-bb73-164df423f147`.
- Fiscal document number recorded: `DEV-SI-00000001`.
- Earlier failed references were preserved as inactive and superseded.
- ExitAuthorization count for the UAT fixture/correlation: `0`.
- Gate event count for the UAT correlation: `0`.
- Payment fixture remained in its pre-seeded confirmed/recorded state; the controlled run did not execute the payment confirmation flow.

## 11. Post-Run POS Server Verification

- One fiscal document exists for payment finality reference `CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`.
- Fiscal document type: `sales_invoice`.
- Fiscal document status: `issued`.
- Fiscal identity: `DEV-FISCAL-IDENTITY-ATC-001`.
- Sequence policy: `DEV-SI-SEQUENCE-POLICY-ATC-001`.
- Fiscal document number: `DEV-SI-00000001`.
- Fiscal sequence value: `1`.
- Fiscal sequence state current/reserved/issued values: `1/1/1`.
- Production sequence usage count for the DEV policy check: `0`.

## 12. What Went Well

- The controlled guard posture prevented payment, exit, gate, and fiscal enforcement side effects.
- Preflight stayed non-mutating and useful for request/config validation.
- Failed attempts were preserved and not reclassified as success evidence.
- The manual evidence package captured request, preflight, final run response, hash, and post-run verification.
- The diagnostic seam successfully exercised Central PMS to POS Server fiscal issuance without integrating into production flows.

## 13. What Caused Delays

- Runtime invocation was unavailable until the guarded internal UAT surface was added.
- Multiple local fixture and schema assumptions had to be corrected against actual database schemas.
- POS Server local runtime configuration and disposable database drift were discovered only during execution attempts.
- Environment output exposed local development secrets during troubleshooting, creating a follow-up hygiene item.

## 14. Lessons Learned

- Controlled UAT diagnostics need both an application seam and a tightly guarded runtime invocation surface.
- Preflight should include as many schema/request-shape checks as practical, but it cannot replace runtime persistence and DB drift checks.
- Local disposable environments still need schema drift verification before first execution.
- Nullable database parameters should be explicitly typed where SQL uses nullable comparison branches.
- UAT evidence should distinguish source-controlled fixes from local runtime-only fixture repairs.
- Logs and environment captures should be treated as potentially sensitive by default.

## 15. Follow-Up Actions

- Review and decide whether the controlled UAT invocation surface remains dev-only, or whether it should be retained behind guarded internal and mTLS controls.
- Add durable developer documentation for the controlled UAT local setup.
- Create a separate POS Server validation/drift task if disposable DB drift recurs.
- Rotate or regenerate exposed local development secrets before sharing logs or promoting any environment.
- Consider adding an automated disposable-environment bootstrap script later.
- Return to remaining SDDs: Operator Console SDD, Management Dashboard and Reporting SDD, and fiscal exception/readback/retry design.

## 16. Items Explicitly Not Promoted to Production

- No production fiscal gating enforcement was enabled.
- No retry scheduler was added.
- No GET readback worker was added.
- No Operator Console exception queue was implemented.
- No Management Dashboard projection was implemented.
- No automatic evidence file writer was added.
- No payment confirmation production flow integration was added.
- No ExitAuthorization production behavior change was added.
- No gate behavior was added.
- POS Server local disposable DB repairs were not promoted as source-controlled migrations or state files.

## 17. Decision / Closure Statement

The controlled Central PMS to POS Server fiscal issuance diagnostic workstream is closed for first-run evidence. The project may now return to remaining v1.3 SDD work, while production enforcement, retry/readback automation, Operator Console exception queues, and dashboard projections remain separate future work.
