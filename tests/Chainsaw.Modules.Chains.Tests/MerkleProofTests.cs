using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// That a transaction is in its block (granskningen etapp 15): the source's merkle proof leads from the txid to the
/// merkle root in the block's own header, computed by Chainsaw. Checked against Bitcoin's block 170 and against a
/// synthetic block whose header carries the root; never the network.
/// </summary>
public sealed class MerkleProofTests : IDisposable
{
    private const string Previous = "00000000000000000001a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2";

    private const string Second = "https://second.example/api/";

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

        // Only one endpoint: the block rests on its answer alone, and that is said.
        Assert.Equal(InBlockCheck.SourceOnly, lookup.Value!.InBlock);
        Assert.Equal(block, lookup.Value.Block!.Hash);
        Assert.Contains(lookup.Notes, note => note.Contains("bara på en källas svar", StringComparison.Ordinal));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_block_another_endpoint_in_step_confirms_is_said_as_confirmed()
    {
        var block = AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 0);
        await _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null).Saved;
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        Height(Fixtures.Base);
        Height(Second);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        _harness.Http.AnswerBytes(Second + "block-height/800003", Encoding.ASCII.GetBytes(block.Value), 200);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(InBlockCheck.ConfirmedElsewhere, lookup.Value!.InBlock);
        Assert.Contains(lookup.Origins, origin => origin.Url.Host == "second.example");
    }

    [Fact]
    public async Task A_block_the_own_node_has_at_the_height_is_said_as_confirmed_by_the_own_node()
    {
        var block = AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 0);
        await AddOwnNodeAsync();
        _harness.Local.AnswerRpc(OwnNode, "getblockhash 800003", $$"""
            {"result":"{{block.Value}}","error":null,"id":"getblockhash 800003"}
            """);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(InBlockCheck.ConfirmedByOwnNode, lookup.Value!.InBlock);
        Assert.Contains(lookup.Notes, note => note.Contains("den egna noden, bitcoind.lan", StringComparison.Ordinal));
        Assert.Contains(lookup.Origins, origin => origin.Source == BitcoinCoreApi.Code);

        // Only the hash at the height went to the own node; the proof and the block came from the source.
        Assert.Equal(["getblockhash 800003"], _harness.Local.RpcIds);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_local_esplora_confirms_by_name_as_a_local_endpoint_not_as_the_own_node()
    {
        var block = AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 0);
        await _harness.Sources.Add(Chain.Bitcoin, new Uri(LocalEsplora), "esplora", null, local: true).Saved;
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        _harness.Local.AnswerBytes(LocalEsplora + "block-height/800003", Encoding.ASCII.GetBytes(block.Value), 200);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(InBlockCheck.ConfirmedElsewhere, lookup.Value!.InBlock);
        Assert.Contains(lookup.Notes, note => note.Contains("en lokal endpoint, esplora.lan", StringComparison.Ordinal));
    }

    /// <summary>An electrs with Esplora's API on the own network in the tests.</summary>
    private const string LocalEsplora = "https://esplora.lan/api/";

    [Fact]
    public async Task An_own_node_with_another_block_at_the_height_leaves_the_block_one_sources()
    {
        AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 0);
        await AddOwnNodeAsync();
        _harness.Local.AnswerRpc(OwnNode, "getblockhash 800003", $$"""
            {"result":"{{Previous}}","error":null,"id":"getblockhash 800003"}
            """);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(InBlockCheck.SourceOnly, lookup.Value!.InBlock);
        Assert.Contains(lookup.Notes, note => note.Contains("egna noden har ett annat block", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_own_node_that_has_not_come_so_far_says_so_and_another_endpoint_is_asked()
    {
        AnswerBlockWithRoot(Fixtures.Real, position: 0);
        AnswerProof(position: 0);
        await AddOwnNodeAsync();
        _harness.Local.AnswerRpc(OwnNode, "getblockhash 800003", """
            {"result":null,"error":{"code":-8,"message":"Block height out of range"},"id":"getblockhash 800003"}
            """, status: 500);

        var lookup = await _harness.Source.VerifyInBlockAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(InBlockCheck.SourceOnly, lookup.Value!.InBlock);
        Assert.Contains(lookup.Notes, note => note.Contains("kunde inte bekräfta", StringComparison.Ordinal));
    }

    /// <summary>The user's own Bitcoin Core in the tests.</summary>
    private const string OwnNode = "https://bitcoind.lan/";

    /// <summary>Adds the own Bitcoin Core, marked local, last in the list, so the block comes from the source.</summary>
    private async Task AddOwnNodeAsync()
    {
        await _harness.Sources.Add(Chain.Bitcoin, new Uri(OwnNode), BitcoinCoreApi.Code, null, local: true).Saved;
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
    }

    [Fact]
    public void A_proof_that_uses_a_duplicated_node_as_a_place_or_the_wrong_depth_is_refused()
    {
        var txid = Txid.Parse(Sibling);

        // The transaction's own copy on its left: the duplicated last node posing as a place of its own.
        Assert.Null(new MerkleProof(170, [Sibling], 1).RootFrom(txid));
        Assert.NotNull(new MerkleProof(170, [Sibling], 0).RootFrom(txid));
        Assert.True(new MerkleProof(1, [Sibling], 1).Fits(2));
        Assert.False(new MerkleProof(1, [Sibling], 1).Fits(1));
        Assert.False(new MerkleProof(1, [Sibling, Sibling], 1).Fits(2));
        Assert.True(new MerkleProof(1, [], 0).Fits(1));
        Assert.True(new MerkleProof(1, [Sibling, Sibling, Sibling], 4).Fits(5));
    }

    /// <summary>Answers an endpoint's height.</summary>
    private void Height(string address) =>
        _harness.Http.AnswerBytes(address + "blocks/tip/height", "800010"u8.ToArray(), 200);

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
        Assert.Equal(InBlockCheck.NotChecked, lookup.Value.InBlock);
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
        var root = new MerkleProof(800_003, [Sibling], position).RootFrom(txid)!;
        var header = new byte[80];
        BinaryPrimitives.WriteInt32LittleEndian(header, 0x20000000);
        Convert.FromHexString(Previous).Reverse().ToArray().CopyTo(header, 4);
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
        _harness.Http.Answer(Fixtures.Url($"tx/{txid}/hex"), "tx-real-hex.txt");
        _harness.Http.AnswerBytes(
            Fixtures.Url($"block/{hash}/header"), Encoding.ASCII.GetBytes(Convert.ToHexStringLower(header)), 200);
        var block = JsonNode.Parse(Fixtures.Text("block-real.json"))!;
        block["id"] = hash.Value;
        block["height"] = 800_003;
        block["timestamp"] = 1_700_001_800;
        block["tx_count"] = 2;
        block["merkle_root"] = root;
        block["previousblockhash"] = Previous;
        block["nonce"] = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(76));
        _harness.Http.AnswerBytes(Fixtures.Url($"block/{hash}"), Encoding.UTF8.GetBytes(block.ToJsonString()), 200);
        return hash;
    }
}
