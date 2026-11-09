using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The graph on Solana: a Solana start point, expansion through Solana's source only, SPL USDT and SOL on the lines,
/// the state and a link from a Solana movement read back from the case (signatures are Base58, case and all), a
/// trace of USDT, and a swap of USDT for SOL read by the pattern.
/// </summary>
public sealed class SolanaGraphTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("victim");
    private static readonly EthereumAddress Scammer = Address("scammer");
    private static readonly EthereumAddress Mule = Address("mule");
    private static readonly EthereumAddress Exchange = Address("exchange");
    private static readonly EthereumAddress Pool = Address("pool");

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// The victim pays USDT and SOL to the scammer, who sends USDT on to a mule, who pays an exchange.
    /// </summary>
    private void Chain()
    {
        _harness.Solana.Add(
            Tx("pay-usdt", 100, Victim, Token(Victim, Scammer, Usdt(1_000))),
            Tx("pay-sol", 101, Victim, (EthereumTransferKind.Native, Victim, Scammer, Sol(2))),
            Tx("to-mule", 110, Scammer, Token(Scammer, Mule, Usdt(600))),
            Tx("cash-out", 130, Mule, Token(Mule, Exchange, Usdt(600))));
    }

    [Fact]
    public async Task Expanding_a_solana_address_asks_solanas_source_only_and_draws_usdt_and_sol()
    {
        Chain();
        Assert.True(_harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer)));

        var result = await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.All(result.NewEthereumTransactions, hash => Assert.Equal(SharedKernel.Chain.Solana, hash.Chain));
        Assert.Equal(0, _harness.Ethereum.Lookups);
        var graph = result.Graph;
        var scammer = graph.NodeOf(Scammer.ToChainAddress())!;
        Assert.Contains(graph.Edges, edge => edge.To == scammer.Key && edge.Amount == Usdt(1_000));
        Assert.Contains(graph.Edges, edge => edge.To == scammer.Key && edge.Amount == Sol(2));
        Assert.All(graph.Nodes, node => Assert.Equal(SharedKernel.Chain.Solana, node.Chain));
    }

    [Fact]
    public async Task The_state_and_a_link_from_a_solana_movement_are_read_back_on_solana()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;
        var seed = AccountSeed.Of(graph.Account.Transfers.First(transfer => transfer.From == Scammer));
        _harness.Service.LinkCrossChain(
            _harness.Case, seed, new ChainTransactionRef(SharedKernel.Chain.Tron, new string('d', 64)), "Brygga");
        var asked = _harness.Solana.Lookups;
        _harness.Reopen();

        var read = await _harness.Load();

        Assert.Equal(asked, _harness.Solana.Lookups);
        Assert.Equal(NodeExpansion.Expanded, read.NodeOf(Scammer.ToChainAddress())!.Expansion);
        Assert.Contains(read.StartPoints, start => start.Chain == SharedKernel.Chain.Solana);
        var from = read.CrossChainLinks.Single().FromTransfer!;
        Assert.Equal(seed, from);
        Assert.Equal(SharedKernel.Chain.Solana, from.Transaction.Chain);
    }

    [Fact]
    public void A_link_to_a_solana_transaction_keeps_its_signature_as_written()
    {
        var signature = Hash("onward").Value;

        var to = new ChainTransactionRef(SharedKernel.Chain.Solana, signature);

        Assert.Equal(signature, to.Id);
        Assert.Throws<ArgumentException>(() => new ChainTransactionRef(SharedKernel.Chain.Solana, new string('a', 64)));
    }

    [Fact]
    public async Task Usdt_on_solana_is_traced_to_where_it_stops()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Mule))).Graph;
        var seeds = graph.Account.Transfers
            .Where(transfer => transfer.To == Scammer && transfer.Amount.Asset == KnownTokens.SolanaUsdt)
            .Select(AccountSeed.Of).ToList();

        var result = await _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, seeds));

        var usdt = Assert.Single(result.Assets);
        Assert.Equal(Usdt(1_000), usdt.Traced);
        var exchange = graph.NodeOf(Exchange.ToChainAddress())!;
        Assert.Equal(Usdt(600), Assert.Single(usdt.Ends, end => end.NodeKey == exchange.Key).Amount.Amount);
    }

    [Fact]
    public async Task Usdt_sold_for_sol_at_a_pool_is_read_as_a_swap()
    {
        _harness.Solana.Add(
            Tx("pay-usdt", 100, Victim, Token(Victim, Scammer, Usdt(1_000))),
            Tx("swap", 120, Scammer,
                Token(Scammer, Pool, Usdt(1_000)), (EthereumTransferKind.Native, Pool, Scammer, Sol(5))));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));

        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;

        var swap = Assert.Single(graph.Account.Swaps);
        Assert.Equal(Usdt(1_000), swap.Sold);
        Assert.Equal(Sol(5), swap.Bought);
    }

    [Fact]
    public async Task A_swap_stop_on_solana_is_read_back_on_solana()
    {
        _harness.Solana.Add(
            Tx("pay-usdt", 100, Victim, Token(Victim, Scammer, Usdt(1_000))),
            Tx("swap", 120, Scammer,
                Token(Scammer, Pool, Usdt(1_000)), (EthereumTransferKind.Native, Pool, Scammer, Sol(5))));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;
        var stop = _harness.Service.StopAtSwap(_harness.Case, Assert.Single(graph.Account.Swaps), null);
        _harness.Reopen();

        var read = Assert.Single((await _harness.Load()).SwapStops);

        Assert.Equal(stop, read);
        Assert.Equal(SharedKernel.Chain.Solana, read.Transaction.Chain);
    }

    [Fact]
    public async Task A_transaction_the_source_could_not_read_whole_is_said_in_the_graph()
    {
        Chain();
        _harness.Solana.Unexplained = [Hash("to-mule")];
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));

        var result = await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        Assert.Contains("flyttade mer än källan kunde läsa", result.Problem, StringComparison.Ordinal);
        Assert.Contains(Hash("to-mule").Brief(), result.Problem, StringComparison.Ordinal);
    }

    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse(SharedKernel.Chain.Solana, Base58.Encode(Bytes(label, 32)));

    private static EthereumTxHash Hash(string label) =>
        EthereumTxHash.Parse(SharedKernel.Chain.Solana, Base58.Encode(Bytes(label, 64)));

    private static AssetAmount Usdt(decimal amount) =>
        new(KnownTokens.SolanaUsdt, new BigInteger(amount) * BigInteger.Pow(10, 6));

    private static AssetAmount Sol(decimal amount) =>
        new(Asset.Sol, new BigInteger(amount) * BigInteger.Pow(10, 9));

    private static (EthereumTransferKind, EthereumAddress, EthereumAddress, AssetAmount) Token(
        EthereumAddress from, EthereumAddress to, AssetAmount amount) =>
        (EthereumTransferKind.Token, from, to, amount);

    /// <summary>Makes a Solana transaction: the fee payer, its slot and its movements, numbered in order.</summary>
    private static EthereumTransaction Tx(
        string label, long slot, EthereumAddress payer,
        params (EthereumTransferKind Kind, EthereumAddress From, EthereumAddress To, AssetAmount Amount)[] moves)
    {
        var hash = Hash(label);
        var block = new EthereumBlockReference(
            slot, Base58.Encode(Convert.FromHexString(FakeAccountSource.HashOf(slot)[2..])),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(slot));
        var transfers = moves.Select((move, index) => new EthereumTransfer(
            hash, move.Kind, move.From, move.To, move.Amount, index, block)).ToList();
        return new EthereumTransaction(
            hash, block, payer, null, AssetAmount.Zero(Asset.Sol), new AssetAmount(Asset.Sol, 5_000),
            EthereumOutcome.Succeeded, transfers);
    }

    /// <summary>
    /// Gives some bytes made from a label, so that every synthetic key and signature belongs to nobody.
    /// </summary>
    private static byte[] Bytes(string label, int length)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(label));
        return digest.Concat(digest).Take(length).ToArray();
    }
}
