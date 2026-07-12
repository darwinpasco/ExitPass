using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Proves the controlled Operator Console statutory discount validation chain end to end.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountE2EIntegrationTests
{
    private const string SessionLookupEndpoint = "/v1/ops/operator-console/sessions/lookup";
    private const string PolicyResolutionEndpoint = "/v1/ops/operator-console/statutory-discounts/resolve-policy";
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string DraftDetailEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/drafts/{0}?correlationId={1}";
    private const string EvidenceEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/evidence";
    private const string EvidenceListEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/evidence?correlationId={1}";
    private const string DecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/decision";
    private const string ApplyEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";

    private static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid DeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid ShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid VendorSystemId = Guid.Parse("77000000-0000-0000-0000-000000000004");
    private static readonly Guid ServiceIdentityId = Guid.Parse("77000000-0000-0000-0000-000000000003");
    private static readonly Guid PosServerSitePosServerId = Guid.Parse("10000000-0000-4000-8000-000000000201");
    private static readonly Guid PosServerFiscalDocumentTypeCodeId = Guid.Parse("10000000-0000-4000-8000-000000000103");
    private static readonly Guid PosServerFiscalDocumentStatusCodeId = Guid.Parse("10000000-0000-4000-8000-000000000107");
    private static readonly Guid PosServerFiscalLineTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000201");
    private static readonly Guid PosServerFiscalTenderTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000301");
    private static readonly Guid PosServerFiscalTaxTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000401");
    private static readonly Guid PosServerFiscalTaxClassificationCodeId = Guid.Parse("10000000-0000-0000-0000-000000000402");
    private static readonly Guid PosServerFiscalDiscountPrivilegeTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000501");
    private static readonly Guid PosServerFiscalTotalTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000601");

    private const string LocalLivePosSmokeEnabledEnvVar = "EXITPASS_RUN_STATUTORY_DISCOUNT_LIVE_POS_SMOKE";
    private const string LocalLivePosSmokeRunIdEnvVar = "EXITPASS_STATUTORY_DISCOUNT_LIVE_POS_SMOKE_RUN_ID";
    private const string LocalLivePosSmokeBaseUrlEnvVar = "EXITPASS_STATUTORY_DISCOUNT_LIVE_POS_BASE_URL";

    private static readonly Guid JurisdictionId = Guid.Parse("23100000-0000-0000-0000-000000000001");
    private const string E2ELguCode = "PH-INT-E2E-231";
    private static readonly Guid PolicyId = Guid.Parse("23100000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("23100000-0000-0000-0000-000000000003");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("23100000-0000-0000-0000-000000000004");

    /// <summary>
    /// Verifies lookup, policy resolution, evidence capture, approval, apply, and final read state as one controlled session.
    /// </summary>
    [Fact]
    public async Task EndToEnd_WhenOperatorCompletesRequiredEvidenceFlow_AppliesApprovedPayableBasis()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetE2EStateAsync();
        try
        {
            await InsertE2EPolicyFixtureAsync();
            await InsertParkingSessionAsync();
            await InsertBaseTariffSnapshotAsync();

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var beforeUnsafeSideEffectCount = await CountUnsafeSideEffectRecordsAsync();

            var lookup = await PostOkAsync<OperatorConsoleSessionLookupResponse>(
                client,
                SessionLookupEndpoint,
                SessionLookupRequest());
            lookup.AccessAllowed.Should().BeTrue();
            lookup.SessionFound.Should().BeTrue();
            lookup.SessionEligible.Should().BeTrue();
            lookup.ParkingSessionId.Should().Be(ParkingSessionId);
            lookup.CurrentPayableAmountMinorUnits.Should().Be(12500);
            lookup.DiscountStatus.Should().Be("NOT_APPLIED");

            var policy = await PostOkAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>(
                client,
                PolicyResolutionEndpoint,
                PolicyResolutionRequest());
            policy.AccessAllowed.Should().BeTrue();
            policy.PolicyResolved.Should().BeTrue();
            policy.StatutoryDiscountPolicyId.Should().Be(PolicyId);
            policy.PolicyCode.Should().Be("PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231");
            policy.RequiresEvidence.Should().BeTrue();

            var draft = await PostOkAsync<OperatorConsoleStatutoryDiscountDraftResponse>(
                client,
                DraftEndpoint,
                DraftRequest(evidenceCaptureRequested: true));
            draft.AccessAllowed.Should().BeTrue();
            draft.DraftAccepted.Should().BeTrue();
            draft.DraftPersisted.Should().BeTrue();
            draft.DraftId.Should().NotBeNull();
            draft.EvidenceRequired.Should().BeTrue();
            draft.PolicyCode.Should().Be("PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231");

            var draftId = draft.DraftId!.Value;
            var initialDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
                client,
                DraftDetailEndpoint(draftId));
            initialDetail.ValidationStatus.Should().Be("REQUESTED");
            initialDetail.EvidenceRequired.Should().BeTrue();
            initialDetail.EvidenceRequiredSatisfied.Should().BeFalse();
            initialDetail.RequiredEvidenceTypes.Should().ContainSingle().Which.Should().Be("SENIOR_CITIZEN_ID");
            initialDetail.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);

            var applyBeforeApproval = await PostOkAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(
                client,
                ApplyEndpoint(draftId),
                ApplyRequest());
            applyBeforeApproval.ApplicationAccepted.Should().BeFalse();
            applyBeforeApproval.ApplicationPersisted.Should().BeFalse();
            applyBeforeApproval.ErrorCode.Should().Be("STATUTORY_DISCOUNT_NOT_APPROVED");
            (await CountApplicationsAsync(draftId)).Should().Be(0);

            var blockedApproval = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
                client,
                DecisionEndpoint(draftId),
                DecisionRequest("APPROVE"));
            blockedApproval.DecisionAccepted.Should().BeFalse();
            blockedApproval.DecisionPersisted.Should().BeFalse();
            blockedApproval.ErrorCode.Should().Be("EVIDENCE_REQUIRED_NOT_CAPTURED");
            (await ReadDraftStatusAsync(draftId)).Should().Be("REQUESTED");

            using (var wrongEvidenceResponse = await client.PostAsJsonAsync(
                EvidenceEndpoint(draftId),
                EvidenceRequest("PWD_ID")))
            {
                wrongEvidenceResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var error = await wrongEvidenceResponse.Content.ReadFromJsonAsync<ErrorResponse>();
                error.Should().NotBeNull();
                error!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST");
            }

            var afterWrongEvidenceDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
                client,
                DraftDetailEndpoint(draftId));
            afterWrongEvidenceDetail.EvidenceRequiredSatisfied.Should().BeFalse();
            (await CountCapturedEvidenceAsync(draftId, "PWD_ID")).Should().Be(0);

            var evidence = await PostOkAsync<OperatorConsoleStatutoryDiscountEvidenceCaptureResponse>(
                client,
                EvidenceEndpoint(draftId),
                EvidenceRequest("SENIOR_CITIZEN_ID"));
            evidence.AccessAllowed.Should().BeTrue();
            evidence.EvidenceRequiredSatisfied.Should().BeTrue();
            evidence.VerificationStatus.Should().Be("CAPTURED");
            evidence.StorageReference.Should().Be("operator-confirmed");
            evidence.ReferenceNumberMasked.Should().BeNull();

            var evidenceList = await GetOkAsync<OperatorConsoleStatutoryDiscountEvidenceListResponse>(
                client,
                EvidenceListEndpoint(draftId));
            evidenceList.EvidenceRequired.Should().BeTrue();
            evidenceList.EvidenceRequiredSatisfied.Should().BeTrue();
            evidenceList.EvidenceCount.Should().BeGreaterThanOrEqualTo(1);
            evidenceList.LatestEvidenceStatus.Should().Be("CAPTURED");
            evidenceList.RequiredEvidenceTypes.Should().Contain("SENIOR_CITIZEN_ID");
            evidenceList.Items.Should().Contain(item =>
                item.EvidenceType == "SENIOR_CITIZEN_ID" &&
                item.CaptureMethod == "OPERATOR_CONFIRMED" &&
                item.VerificationStatus == "CAPTURED");

            var approved = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
                client,
                DecisionEndpoint(draftId),
                DecisionRequest("APPROVE"));
            approved.AccessAllowed.Should().BeTrue();
            approved.DecisionAccepted.Should().BeTrue();
            approved.DecisionPersisted.Should().BeTrue();
            approved.CurrentValidationStatus.Should().Be("APPROVED");

            var applied = await PostOkAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(
                client,
                ApplyEndpoint(draftId),
                ApplyRequest());
            applied.AccessAllowed.Should().BeTrue();
            applied.ApplicationAccepted.Should().BeTrue();
            applied.ApplicationPersisted.Should().BeTrue();
            applied.ApplicationStatus.Should().Be("APPLIED");
            applied.PayableBasisApplicationId.Should().NotBeNull();
            applied.StatutoryDiscountValidationId.Should().Be(draftId);
            applied.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
            applied.AppliedTariffSnapshotId.Should().NotBeNull();
            applied.StatutoryDiscountPolicyId.Should().Be(PolicyId);
            applied.PolicyCode.Should().Be("PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231");
            applied.PolicySnapshotUsed.Should().BeTrue();
            applied.GrossAmountMinorUnits.Should().Be(12500);
            applied.VatAmountMinorUnits.Should().Be(1339);
            applied.VatExclusiveAmountMinorUnits.Should().Be(11161);
            applied.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
            applied.FinalPayableAmountMinorUnits.Should().Be(8929);

            var finalDetail = await GetOkAsync<OperatorConsoleStatutoryDiscountDraftDetailResponse>(
                client,
                DraftDetailEndpoint(draftId));
            finalDetail.ValidationStatus.Should().Be("APPROVED");
            finalDetail.EvidenceRequiredSatisfied.Should().BeTrue();
            finalDetail.LatestEvidenceStatus.Should().Be("CAPTURED");
            finalDetail.PayableBasisApplicationId.Should().Be(applied.PayableBasisApplicationId);
            finalDetail.PayableBasisApplicationStatus.Should().Be("APPLIED");
            finalDetail.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
            finalDetail.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
            finalDetail.VatAmountMinorUnits.Should().Be(1339);
            finalDetail.VatExclusiveAmountMinorUnits.Should().Be(11161);
            finalDetail.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
            finalDetail.PayableAmountMinorUnits.Should().Be(8929);
            finalDetail.FinalPayableAmountMinorUnits.Should().Be(8929);

            (await CountApplicationsAsync(draftId)).Should().Be(1);

            var paymentAttempt = await CreatePaymentAttemptForAppliedBasisAsync(
                applied.AppliedTariffSnapshotId!.Value,
                $"operator-console-statutory-discount-e2e-payment-{Guid.NewGuid():N}");
            paymentAttempt.ParkingSessionId.Should().Be(ParkingSessionId);
            paymentAttempt.TariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId.Value);
            paymentAttempt.AttemptStatus.Should().Be("REQUESTED");

            var persistedAttempt = await ReadPaymentAttemptFinancialsAsync(paymentAttempt.PaymentAttemptId);
            persistedAttempt.Should().NotBeNull();
            persistedAttempt!.Amount.Should().Be(89.29m);
            persistedAttempt.CurrencyCode.Should().Be("PHP");
            persistedAttempt.GrossAmountSnapshot.Should().Be(125.00m);
            persistedAttempt.StatutoryDiscountSnapshot.Should().Be(22.32m);
            persistedAttempt.NetAmountSnapshot.Should().Be(89.29m);
            persistedAttempt.TariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId.Value);

            var paymentConfirmation = await RecordDiscountedPaymentConfirmationAsync(
                paymentAttempt.PaymentAttemptId,
                $"PCONF-STAT-DISCOUNT-{Guid.NewGuid():N}",
                AmountConfirmed: 89.29m);
            paymentConfirmation.PaymentAttemptId.Should().Be(paymentAttempt.PaymentAttemptId);

            var persistedConfirmation = await PaymentRoutineTestHelper.GetPaymentConfirmationByIdAsync(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString(),
                paymentConfirmation.PaymentConfirmationId);
            persistedConfirmation.Should().NotBeNull();
            persistedConfirmation!.AmountConfirmed.Should().Be(89.29m);
            persistedConfirmation.CurrencyCode.Trim().Should().Be("PHP");

            var fiscalReference = await PrepareFiscalIssuanceReferenceAsync(
                paymentAttempt.PaymentAttemptId,
                paymentConfirmation.PaymentConfirmationId,
                applied.AppliedTariffSnapshotId!.Value);
            fiscalReference.PaymentAttemptId.Should().Be(paymentAttempt.PaymentAttemptId);
            fiscalReference.PaymentConfirmationId.Should().Be(paymentConfirmation.PaymentConfirmationId);
            fiscalReference.TariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
            fiscalReference.PayableBasisRef.Should().Be(applied.AppliedTariffSnapshotId.Value.ToString("D"));

            var fiscalContext = BuildDiscountedFiscalContext(
                draftId,
                applied.PayableBasisApplicationId!.Value,
                applied.AppliedTariffSnapshotId.Value,
                paymentAttempt.PaymentAttemptId,
                paymentConfirmation.PaymentConfirmationId,
                fiscalReference);
            var posServerRequest = new PosServerFiscalDocumentRequestMapper().Map(fiscalContext);

            posServerRequest.PayableBasis.PayableAmountMinorUnits.Should().Be(8929);
            posServerRequest.Tenders.Should().ContainSingle().Which.AmountMinorUnits.Should().Be(8929);
            posServerRequest.DocumentLines.Should().ContainSingle().Which.Should().Match<PosServerFiscalDocumentLineRequest>(line =>
                line.GrossAmountMinorUnits == 11161 &&
                line.DiscountAmountMinorUnits == 2232 &&
                line.TaxAmountMinorUnits == 0 &&
                line.NetAmountMinorUnits == 8929);
            posServerRequest.TaxDetails.Should().ContainSingle().Which.Should().Match<PosServerFiscalTaxDetailRequest>(tax =>
                tax.TaxableAmountMinorUnits == 11161 &&
                tax.TaxAmountMinorUnits == 1339 &&
                tax.TaxRate == 12m);
            posServerRequest.PayableBasis.DiscountReferences.Should().ContainSingle().Which.Should()
                .Match<PosServerFiscalDiscountReferenceRequest>(discount =>
                    discount.DiscountValidationRef == draftId.ToString("D") &&
                    discount.Status == "approved" &&
                    discount.AppliesStatutoryDiscountTreatment);
            posServerRequest.DiscountPrivilegeDetails.Should().ContainSingle().Which.Should()
                .Match<PosServerFiscalDiscountPrivilegeDetailRequest>(discount =>
                    discount.BasisAmountMinorUnits == 11161 &&
                    discount.DiscountAmountMinorUnits == 2232 &&
                    discount.VatPrivilegeAmountMinorUnits == 1339 &&
                    discount.ApprovalRef == draftId.ToString("D"));
            posServerRequest.ReferenceContext.Should().Contain("payableBasisApplicationId", applied.PayableBasisApplicationId.Value.ToString("D"));
            posServerRequest.ReferenceContext.Should().Contain("statutoryDiscountValidationId", draftId.ToString("D"));
            posServerRequest.PayableBasis.ReferenceContext.Should().Contain("appliedTariffSnapshotId", applied.AppliedTariffSnapshotId.Value.ToString("D"));
            posServerRequest.PayableBasis.ReferenceContext.Should().Contain("entitlementType", "SENIOR_CITIZEN");

            var semanticHash = new FiscalSemanticRequestHashCalculator().Calculate(posServerRequest);
            semanticHash.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);

            AssertNoSensitiveEvidenceOrPii(posServerRequest);

            (await PaymentRoutineTestHelper.CountPaymentAttemptsForParkingSessionAsync(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString(),
                ParkingSessionId)).Should().Be(1);
            (await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString(),
                paymentAttempt.PaymentAttemptId)).Should().Be(1);
            (await CountFiscalIssuanceReferencesAsync()).Should().Be(1);
            var afterUnsafeSideEffectCount = await CountUnsafeSideEffectRecordsAsync();
            afterUnsafeSideEffectCount.Should().Be(beforeUnsafeSideEffectCount);
        }
        finally
        {
            await ResetE2EStateAsync();
        }
    }

    /// <summary>
    /// Opt-in local runtime proof for Central PMS live POS Server recording of the statutory discount fiscal request.
    /// </summary>
    [Fact]
    public async Task LocalRuntime_WhenEnabled_IssuesDiscountedSalesInvoiceThroughCentralPmsLivePosServer()
    {
        if (!IsLocalLivePosSmokeEnabled())
        {
            return;
        }

        if (!await CanOpenDatabaseAsync())
        {
            throw new InvalidOperationException(
                "The opt-in local live POS smoke requires the Central PMS integration database to be available.");
        }

        var posServerBaseUrl = Environment.GetEnvironmentVariable(LocalLivePosSmokeBaseUrlEnvVar)
            ?? "http://localhost:5000";
        var runId = Environment.GetEnvironmentVariable(LocalLivePosSmokeRunIdEnvVar)
            ?? DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var upstreamFinalityReference =
            $"STAT-DISCOUNT-CPS-LIVE-POS:{runId}:SENIOR_CITIZEN:001";
        var correlationId = Guid.NewGuid();

        await EnsurePosServerRuntimeAvailableAsync(posServerBaseUrl);
        await SeedManualFixtureAsync();
        await ResetE2EStateAsync();

        await InsertE2EPolicyFixtureAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();

        var beforeUnsafeSideEffectCount = await CountUnsafeSideEffectRecordsAsync();

        using var factory = new CustomWebApplicationFactory();
        using var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add(
            CentralPmsRbacPolicyCatalog.PermissionsHeaderName,
            "fiscal-issuance.status.read");

        using var posHttpClient = new HttpClient { BaseAddress = new Uri(posServerBaseUrl) };
        var posClient = new HttpPosServerFiscalDocumentClient(posHttpClient);
        var referenceRepository = new PostgresFiscalIssuanceReferenceRepository(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        var orchestrationService = new FiscalIssuanceOrchestrationService(referenceRepository);
        var liveIntegration = new FiscalIssuancePosServerLiveIntegrationService(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true,
                EnableControlledUatDiagnosticPath = true,
                PosServerBaseUrl = posServerBaseUrl,
                TimeoutSeconds = 10,
                EnableLiveFiscalIssuanceFromPaymentFlow = false,
                EnableLiveFiscalIssuanceFromExitFlow = false
            },
            new PosServerFiscalDocumentRequestMapper(),
            new FiscalSemanticRequestHashCalculator(),
            posClient,
            orchestrationService);

        var applied = await PrepareApprovedStatutoryDiscountApplicationAsync();
        var paymentAttempt = await CreatePaymentAttemptForAppliedBasisAsync(
            applied.AppliedTariffSnapshotId!.Value,
            $"operator-console-statutory-discount-live-pos-payment-{runId}");
        var paymentConfirmation = await RecordDiscountedPaymentConfirmationAsync(
            paymentAttempt.PaymentAttemptId,
            $"PCONF-STAT-DISCOUNT-LIVE-POS-{runId}",
            AmountConfirmed: 89.29m);
        var fiscalReference = await PrepareFiscalIssuanceReferenceAsync(
            paymentAttempt.PaymentAttemptId,
            paymentConfirmation.PaymentConfirmationId,
            applied.AppliedTariffSnapshotId.Value,
            upstreamFinalityReference);
        var fiscalContext = BuildDiscountedFiscalContext(
            applied.StatutoryDiscountValidationId!.Value,
            applied.PayableBasisApplicationId!.Value,
            applied.AppliedTariffSnapshotId.Value,
            paymentAttempt.PaymentAttemptId,
            paymentConfirmation.PaymentConfirmationId,
            fiscalReference);

        var firstResult = await liveIntegration.TryIssueFiscalDocumentViaPosServerAsync(
            fiscalReference.FiscalIssuanceReferenceId,
            fiscalContext,
            RecordingContext(fiscalReference, correlationId),
            CancellationToken.None);

        firstResult.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        firstResult.PosServerResult.Should().NotBeNull();
        firstResult.PosServerResult!.Succeeded.Should().BeTrue(
            $"{firstResult.PosServerResult.Code}: {firstResult.PosServerResult.Message}");
        firstResult.PosServerResult.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
        firstResult.FiscalIssuanceReference.Should().NotBeNull();
        firstResult.FiscalIssuanceReference!.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        firstResult.FiscalIssuanceReference.FiscalDocumentNumber.Should().NotBeNullOrWhiteSpace();
        firstResult.FiscalIssuanceReference.FiscalSequenceValue.Should().NotBeNull();

        var statusResponse = await apiClient.GetAsync(
            $"/v1/fiscal-issuance/references/{fiscalReference.FiscalIssuanceReferenceId:D}");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        statusBody.Should().Contain(firstResult.FiscalIssuanceReference.FiscalDocumentNumber!);
        statusBody.Should().Contain("FISCAL_ISSUANCE_RECORDED");
        statusBody.Should().Contain("NEWLY_CREATED");
        statusBody.Should().Contain("FISCAL_DOCUMENT_NUMBER_ASSIGNED");
        statusBody.Should().Contain("AVAILABLE");

        var posRead = await posClient.GetFiscalDocumentAsync(
            firstResult.PosServerResult.FiscalDocumentId!.Value,
            CancellationToken.None);
        posRead.Succeeded.Should().BeTrue();
        posRead.FiscalDocumentId.Should().Be(firstResult.PosServerResult.FiscalDocumentId);
        posRead.FiscalDocumentNumber.Should().Be(firstResult.FiscalIssuanceReference.FiscalDocumentNumber);
        posRead.FiscalSequenceValue.Should().Be(firstResult.FiscalIssuanceReference.FiscalSequenceValue);
        posRead.FiscalDocumentStatusCodeKey.Should().BeOneOf("issued", "central_pms_uat_created");

        using var opsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/ops/operator-console/fiscal-issuance/references/{fiscalReference.FiscalIssuanceReferenceId:D}");
        AddOperatorHeaders(opsRequest);
        opsRequest.Headers.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "fiscal-issuance.status.read");
        using var opsResponse = await apiClient.SendAsync(opsRequest);
        opsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var opsBody = await opsResponse.Content.ReadAsStringAsync();
        opsBody.Should().Contain(firstResult.FiscalIssuanceReference.FiscalDocumentNumber!);

        var replayResult = await liveIntegration.TryIssueFiscalDocumentViaPosServerAsync(
            fiscalReference.FiscalIssuanceReferenceId,
            fiscalContext,
            RecordingContext(fiscalReference, correlationId),
            CancellationToken.None);
        replayResult.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        replayResult.PosServerResult!.ResultClassification.Should().Be(FiscalIssuanceResultClassification.IdempotentReplay);
        replayResult.PosServerResult.FiscalDocumentId.Should().Be(firstResult.PosServerResult.FiscalDocumentId);
        replayResult.PosServerResult.FiscalDocumentNumber.Should().Be(firstResult.PosServerResult.FiscalDocumentNumber);
        replayResult.PosServerResult.FiscalSequenceValue.Should().Be(firstResult.PosServerResult.FiscalSequenceValue);

        var conflictContext = fiscalContext with
        {
            TaxDetails =
            [
                fiscalContext.TaxDetails[0] with
                {
                    TaxAmountMinorUnits = 1340,
                    TaxContext = new Dictionary<string, string>
                    {
                        ["basis"] = "VAT_EXCLUSIVE",
                        ["conflictProbe"] = "changed_tax_amount"
                    }
                }
            ]
        };
        var conflictResult = await liveIntegration.TryIssueFiscalDocumentViaPosServerAsync(
            fiscalReference.FiscalIssuanceReferenceId,
            conflictContext,
            RecordingContext(fiscalReference, correlationId),
            CancellationToken.None);
        conflictResult.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        conflictResult.PosServerResult!.Outcome.Should().Be(PosServerFiscalDocumentOutcome.Conflict);
        conflictResult.PosServerResult.Succeeded.Should().BeFalse();
        conflictResult.PosServerResult.Code.Should().Be("fiscal_document_idempotency_conflict");
        conflictResult.PosServerResult.FiscalDocumentNumber.Should().BeNull();
        conflictResult.FiscalIssuanceReference!.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceConflict);

        var restoredReplayResult = await liveIntegration.TryIssueFiscalDocumentViaPosServerAsync(
            fiscalReference.FiscalIssuanceReferenceId,
            fiscalContext,
            RecordingContext(fiscalReference, correlationId),
            CancellationToken.None);
        restoredReplayResult.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        restoredReplayResult.PosServerResult!.ResultClassification.Should().Be(FiscalIssuanceResultClassification.IdempotentReplay);
        restoredReplayResult.PosServerResult.FiscalDocumentId.Should().Be(firstResult.PosServerResult.FiscalDocumentId);
        restoredReplayResult.PosServerResult.FiscalDocumentNumber.Should().Be(firstResult.PosServerResult.FiscalDocumentNumber);
        restoredReplayResult.PosServerResult.FiscalSequenceValue.Should().Be(firstResult.PosServerResult.FiscalSequenceValue);
        restoredReplayResult.FiscalIssuanceReference!.FiscalIssuanceState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed);

        var afterConflictRead = await posClient.GetFiscalDocumentAsync(
            firstResult.PosServerResult.FiscalDocumentId!.Value,
            CancellationToken.None);
        afterConflictRead.Succeeded.Should().BeTrue();
        afterConflictRead.FiscalDocumentNumber.Should().Be(firstResult.PosServerResult.FiscalDocumentNumber);
        afterConflictRead.FiscalSequenceValue.Should().Be(firstResult.PosServerResult.FiscalSequenceValue);

        var afterUnsafeSideEffectCount = await CountUnsafeSideEffectRecordsAsync();
        afterUnsafeSideEffectCount.Should().Be(beforeUnsafeSideEffectCount);

        Console.WriteLine(
            "STATUTORY_DISCOUNT_LIVE_POS_SMOKE " +
            $"runId={runId} " +
            $"fiscalIssuanceReferenceId={fiscalReference.FiscalIssuanceReferenceId:D} " +
            $"posServerFiscalDocumentId={firstResult.PosServerResult.FiscalDocumentId:D} " +
            $"salesInvoiceNumber={firstResult.PosServerResult.FiscalDocumentNumber} " +
            $"fiscalSequenceValue={firstResult.PosServerResult.FiscalSequenceValue} " +
            $"first={firstResult.PosServerResult.ResultClassification} " +
            $"replay={replayResult.PosServerResult.ResultClassification} " +
            $"conflict={conflictResult.PosServerResult.Code} " +
            $"restoredReplay={restoredReplayResult.PosServerResult.ResultClassification}");
    }

    private static OperatorConsoleSessionLookupRequest SessionLookupRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "E2E-231-SESSION-001",
            PlateNumber: null,
            "PARKING_SESSION_ID",
            $"operator-console-statutory-discount-e2e-lookup-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountPolicyResolutionRequest PolicyResolutionRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            $"operator-console-statutory-discount-e2e-policy-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest(bool evidenceCaptureRequested) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "E2E-231-SESSION-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "SC-UAT-****-0001",
            EntitlementFingerprint: null,
            evidenceCaptureRequested,
            evidenceCaptureRequested ? "SUPERVISOR_REVIEW" : null,
            OperatorAttestation: true,
            AttestationNotes: "Controlled E2E statutory discount validation session.",
            ReasonCode: "INTEGRATION_E2E_231",
            $"operator-console-statutory-discount-e2e-draft-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureRequest EvidenceRequest(string evidenceType) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            evidenceType,
            "OPERATOR_CONFIRMED",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            StorageReference: null,
            ReferenceNumber: null,
            Notes: "Controlled E2E metadata-only evidence capture.",
            OperatorConfirmation: true,
            $"operator-console-statutory-discount-e2e-evidence-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDecisionRequest DecisionRequest(string decision) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            DecisionReasonCode: null,
            DecisionNotes: "Controlled E2E statutory discount validation decision.",
            ReviewerAttestation: true,
            $"operator-console-statutory-discount-e2e-decision-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisRequest ApplyRequest() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            OriginalTariffSnapshotId,
            $"operator-console-statutory-discount-e2e-apply-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static string DraftDetailEndpoint(Guid draftId) =>
        string.Format(DraftDetailEndpointTemplate, draftId, Guid.NewGuid());

    private static string EvidenceEndpoint(Guid draftId) =>
        string.Format(EvidenceEndpointTemplate, draftId);

    private static string EvidenceListEndpoint(Guid draftId) =>
        string.Format(EvidenceListEndpointTemplate, draftId, Guid.NewGuid());

    private static string DecisionEndpoint(Guid draftId) =>
        string.Format(DecisionEndpointTemplate, draftId);

    private static string ApplyEndpoint(Guid draftId) =>
        string.Format(ApplyEndpointTemplate, draftId);

    private static async Task<T> PostOkAsync<T>(HttpClient client, string endpoint, object body)
    {
        using var response = await client.PostAsJsonAsync(endpoint, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var parsed = await response.Content.ReadFromJsonAsync<T>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static async Task<T> GetOkAsync<T>(HttpClient client, string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddOperatorHeaders(request);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var parsed = await response.Content.ReadFromJsonAsync<T>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static void AddOperatorHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Operator-User-Id", UserId.ToString());
        request.Headers.Add("X-Operator-Device-Binding-Id", DeviceBindingId.ToString());
        request.Headers.Add("X-Operator-Shift-Id", ShiftId.ToString());
        request.Headers.Add("X-Site-Id", SiteId.ToString());
        request.Headers.Add("X-Site-Group-Id", SiteGroupId.ToString());
    }

    private static bool IsLocalLivePosSmokeEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(LocalLivePosSmokeEnabledEnvVar),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static async Task EnsurePosServerRuntimeAvailableAsync(string posServerBaseUrl)
    {
        using var client = new HttpClient { BaseAddress = new Uri(posServerBaseUrl) };
        using var response = await client.GetAsync("/v1/fiscal-documents/00000000-0000-0000-0000-000000000000");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static PosServerCreateResultRecordingContext RecordingContext(
        FiscalIssuanceReferenceRecord fiscalReference,
        Guid correlationId) =>
        new(
            UpstreamFinalityReference: fiscalReference.UpstreamFinalityReference,
            SitePosServerId: fiscalReference.SitePosServerId,
            FiscalDocumentTypeCodeId: fiscalReference.FiscalDocumentTypeCodeId,
            CorrelationId: correlationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow,
            ServiceIdentityId: ServiceIdentityId);

    private static async Task<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse> PrepareApprovedStatutoryDiscountApplicationAsync()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var policy = await PostOkAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>(
            client,
            PolicyResolutionEndpoint,
            PolicyResolutionRequest());
        policy.PolicyResolved.Should().BeTrue();

        var draft = await PostOkAsync<OperatorConsoleStatutoryDiscountDraftResponse>(
            client,
            DraftEndpoint,
            DraftRequest(evidenceCaptureRequested: true));
        draft.DraftAccepted.Should().BeTrue();
        draft.DraftId.Should().NotBeNull();

        var draftId = draft.DraftId!.Value;
        var evidence = await PostOkAsync<OperatorConsoleStatutoryDiscountEvidenceCaptureResponse>(
            client,
            EvidenceEndpoint(draftId),
            EvidenceRequest("SENIOR_CITIZEN_ID"));
        evidence.EvidenceRequiredSatisfied.Should().BeTrue();

        var approved = await PostOkAsync<OperatorConsoleStatutoryDiscountDecisionResponse>(
            client,
            DecisionEndpoint(draftId),
            DecisionRequest("APPROVE"));
        approved.DecisionAccepted.Should().BeTrue();

        var applied = await PostOkAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(
            client,
            ApplyEndpoint(draftId),
            ApplyRequest());
        applied.ApplicationAccepted.Should().BeTrue();
        applied.ApplicationPersisted.Should().BeTrue();
        applied.PayableBasisApplicationId.Should().NotBeNull();
        applied.AppliedTariffSnapshotId.Should().NotBeNull();
        applied.FinalPayableAmountMinorUnits.Should().Be(8929);

        return applied;
    }

    private static async Task SeedManualFixtureAsync()
    {
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
    }

    private static async Task ResetE2EStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM gates.gate_authorization_consumptions gac
            USING core.exit_authorizations ea
            WHERE gac.exit_authorization_id = ea.exit_authorization_id
              AND ea.parking_session_id = @parking_session_id;

            DELETE FROM core.exit_authorizations
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.fiscal_issuance_references
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.payment_confirmations pc
            USING core.payment_attempts pa
            WHERE pc.payment_attempt_id = pa.payment_attempt_id
              AND pa.parking_session_id = @parking_session_id;

            DELETE FROM payments.provider_outcomes po
            USING core.payment_attempts pa
            WHERE po.payment_attempt_id = pa.payment_attempt_id
              AND pa.parking_session_id = @parking_session_id;

            DELETE FROM reconciliation.reconciliation_items ri
            USING core.payment_attempts pa
            WHERE ri.payment_attempt_id = pa.payment_attempt_id
              AND pa.parking_session_id = @parking_session_id;

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM coupons.coupon_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
               SET superseded_by_tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_policy_references
            WHERE policy_code = 'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231';

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE policy_code = 'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231';

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertE2EPolicyFixtureAsync()
    {
        const string sql = """
            BEGIN;

            UPDATE sites.sites
               SET lgu_code = @lgu_code,
                   updated_at = now()
             WHERE site_id = @site_id;

            INSERT INTO discounts.discount_policy_references (
                discount_policy_reference_id,
                policy_code,
                policy_name,
                policy_description,
                policy_type,
                policy_level,
                entitlement_type,
                local_ordinance_reference,
                lgu_code,
                site_id,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                effective_from,
                policy_status
            )
            VALUES (
                @policy_id,
                'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231',
                'ATC Senior Citizen Site Policy 231',
                'Senior Citizen site policy requiring metadata-only evidence.',
                'SITE_POLICY',
                'SITE_POLICY',
                'SENIOR_CITIZEN',
                'ATC-ORD-231',
                @lgu_code,
                @site_id,
                0,
                'policy-v1',
                true,
                true,
                now() - interval '1 day',
                'ACTIVE'
            );

            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_registry_id,
                policy_code,
                policy_name,
                policy_description,
                entitlement_type,
                policy_status,
                verification_status,
                policy_level,
                policy_type,
                policy_resolution_basis,
                benefit_type,
                discount_base_scope,
                jurisdiction_code,
                jurisdiction_name,
                site_group_id,
                site_id,
                beneficiary_residency_scope,
                facility_scope,
                requires_evidence,
                required_evidence_type,
                requires_operator_validation,
                legal_basis_reference,
                ordinance_reference,
                source_reference,
                reviewed_by,
                reviewed_at,
                approved_by,
                approved_at,
                effective_from,
                effective_to,
                notes,
                correlation_id
            )
            VALUES (
                @policy_id,
                'PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231',
                'ATC Senior Citizen Site Policy 231',
                'Senior Citizen site policy requiring metadata-only evidence.',
                'SENIOR_CITIZEN'::discounts.statutory_entitlement_type_enum,
                'ACTIVE'::discounts.discount_policy_status_enum,
                'ACTIVE_APPROVED'::discounts.policy_verification_status_enum,
                'SITE_POLICY'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'SITE_POLICY_OPERATIONAL_ONLY'::discounts.policy_resolution_basis_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                @lgu_code,
                'ATC Jurisdiction',
                @site_group_id,
                @site_id,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                'ATC parking facility.',
                true,
                'SENIOR_CITIZEN_ID'::discounts.discount_evidence_type_enum,
                true,
                'ATC-ORD-231',
                'ATC-ORD-231',
                'policy-v1',
                'policy-reviewer-231',
                now() - interval '2 days',
                'policy-approver-231',
                now() - interval '1 day',
                now() - interval '1 day',
                NULL,
                'Senior Citizen site policy requiring evidence capture.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                requires_evidence = EXCLUDED.requires_evidence,
                required_evidence_type = EXCLUDED.required_evidence_type,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                approved_by = EXCLUDED.approved_by,
                approved_at = EXCLUDED.approved_at,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_at = now();

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = E2ELguCode;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = SiteGroupId;
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = PolicyId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertParkingSessionAsync()
    {
        const string sql = """
            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                vendor_session_ref,
                plate_number_hash,
                plate_number_masked,
                ticket_number_hash,
                ticket_number_masked,
                entry_at,
                vendor_session_status,
                session_status,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_id,
                'E2E-231-SESSION-001',
                '2312312312312312312312312312312312312312312312312312312312312312',
                'E2E-231',
                'd6f5f9ecab9492c63d3dd2795db3f74d14fd2f071b7fc27a9c9d8fa6d341f199',
                'E2E-231-SESSION-001',
                '2026-05-29T00:00:00Z',
                'ACTIVE',
                'ACTIVE',
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = SiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = SiteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = VendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertBaseTariffSnapshotAsync()
    {
        const string sql = """
            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                snapshot_status,
                calculated_at,
                expires_at,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @tariff_snapshot_id,
                @parking_session_id,
                @vendor_system_id,
                'INTEGRATION-OPERATOR-CONSOLE-E2E-231',
                'ATC-POLICY-V1',
                'PHP',
                125.00,
                0,
                0,
                125.00,
                'ACTIVE'::core.tariff_snapshot_status_enum,
                now(),
                now() + interval '1 hour',
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = OriginalTariffSnapshotId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = VendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = ServiceIdentityId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadDraftStatusAsync(Guid draftId)
    {
        const string sql = """
            SELECT validation_status::text
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task<int> CountApplicationsAsync(Guid validationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountCapturedEvidenceAsync(Guid draftId, string evidenceType)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND evidence_capture_status = 'CAPTURED'::discounts.evidence_capture_status_enum
              AND purged_at IS NULL;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<PaymentAttemptFinancials?> ReadPaymentAttemptFinancialsAsync(Guid paymentAttemptId)
    {
        const string sql = """
            SELECT
                pa.payment_attempt_id,
                pa.parking_session_id,
                pa.tariff_snapshot_id,
                pa.amount,
                pa.currency_code::text AS currency_code,
                ts.gross_amount,
                ts.statutory_discount_amount,
                ts.net_amount
            FROM core.payment_attempts AS pa
            JOIN core.tariff_snapshots AS ts
                ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
            WHERE pa.payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentAttemptFinancials(
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetDecimal(reader.GetOrdinal("amount")),
            reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
            reader.GetDecimal(reader.GetOrdinal("gross_amount")),
            reader.GetDecimal(reader.GetOrdinal("statutory_discount_amount")),
            reader.GetDecimal(reader.GetOrdinal("net_amount")));
    }

    private static async Task<PaymentRoutineTestHelper.CreateAttemptResult> CreatePaymentAttemptForAppliedBasisAsync(
        Guid appliedTariffSnapshotId,
        string idempotencyKey)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                attempt_status,
                payment_provider_code
            FROM core.create_or_reuse_payment_attempt(
                @p_parking_session_id,
                @p_tariff_snapshot_id,
                @p_payment_provider_code,
                @p_idempotency_key,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("p_parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        command.Parameters.Add("p_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = appliedTariffSnapshotId;
        command.Parameters.Add("p_payment_provider_code", NpgsqlDbType.Text).Value = "GCASH";
        command.Parameters.Add("p_idempotency_key", NpgsqlDbType.Text).Value = idempotencyKey;
        command.Parameters.Add("p_requested_by", NpgsqlDbType.Text).Value = "statutory-discount-payment-sales-invoice-proof";
        command.Parameters.Add("p_correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("p_now", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.UtcNow;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        return new PaymentRoutineTestHelper.CreateAttemptResult(
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("attempt_status")),
            reader.GetString(reader.GetOrdinal("payment_provider_code")));
    }

    private static async Task<RecordPaymentConfirmationResult> RecordDiscountedPaymentConfirmationAsync(
        Guid paymentAttemptId,
        string providerReference,
        decimal AmountConfirmed)
    {
        var service = new RecordPaymentConfirmationService(
            new RecordPaymentConfirmationGateway(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString()));

        return await service.ExecuteAsync(
            new RecordPaymentConfirmationCommand(
                paymentAttemptId,
                providerReference,
                "SUCCESS",
                "statutory-discount-payment-sales-invoice-proof",
                RawCallbackReference: null,
                ProviderSignatureValid: true,
                ProviderPayloadHash: null,
                AmountConfirmed,
                "PHP",
                Guid.NewGuid()),
            CancellationToken.None);
    }

    private static async Task<FiscalIssuanceReferenceRecord> PrepareFiscalIssuanceReferenceAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid appliedTariffSnapshotId,
        string? upstreamFinalityReference = null)
    {
        var service = new FiscalIssuanceOrchestrationService(
            new PostgresFiscalIssuanceReferenceRepository(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString()));

        return await service.PreparePendingAsync(
            new PrepareFiscalIssuanceCommand(
                PaymentConfirmationId: paymentConfirmationId,
                PaymentAttemptId: paymentAttemptId,
                ParkingSessionId: ParkingSessionId,
                TariffSnapshotId: appliedTariffSnapshotId,
                SiteId: SiteId,
                SitePosServerId: PosServerSitePosServerId,
                SitePosServerRef: "DEV-POS-SERVER-ATC-001",
                FiscalDocumentTypeCodeId: PosServerFiscalDocumentTypeCodeId,
                FiscalDocumentTypeCodeKey: "sales_invoice",
                PayableBasisRef: appliedTariffSnapshotId.ToString("D"),
                UpstreamFinalityReference: upstreamFinalityReference ?? $"STAT-DISCOUNT-E2E:{paymentConfirmationId:D}:sales_invoice",
                CorrelationId: Guid.NewGuid(),
                ServiceIdentityId: ServiceIdentityId),
            CancellationToken.None);
    }

    private static CentralPmsFiscalDocumentMappingContext BuildDiscountedFiscalContext(
        Guid validationId,
        Guid payableBasisApplicationId,
        Guid appliedTariffSnapshotId,
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        FiscalIssuanceReferenceRecord fiscalReference) =>
        new(
            SitePosServerId: PosServerSitePosServerId,
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            FiscalDocumentTypeCodeId: PosServerFiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: "sales_invoice",
            FiscalDocumentStatusCodeId: PosServerFiscalDocumentStatusCodeId,
            BusinessDayDate: new DateOnly(2026, 7, 11),
            CentralPmsParkingSessionRef: ParkingSessionId.ToString("D"),
            CentralPmsPaymentAttemptRef: paymentAttemptId.ToString("D"),
            CentralPmsPaymentConfirmationRef: paymentConfirmationId.ToString("D"),
            PayableBasis: new CentralPmsPayableBasisContext(
                PayableBasisRef: appliedTariffSnapshotId.ToString("D"),
                UpstreamFinalityRef: fiscalReference.UpstreamFinalityReference,
                CurrencyCode: "PHP",
                PayableAmountMinorUnits: 8929,
                DiscountReferences:
                [
                    new CentralPmsFiscalDiscountReferenceContext(
                        DiscountValidationRef: validationId.ToString("D"),
                        Status: "approved",
                        AppliesStatutoryDiscountTreatment: true,
                        ReferenceContext: new Dictionary<string, string>
                        {
                            ["payableBasisApplicationId"] = payableBasisApplicationId.ToString("D"),
                            ["entitlementType"] = "SENIOR_CITIZEN",
                            ["policyCode"] = "PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231"
                        })
                ],
                ReferenceContext: new Dictionary<string, string>
                {
                    ["appliedTariffSnapshotId"] = appliedTariffSnapshotId.ToString("D"),
                    ["originalTariffSnapshotId"] = OriginalTariffSnapshotId.ToString("D"),
                    ["payableBasisApplicationId"] = payableBasisApplicationId.ToString("D"),
                    ["entitlementType"] = "SENIOR_CITIZEN"
                }),
            DocumentLines:
            [
                new CentralPmsFiscalDocumentLineContext(
                    LineSequence: 1,
                    LineTypeCodeId: PosServerFiscalLineTypeCodeId,
                    Description: "Parking fee - statutory discount applied",
                    Quantity: 1m,
                    UnitAmountMinorUnits: 11161,
                    GrossAmountMinorUnits: 11161,
                    DiscountAmountMinorUnits: 2232,
                    TaxAmountMinorUnits: 0,
                    NetAmountMinorUnits: 8929,
                    CurrencyCode: "PHP",
                    LineStatusCodeId: null,
                    SourceRef: appliedTariffSnapshotId.ToString("D"),
                    LineContext: new Dictionary<string, string>
                    {
                        ["source"] = "central-pms-applied-payable-basis",
                        ["entitlementType"] = "SENIOR_CITIZEN",
                        ["originalGrossAmountMinorUnits"] = "12500",
                        ["vatAmountMinorUnits"] = "1339",
                        ["vatPrivilegeAmountMinorUnits"] = "1339"
                    })
            ],
            Tenders:
            [
                new CentralPmsFiscalTenderContext(
                    TenderTypeCodeId: PosServerFiscalTenderTypeCodeId,
                    AmountMinorUnits: 8929,
                    CurrencyCode: "PHP",
                    CentralPmsPaymentAttemptRef: paymentAttemptId.ToString("D"),
                    CentralPmsPaymentConfirmationRef: paymentConfirmationId.ToString("D"),
                    PaymentFinalityRef: paymentConfirmationId.ToString("D"),
                    ProviderRef: "statutory-discount-proof-provider-ref",
                    TenderContext: new Dictionary<string, string> { ["paymentProvider"] = "GCASH" })
            ],
            TaxDetails:
            [
                new CentralPmsFiscalTaxDetailContext(
                    TaxTypeCodeId: PosServerFiscalTaxTypeCodeId,
                    TaxClassificationCodeId: PosServerFiscalTaxClassificationCodeId,
                    TaxableAmountMinorUnits: 11161,
                    TaxAmountMinorUnits: 1339,
                    CurrencyCode: "PHP",
                    LineSequence: 1,
                    TaxRate: 12m,
                    TaxContext: new Dictionary<string, string> { ["basis"] = "VAT_EXCLUSIVE" })
            ],
            DiscountPrivilegeDetails:
            [
                new CentralPmsFiscalDiscountPrivilegeDetailContext(
                    DiscountPrivilegeTypeCodeId: PosServerFiscalDiscountPrivilegeTypeCodeId,
                    BasisAmountMinorUnits: 11161,
                    DiscountAmountMinorUnits: 2232,
                    VatPrivilegeAmountMinorUnits: 1339,
                    CurrencyCode: "PHP",
                    LineSequence: 1,
                    BeneficiaryRef: "metadata-only-beneficiary-ref",
                    EvidenceRef: "metadata-only-evidence-captured",
                    ApprovalRef: validationId.ToString("D"),
                    DiscountPrivilegeContext: new Dictionary<string, string>
                    {
                        ["entitlementType"] = "SENIOR_CITIZEN",
                        ["discountBaseScope"] = "VAT_EXCLUSIVE",
                        ["discountRateBasisPoints"] = "2000",
                        ["roundingMode"] = "HALF_AWAY_FROM_ZERO",
                        ["payableBasisApplicationId"] = payableBasisApplicationId.ToString("D")
                    })
            ],
            Totals:
            [
                new CentralPmsFiscalTotalContext(
                    TotalTypeCodeId: PosServerFiscalTotalTypeCodeId,
                    AmountMinorUnits: 8929,
                    CurrencyCode: "PHP",
                    TotalContext: new Dictionary<string, string> { ["kind"] = "final_payable" })
            ],
            ReferenceContext: new Dictionary<string, string>
            {
                ["statutoryDiscountValidationId"] = validationId.ToString("D"),
                ["payableBasisApplicationId"] = payableBasisApplicationId.ToString("D"),
                ["appliedTariffSnapshotId"] = appliedTariffSnapshotId.ToString("D"),
                ["fiscalIssuanceReferenceId"] = fiscalReference.FiscalIssuanceReferenceId.ToString("D")
            },
            PaymentFinalityRef: paymentConfirmationId.ToString("D"),
            VendorAckRef: null);

    private static void AssertNoSensitiveEvidenceOrPii(PosServerFiscalDocumentCreateRequest request)
    {
        var serialized = System.Text.Json.JsonSerializer.Serialize(request).ToLowerInvariant();
        serialized.Should().NotContain("raw_payload");
        serialized.Should().NotContain("callback_payload");
        serialized.Should().NotContain("entitlement_evidence_image");
        serialized.Should().NotContain("base64");
        serialized.Should().NotContain("1234");
        serialized.Should().NotContain("osca");
    }

    private static async Task<int> CountFiscalIssuanceReferencesAsync()
    {
        const string sql = """
            SELECT COUNT(*)::int
            FROM core.fiscal_issuance_references
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountUnsafeSideEffectRecordsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM gates.gate_authorization_consumptions gac
                   JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
                  WHERE ea.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM coupons.coupon_applications WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM payments.provider_outcomes po
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM reconciliation.reconciliation_items ri
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = ri.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id) AS boundary_count;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = ParkingSessionId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record PaymentAttemptFinancials(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        decimal Amount,
        string CurrencyCode,
        decimal GrossAmountSnapshot,
        decimal StatutoryDiscountSnapshot,
        decimal NetAmountSnapshot);

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<bool> CanOpenDatabaseAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }
}
