# ExitPass Realistic Carpark Fixture Identity Reconciliation v1.0

## Decision

The UUIDs `77000000-0000-0000-0000-000000000001` and `77000000-0000-0000-0000-000000000002` are overloaded by incompatible synthetic fixture families. They are excluded from the realistic catalog and must never be renamed or repurposed in place.

Current meanings include the statutory-discount pilot (`SANDBOX_OC_SD_PILOT_GROUP` / `SANDBOX_OC_SD_PILOT_SITE`), operator-access manual evaluation (`MANUAL_TEST_OPERATOR_ACCESS_GROUP` / `MANUAL_TEST_OPERATOR_ACCESS_SITE`), and generic `Test Site` UAT/test material. These are disposable or manual fixtures and documentation, not canonical realistic reference data.

## Controlled future mapping

| Owning scenario | Current identity | Proposed realistic mapping | Recommendation |
| --- | --- | --- | --- |
| HikCentral local activation | Generic Test Site / 7700 pair | PITX / PITX Level 3 | Create new deterministic canonical IDs after catalog seed approval; never update the 7700 records into PITX. |
| Statutory-discount pilot and WebPay walkthrough | SANDBOX pilot / 7700 pair | A later explicitly approved realistic Site; PITX only if the scenario owner confirms policy and isolation | Preserve deterministic seeded isolation until a separate fixture migration changes all validators and cleanup boundaries. |
| Operator-access manual evaluation | MANUAL_TEST operator-access / 7700 pair | A later approved realistic Site | Migrate the disposable fixture by creating a new scenario identity; do not rewrite historical audit or transaction references. |
| Unit/integration/browser fixtures | Generic Test Site / 7700 pair | Usually retain synthetic identity | Keep synthetic tests where realism is not a contract requirement; change only under focused fixture ownership. |

Historical transaction, audit, payment, statutory-decision, and evidence references may exist in controlled test databases or retained evidence. Future migration must add new realistic records, move only explicitly disposable fixture setup, update validators and harness expectations together, and leave historical foreign keys untouched. Deletion is allowed only for resources conclusively owned by disposable fixture cleanup; retained evidence and documentation should remain as historical proof.

## Inventory method

The appendix is the tracked-file result of repository-wide Git search for the two UUIDs and the named fixture identities at baseline `202918b0568021eed54f55dcadc6bde1bf7af24d`. Each path is classified by owning area. Runtime source occurrences are fixture defaults or test-facing configuration; no current path is converted by this task.

## Exact tracked-file inventory

The mechanically generated inventory follows this heading.

| Path | Owning scenario | Classification |
| --- | --- | --- |
| `bruno/operator-console-access-evaluation/README.md` | Operator-access fixture | Tracked fixture/test/documentation; no change in this task. |
| `bruno/operator-console-access-evaluation/environments/local.bru` | Operator-access fixture | Tracked fixture/test/documentation; no change in this task. |
| `bruno/operator-console-session-lookup/README.md` | Operator Console test/UAT | Tracked fixture/test/documentation; no change in this task. |
| `bruno/operator-console-session-lookup/environments/local.bru` | Operator Console test/UAT | Tracked fixture/test/documentation; no change in this task. |
| `bruno/operator-console-statutory-discount-draft/environments/local.bru` | Statutory-discount/WebPay fixture | Tracked fixture/test/documentation; no change in this task. |
| `docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md` | Operator Console test/UAT | Tracked fixture/test/documentation; no change in this task. |
| `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md` | Statutory-discount/WebPay fixture | Tracked fixture/test/documentation; no change in this task. |
| `docs/operator-console/OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md` | Statutory-discount/WebPay fixture | Tracked fixture/test/documentation; no change in this task. |
| `docs/operator-console/statutory-discount-payable-basis-application-design.md` | Statutory-discount/WebPay fixture | Tracked fixture/test/documentation; no change in this task. |
| `docs/v1.3/central-pms/db-alignment/ExitPass_Central_PMS_DB_Alignment_With_exitpassdb_v1.2_Audit_v1.0.md` | Shared test/documentation | Historical alignment evidence; retain. |
| `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md` | Shared test/documentation | Controlled UAT reference; retain. |
| `docs/v1.3/management-platform/ExitPass_Management_Platform_UAT_User_Role_Permission_Seed_Result_v1.0.md` | Management Platform test/UAT | Retained evidence; no identity rewrite. |
| `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discount_Aligned_DB_UAT_Result_v1.0.md` | Statutory-discount fixture | Retained evidence; no identity rewrite. |
| `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discount_Review_Apply_UAT_Smoke_Result_v1.0.md` | Statutory-discount fixture | Retained evidence; no identity rewrite. |
| `docs/v1.3/webpay/runbooks/ExitPass_WebPay_Statutory_Discount_Local_Walkthrough_v1.0.md` | WebPay walkthrough | Deterministic isolated fixture; migrate only in a separate harness task. |
| `infra/db/fixtures/operator-console-access-evaluation/Seed-OperatorConsoleAccessEvaluationManualFixtures.sql` | Operator-access fixture | Disposable manual fixture; future migration must allocate a new scenario identity. |
| `scripts/dev-data/New-WebPayDailySeed.ps1` | WebPay fixture | Disposable dev-data generator; future migration must preserve isolation. |
| `scripts/dev-data/webpay-20260519-seed.sql` | WebPay fixture | Historical dev seed; retain. |
| `scripts/dev-data/webpay-20260521-seed.sql` | WebPay fixture | Historical dev seed; retain. |
| `scripts/dev-data/webpay-20260523-seed.sql` | WebPay fixture | Historical dev seed; retain. |
| `scripts/dev-data/webpay-20260524-seed.sql` | WebPay fixture | Historical dev seed; retain. |
| `scripts/hikcentral/Invoke-HikCentralVendorAckUat.ps1` | HikCentral UAT helper | Future realistic mapping candidate is PITX Level 3; change only after canonical seed approval. |
| `scripts/management-platform/Invoke-ManagementPlatformUatIdentityRbacPreflight.ps1` | Management Platform UAT | Fixture control; separate migration required. |
| `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql` | Management Platform UAT | Fixture validator; update with owning migration. |
| `scripts/operator-console/Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1` | Statutory-discount fixture | Separate migration required. |
| `scripts/operator-console/Invoke-StatutoryDiscountPilotPreflight.ps1` | Statutory-discount fixture | Separate migration required. |
| `scripts/operator-console/Seed-StatutoryDiscountPilotFixture.sql` | Statutory-discount fixture | Disposable fixture; never repurpose UUID in place. |
| `scripts/operator-console/Verify-StatutoryDiscountPilotFixture.sql` | Statutory-discount fixture | Update only with seed migration. |
| `scripts/v1.3/webpay/Seed-WebPayStatutoryDiscountWalkthrough.sql` | WebPay walkthrough | Deterministic isolated fixture; no change in this task. |
| `scripts/v1.3/webpay/Verify-WebPayStatutoryDiscountWalkthrough.sql` | WebPay walkthrough | Paired fixture validator; no change in this task. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/VendorParkingResolutionContractTests.cs` | Contract test | Synthetic identity remains appropriate unless contract fixture owner approves migration. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleAccessReadinessApiIntegrationTests.cs` | Operator-access fixture | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleDedicatedPolicyRegistryIntegrationTests.cs` | Operator Console test | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountAlignedDbUatApiIntegrationTests.cs` | Statutory-discount fixture | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs` | Statutory-discount fixture | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDraftApiIntegrationTests.cs` | Statutory-discount fixture | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs` | Statutory-discount fixture | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountLockedSchemaFixture.cs` | Statutory-discount fixture | Synthetic locked-schema fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountPolicyResolutionApiIntegrationTests.cs` | Statutory-discount fixture | Synthetic integration fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/ManagementPlatformStatutoryDiscountPolicyCoverageRepositoryIntegrationTests.cs` | Statutory-discount fixture | Synthetic persistence fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/ManagementPlatformIdentityRbacInventoryServiceTests.cs` | Management Platform test | Synthetic unit fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleAccessReadinessServiceTests.cs` | Operator-access fixture | Synthetic unit fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleStatutoryDiscountApplyPayableBasisServiceTests.cs` | Statutory-discount fixture | Synthetic unit fixture. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleStatutoryDiscountDecisionServiceTests.cs` | Statutory-discount fixture | Synthetic unit fixture. |
| `src/Services/GateIntegrationService/tests/ExitPass.GateIntegrationService.IntegrationTests/GateExit/GateAuthorizationConsumedProcessingInboxIntegrationTests.cs` | Shared integration test | Synthetic identity; historical test semantics retained. |
| `src/Services/ManagementPlatformUi/e2e/sales-invoice-profile-manage.spec.ts` | Management Platform test | Synthetic browser fixture. |
| `src/Services/ManagementPlatformUi/src/App.test.tsx` | Management Platform test | Synthetic UI fixture. |
| `src/Services/ManagementPlatformUi/src/auth.ts` | Management Platform local/test defaults | Review in a separate fixture ownership task. |
| `src/Services/OperatorConsoleUi/e2e/fixtures/operator-console-ordinance-browser-smoke-server.mjs` | Operator Console fixture | Synthetic browser fixture. |
| `src/Services/OperatorConsoleUi/src/App.test.tsx` | Operator Console test | Synthetic UI fixture. |
| `src/Services/OperatorConsoleUi/src/apiClient.ts` | Operator Console local/test defaults | Review in a separate fixture ownership task. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.ContractTests/WebPay/WebPayPaymentIntentContractTests.cs` | WebPay payment test | Synthetic contract fixture. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.IntegrationTests/WebPay/WebPayPaymentIntentEndpointIntegrationTests.cs` | WebPay payment test | Synthetic integration fixture. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.UnitTests/Application/UseCases/WebPayPaymentIntent/WebPayPaymentIntentHandlerTests.cs` | WebPay payment test | Synthetic unit fixture. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.UnitTests/Infrastructure/Integrations/CentralPmsWebPayClientTests.cs` | WebPay integration test | Synthetic unit fixture. |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.UnitTests/Infrastructure/Providers/PayMongo/PayMongoClientCheckoutRequestTests.cs` | Payment provider test | Synthetic unit fixture. |
| `src/Services/WebPayUi/src/App.test.tsx` | WebPay fixture | Synthetic UI fixture. |
| `src/Services/WebPayUi/src/webpay.test.ts` | WebPay fixture | Synthetic unit fixture. |

All 58 tracked paths are retained unchanged. The realistic catalog uses new UUIDv5 identifiers and cannot collide with or silently reinterpret these identities.
