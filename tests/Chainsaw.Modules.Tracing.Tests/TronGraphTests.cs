using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The graph on TRON (round 8): a TRON start point, expansion through TRON's source, USDT on TRON and TRX on the lines
/// with TRON's block ids, Ethereum and TRON side by side in one case, the state read back from the case, and a trace of
/// USDT on TRON with the account methods.
/// </summary>
public sealed class TronGraphTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("victim");
    private static readonly EthereumAddress Scammer = Address("scammer");
    private static readonly EthereumAddress Mule = Address("mule");
    private static readonly EthereumAddress Exchange = Address("exchange");

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>The victim pays USDT and TRX to the scammer, who sends USDT on to a mule, who pays an exchange.</summary>
    private void Chain()
    {
        _harness.Tron.Add(
            Tx("pay-usdt", 100, Victim, Token(Victim, Scammer, Usdt(1_000))),
            Tx("pay-trx", 101, Victim, (EthereumTransferKind.Native, Victim, Scammer, Trx(50))),
            Tx("to-mule", 110, Scammer, Token(Scammer, Mule, Usdt(600))),
            Tx("cash-out", 130, Mule, Token(Mule, Exchange, Usdt(600))));
    }

    [Fact]
    public async Task A_tron_address_is_a_start_point_on_tron()
    {
        Assert.True(_harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer)));

        var node = Assert.Single((await _harness.Load()).Nodes);

        Assert.Equal(SharedKernel.Chain.Tron, node.Chain);
        Assert.Equal([Scammer.ToChainAddress()], node.Addresses);
        Assert.Equal(SharedKernel.Chain.Tron, TraceStartPoint.ForEthereumAddress(Scammer).Chain);
    }

    [Fact]
    public async Task Expanding_a_tron_address_asks_trons_source_and_draws_usdt_and_trx_with_trons_block_ids()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));

        var result = await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Equal(3, result.NewEthereumTransactions.Count);
        Assert.All(result.NewEthereumTransactions, hash => Assert.Equal(SharedKernel.Chain.Tron, hash.Chain));
        Assert.Equal(0, _harness.Ethereum.Lookups);
        var graph = result.Graph;
        var scammer = graph.NodeOf(Scammer.ToChainAddress())!;
        Assert.Contains(graph.Edges, edge => edge.To == scammer.Key && edge.Amount == Usdt(1_000));
        Assert.Contains(graph.Edges, edge => edge.To == scammer.Key && edge.Amount == Trx(50));
        Assert.All(graph.Edges.SelectMany(edge => edge.Transfers), transfer =>
        {
            Assert.Equal(SharedKernel.Chain.Tron, transfer.Transaction.Chain);
            Assert.Equal(64, transfer.Block!.Hash.Value.Length);
        });
        Assert.All(graph.Nodes, node => Assert.Equal(SharedKernel.Chain.Tron, node.Chain));
    }

    [Fact]
    public async Task Ethereum_and_tron_in_one_case_keep_their_own_nodes_for_the_same_bytes()
    {
        Chain();
        var twin = EthereumAddress.Parse(SharedKernel.Chain.Ethereum, Scammer.Hex);
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(twin));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        var graph = await _harness.Load();

        var onTron = graph.NodeOf(Scammer.ToChainAddress())!;
        var onEthereum = graph.NodeOf(twin.ToChainAddress())!;
        Assert.NotEqual(onTron.Key, onEthereum.Key);
        Assert.Equal(SharedKernel.Chain.Ethereum, onEthereum.Chain);
        Assert.Equal(NodeExpansion.Expanded, onTron.Expansion);
        Assert.Equal(NodeExpansion.NotExpanded, onEthereum.Expansion);
    }

    [Fact]
    public async Task The_tron_state_is_read_back_from_the_case_without_asking()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumTransaction(Hash("to-mule")));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumTransaction(Hash("to-mule")));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Mule));
        var before = await _harness.Load();
        var asked = _harness.Tron.Lookups;
        _harness.Reopen();

        var after = await _harness.Load();

        Assert.Equal(asked, _harness.Tron.Lookups);
        Assert.Equal(before.Nodes.Select(node => node.Key).Order(), after.Nodes.Select(node => node.Key).Order());
        Assert.Equal(SharedKernel.Chain.Tron, Assert.Single(after.StartPoints).Chain);
    }

    [Fact]
    public async Task Usdt_on_tron_is_traced_to_where_it_stops()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Mule))).Graph;
        var seeds = graph.Account.Transfers
            .Where(transfer => transfer.To == Scammer && transfer.Amount.Asset == KnownTokens.TronUsdt)
            .Select(AccountSeed.Of).ToList();

        var result = await _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, seeds));

        var usdt = Assert.Single(result.Assets);
        Assert.Equal(Usdt(1_000), usdt.Traced);
        var exchange = graph.NodeOf(Exchange.ToChainAddress())!;
        Assert.Equal(Usdt(600), Assert.Single(usdt.Ends, end => end.NodeKey == exchange.Key).Amount.Amount);
    }

    [Fact]
    public async Task A_token_the_whole_transaction_cannot_count_stays_in_the_graph_from_the_history()
    {
        var usdc = new AssetAmount(Asset.Token(SharedKernel.Chain.Tron, Address("usdc").Value, "USDC", 6), 700_000_000);
        var full = Tx("mixed", 140, Scammer, Token(Scammer, Mule, Usdt(100)), Token(Scammer, Mule, usdc));
        _harness.Tron.Add(full);

        // Read whole, the transaction counts only USDT and says it is incomplete, as TRON's source does.
        _harness.Tron.Whole[full.Hash] = full with
        {
            Transfers = [.. full.Transfers.Where(transfer => transfer.Amount.Asset == KnownTokens.TronUsdt)],
            IsComplete = false,
        };
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumTransaction(full.Hash))).Graph;

        var movements = graph.Account.Transfers.Where(transfer => transfer.Transaction == full.Hash).ToList();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, transfer => transfer.Amount == usdc);
    }

    [Fact]
    public async Task A_contracts_trx_the_whole_transaction_lacks_stays_from_the_history_and_is_counted_once()
    {
        var contract = Address("contract");
        var full = Tx("payout", 150, Scammer, (EthereumTransferKind.Native, Scammer, contract, Trx(10)),
            (EthereumTransferKind.Internal, contract, Mule, Trx(9)));
        _harness.Tron.Add(full);

        // Its info could not be had: the whole transaction has only what it sent of its own, and says so.
        _harness.Tron.Whole[full.Hash] = full with
        {
            Transfers = [.. full.Transfers.Where(transfer => transfer.Kind == EthereumTransferKind.Native)],
            IsComplete = false,
        };
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Mule));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Mule));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumTransaction(full.Hash))).Graph;

        var movements = graph.Account.Transfers.Where(transfer => transfer.Transaction == full.Hash).ToList();
        Assert.Single(movements, transfer => transfer.Kind == EthereumTransferKind.Native);
        Assert.Single(movements, transfer => transfer.Kind == EthereumTransferKind.Internal);
    }

    [Fact]
    public async Task A_link_from_a_tron_movement_leads_to_another_chain_only()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;
        var seed = AccountSeed.Of(graph.Account.Transfers.First(transfer => transfer.From == Scammer));

        Assert.Throws<ArgumentException>(() => _harness.Service.LinkCrossChain(
            _harness.Case, seed, new ChainTransactionRef(SharedKernel.Chain.Tron, new string('d', 64)), "Brygga"));
        var link = _harness.Service.LinkCrossChain(
            _harness.Case, seed, new ChainTransactionRef(SharedKernel.Chain.Ethereum, "0x" + new string('d', 64)),
            "Brygga");

        Assert.Equal(SharedKernel.Chain.Tron, link.FromChain);
        _harness.Reopen();
        Assert.Equal(seed, (await _harness.Load()).CrossChainLinks.Single().FromTransfer);
    }

    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse(SharedKernel.Chain.Tron, "41" + Hex(label, 40));

    private static EthereumTxHash Hash(string label) => EthereumTxHash.Parse(SharedKernel.Chain.Tron, Hex(label, 64));

    private static AssetAmount Usdt(decimal amount) =>
        new(KnownTokens.TronUsdt, new System.Numerics.BigInteger(amount * 1_000_000m));

    private static AssetAmount Trx(decimal amount) =>
        new(Asset.Tronix, new System.Numerics.BigInteger(amount * 1_000_000m));

    private static (EthereumTransferKind, EthereumAddress, EthereumAddress, AssetAmount) Token(
        EthereumAddress from, EthereumAddress to, AssetAmount amount) =>
        (EthereumTransferKind.Token, from, to, amount);

    /// <summary>Makes a TRON transaction: the signer, its block (TRON's id) and its movements.</summary>
    private static EthereumTransaction Tx(
        string label, long height, EthereumAddress from,
        params (EthereumTransferKind Kind, EthereumAddress From, EthereumAddress To, AssetAmount Amount)[] moves)
    {
        var hash = Hash(label);
        var block = new EthereumBlockReference(
            height, FakeAccountSource.HashOf(height)[2..],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(3 * height));
        var transfers = moves.Select((move, index) => new EthereumTransfer(
            hash, move.Kind, move.From, move.To, move.Amount, move.Kind == EthereumTransferKind.Native ? 0 : index,
            block)).ToList();
        var value = moves.Where(move => move.Kind == EthereumTransferKind.Native).Select(move => move.Amount)
            .FirstOrDefault(AssetAmount.Zero(Asset.Tronix));
        return new EthereumTransaction(
            hash, block, from, moves[0].To, value, Trx(0.345m), EthereumOutcome.Succeeded, transfers);
    }

    private static string Hex(string label, int digits)
    {
        var text = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)))
            .ToLower(CultureInfo.InvariantCulture);
        return (text + text)[..digits];
    }
}
