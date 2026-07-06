using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalSemanticRequestHashCalculatorTests
{
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
        result.NormalizedFacts.Should().Contain("payable_basis.upstream_finality_ref=upstream-finality-ref");
        result.NormalizedFacts.Should().Contain("document_lines[0].net_amount_minor_units=11300");
        result.NormalizedFacts.Should().Contain("tenders[0].amount_minor_units=12500");
        result.NormalizedFacts.Should().Contain("tax_details[0].tax_amount_minor_units=1200");
        result.NormalizedFacts.Should().Contain("totals[0].amount_minor_units=12500");
        result.CanonicalSourceText.Should().Contain("hash_source_version=central-pms-pos-server-fiscal-request-v1");
        result.HashValue.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData("upstream")]
    [InlineData("line")]
    [InlineData("tender")]
    [InlineData("tax")]
    [InlineData("total")]
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
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.")
        };

        var baselineResult = _sut.Calculate(baseline);
        var changedResult = _sut.Calculate(changed);

        changedResult.Status.Should().Be(FiscalSemanticRequestHashSourceStatus.Available);
        changedResult.HashValue.Should().NotBe(baselineResult.HashValue);
    }

    [Fact]
    public void Calculate_WhenVolatileTransportFieldsChange_ReturnsSameHash()
    {
        var baseline = _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());
        var changed = baseline with
        {
            ChannelTerminalId = Guid.NewGuid()
        };

        var baselineResult = _sut.Calculate(baseline);
        var changedResult = _sut.Calculate(changed);

        changedResult.HashValue.Should().Be(baselineResult.HashValue);
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
