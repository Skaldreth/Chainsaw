using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Litecoin's MWEB as an end of the trail: a peg-in and the extension block's own address end it as a privacy coin,
/// read from the script; the same witness version on Bitcoin, and any other address, do not.
/// </summary>
public sealed class MwebTrailEndsTests
{
    private readonly MwebTrailEnds _classifier = new();

    [Theory]
    [InlineData(9)]
    [InlineData(8)]
    public void Money_into_mweb_ends_the_trail_as_a_privacy_coin(int version)
    {
        var address = Witness(version, BitcoinNetwork.Litecoin);

        var hint = Assert.Single(_classifier.ClassifyAddress(null!, address));

        Assert.Equal(TrailEndReason.PrivacyCoin, hint.Reason);
        Assert.Equal(MwebTrailEnds.Source, hint.Source);
        Assert.Contains("MWEB", hint.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_version_on_bitcoin_and_an_ordinary_litecoin_address_do_not_end_it()
    {
        Assert.Empty(_classifier.ClassifyAddress(null!, Witness(9, BitcoinNetwork.Mainnet)));
        Assert.Empty(_classifier.ClassifyAddress(null!, BitcoinAddress.FromWitness(
            0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Litecoin)));
    }

    /// <summary>Gives a witness program of 32 bytes of 0x66 at a version.</summary>
    private static BitcoinAddress Witness(int version, BitcoinNetwork network) =>
        BitcoinAddress.FromWitness(version, Enumerable.Repeat((byte)0x66, 32).ToArray(), network);
}
