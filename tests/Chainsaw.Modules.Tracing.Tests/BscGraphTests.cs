using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The graph on BSC: a BSC start point, expansion through BSC's source only, BEP-20 USDT and BNB on the lines,
/// Ethereum and BSC side by side with their own nodes for the same 20 bytes, the state and a link from a BSC movement
/// read back from the case, and a trace of USDT on BSC with the account methods.
/// </summary>
public sealed class BscGraphTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("victim");
    private static readonly EthereumAddress Scammer = Address("scammer");
    private static readonly EthereumAddress Mule = Address("mule");
    private static readonly EthereumAddress Exchange = Address("exchange");

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>The victim pays USDT and BNB to the scammer, who sends USDT on to a mule, who pays an exchange.</summary>
    private void Chain()
    {
        _harness.Bsc.Add(
            Tx("pay-usdt", 100, Victim, Token(Victim, Scammer, Usdt(1_000))),
            Tx("pay-bnb", 101, Victim, (EthereumTransferKind.Native, Victim, Scammer, Bnb(2))),
            Tx("to-mule", 110, Scammer, Token(Scammer, Mule, Usdt(600))),
            Tx("cash-out", 130, Mule, Token(Mule, Exchange, Usdt(600))));
    }

    [Fact]
    public async Task Expanding_a_bsc_address_asks_bscs_source_only_and_draws_usdt_and_bnb()
    {
        Chain();
        Assert.True(_harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer)));

        var result = await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.All(result.NewEthereumTransactions, hash => Assert.Equal(SharedKernel.Chain.Bsc, hash.Chain));
        Assert.Equal(0, _harness.Ethereum.Lookups);
        var graph = result.Graph;
        var scammer = graph.NodeOf(Scammer.ToChainAddress())!;
        Assert.Contains(graph.Edges, edge => edge.To == scammer.Key && edge.Amount == Usdt(1_000));
        Assert.Contains(graph.Edges, edge => edge.To == scammer.Key && edge.Amount == Bnb(2));
        Assert.All(graph.Nodes, node => Assert.Equal(SharedKernel.Chain.Bsc, node.Chain));
    }

    [Fact]
    public async Task Ethereum_and_bsc_keep_their_own_nodes_for_the_same_bytes_and_are_read_back()
    {
        Chain();
        var twin = EthereumAddress.Parse(SharedKernel.Chain.Ethereum, Scammer.Value);
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(twin));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var asked = _harness.Bsc.Lookups;
        _harness.Reopen();

        var graph = await _harness.Load();

        Assert.Equal(asked, _harness.Bsc.Lookups);
        var onBsc = graph.NodeOf(Scammer.ToChainAddress())!;
        var onEthereum = graph.NodeOf(twin.ToChainAddress())!;
        Assert.NotEqual(onBsc.Key, onEthereum.Key);
        Assert.Equal(NodeExpansion.Expanded, onBsc.Expansion);
        Assert.Equal(NodeExpansion.NotExpanded, onEthereum.Expansion);
        Assert.Contains(graph.StartPoints, start => start.Chain == SharedKernel.Chain.Bsc);
    }

    [Fact]
    public async Task Usdt_on_bsc_is_traced_to_where_it_stops()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Mule))).Graph;
        var seeds = graph.Account.Transfers
            .Where(transfer => transfer.To == Scammer && transfer.Amount.Asset == KnownTokens.BscUsdt)
            .Select(AccountSeed.Of).ToList();

        var result = await _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, seeds));

        var usdt = Assert.Single(result.Assets);
        Assert.Equal(Usdt(1_000), usdt.Traced);
        var exchange = graph.NodeOf(Exchange.ToChainAddress())!;
        Assert.Equal(Usdt(600), Assert.Single(usdt.Ends, end => end.NodeKey == exchange.Key).Amount.Amount);
    }

    [Fact]
    public async Task A_link_from_a_bsc_movement_is_read_back_on_bsc()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;
        var seed = AccountSeed.Of(graph.Account.Transfers.First(transfer => transfer.From == Scammer));

        var link = _harness.Service.LinkCrossChain(
            _harness.Case, seed, new ChainTransactionRef(SharedKernel.Chain.Tron, new string('d', 64)), "Brygga");

        Assert.Equal(SharedKernel.Chain.Bsc, link.FromChain);
        _harness.Reopen();
        var read = (await _harness.Load()).CrossChainLinks.Single().FromTransfer!;
        Assert.Equal(seed, read);
        Assert.Equal(SharedKernel.Chain.Bsc, read.Transaction.Chain);
    }

    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse(SharedKernel.Chain.Bsc, "0x" + Hex(label, 40));

    private static EthereumTxHash Hash(string label) =>
        EthereumTxHash.Parse(SharedKernel.Chain.Bsc, "0x" + Hex(label, 64));

    private static AssetAmount Usdt(decimal amount) =>
        new(KnownTokens.BscUsdt, new BigInteger(amount) * BigInteger.Pow(10, 18));

    private static AssetAmount Bnb(decimal amount) =>
        new(Asset.Bnb, new BigInteger(amount) * BigInteger.Pow(10, 18));

    private static (EthereumTransferKind, EthereumAddress, EthereumAddress, AssetAmount) Token(
        EthereumAddress from, EthereumAddress to, AssetAmount amount) =>
        (EthereumTransferKind.Token, from, to, amount);

    /// <summary>Makes a BSC transaction: the signer, its block and its movements.</summary>
    private static EthereumTransaction Tx(
        string label, long height, EthereumAddress from,
        params (EthereumTransferKind Kind, EthereumAddress From, EthereumAddress To, AssetAmount Amount)[] moves)
    {
        var hash = Hash(label);
        var block = new EthereumBlockReference(
            height, FakeAccountSource.HashOf(height),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(3 * height));
        var transfers = moves.Select((move, index) => new EthereumTransfer(
            hash, move.Kind, move.From, move.To, move.Amount, move.Kind == EthereumTransferKind.Native ? 0 : index,
            block)).ToList();
        var value = moves.Where(move => move.Kind == EthereumTransferKind.Native).Select(move => move.Amount)
            .FirstOrDefault(AssetAmount.Zero(Asset.Bnb));
        return new EthereumTransaction(
            hash, block, from, moves[0].To, value, new AssetAmount(Asset.Bnb, 21_000), EthereumOutcome.Succeeded,
            transfers);
    }

    private static string Hex(string label, int digits)
    {
        var text = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)))
            .ToLower(CultureInfo.InvariantCulture);
        return (text + text)[..digits];
    }
}
