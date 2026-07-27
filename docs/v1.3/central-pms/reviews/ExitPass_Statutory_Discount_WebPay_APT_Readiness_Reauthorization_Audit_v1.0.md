# ExitPass Statutory Discount WebPay APT Readiness Reauthorization Audit v1.0

## 1. Purpose

This docs-only audit reauthorizes, or declines to reauthorize, implementation work for WebPay and Assisted Payment Terminal statutory-discount integration after the merged Central PMS backend work:

- canonical database promotion
- application-local statutory patch retirement
- canonical generated SQL disposable fixture alignment
- service-channel pending-review intake
- Operator Console review linkage
- post-approval service-channel payable-basis application intent
- application-intent concurrency recovery
- channel-safe durable readback hardening

The audit does not implement WebPay, APT, SQL, API, DTO, runtime, test, Bruno, Operator Console UI, POS Server, fiscal, ExitAuthorization, gate, or payment-provider behavior.

## 2. Repositories and exact commits inspected

| Repository | Branch | Commit | Posture |
| --- | --- | --- | --- |
| `D:\SourceCodes\ExitPass-Discounts` | `docs/statutory-discount-webpay-apt-readiness-reauthorization` from `dev` | `8242e299f7d276fe1ab92e8234db3adba8c96d21` | Audit report only |
| `D:\SourceCodes\exitpassdb_v1.2` | `develop` | `636ca9c4b229b1d4e9d517f9251a0d5042950834` | Read-only canonical database source |

`dev` equaled `origin/dev` and the canonical DB `develop` equaled `origin/develop` at inspection time.

## 3. Prior authorization posture

The prior WebPay/APT readiness authorization report left both channels unauthorized. The reasons were architectural and runtime blockers, not a policy decision against the channels:

- service-channel decision authority was unresolved
- service-channel post-approval application intent was unavailable
- Operator Console review linkage for service-channel decisions was incomplete
- canonical database source did not contain the staged/service-channel objects
- proof-grade tests depended on app-local patches and shared accumulated DB state
- application-intent concurrency recovery was incomplete
- shared readback lacked Site, VAT, and channel-safe readiness facts

## 4. Completed blocker inventory

| Prior blocker | Current status | Evidence |
| --- | --- | --- |
| Service-channel approval authority unresolved | CLOSED | Review-mediated authority model selected; WebPay/APT submit facts only; Operator Console reviews |
| Pending-review service-channel intake unavailable | CLOSED | Shared POST creates canonical decision-v2 as `AWAITING_REVIEW` / `NOT_DECIDED` |
| Operator Console review linkage incomplete | CLOSED | Operator Console can list/detail/approve/reject service-channel-originated canonical decisions |
| Post-approval application intent unavailable | CLOSED | WebPay/APT can submit `applyPayableBasis=true` after `COMPLETED` / `APPROVED` |
| Validation/payable-basis linkage gap | CLOSED | Operator Console approval creates statutory validation and durable payable-basis facts required by the writer |
| Canonical DB promotion incomplete | CLOSED | Canonical generated SQL contains decision-v2, application-v1, service-channel review linkage, metadata, constraints, and indexes |
| App-local patch dependency | CLOSED | Six promoted statutory patches retired and guard passes |
| Proof-grade fixture drift | CLOSED | Statutory tests use disposable DBs built from canonical generated SQL |
| SQLSTATE `40P01` concurrency leak | CLOSED | Application-intent concurrency recovery maps deadlock/replay through canonical application state |
| Channel-safe durable readback incomplete | CLOSED | Shared response includes Site, Site Group, VAT-exclusive amount, VAT amount, VAT treatment, and readiness posture |

## 5. Remaining blocker inventory

No Central PMS backend blocker remains for starting WebPay or APT statutory-discount integration implementation.

Remaining non-backend blockers:

- WebPay client implementation does not yet exist.
- APT desktop implementation does not yet exist.
- Live authenticated channel execution and Bruno/equivalent proof remain required before controlled UAT.
- APT cash acceptance remains blocked pending terminal-side readiness proof and immediate pre-`CASH_RECEIVED` revalidation.
- Production authorization remains separate.

## 6. Governing authority model

The reviewed source preserves the intended authority boundaries:

- WebPay and APT submit permitted facts and display Central PMS results.
- WebPay and APT do not approve entitlement.
- Operator Console performs human entitlement review.
- Central PMS owns canonical decision-v2 persistence.
- Central PMS owns payable-basis application and exactly-once mutation.
- Payment initiation consumes the effective applied tariff snapshot.
- POS Server consumes finalized facts for fiscalization only.
- Channels do not calculate statutory discounts or VAT.
- Channels do not mark payment final, issue ExitAuthorization, trigger fiscal issuance, or control gates.

## 7. Canonical database posture

The canonical database repository is now the supported source of truth. Evidence from `build/generated/exitpass-full-object.generated.sql` and object source confirms:

- `discounts.statutory_discount_decision_commands`
- `discounts.statutory_discount_payable_basis_application_commands`
- `operator_console.statutory_discount_service_channel_reviews`
- `AWAITING_REVIEW` command status support
- `NOT_DECIDED` decision result support
- decision-v2 metadata on `discounts.statutory_discount_validations`
- review-to-validation linkage
- application-v1 VAT amount columns
- business-identity, idempotency, decision/application uniqueness, review queue, validation, and correlation indexes
- comments preserving privacy and authority boundaries

The application-local promotion patches are retired and are no longer required for proof-grade statutory tests.

## 8. Shared Central PMS contract posture

The retained shared contract supports:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`
- authenticated channel derivation for `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL`
- body source-channel mismatch rejection
- ambiguous-channel permission rejection
- service-channel prohibition of decision, reviewer, Operator Console device, and shift fields
- pending-review intake as `AWAITING_REVIEW` / `NOT_DECIDED`
- Operator Console-mediated approval/rejection of the same canonical decision
- post-approval application intent with `applyPayableBasis=true`
- canonical decision-v2 and application-v1 identities
- idempotent replay and cross-channel convergence
- application-intent concurrency recovery
- shared POST/GET durable readback
- Site and Site Group readback
- VAT-exclusive and VAT amount readback
- original, discount, final payable, and currency readback
- payable-basis readiness status/action
- privacy-safe response shape

## 9. WebPay contract matrix

| Capability | WebPay status | Notes |
| --- | --- | --- |
| Pending-review intake | READY | Authenticated `WEBPAY` can submit permitted facts without reviewer/decision fields |
| Decision polling | READY | Shared GET is non-mutating |
| Approval observation | READY | GET/POST replay exposes `COMPLETED` / `APPROVED` |
| Rejection observation | READY | GET/POST replay exposes rejection and terminal posture |
| Application intent | READY | `applyPayableBasis=true` works only after approved canonical decision |
| Applied readback | READY | Canonical application ID, applied snapshot, final amount, currency, VAT facts, readiness |
| Restart/browser refresh recovery | READY | Recovery uses durable command IDs and GET |
| Concurrency recovery | READY | Deadlock path reconciles to durable application, in-progress, or retryable posture |
| Site scope | READY | `siteId` and `siteGroupId` are returned from durable linkage |
| VAT facts | READY | Explicit VAT-exclusive and VAT amount fields are present |
| Payable-basis readiness | READY | `payableBasisReady` plus status/action is exposed |
| Payment readiness | READY_WITH_CONSTRAINT | WebPay must initiate payment only after `payableBasisReady=true` and use applied snapshot |
| Retryable failure | READY | Retryability and recovery action are exposed |
| Terminal failure | READY | Terminal/non-retryable posture is exposed |
| Semantic conflict | READY | Deterministic 409 conflict remains exposed |
| Privacy | READY | No raw evidence, full IDs, reviewer-only facts, or device/shift facts exposed |
| RBAC | READY | Service identity must hold the WebPay submit/read permissions |
| Idempotency | READY | Original idempotency-key posture remains enforced |
| Cross-channel replay | READY | Equivalent APT/WebPay facts converge on one canonical decision/application |

## 10. APT contract matrix

| Capability | APT status | Notes |
| --- | --- | --- |
| Pending-review intake | READY | Authenticated `ASSISTED_PAYMENT_TERMINAL` can submit permitted facts |
| Decision polling | READY | Shared GET is non-mutating and restart-safe |
| Approval observation | READY | APT can observe Operator Console completion without reviewer facts |
| Rejection observation | READY | Rejection is terminal and not payable-ready |
| Application intent | READY | `applyPayableBasis=true` works only after approved canonical decision |
| Applied readback | READY | Canonical application, applied snapshot, final amount, VAT facts, readiness |
| Terminal restart recovery | READY | Durable GET supports reconstructing statutory state |
| Concurrency recovery | READY | Equivalent service-channel/Operator Console apply converges |
| Site scope | READY | Site/Site Group returned for terminal scope checks |
| VAT facts | READY | APT can display authoritative VAT/discount breakdown without local calculation |
| Payable-basis readiness | READY | `payableBasisReady` is available for later cash-readiness input |
| Payment/cash readiness | READY_WITH_CONSTRAINT | Statutory readiness is only one input; cash acceptance remains blocked |
| Retryable failure | READY | Recovery action is exposed |
| Terminal failure | READY | Terminal posture is exposed |
| Semantic conflict | READY | Deterministic conflict remains exposed |
| Privacy | READY | No raw evidence, full IDs, reviewer-only facts, or Operator Console device/shift facts exposed |
| RBAC | READY | Service identity must hold APT submit/read permissions |
| Idempotency | READY | Original idempotency-key posture remains enforced |
| Cross-channel replay | READY | Equivalent WebPay/APT facts converge |

## 11. WebPay implementation authorization decision

`WEBPAY_INTEGRATION_IMPLEMENTATION_AUTHORIZED`

WebPay implementation may proceed because the Central PMS backend now supports the full review-mediated statutory-discount flow needed by WebPay:

1. submit facts
2. poll awaiting review
3. observe approval or rejection
4. submit post-approval application intent
5. recover by GET after refresh/restart
6. display authoritative Site, VAT, discount, and final payable facts
7. initiate payment only after `payableBasisReady=true`

This is implementation authorization only. It is not controlled-UAT or production authorization.

## 12. APT implementation authorization decision

`APT_INTEGRATION_IMPLEMENTATION_AUTHORIZED`

APT statutory-discount integration implementation may proceed because the Central PMS backend now supports the required service-channel flow without Operator Console-only reviewer, device, or shift fields:

1. submit facts
2. poll awaiting review
3. observe approval or rejection
4. submit post-approval application intent
5. recover after terminal restart
6. consume durable Site, VAT, discount, final payable, and readiness facts

This is implementation authorization only. It does not authorize APT cash acceptance.

## 13. APT cash-acceptance authorization decision

`APT_CASH_ACCEPTANCE_NOT_AUTHORIZED`

APT must not accept cash merely because a statutory payable basis is applied. Cash acceptance still requires terminal-side and operational proof for:

- parking-session readiness
- current tariff snapshot posture
- terminal-cash eligibility
- Site and terminal authorization
- local cashier/custody prerequisites
- POS Server and fiscal readiness
- immediate pre-`CASH_RECEIVED` revalidation
- statutory `payableBasisReady=true` when a discount applies

## 14. WebPay controlled-UAT posture

WebPay controlled UAT is not authorized by this audit.

Remaining prerequisites:

- WebPay client implementation against the retained shared routes
- authenticated WebPay service identity in the target environment
- live or Bruno-equivalent authenticated proof
- browser refresh/restart proof
- payment initiation proof after `payableBasisReady=true`
- privacy/error-handling review
- deployment evidence using canonical database migration/source

## 15. APT controlled-UAT posture

APT controlled UAT for statutory-discount client implementation is not authorized by this audit.

Remaining prerequisites:

- APT desktop implementation against the shared routes
- authenticated APT service identity in the target environment
- terminal restart proof
- Site/terminal configuration proof
- statutory replay/conflict/recovery proof
- live or Bruno-equivalent authenticated proof
- no local statutory/VAT calculation proof

## 16. APT cash controlled-UAT posture

APT cash controlled UAT remains unauthorized.

Cash UAT must wait for terminal-side readiness integration and immediate pre-cash validation that includes but is not limited to Central PMS statutory `payableBasisReady=true`.

## 17. Production posture

Neither WebPay nor APT is production-authorized by this audit.

Production rollout still requires:

- merged client implementations
- controlled UAT completion
- operational runbooks
- channel identities and RBAC in production
- production database migration verification
- payment and fiscal operational signoff
- privacy/security signoff
- rollback and monitoring posture

## 18. Retry, recovery, and concurrency posture

The current backend exposes retryable and terminal posture through existing decision/application retryability, recovery classification, recovery action, and safe error codes.

Concurrency recovery is sufficient for channel implementation:

- equivalent WebPay/APT/Operator Console application requests converge on one canonical application
- SQLSTATE `40P01` does not leak as HTTP 500 in the fixed application-intent boundary
- durable winner replay does not reapply
- in-progress states instruct recovery or polling
- semantic conflicts remain deterministic

## 19. Site and VAT readback posture

The shared response now exposes:

- `siteId`
- `siteGroupId`
- `vatExclusiveBasisAmountMinorUnits`
- `vatAmountMinorUnits`
- `vatTreatment`
- original gross amount
- statutory discount amount
- final payable amount
- currency

Amounts use the existing minor-unit convention. Missing historical facts remain nullable and do not imply readiness.

## 20. Restart and polling posture

Shared GET readback is durable and non-mutating. It reconstructs decision, application, Site, VAT, amount, readiness, retryability, and recovery facts from canonical tables and review linkage. It does not depend on browser memory, terminal memory, Operator Console screen state, or current vendor tariff recalculation.

## 21. Payment-initiation posture

Payment initiation remains a consumer of the effective applied tariff snapshot. Focused tests confirm payment uses the applied snapshot after statutory application. Channels must not initiate payment before `payableBasisReady=true` and must not recalculate statutory discount or VAT locally.

## 22. Security and privacy posture

The shared service-channel response remains privacy-safe. It does not expose:

- full statutory ID values
- raw identity images
- Base64 evidence
- raw evidence bytes
- reviewer-sensitive notes
- Operator Console device or shift identity
- permission internals
- SQL or persistence details
- provider payloads
- HikCentral details
- secrets or stack traces

Site, VAT, amount, readiness, and recovery fields are safe and necessary for channel implementation.

## 23. Reviewer-attribution posture

Reviewer attribution remains Operator Console and audit scoped. Service channels receive canonical decision outcome and timestamp posture, not reviewer identity, reviewer notes, Operator Console device binding, or shift identity.

## 24. Vendor dependency posture

Completed statutory readback does not require live HikCentral/vendor tariff recalculation. Vendor/session freshness and parking readiness remain separate channel/payment concerns.

## 25. Validation evidence

Commands run from `D:\SourceCodes\ExitPass-Discounts`:

```powershell
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Application\ExitPass.CentralPms.Application.csproj --no-restore -c Release -v quiet /clp:ErrorsOnly -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore -c Release -v quiet /clp:ErrorsOnly -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet build src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore -c Release -v quiet /clp:ErrorsOnly -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet build src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore -c Release -v quiet /clp:ErrorsOnly -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet build src\Services\CentralPms\tests\ExitPass.CentralPms.ContractTests\ExitPass.CentralPms.ContractTests.csproj --no-restore -c Release -v quiet /clp:ErrorsOnly -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.ContractTests\ExitPass.CentralPms.ContractTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~StatutoryDiscountDecisionContractTests" --logger "console;verbosity=minimal"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests|FullyQualifiedName~OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests" --logger "console;verbosity=minimal"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~ConcurrentServiceChannelAndOperatorConsoleApplicationIntent_CreatesOneApplicationAndOneAppliedSnapshot" --logger "console;verbosity=minimal"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~StatutoryDiscount&FullyQualifiedName!~OperatorConsoleStatutoryDiscountAlignedDbUatApiIntegrationTests" --logger "console;verbosity=minimal"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~CreatePaymentAttemptPublicApiIntegrationTests|FullyQualifiedName~CreateOrReusePaymentAttemptDbRoutineGatewayTests|FullyQualifiedName~AptPayableBasisReadinessApiIntegrationTests|FullyQualifiedName~AptPayableBasisReadinessContractTests" --logger "console;verbosity=minimal"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~TerminalCashPaymentApiIntegrationTests|FullyQualifiedName~TerminalCashReceiptReadbackIntegrationTests|FullyQualifiedName~TerminalCashFiscalIssuanceIntegrationTests" --logger "console;verbosity=minimal"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~CreateOrReusePaymentAttemptHandlerTests|FullyQualifiedName~WebPayReceiptPresentationServiceTests|FullyQualifiedName~PosServerFiscalDocumentRequestMapperTests|FullyQualifiedName~FiscalSemanticRequestHashCalculatorTests|FullyQualifiedName~FiscalSemanticRequestHashParityProofServiceTests" --logger "console;verbosity=minimal"
powershell -NoProfile -ExecutionPolicy Bypass -File infra\db\patches\validation\Validate_RetiredStatutoryDiscountCanonicalPatches.ps1
docker exec exitpass-postgres psql -U exitpass -d postgres -tAc "SELECT datname FROM pg_database WHERE datname LIKE 'exitpass_statutory_fixture_%' ORDER BY datname;"
```

Results:

- Application build passed.
- API build passed.
- Unit-test build initially hit a transient output file lock while Application/API builds were still active, then passed on rerun.
- Integration-test build passed.
- Contract-test build passed.
- Focused statutory contract tests passed 2/2.
- Focused post-approval application and service-channel review API tests passed 16/16.
- Focused concurrency recovery test passed 1/1.
- Grouped canonical statutory suite passed 153/153.
- Payment-initiation/APT payable-basis subset passed 20/20.
- TerminalCash regressions passed 51/51.
- WebPay-adjacent/POS/fiscal unit subset passed 62/62.
- Retirement guard passed.
- Disposable fixture cleanup query returned no remaining `exitpass_statutory_fixture_%` databases.

## 26. Known unrelated failures

No readiness-blocking failure was observed in the focused validation.

The previous readback-hardening task observed unrelated broad contract-suite failures in non-statutory tests requiring `localhost:8080` and existing payment-state conditions. Those are not used as channel-readiness blockers in this audit.

## 27. Exact next bounded task

Start WebPay statutory-discount integration implementation as the canonical service-channel reference:

- repository: WebPay repository, not `ExitPass-Discounts`
- purpose: implement pending-review submission, polling, post-approval application intent, durable readback display, payment initiation only after `payableBasisReady=true`, idempotent replay, and retry/terminal handling
- dependency: authenticated WebPay service identity and environment configuration
- validation: live authenticated API proof plus browser refresh/restart proof

APT implementation may proceed in parallel only if it stays repository-separated and follows the same shared Central PMS contract. The more conservative sequencing is WebPay first, then APT alignment, because WebPay is the canonical service-channel reference.

## 28. Tasks that must wait

- APT cash acceptance implementation and UAT authorization
- production enablement for either channel
- POS/fiscal or ExitAuthorization changes
- any statutory calculation or VAT rule change
- any privacy-retention policy change

## 29. Sequencing decision

`AUTHORIZE_WEBPAY_AND_APT_INTEGRATION_IMPLEMENTATION`

This decision authorizes client implementation work only. It does not authorize controlled UAT, production, or APT cash acceptance.

## 30. Known limitations

- This audit did not inspect WebPay or APT external repositories because client implementation is intentionally out of scope.
- Live authenticated Bruno/equivalent channel execution remains required before controlled UAT.
- APT cash-readiness remains a separate terminal-side proof.
- Production deployment remains subject to separate operational and security signoff.

## 31. Evidence appendix

Direct source inspected included:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountStagedCommandService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountStagedCommandRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountServiceChannelReviewRepository.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/StatutoryDiscountDecisionContractTests.cs`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_decision_commands.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_payable_basis_application_commands.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\operator_console\tables\operator_console.statutory_discount_service_channel_reviews.sql`
- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`

## 32. Final authorization lines

WebPay integration: authorized
APT integration: authorized
