using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalSemanticRequestHashCalculatorTests
{
    private const string ExpectedPosServerHash =
        "aa30eaa0de7acf8f12acefc2bcbb520cd1363594ef720aa8f28ca8ab0cf326e4";

    private readonly PosServerFiscalDocumentRequestMapper _mapper = new();
    private readonly FiscalSemanticRequestHashCalculator _sut = new();

    [Fact]
    public void Calculate_WhenFiscalRequestFactsAreIdentical_ReturnsSameHash()
    {
        var first = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());
        var second = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

        var firstResult = _sut.Calculate(first);
        var secondResult = _sut.Calculate(second);

        firstResult.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        firstResult.HashValue.Should().Be(secondResult.HashValue);
        firstResult.HashAlgorithm.Should().Be("SHA-256");
        firstResult.HashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        firstResult.SourceFactCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void InspectCanonicalSource_WhenFiscalRequestIsRepresentative_ReturnsSafeFactListAndCanonicalText()
    {
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

        var result = _sut.InspectCanonicalSource(request);

        result.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        result.HashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.NormalizedFacts.Should().Contain("payable_basis");
        result.NormalizedFacts.Should().Contain("document_lines");
        result.NormalizedFacts.Should().Contain("tenders");
        result.NormalizedFacts.Should().Contain("tax_details");
        result.NormalizedFacts.Should().Contain("totals");
        result.CanonicalSourceText.Should().Contain("\"payable_basis\"");
        result.CanonicalSourceText.Should().Contain("\"document_lines\"");
        result.CanonicalSourceText.Should().Contain("\"tenders\"");
        result.CanonicalSourceText.Should().Contain("\"tax_details\"");
        result.CanonicalSourceText.Should().Contain("\"totals\"");
        result.CanonicalSourceText.Should().NotContain("hash_source_version");
        result.HashValue.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void InspectCanonicalSource_WhenPosServerFixtureIsMapped_ReturnsExactSha256V1CanonicalJsonAndHash()
    {
        var fixture = PosServerSemanticHashSha256V1Fixture.Read();

        var result = _sut.InspectCanonicalSource(fixture.RepresentativeCreateRequest);

        result.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        result.HashSourceVersion.Should().Be("sha256:v1");
        result.CanonicalSourceText.Should().Be(fixture.CanonicalSourceText);
        result.HashValue.Should().Be(ExpectedPosServerHash);
        result.HashValue.Should().Be(fixture.ExpectedSha256Hash);
    }

    [Theory]
    [InlineData("upstream")]
    [InlineData("line")]
    [InlineData("tender")]
    [InlineData("tax")]
    [InlineData("total")]
    [InlineData("statutoryDecision")]
    public void Calculate_WhenSemanticFiscalFactsChange_ReturnsDifferentHash(string mutation)
    {
        var baseline = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());
        var changed = mutation switch
        {
            "upstream" => _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext() with
            {
                PayableBasis = PosServerFiscalDocumentRequestMapperTests.ValidContext().PayableBasis with
                {
                    UpstreamFinalityRef = "upstream-finality-ref-changed"
                }
            }),
            "line" => baseline with
            {
                DocumentLines =
                [
                    baseline.DocumentLines[0] with { NetAmountMinorUnits = 11299 }
                ],
                Lines =
                [
                    baseline.Lines[0] with { NetAmountMinorUnits = 11299 }
                ]
            },
            "tender" => baseline with
            {
                Tenders =
                [
                    baseline.Tenders[0] with { AmountMinorUnits = 12499 }
                ]
            },
            "tax" => baseline with
            {
                TaxDetails =
                [
                    baseline.TaxDetails[0] with { TaxAmountMinorUnits = 1199 }
                ]
            },
            "total" => baseline with
            {
                Totals =
                [
                    baseline.Totals[0] with { AmountMinorUnits = 12499 }
                ]
            },
            "statutoryDecision" => baseline with
            {
                PayableBasis = baseline.PayableBasis with
                {
                    DiscountReferences =
                    [
                        baseline.PayableBasis.DiscountReferences[0] with
                        {
                            StatutoryDiscountDecisionCommandRef = "changed-statutory-decision-command-ref"
                        }
                    ]
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.")
        };

        var baselineResult = _sut.Calculate(baseline);
        var changedResult = _sut.Calculate(changed);

        changedResult.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        changedResult.HashValue.Should().NotBe(baselineResult.HashValue);
    }

    [Fact]
    public void Calculate_WhenOnlyTransportCorrelationContextChanges_ReturnsSameHash()
    {
        var baseline = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());
        var changed = baseline with
        {
            ReferenceContext = baseline.ReferenceContext
                .Concat(new[] { new KeyValuePair<string, string>("correlation", "different-correlation-ref") })
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal)
        };

        var baselineResult = _sut.Calculate(baseline);
        var changedResult = _sut.Calculate(changed);

        changedResult.HashValue.Should().NotBeNull();
        changedResult.HashValue.Should().Be(baselineResult.HashValue);
    }

    [Fact]
    public void Calculate_WhenAppliedStatutoryFactsArePresent_UsesPosServerStatutoryHashVersion()
    {
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.StatutoryContext("WEBPAY"));

        var result = _sut.InspectCanonicalSource(request);

        result.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        result.HashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentStatutoryHashSourceVersion);
        result.NormalizedFacts.Should().Contain("applied_statutory_fiscal_facts");
        result.CanonicalSourceText.Should().Contain("\"applied_statutory_fiscal_facts\"");
        result.CanonicalSourceText.Should().Contain("\"statutory_payable_basis_application_command_id\"");
        result.CanonicalSourceText.Should().Contain("\"final_payable_amount_minor_units\":8929");
    }

    [Fact]
    public void Calculate_WhenAppliedStatutoryMaterialFactChanges_ReturnsDifferentHash()
    {
        var baseline = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.StatutoryContext("WEBPAY"));
        var changed = baseline with
        {
            AppliedStatutoryFiscalFacts = baseline.AppliedStatutoryFiscalFacts! with
            {
                StatutoryDiscountAmountMinorUnits = 2_231
            }
        };

        var baselineResult = _sut.Calculate(baseline);
        var changedResult = _sut.Calculate(changed);

        baselineResult.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        changedResult.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        changedResult.HashValue.Should().NotBe(baselineResult.HashValue);
    }

    [Fact]
    public void Calculate_WhenPosServerSemanticScopeFieldChannelTerminalChanges_ReturnsDifferentHash()
    {
        var baseline = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());
        var changed = baseline with
        {
            ChannelTerminalId = Guid.NewGuid()
        };

        var baselineResult = _sut.Calculate(baseline);
        var changedResult = _sut.Calculate(changed);

        changedResult.HashValue.Should().NotBe(baselineResult.HashValue);
    }

    [Fact]
    public void InspectCanonicalSource_WhenCanonicalJsonIsBuilt_ExcludesResponseRetryAndFiscalNumberOutcomeFields()
    {
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

        var result = _sut.InspectCanonicalSource(request);

        result.CanonicalSourceText.Should().NotBeNull();
        result.CanonicalSourceText.Should().NotContain("fiscal_document_id");
        result.CanonicalSourceText.Should().NotContain("fiscal_document_number");
        result.CanonicalSourceText.Should().NotContain("fiscal_sequence_value");
        result.CanonicalSourceText.Should().NotContain("retry");
        result.CanonicalSourceText.Should().NotContain("replay");
        result.CanonicalSourceText.Should().NotContain("response");
    }

    [Fact]
    public void Calculate_WhenRequiredFactsAreMissing_ReturnsIncompleteWithoutFakeHash()
    {
        var request = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext()) with
        {
            UpstreamFinalityRef = "",
            PayableBasis = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext()).PayableBasis with
            {
                UpstreamFinalityRef = ""
            }
        };

        var result = _sut.Calculate(request);

        result.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Incomplete);
        result.HashValue.Should().BeNull();
        result.BlockReasonCode.Should().Be("upstream_finality_reference_required");
    }

    [Fact]
    public void Calculate_WhenDictionaryOrderDiffers_ReturnsSameHash()
    {
        var context = PosServerFiscalDocumentRequestMapperTests.ValidContext();
        var reordered = context with
        {
            ReferenceContext = new Dictionary<string, string>
            {
                ["z"] = "last",
                ["a"] = "first"
            }
        };
        var equivalent = context with
        {
            ReferenceContext = new Dictionary<string, string>
            {
                ["a"] = "first",
                ["z"] = "last"
            }
        };

        var firstResult = _sut.Calculate(_mapper.Map(reordered));
        var secondResult = _sut.Calculate(_mapper.Map(equivalent));

        firstResult.HashValue.Should().Be(secondResult.HashValue);
    }
}
