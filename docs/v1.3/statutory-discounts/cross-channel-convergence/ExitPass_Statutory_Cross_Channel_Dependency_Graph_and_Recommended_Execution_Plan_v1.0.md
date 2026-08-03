# ExitPass Statutory Cross-Channel Dependency Graph and Recommended Execution Plan v1.0

## Dependency Graph
```text
Operator Console legacy apply route removal
  -> cross-channel authority signoff

I-006/I-007 canonical database persistent alignment
  -> canonical LGU read-model adoption
  -> Management Platform reliable policy coverage display

I-003 evidence contract
  -> evidence metadata schema
  -> object storage/upload authorization
  -> validation/scanning worker
  -> channel upload consumers
  -> Operator Console secure preview
  -> retention/hold/deletion worker
  -> controlled evidence UAT

POS applied statutory fiscal facts boundary
  -> WebPay statutory fiscal payload proof
  -> APT statutory fiscal payload proof
  -> APT statutory cash enablement
  -> controlled statutory payment UAT

I-002 RBAC contract
  -> durable grants/persistence
  -> Management Platform RBAC administration UI
  -> production authorization readiness
```

## Next Bounded Tasks
| Proposed task | Persona | Repository | Branch proposal | Objective | Dependencies | Entry criteria | Exit criteria | Manual test | Recommended model |
|---|---|---|---|---|---|---|---|---|---|
| I-009 Operator Console Legacy Application Route Hard-Deny | Codex I | `D:\SourceCodes\ExitPass-Discounts` | `feature/remove-operator-console-statutory-apply-route` | Remove or hard-deny reviewer access to legacy Operator Console payable-basis application | None | I-008 accepted | Route unavailable to human reviewers; WebPay/APT application unaffected | No UI walkthrough unless UI changed | GPT-5.6 Sol High |
| I-010 Canonical LGU Coverage Read Model Adoption | Codex I | `D:\SourceCodes\ExitPass-Discounts` | `feature/central-pms-policy-coverage-canonical-lgu-read-model` | Move Management Platform coverage repository to I-006 Site-LGU and coverage views | I-006 source merged | Canonical DDL available | Site and Site Group coverage reads match canonical views | No product UI unless ManagementPlatform updated | GPT-5.6 Sol High |
| I-011 POS Source Payment Channel Tightening | Codex I | `D:\SourceCodes\ExitPass-PoSServer` | `feature/pos-server-statutory-source-channel-boundary` | Remove or justify `OPERATOR_CONSOLE` from applied statutory fiscal fact source channels | I-008 accepted | POS tests green | POS accepts only governed payment channels or documented exception | No | GPT-5.6 Sol High |
| G-006 WebPay Statutory Fiscal Payload Proof | Codex G | `D:\SourceCodes\ExitPass` | `feature/webpay-statutory-fiscal-facts-pos-proof` | Prove WebPay sends final Central PMS applied facts to POS for statutory payment | POS boundary stable | WebPay latest dev clean | End-to-end WebPay statutory fiscal tests pass | Controlled browser UAT later | GPT-5.6 Sol High |
| J-006 APT Statutory Fiscal Payload Proof | Codex J | APT repos and POS contract | `feature/apt-statutory-fiscal-facts-pos-proof` | Wire/prove APT statutory cash fiscal payload and preserve irreversible boundary | POS boundary stable; APT cash guard retained | APT latest develop clean | APT statutory cash remains blocked unless applied facts complete; fiscal payload proof passes | Windows manual later | GPT-5.6 Sol High |
| I-012 Evidence Metadata Schema | Codex I | `D:\SourceCodes\ExitPass-Discounts` plus canonical DB when authorized | `feature/statutory-evidence-metadata-schema` | Implement governed metadata only, no bytes | I-003 accepted | DB migration approved | Metadata model and tests pass | No UI | GPT-5.6 Sol High |
| I-013 Evidence Object Storage and Upload Authorization | Codex I | `D:\SourceCodes\ExitPass-Discounts` | `feature/statutory-evidence-upload-authorization` | Implement upload operation and opaque refs | I-012 | Disposable object storage available | Upload auth and scanning quarantine tests pass | Security manual later | GPT-5.6 Sol High |
| I-014 Operator Console Secure Evidence Preview | Codex I | `D:\SourceCodes\ExitPass-Discounts` | `feature/operator-console-secure-evidence-preview` | Authorized short-lived reviewer preview | I-013 | Evidence reviewable state implemented | Preview access tests and browser proof pass | Yes | GPT-5.6 Sol High |
| H-004 Statutory RBAC Administration UI | Codex H | `D:\SourceCodes\ExitPass-ManagementPlatform` | `feature/statutory-rbac-administration-ui` | Administer durable RBAC grants | RBAC persistence APIs | Backend writes exist | UI can manage real grants safely | Yes | GPT-5.6 Sol High |
| UAT-001 Controlled Statutory End-to-End UAT Runbook | Codex I | Docs across repos | `docs/statutory-controlled-uat-runbook` | Consolidate WebPay/APT/OC/POS/manual UAT | Major runtime gaps closed | Stable UAT env | Approved runbook and evidence checklist | Yes | GPT-5.6 Sol High |

## Parallelization
- I-009, I-011, and I-010 can run in parallel if database state is ready for I-010 tests.
- Evidence runtime tasks must be sequential because schema, storage, scanning, and preview depend on each other.
- WebPay and APT fiscal payload proof may run in parallel after POS source-channel posture is settled.
- RBAC UI remains blocked until durable backend persistence and mutation APIs exist.

## Blocked Pending Decisions
- Whether Operator Console apply route is removed entirely, hidden behind a service-only compatibility policy, or retained only for historical test fixtures.
- Whether POS Server should accept `OPERATOR_CONSOLE` as a source payment channel in applied statutory facts.
- Legal/privacy-approved statutory evidence retention periods.
- Controlled UAT authorization and environment readiness.
- Persistent development database migration/cutover authorization.
