using ExitPass.CentralPms.Application.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalSemanticRequestHashParityProofServiceTests
{
    private const string ExpectedPosServerHash =
        "6a490379e4275a57f0a0695ff9dbd1271c4480adaeeefb9b6bfbd11e4d1ed201";
    private const string CurrentCentralPmsFixtureHash =
        "1430a2fbd8c9c128d101658777ba427bb34564bb84460621af179a464b5d7ab8";

    private readonly PosServerFiscalDocumentRequestMapper _mapper = new();
    private readonly FiscalSemanticRequestHashCalculator _calculator = new();
    private readonly FiscalSemanticRequestHashParityProofService _sut = new();

    [Fact]
    public void Fixture_WhenCanonicalSourceTextIsHashed_ReturnsExpectedPosServerSha256V1Hash()
    {
        var fixture = PosServerSemanticHashSha256V1Fixture.Read();

        fixture.CanonicalSourceVersion.Should().Be("sha256:v1");
        fixture.HashAlgorithm.Should().Be("SHA-256");
        fixture.ExpectedSha256Hash.Should().Be(ExpectedPosServerHash);
        fixture.ComputeCanonicalSourceSha256LowerHex().Should().Be(ExpectedPosServerHash);
    }

    [Fact]
    public void FixtureMapping_WhenReadMultipleTimes_ProducesDeterministicCentralPmsHash()
    {
        var first = PosServerSemanticHashSha256V1Fixture.Read().RepresentativeCreateRequest;
        var second = PosServerSemanticHashSha256V1Fixture.Read().RepresentativeCreateRequest;

        var firstResult = _calculator.InspectCanonicalSource(first);
        var secondResult = _calculator.InspectCanonicalSource(second);

        firstResult.HashValue.Should().Be(secondResult.HashValue);
        firstResult.CanonicalSourceText.Should().Be(secondResult.CanonicalSourceText);
        firstResult.HashValue.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Prove_WhenActualPosServerFixtureIsConsumed_ReturnsMismatchWithExactReason()
    {
        var fixture = PosServerSemanticHashSha256V1Fixture.Read();
        var centralPms = _calculator.InspectCanonicalSource(fixture.RepresentativeCreateRequest);

        var result = _sut.Prove(fixture.RepresentativeCreateRequest, fixture.ToParityFixture());

        result.Status.Should().Be(FiscalSemanticRequestHashParityProofStatus.Mismatch);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_mismatch");
        result.CentralPmsHashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.CentralPmsCanonicalSourceText.Should().Be(centralPms.CanonicalSourceText);
        result.CentralPmsSemanticRequestHash.Should().Be(centralPms.HashValue);
        result.CentralPmsSemanticRequestHash.Should().Be(CurrentCentralPmsFixtureHash);
        result.CentralPmsSemanticRequestHash.Should().NotBe(ExpectedPosServerHash);
        result.PosServerExpectedHashSourceVersion.Should().Be("sha256:v1");
        result.PosServerExpectedSemanticRequestHash.Should().Be(ExpectedPosServerHash);
    }

    [Fact]
    public void Prove_WhenExactPosServerExpectedSourceAndHashAreAvailable_ReturnsProven()
    {
        var request = RepresentativeRequest();
        var centralPms = _calculator.InspectCanonicalSource(request);

        var result = _sut.Prove(request, ExactFixture(centralPms));

        result.Status.Should().Be(FiscalSemanticRequestHashParityProofStatus.Proven);
        result.BlockReasonCode.Should().BeNull();
        result.CentralPmsHashSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.PosServerExpectedHashSourceVersion
            .Should().Be(FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion);
        result.CentralPmsCanonicalSourceText.Should().Be(centralPms.CanonicalSourceText);
        result.CentralPmsSemanticRequestHash.Should().Be(centralPms.HashValue);
    }

    [Fact]
    public void Prove_WhenPosServerExpectedSourceIsMissing_ReturnsUnconfirmedWithoutFakingParity()
    {
        var result = _sut.Prove(RepresentativeRequest(), posServerExpected: null);

        result.Status.Should().Be(FiscalSemanticRequestHashParityProofStatus.Unconfirmed);
        result.BlockReasonCode.Should().Be("pos_server_hash_source_code_not_available_for_parity_proof");
        result.CentralPmsSemanticRequestHash.Should().MatchRegex("^[0-9a-f]{64}$");
        result.PosServerExpectedSemanticRequestHash.Should().BeNull();
    }

    [Fact]
    public void Prove_WhenExpectedPosServerHashDiffers_ReturnsMismatch()
    {
        var request = RepresentativeRequest();
        var centralPms = _calculator.InspectCanonicalSource(request);
        var fixture = ExactFixture(centralPms) with
        {
            PosServerSemanticRequestHash = new string('0', 64)
        };

        var result = _sut.Prove(request, fixture);

        result.Status.Should().Be(FiscalSemanticRequestHashParityProofStatus.Mismatch);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_mismatch");
        result.CentralPmsSemanticRequestHash.Should().NotBe(result.PosServerExpectedSemanticRequestHash);
    }

    [Fact]
    public void Prove_WhenExpectedPosServerSourceDiffers_ReturnsMismatch()
    {
        var request = RepresentativeRequest();
        var centralPms = _calculator.InspectCanonicalSource(request);
        var fixture = ExactFixture(centralPms) with
        {
            PosServerCanonicalSourceText = $"{centralPms.CanonicalSourceText}\nextra_pos_fact=changed"
        };

        var result = _sut.Prove(request, fixture);

        result.Status.Should().Be(FiscalSemanticRequestHashParityProofStatus.Mismatch);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_mismatch");
    }

    [Fact]
    public void Prove_WhenPosServerSourceVersionIsNotSha256V1_ReturnsUnconfirmed()
    {
        var request = RepresentativeRequest();
        var centralPms = _calculator.InspectCanonicalSource(request);
        var fixture = ExactFixture(centralPms) with
        {
            PosServerHashSourceVersion = "sha256:v0"
        };

        var result = _sut.Prove(request, fixture);

        result.Status.Should().Be(FiscalSemanticRequestHashParityProofStatus.Unconfirmed);
        result.BlockReasonCode.Should().Be("pos_server_semantic_hash_parity_unproven");
    }

    private PosServerFiscalDocumentCreateRequest RepresentativeRequest() =>
        _mapper.Map(PosServerFiscalDocumentRequestMapperTests.ValidContext());

    private static FiscalSemanticRequestHashParityFixture ExactFixture(
        FiscalSemanticRequestHashCanonicalInspectionResult centralPms)
    {
        centralPms.CanonicalSourceText.Should().NotBeNull();
        centralPms.HashValue.Should().NotBeNull();

        return new FiscalSemanticRequestHashParityFixture(
            PosServerHashSourceVersion:
                FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion,
            PosServerCanonicalSourceText: centralPms.CanonicalSourceText!,
            PosServerSemanticRequestHash: centralPms.HashValue!);
    }
}
