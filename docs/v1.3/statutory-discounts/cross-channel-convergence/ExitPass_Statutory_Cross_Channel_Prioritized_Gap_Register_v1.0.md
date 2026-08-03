# ExitPass Statutory Cross-Channel Prioritized Gap Register v1.0

## Summary
| Severity | Count | Meaning |
|---|---:|---|
| P0 | 0 | No immediate safety, financial, fiscal, or irreversible-custody defect was proven. |
| P1 | 6 | Authority, correctness, privacy, or production-readiness gaps. |
| P2 | 7 | Operational, reconciliation, persistence, or validation gaps. |
| P3 | 5 | Usability, observability, documentation, or support consistency gaps. |

## Gaps
| Gap ID | Severity | Affected repositories | Current behavior | Expected behavior | Evidence | Impact | Dependency | Owner | Repository | Work type | Order |
|---|---|---|---|---|---|---|---|---|---|---|---:|
| I008-GAP-P1-001 | P1 | ExitPass-Discounts | Legacy Operator Console apply-payable-basis route remains | Operator Console approves/rejects only | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Authority contradiction if reachable | None | Codex I | ExitPass-Discounts | Runtime/test | 1 |
| I008-GAP-P1-002 | P1 | ExitPass-Discounts, exitpassdb_v1.2 | Coverage repository uses legacy `lgu_code` | Use I-006 canonical Site-LGU authority/views | `ManagementPlatformStatutoryDiscountPolicyCoverageRepository.cs` | Coverage display can diverge | I-006 DB alignment | Codex I | ExitPass-Discounts | Runtime/test | 2 |
| I008-GAP-P1-003 | P1 | ExitPass-Discounts, WebPay, APT, Operator Console | Evidence byte lifecycle is contract-only | Opaque references and protected object storage | I-003 docs; metadata routes | Review cannot safely verify ID images end to end | Evidence schema/storage | Codex I | ExitPass-Discounts | Runtime/database/security | 3 |
| I008-GAP-P1-004 | P1 | POS Server, ExitPass-Discounts, APT/WebPay | POS boundary supports facts; channel proof incomplete | Every statutory payment fiscalizes with final Central PMS facts | POS runtime; APT cash-readiness audit | Risk if statutory payment enabled before proof | POS/channel integration tests | Codex I/J/G | Multiple | Runtime/UAT | 4 |
| I008-GAP-P1-005 | P1 | ExitPass-Discounts, ManagementPlatform, canonical DB | Durable RBAC grant persistence/admin incomplete | Human/service/Site grants durable and administrable | I-002 contract, policy catalog | Production authorization gap | RBAC persistence | Codex I/H | Multiple | Runtime/database/UI | 5 |
| I008-GAP-P1-006 | P1 | POS Server | POS accepts `OPERATOR_CONSOLE` source payment channel | Applied facts should originate from governed payment channels | `FiscalDocumentCreationService.cs` | Terminology/authority risk | Source-channel decision | Codex I | POS Server | Runtime/test | 6 |
| I008-GAP-P2-001 | P2 | ExitPass | WebPay local worktree is behind latest `origin/dev` and has untracked files | Clean latest worktree for future WebPay audit | Git baseline | Evidence ambiguity | Worktree hygiene | Darwin/Codex G | ExitPass | Repo hygiene | 7 |
| I008-GAP-P2-002 | P2 | APT desktop | APT statutory cash guarded | Enable only after POS facts proof | APT cash audit | APT statutory flow not UAT-ready | P1-004 | Codex J | APT repos | Runtime/UAT | 8 |
| I008-GAP-P2-003 | P2 | canonical DB | Persistent dev DB migration gap remains | Dev DB aligned to I-006 | I-007 context | DB-backed validation gap | I-007 approval | Codex I | exitpassdb_v1.2 | DB execution | 9 |
| I008-GAP-P2-004 | P2 | Multiple | End-to-end statutory tests are fragmented | One journey suite across WebPay/APT/OC/Central PMS/POS | Test inventory | Regression risk | Runtime gaps closed | Codex I/G/J | Multiple | Test/UAT | 10 |
| I008-GAP-P2-005 | P2 | WebPay/APT | Safe classification labels differ | Support mapping shared | Contract matrix | Support inconsistency | None | Codex I/J/G | Docs/runtime | Documentation/test | 11 |
| I008-GAP-P2-006 | P2 | ExitPass-Discounts | `reconciliation.manage` broad bypass appears in statutory policies | Narrow or justify bypass | `CentralPmsRbacPolicyCatalog.cs` | Over-broad access risk | RBAC persistence | Codex I | ExitPass-Discounts | Runtime/test | 12 |
| I008-GAP-P2-007 | P2 | ExitPass-Discounts/ManagementPlatform | Audit/reconciliation read views incomplete | Immutable chain read without mutation | I-002/I-003 contracts | Investigation friction | Evidence/fiscal linkage | Codex I/H | Multiple | Runtime/UI | 13 |
| I008-GAP-P3-001 | P3 | Multiple | Support reference glossary differs | Shared support terminology | Contract matrix | Training overhead | None | Codex I | Docs | Documentation | 14 |
| I008-GAP-P3-002 | P3 | Multiple | Manual evidence not consolidated | Single UAT evidence index | Prior docs | Review overhead | Runtime gaps closed | Codex I | Docs | Documentation | 15 |
| I008-GAP-P3-003 | P3 | ManagementPlatform | Admin workspace shows technical policy data by design | Clarify not customer/reviewer UI | Source/docs | Misinterpretation risk | None | Codex H | ManagementPlatform | Docs/UI copy | 16 |
| I008-GAP-P3-004 | P3 | POS Server | Final accredited SI text compliance-dependent | Compliance-approved presentation copy | POS tests/docs | UAT signoff dependency | POS facts final | Compliance/Codex K | POS Server | Compliance/UAT | 17 |
| I008-GAP-P3-005 | P3 | canonical DB | Research seeds are non-production and auto-application disabled | Runbooks warn operators | I-006 seed posture | Operational misunderstanding | None | Codex I | DB/docs | Documentation | 18 |
