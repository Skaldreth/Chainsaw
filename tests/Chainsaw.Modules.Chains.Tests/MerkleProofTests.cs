using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// That a transaction is in its block (granskningen etapp 15): the source's merkle proof leads from the txid to the
/// merkle root in the block's own header, computed by Chainsaw. Checked against Bitcoin's block 170 and against a
/// synthetic block whose header carries the root; never the network.
/// </summary>
public sealed class MerkleProofTests : IDisposable
{
    private const string Sibling = "b1fea52486ce0c62bb442b530a3f0132b826c74e473d1f2c220bfa78111c5082";

    private readonly SourceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Block_170s_second_transaction_leads_to_its_merkle_root()
    {
        // Block 170: the coinbase and the first payment between people (Satoshi to Hal Finney).
        var proof = MerkleProof.Parse(Encoding.UTF8.GetBytes($$"""
            {"block_height":170,"merkle":["{{Sibling}}"],"pos":1}
            """));

        var root = proof.RootFrom(Txid.Parse("f4184fc596403b9d638783cf57adfe4c75c605f6356fbc91338530e9831e9e16"));

        Assert.Equal("7dac2c5666815c17a3b36427de37bb9d2e2c5ccec3f8633eb91a4205cb4c10ff", root);
    }

    [Theory]
    [InlineData("""{"block_height":170,"merkle":["zz"],"pos":1}""")]
    [InlineData("""{"block_height":170,"merkle":[],"pos":1}""")]
    [InlineData("""{"block_height":-1,"merkle":[],"pos":0}""")]
    [InlineData("""{"merkle":[],"pos":0}""")]
    [InlineData("""not json""")]
    public void Anything_else_is_not_a_proof(string body)
    {
        Assert.Throws<EsploraFormatException>(() => MerkleProof.Parse(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public async Task A_proof_that_leads_to_the_headers_root_marks_the_transaction_in_its_block()
    {
        var block = AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 0);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.True(lookup.Value!.InBlockVerified);
        Assert.Equal(block, lookup.Value.Block!.Hash);
        Assert.Equal(3, lookup.Origins.Count);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_proof_that_leads_elsewhere_is_never_accepted()
    {
        AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 1);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Contains("huvud", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_proof_the_transaction_is_given_unchecked_and_says_so()
    {
        _harness.AnswerReal();

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.NotNull(lookup.Value);
        Assert.False(lookup.Value.InBlockVerified);
        Assert.Contains(lookup.Notes, note => note.Contains("inte kontrollerat", StringComparison.Ordinal));
    }

    /// <summary>Answers the proof of the real transaction with the sibling at a place.</summary>
    /// <param name="position">Its place in the block.</param>
    private void AnswerProof(long position) => _harness.Http.AnswerBytes(
        Fixtures.Url($"tx/{Fixtures.Real}/merkle-proof"),
        Encoding.UTF8.GetBytes($$"""{"block_height":800003,"merkle":["{{Sibling}}"],"pos":{{position}}}"""),
        200);

    /// <summary>
    /// Makes a block whose header carries the root that the transaction and the sibling lead to, mined at regtest's
    /// ease, and answers the transaction as in that block and the block's header.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="position">The transaction's place.</param>
    /// <returns>The block's hash.</returns>
    private BlockHash AnswerBlockWithRoot(Txid txid, long position)
    {
        var root = new MerkleProof(800_003, [Sibling], position).RootFrom(txid);
        var header = new byte[80];
        BinaryPrimitives.WriteInt32LittleEndian(header, 0x20000000);
        Convert.FromHexString(root).Reverse().ToArray().CopyTo(header, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), 1_700_001_800);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), 0x207fffff);
        BlockHash hash;
        for (uint nonce = 0; ; nonce++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76), nonce);
            var shown = SHA256.HashData(SHA256.HashData(header));
            Array.Reverse(shown);
            hash = BlockHash.Parse(Convert.ToHexStringLower(shown));
            if (BlockHeaderReader.MeetsTarget(hash, 0x207fffff))
            {
                break;
            }
        }

        var transaction = JsonNode.Parse(Fixtures.Text("tx-real.json"))!;
        transaction["status"]!["block_hash"] = hash.Value;
        _harness.Http.AnswerBytes(
            Fixtures.Url($"tx/{txid}"), Encoding.UTF8.GetBytes(transaction.ToJsonString()), 200);
        _harness.Http.AnswerBytes(
            Fixtures.Url($"block/{hash}/header"), Encoding.ASCII.GetBytes(Convert.ToHexStringLower(header)), 200);
        return hash;
    }
}
