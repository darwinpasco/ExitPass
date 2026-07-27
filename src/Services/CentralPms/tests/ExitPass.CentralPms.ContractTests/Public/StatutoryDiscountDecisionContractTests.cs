using System.Text.Json;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Public;

/// <summary>
/// Verifies the shared statutory-discount decision/readback API contract shape.
/// </summary>
public sealed class StatutoryDiscountDecisionContractTests
{
    /// <summary>
    /// Verifies additive channel-safe readback fields serialize with stable JSON names.
    /// </summary>
    [Fact]
    public void StatutoryDiscountDecisionResponse_includes_channel_safe_site_vat_and_readiness_fields()
    {
        var response = Response();

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("siteId", out _).Should().BeTrue();
        root.TryGetProperty("siteGroupId", out _).Should().BeTrue();
        root.TryGetProperty("vatExclusiveBasisAmountMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("vatAmountMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("vatTreatment", out _).Should().BeTrue();
        root.TryGetProperty("payableBasisReady", out _).Should().BeTrue();
        root.TryGetProperty("payableBasisReadinessStatus", out _).Should().BeTrue();
        root.TryGetProperty("payableBasisReadinessAction", out _).Should().BeTrue();

        root.GetProperty("vatExclusiveBasisAmountMinorUnits").GetInt64().Should().Be(8929);
        root.GetProperty("vatAmountMinorUnits").GetInt64().Should().Be(1071);
        root.GetProperty("payableBasisReady").GetBoolean().Should().BeTrue();
        root.GetProperty("payableBasisReadinessStatus").GetString().Should().Be("PAYABLE_BASIS_READY");

        root.TryGetProperty("reviewerUserId", out _).Should().BeFalse();
        root.TryGetProperty("reviewerAttestation", out _).Should().BeFalse();
        root.TryGetProperty("operatorDeviceBindingId", out _).Should().BeFalse();
        root.TryGetProperty("operatorShiftId", out _).Should().BeFalse();
        root.TryGetProperty("rawEvidence", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies unavailable historical values remain nullable and do not become zero or ready.
    /// </summary>
    [Fact]
    public void StatutoryDiscountDecisionResponse_preserves_nullable_historical_readback_posture()
    {
        var response = Response(
            includeSiteContext: false,
            vatExclusiveBasisAmountMinorUnits: null,
            vatAmountMinorUnits: null,
            payableBasisReady: false,
            payableBasisReadinessStatus: "REQUIRED_FACTS_UNAVAILABLE");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));
        var root = document.RootElement;

        root.GetProperty("siteId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("siteGroupId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("vatExclusiveBasisAmountMinorUnits").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("vatAmountMinorUnits").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("payableBasisReady").GetBoolean().Should().BeFalse();
        root.GetProperty("payableBasisReadinessStatus").GetString().Should().Be("REQUIRED_FACTS_UNAVAILABLE");
    }

    private static StatutoryDiscountDecisionResponse Response(
        bool includeSiteContext = true,
        long? vatExclusiveBasisAmountMinorUnits = 8929,
        long? vatAmountMinorUnits = 1071,
        bool payableBasisReady = true,
        string payableBasisReadinessStatus = "PAYABLE_BASIS_READY") =>
        new(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"),
            "WEBPAY",
            "SENIOR_CITIZEN",
            "APPLIED_PAYABLE_BASIS",
            "NATIONAL_LAW_FALLBACK",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"),
            FallbackPolicyReferenceId: null,
            LocalOrdinanceApplied: false,
            GrossAmountMinorUnits: 10000,
            StatutoryDiscountAmountMinorUnits: 1786,
            NetPayableAmountMinorUnits: 8214,
            Currency: "PHP",
            EvidenceRequired: true,
            EvidenceRecorded: true,
            ReasonCode: "ELIGIBLE",
            ErrorCode: null,
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"),
            DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-27T00:01:00Z"),
            DateTimeOffset.Parse("2026-07-27T00:02:00Z"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000008"),
            "COMPLETED",
            "APPROVED",
            "DECISION_AND_APPLICATION_COMPLETED",
            "statutory-discount-decision:sha256:v2",
            Retryable: false,
            RecoveryClassification: "READ_CANONICAL_RESULT",
            RecoveryAction: "READ_CANONICAL_DECISION",
            SafeErrorCode: null,
            DecisionCommandStatus: "COMPLETED",
            DecisionResultStatus: "APPROVED",
            DecisionRetryable: false,
            DecisionRecoveryClassification: "READ_CANONICAL_RESULT",
            DecisionRecoveryAction: "READ_CANONICAL_DECISION",
            StatutoryDiscountPayableBasisApplicationCommandId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000009"),
            ApplicationRequested: true,
            ApplicationCommandStatus: "APPLIED",
            ApplicationResultClassification: "APPLIED",
            ApplicationSemanticHashSourceVersion: "statutory-discount-payable-basis-application:sha256:v1",
            ApplicationRetryable: false,
            ApplicationRecoveryClassification: "READ_CANONICAL_RESULT",
            ApplicationRecoveryAction: "READ_CANONICAL_DECISION",
            OverallResultClassification: "DECISION_AND_APPLICATION_COMPLETED",
            OneShotComplete: true,
            SiteId: includeSiteContext ? Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a") : null,
            SiteGroupId: includeSiteContext ? Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b") : null,
            VatExclusiveBasisAmountMinorUnits: vatExclusiveBasisAmountMinorUnits,
            VatAmountMinorUnits: vatAmountMinorUnits,
            VatTreatment: vatExclusiveBasisAmountMinorUnits.HasValue ? "VAT_EXCLUSIVE" : null,
            PayableBasisReady: payableBasisReady,
            PayableBasisReadinessStatus: payableBasisReadinessStatus,
            PayableBasisReadinessAction: payableBasisReady ? null : "POLL_READBACK");

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
}
