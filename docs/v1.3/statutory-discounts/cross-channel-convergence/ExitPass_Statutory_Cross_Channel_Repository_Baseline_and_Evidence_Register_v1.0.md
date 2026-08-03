# ExitPass Statutory Cross-Channel Repository Baseline and Evidence Register v1.0

## Decision
This register records the repositories inspected for the ExitPass v1.3 statutory-discount convergence audit. The audit is read-only for secondary repositories and documentation-only in `D:\SourceCodes\ExitPass-Discounts-I-StatutoryConvergenceAudit`.

## Repository Baselines
| Repository | Role | Branch | HEAD | Origin branch HEAD | Divergence | Status | Notes |
|---|---|---|---|---|---|---|---|
| `D:\SourceCodes\ExitPass-Discounts-I-StatutoryConvergenceAudit` | Primary Central PMS and documentation worktree | `docs/statutory-cross-channel-convergence-audit` | `bb98ed6a5f8307f710424f03f39a7a3c7f994136` | `origin/dev` = `bb98ed6a5f8307f710424f03f39a7a3c7f994136` | `0/0` | Clean at preflight | Latest Central PMS statutory authority source. |
| `D:\SourceCodes\ExitPass` | WebPay and historical Central PMS baseline | `dev` | `6c6fc2228ea9d267c42748725d8fc6be2bd4a915` | `origin/dev` = `bb98ed6a5f8307f710424f03f39a7a3c7f994136` | `0/2` | Untracked WebPay walkthrough artifacts | Read-only; stale local branch means latest WebPay-facing Central PMS evidence is taken from primary worktree. |
| `D:\SourceCodes\ExitPass-APT` | APT-facing Central PMS repository | `dev` | `bb98ed6a5f8307f710424f03f39a7a3c7f994136` | `origin/dev` = `bb98ed6a5f8307f710424f03f39a7a3c7f994136` | `0/0` | Clean | Central PMS APT route evidence. |
| `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` | Windows APT desktop | `develop` | `26770ea92df182ccf2511397f98f48ed824ec7b4` | `origin/develop` = `26770ea92df182ccf2511397f98f48ed824ec7b4` | `0/0` | Clean | Desktop APT consumer and cash-readiness evidence. |
| `D:\SourceCodes\ExitPass-ManagementPlatform` | Management Platform | `develop` | `b6f4b2f60c75dd2f3ed97f72dc54db52b84d81a3` | `origin/develop` = `b6f4b2f60c75dd2f3ed97f72dc54db52b84d81a3` | `0/0` | Clean | Read-only policy coverage workspace evidence. |
| `D:\SourceCodes\ExitPass-PoSServer` | POS Server | `dev` | `a70117d7197b778eeb97d3e99ec84b4076c96075` | `origin/dev` = `a70117d7197b778eeb97d3e99ec84b4076c96075` | `0/0` | Clean | Applied statutory fiscal facts evidence. |
| `D:\SourceCodes\exitpassdb_v1.2` | Canonical database | `develop` | `6d2740cd7e7f2ab068ce3c9828adb9cda6c44850` | `origin/develop` = `6d2740cd7e7f2ab068ce3c9828adb9cda6c44850` | `0/0` | Clean | I-006 canonical geography and statutory coverage authority. |

The retired database repository `D:\SourceCodes\ExitPass_DBv1.2` was not used.

## Evidence Register
| Evidence | Repository | Finding |
|---|---|---|
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs` | Primary | Canonical decision-v2 submit/read/availability routes are channel-neutral and enforce service-channel limits. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs` | Primary | Decision and application lifecycle are separate; service-channel application requires an approved decision. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Primary | Operator Console review queue/detail/approve/reject exist; a legacy apply-payable-basis route also still exists. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/WebPayStatutoryDiscountPendingLifecycleRediscoveryEndpoints.cs` | Primary | WebPay rediscovery route is read-only, WebPay-service authenticated, and safe-error mapped. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/WebPay/PostgresWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository.cs` | Primary | Rediscovery repository uses SELECT-only queries over parking sessions, reviews, decisions, and applications. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/AptStatutoryOrdinanceAvailabilityEndpoints.cs` | Primary/APT | APT ordinance availability and revalidation routes are Site-scoped and service-authenticated. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/AptPayableBasisEndpoints.cs` | Primary/APT | Terminal cash payable-basis resolve/revalidate routes are service-scoped and Site-scoped. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/ManagementPlatformStatutoryDiscountPolicyCoverageEndpoints.cs` | Primary | Management Platform policy coverage API is read-only and permission-gated. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/ManagementPlatform/ManagementPlatformStatutoryDiscountPolicyCoverageRepository.cs` | Primary | Current read model still resolves LGU coverage through legacy `lgu_code` compatibility fields rather than the I-006 canonical Site-LGU views. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs` | Primary | Named policies and permissions include WebPay rediscovery, APT availability, terminal cash payable-basis, Operator Console review, and Management Platform coverage. |
| `docs/v1.3/statutory-discounts/ExitPass_APT_Statutory_Ordinance_Availability_Consumer_Implementation_v1.0.md` | APT desktop | APT treats local state as non-authoritative and revalidates ordinance coverage immediately before cash. |
| `docs/v1.3/statutory-discounts/ExitPass_APT_Statutory_Cash_Acceptance_Readiness_Authorization_Audit_v1.0.md` | APT desktop | Statutory terminal-cash readiness is guarded pending complete fiscal linkage; ordinary payment remains available. |
| `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentCreationService.cs` | POS Server | POS validates applied statutory facts, rejects sensitive evidence markers, reconciles totals, and does not adjudicate entitlement. |
| `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentRepository.cs` | POS Server | POS persists applied statutory fiscal facts in `pos.fiscal_document_applied_statutory_facts` when supplied. |
| `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentSemanticRequestHasher.cs` | POS Server | Ordinary fiscal documents use `sha256:v1`; statutory requests use `pos-server-fiscal-document-create:sha256:v2`. |
| `db/state/tables/pos.fiscal_document_applied_statutory_facts.sql` | POS Server | Applied statutory fiscal facts are immutable, reference-only, and unique by decision/request/application. |
| `src/policyCoverage.ts` and `src/PolicyCoveragePage.tsx` | Management Platform | UI is read-only, calls Central PMS coverage route, and does not mutate policies. |
| `build/generated/exitpass-full-object.generated.sql` | Canonical DB | Generated DDL includes I-006 region/province/LGU/metropolitan/site/policy coverage objects; SHA-256 observed as `394C29A4C457CBA6112ED224A4B0F11E313E4AC44DA1FD03CE26F2BF050CAB3A`. |

## Baseline Caveats
- The `D:\SourceCodes\ExitPass` worktree is behind `origin/dev` and has untracked WebPay walkthrough files; it is suitable only as supplemental WebPay evidence.
- The persistent development database migration gap from I-007 remains outside this audit. The source-controlled canonical database supports I-006; stale persistent database alignment remains a separate execution concern.
- The audit did not run product flows or mutate databases. Unknown runtime behavior is classified as requiring future validation rather than assumed converged.
