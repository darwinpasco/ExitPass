# ExitPass Statutory Cross-Channel Canonical Authority Matrix v1.0

## Executive Decision
The canonical model intentionally splits authority. Central PMS owns statutory eligibility decisions and payment-time payable-basis application. The canonical database owns configured jurisdiction and statutory policy coverage. Operator Console owns human review decisions only. WebPay and APT are service-channel request and payment-time application clients. POS Server fiscalizes only final applied facts supplied by Central PMS.

## Authority Matrix
| Concern | Authoritative system | Allowed initiators | Allowed reviewers | Allowed appliers | Read-only consumers | Prohibited authorities |
|---|---|---|---|---|---|---|
| Site ordinance coverage | Canonical database exposed through Central PMS | Central PMS policy readers | None | None | WebPay, APT, Management Platform, Operator Console | Browser, APT local state, POS Server, HikCentral |
| Customer entitlement request | Central PMS decision-v2 | WebPay, APT, authorized Operator Console request workflow when enabled | None at submit time | None | Operator Console, WebPay rediscovery | POS Server, Payment Orchestrator, browser-local recovery |
| Decision initiation | Central PMS decision-v2 | WebPay service, APT service, authorized Operator Console human | None | None | WebPay rediscovery, Operator Console | POS Server, Management Platform UI, HikCentral |
| Eligibility approval | Central PMS with Operator Console reviewer action | None | Authorized human reviewer with approve permission | None | WebPay/APT readback | WebPay service, APT service, POS Server, policy administrators by implication |
| Eligibility rejection | Central PMS with Operator Console reviewer action | None | Authorized human reviewer with reject permission | None | WebPay/APT readback | WebPay service, APT service, POS Server |
| Payable-basis application | Central PMS application-v1 | WebPay service at payment time; APT service before cash | None | Authorized service channel only | WebPay, APT, POS Server fiscal caller | Operator Console reviewer, Management Platform, browser alone |
| Parking-session resolution | Central PMS over authoritative parking state | WebPay/APT lookup contexts | None | None | WebPay, APT, Operator Console | Browser storage, APT SQLite, POS Server |
| Tariff calculation | Central PMS | Payment-time application caller | None | Central PMS only | POS Server receives final facts | WebPay, APT, Operator Console, POS Server |
| Payment readiness | WebPay/Payment Orchestrator for online; APT/Central PMS for cash readiness | WebPay or APT | None | Payment channel | Central PMS, POS Server | Operator Console, Management Platform |
| Cash acceptance | APT desktop after Central PMS readiness | APT only | None | APT cash module | Central PMS/POS audit | WebPay, Operator Console |
| Fiscal issuance | POS Server | Payment channel with final payment facts | None | POS Server only | Digital SI/readback clients | Central PMS eligibility engine, Operator Console, Management Platform |
| Sales Invoice presentation | POS Server | Fiscal readback clients | None | None | Customer, authorized operations | Central PMS, WebPay or APT local reconstruction |
| Evidence storage | Future evidence-control owner in Central PMS boundary | WebPay, APT, Operator Console as clients | None | None | Operator Console reviewer, auditor | POS Server, browser storage, APT SQLite, PostgreSQL bytes |
| Evidence review | Operator Console with Central PMS authorization | None | Authorized evidence reviewer | None | Auditor | WebPay, APT, POS Server, Management Platform policy UI |
| Policy administration | Future governed Management Platform/Central PMS admin capability | Authorized policy administrator | None | None | Management Platform read-only workspace | Operator Console reviewer, WebPay, APT, POS Server |
| Site/LGU mapping | Canonical database | Database migration/controlled admin task | None | None | Central PMS, Management Platform | Channel UI, Site Group direct manual override |
| Gate authorization | Gate/HikCentral integration boundary | Central PMS/Parking domain commands | None | Gate service only | Operations | APT direct HikCentral access, WebPay |

## Split-Authority Notes
- Approval and application are intentionally separate; approval can occur long before payment, while application must happen against the current payable basis at payment time.
- Site is the operational resource scope. LGU is the statutory ordinance scope. Site Group is an administrative grouping and query scope, not an ordinance authority.
- POS Server is authoritative for fiscal issuance and immutable fiscal persistence, but it is not authoritative for eligibility, tariff, payable-basis application, or evidence.

## Authority Gaps
| Gap | Evidence | Impact | Recommended owner |
|---|---|---|---|
| Legacy Operator Console payable-basis apply route remains in Central PMS | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` maps `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis` | Contradicts the approved Operator Console approval-only boundary if reachable by policy | Codex I, Central PMS |
| Management Platform coverage read model has not fully moved to I-006 canonical LGU views | `ManagementPlatformStatutoryDiscountPolicyCoverageRepository.cs` still uses `s.lgu_code` and policy compatibility fields | Policy visibility can diverge from canonical Site-LGU authority after database migration | Codex I, Central PMS |
| Evidence-control owner is contract-frozen but runtime absent | I-003 documentation; current Operator Console evidence metadata routes only | Review readiness cannot securely preview protected document images end to end | Codex I, future evidence runtime |
| Runtime RBAC persistence remains incomplete | I-002 contract and current policy catalog | Service/human separation and Site scope are partly contractual or fixture-backed until persistence/admin exist | Codex I/Codex H |
