using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for <c>cross-chain v1</c>: money into a swap or a bridge, and payments on other chains that may be the
/// same money. The right candidate comes first, a payment whose value does not fit is never a candidate, several that
/// fit are ranked, the memo's recipient comes first, and approving a candidate links the money in the same
/// transaction as the decision, never by itself.
/// </summary>
public sealed class CrossChainTests : IDisposable
{
    /// <summary>The block the payment into the swap is in.</summary>
    private const long Height = 900_000;

    /// <summary>The swap service's deposit address on Bitcoin.</summary>
    private static readonly BitcoinAddress SwapIn = Wpkh("swap-in");

    /// <summary>The victim's address that pays the swap.</summary>
    private static readonly BitcoinAddress Payer = Wpkh("payer");

    /// <summary>When the payment into the swap was mined.</summary>
    private static readonly DateTimeOffset Start = Block(Height).Time;

    private readonly FakeCrossChain _lookup = new();
    private readonly FakeLinks _links = new();
    private readonly Harness _harness;

    public CrossChainTests()
    {
        _harness = new Harness(services =>
        {
            services.AddSingleton<ICrossChainLookup>(_lookup);
            services.AddSingleton<ICrossChainLinks>(_links);
        });
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_payment_whose_value_fits_after_a_fee_comes_first()
    {
        // 0.01 BTC is 6 000 SEK; 0.198 ETH is 5 940 SEK (1 % fee) 20 minutes later.
        var right = Ether("right", "recipient", "0.198", Start.AddMinutes(20));
        var later = Ether("later", "other", "0.190", Start.AddHours(30));
        var wrong = Ether("wrong", "third", "0.5", Start.AddMinutes(30));

        var suggestion = Single(Graph(Deposit(), right, later, wrong));

        Assert.Equal([right.Key, later.Key], suggestion.Finding.Candidates.Select(c => c.Key));
        Assert.Equal(CheckKind.Suggestion, suggestion.Finding.Kind);
        Assert.Equal(AreaSort.OtherChain, suggestion.Finding.Area);
        var best = suggestion.Finding.Candidates[0];
        Assert.Null(best.Address);
        Assert.Equal(right.Transaction, best.Arrival!.Transaction);
        Assert.Contains("1 % trolig avgift", best.Reason, StringComparison.Ordinal);
        Assert.Contains("bedömning", suggestion.Finding.Why, StringComparison.Ordinal);
        Assert.Contains("cross-chain v1", suggestion.Finding.Why, StringComparison.Ordinal);
        Assert.Equal(new Outpoint(Txid("swap-deposit"), 0), suggestion.Finding.Departure!.Coin);
        Assert.Equal(SwapIn, suggestion.Finding.Addresses[0]);
        Assert.Equal(Height, suggestion.Finding.Basis[0].Block!.Height);
    }

    [Fact]
    public void No_suggestion_when_no_value_fits()
    {
        var tooMuch = Ether("much", "recipient", "0.5", Start.AddMinutes(20));
        var tooLittle = Ether("little", "recipient", "0.1", Start.AddMinutes(20));

        Assert.Empty(CrossChain(Run(Graph(Deposit(), tooMuch, tooLittle))));
    }

    [Fact]
    public void Several_that_fit_are_all_candidates_the_soonest_first_when_they_score_alike()
    {
        var first = Ether("first", "a", "0.198", Start.AddMinutes(10));
        var second = Ether("second", "b", "0.198", Start.AddMinutes(40));

        var suggestion = Single(Graph(Deposit(), first, second));

        Assert.Equal([first.Key, second.Key], suggestion.Finding.Candidates.Select(c => c.Key));
        Assert.Equal(suggestion.Finding.Candidates[0].Score, suggestion.Finding.Candidates[1].Score);
    }

    [Fact]
    public void Payments_on_the_same_chain_or_outside_the_window_are_not_weighed()
    {
        var before = Ether("before", "a", "0.198", Start.AddHours(-1));
        var tooLate = Ether("late", "b", "0.198", Start.AddDays(4));

        Assert.Empty(CrossChain(Run(Graph(Deposit(), before, tooLate))));
    }

    [Fact]
    public void The_recipient_the_memo_names_comes_first_also_without_a_rate()
    {
        var named = Account(Chain.Ethereum, "memo-recipient");
        var memo = $"=:ETH.ETH:{named.Value}:0/1/0";
        var other = Ether("other", "other", "0.198", Start.AddMinutes(5));
        var toNamed = Ether("named", "memo-recipient", "0.3", Start.AddHours(3));
        _lookup.Rates.Remove("ETH");

        var suggestion = Single(Graph(Deposit(memo), other, toNamed));

        // Without the ether's rate only the memo's recipient can be weighed; the chain names it.
        var candidate = Assert.Single(suggestion.Finding.Candidates);
        Assert.Equal(toNamed.Key, candidate.Key);
        Assert.Contains("memot =:ETH.ETH:", candidate.Reason, StringComparison.Ordinal);
        Assert.Contains("kursen saknas", candidate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_memo_ranks_its_recipient_above_a_better_fit()
    {
        var named = Account(Chain.Ethereum, "memo-recipient");
        var better = Ether("better", "other", "0.198", Start.AddMinutes(5));
        var toNamed = Ether("named", "memo-recipient", "0.18", Start.AddHours(6));

        var suggestion = Single(Graph(Deposit($"SWAP:ETH.ETH:{named.Value}"), better, toNamed));

        Assert.Equal([toNamed.Key, better.Key], suggestion.Finding.Candidates.Select(c => c.Key));
    }

    [Fact]
    public void The_same_key_on_another_account_chain_weighs_more()
    {
        // Ether into a bridge on Ethereum, USDT out on TRON: to the same 20 bytes as the payer, or to a stranger.
        var payer = Account(Chain.Ethereum, "suspect");
        var bridge = Account(Chain.Ethereum, "bridge");
        var into = Movement(Chain.Ethereum, "into-bridge", payer, bridge, Asset.Ether, "0.2", Start);
        var sameKey = Movement(
            Chain.Tron, "to-same-key", Account(Chain.Tron, "pool"), Account(Chain.Tron, "suspect"), Usdt, "5900",
            Start.AddMinutes(30));
        var stranger = Movement(
            Chain.Tron, "to-stranger", Account(Chain.Tron, "pool"), Account(Chain.Tron, "stranger"), Usdt, "5900",
            Start.AddMinutes(20));
        var graph = Empty() with
        {
            Nodes = [AccountNode(payer), AccountNode(bridge, OtherChainEnd), AccountNode(sameKey.From!),
                AccountNode(sameKey.To), AccountNode(stranger.To)],
            Account = AccountOf(into, sameKey, stranger),
        };

        var suggestion = Single(graph);

        Assert.Equal([sameKey.Key, stranger.Key], suggestion.Finding.Candidates.Select(c => c.Key));
        Assert.Contains("samma nyckel", suggestion.Finding.Candidates[0].Reason, StringComparison.Ordinal);
        Assert.Empty(suggestion.Finding.Addresses);
        Assert.Empty(suggestion.Finding.Basis);
        Assert.Equal(into.Movement, suggestion.Finding.Departure!.Movement);
    }

    [Fact]
    public void Money_already_linked_by_hand_is_not_suggested()
    {
        var right = Ether("right", "recipient", "0.198", Start.AddMinutes(20));
        var graph = Graph(Deposit(), right) with
        {
            CrossChainLinks = [new CrossChainLink(
                1, new Outpoint(Txid("swap-deposit"), 0), right.Transaction, "För hand.", 1, Start)],
        };

        Assert.Empty(CrossChain(Run(graph)));
    }

    [Fact]
    public void Approving_a_candidate_links_the_money_with_the_decision_and_taking_it_back_unlinks_it()
    {
        var right = Ether("right", "recipient", "0.198", Start.AddMinutes(20));
        var suggestion = Single(Graph(Deposit(), right));
        Assert.Empty(_links.Linked);

        var approval = _harness.Service.Decide(_harness.Case, new DecisionRequest(
            suggestion.Analysis, suggestion.Finding.Key, right.Key, CheckVerdict.Approved));

        var draft = Assert.Single(_links.Linked);
        Assert.Equal(new Outpoint(Txid("swap-deposit"), 0), draft.From);
        Assert.Equal(right.Transaction, draft.To);
        Assert.Null(draft.Reason);
        Assert.Equal(new CrossChainOrigin("cross-chain v1", approval.Sequence), draft.Origin);

        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(approval) with { Reason = "Fel" });

        Assert.Equal(("cross-chain v1", approval.Sequence, "Fel"), Assert.Single(_links.Unlinked));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_link_that_cannot_be_made_seals_no_approval()
    {
        var right = Ether("right", "recipient", "0.198", Start.AddMinutes(20));
        var suggestion = Single(Graph(Deposit(), right));
        _links.Refuse = true;
        var before = _harness.Journal(CheckJournalKinds.Decided).Count;

        Assert.Throws<ArgumentException>(() => _harness.Service.Decide(_harness.Case, new DecisionRequest(
            suggestion.Analysis, suggestion.Finding.Key, right.Key, CheckVerdict.Approved)));

        Assert.Equal(before, _harness.Journal(CheckJournalKinds.Decided).Count);
    }

    [Fact]
    public void Taking_back_a_link_from_the_history_takes_its_approval_back()
    {
        var right = Ether("right", "recipient", "0.198", Start.AddMinutes(20));
        var suggestion = Single(Graph(Deposit(), right));
        var approval = _harness.Service.Decide(_harness.Case, new DecisionRequest(
            suggestion.Analysis, suggestion.Finding.Key, right.Key, CheckVerdict.Approved));
        var coin = new Outpoint(Txid("swap-deposit"), 0);
        var link = new CrossChainLink(7, coin, right.Transaction, string.Empty, 1, Start)
        {
            Origin = new CrossChainOrigin("cross-chain v1", approval.Sequence),
        };

        Assert.True(_harness.Service.TakeBackLink(_harness.Case, link, null));

        Assert.Single(_links.Unlinked);
        var after = Single(Graph(Deposit(), right));
        Assert.Null(after.DecisionFor(right.Key));
    }

    [Fact]
    public void A_suggestion_reads_back_as_it_was_sealed_and_is_not_sealed_again()
    {
        var graph = Graph(Deposit(), Ether("right", "recipient", "0.198", Start.AddMinutes(20)));
        var first = Single(graph);

        var again = Run(graph);

        Assert.Equal(0, again.Sealed);
        var read = Assert.Single(CrossChain(again));
        Assert.Equal(first.Finding.Departure, read.Finding.Departure);
        Assert.Equal(first.Finding.Candidates[0].Arrival, read.Finding.Candidates[0].Arrival);
    }

    [Theory]
    [InlineData("=:ETH.ETH:0xabc", "ETH", "ETH", "0xabc")]
    [InlineData("SWAP:TRON.USDT-TR7NHQ:TXyz/TRefund:0/1/0", "TRON", "USDT-TR7NHQ", "TXyz")]
    [InlineData("s:e:0xdef", "ETH", "ETH", "0xdef")]
    [InlineData("=:b:bc1qxyz:100", "BTC", "BTC", "bc1qxyz")]
    public void A_memo_in_THORChains_form_is_read(string text, string chain, string asset, string destination)
    {
        var memo = SwapMemo.Parse(text)!;

        Assert.Equal((chain, asset, destination), (memo.ChainCode, memo.Asset, memo.Destination));
    }

    [Theory]
    [InlineData("ADD:ETH.ETH:0xabc")]
    [InlineData("=:ETH~ETH:thor1abc")]
    [InlineData("=:ETH.ETH")]
    [InlineData("hello")]
    public void Anything_but_a_swap_to_a_chain_is_no_memo(string text) => Assert.Null(SwapMemo.Parse(text));

    [Fact]
    public void A_memo_is_read_from_an_op_return_script_and_names_an_address_only_on_a_chain_Chainsaw_has()
    {
        var recipient = Account(Chain.Ethereum, "memo-recipient");
        var text = $"=:ETH.ETH:{recipient.Value}";

        var memo = SwapMemo.FromScript(Script(text))!;

        Assert.Equal(recipient, memo.Address);
        Assert.Null(SwapMemo.Parse("=:BSC.BNB:0xabc")!.Address);
        Assert.Null(SwapMemo.FromScript("6a"));
        Assert.Null(SwapMemo.FromScript("0014" + new string('0', 40)));
    }

    /// <summary>USDT on TRON, as the facit counts it.</summary>
    private static readonly Asset Usdt = Asset.Token(Chain.Tron, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "USDT", 6);

    /// <summary>The end of the trail a label <em>Swap eller brygga</em> gives.</summary>
    private static readonly TrailEndHint OtherChainEnd =
        new(TrailEndReason.OtherChain, "egen v1", "Swap eller brygga (Syntetisk)");

    private CheckRun Run(TraceGraph graph) => _harness.Service.Run(_harness.Case, graph);

    private StoredCheck Single(TraceGraph graph) => Assert.Single(CrossChain(Run(graph)));

    private static IEnumerable<StoredCheck> CrossChain(CheckRun run) =>
        run.Checks.Where(check => check.Analysis.Name == CheckAnalysisNames.CrossChain);

    /// <summary>The payment of 0.01 BTC into the swap, with a memo when one is given.</summary>
    private static BitcoinTransaction Deposit(string? memo = null)
    {
        var tx = Tx("swap-deposit", Height).In(Payer, 1_010_000).Out(SwapIn, 1_000_000).Build();
        return memo is null
            ? tx
            : tx with
            {
                Outputs = [.. tx.Outputs, new BitcoinOutput(1, BitcoinScriptType.OpReturn, null, Satoshi.Zero,
                    Script(memo), null)],
            };
    }

    /// <summary>A graph of the deposit, the swap's address as an actor the trail ends at, and the payments.</summary>
    private static TraceGraph Graph(BitcoinTransaction deposit, params ChainMovement[] arrivals)
    {
        var nodes = new List<GraphNode>
        {
            BitcoinNode(Payer),
            BitcoinNode(SwapIn, OtherChainEnd),
        };
        nodes.AddRange(arrivals.SelectMany(arrival => new[] { arrival.From!, arrival.To })
            .Distinct()
            .Select(address => AccountNode(address)));
        return Empty() with
        {
            Nodes = nodes,
            Transactions = new Dictionary<Txid, GraphTransaction>
            {
                [deposit.Txid] = new(deposit, LookupStatus.FromCase, null),
            },
            Account = AccountOf(arrivals),
        };
    }

    private static TraceGraph Empty() => new(
        [],
        [],
        [],
        new UtxoGraph(
            new Dictionary<Txid, GraphTransaction>(),
            new Dictionary<Outpoint, CoinSpend>(),
            new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
        [],
        [],
        []);

    private static AccountGraph AccountOf(params ChainMovement[] movements) => AccountGraph.Empty with
    {
        Transfers = [.. movements.Select(Transfer)],
    };

    /// <summary>A payment of ether from a payout address to a recipient.</summary>
    private static ChainMovement Ether(string label, string recipient, string ether, DateTimeOffset time) =>
        Movement(Chain.Ethereum, label, Account(Chain.Ethereum, "payout"), Account(Chain.Ethereum, recipient),
            Asset.Ether, ether, time);

    private static ChainMovement Movement(
        Chain chain, string label, ChainAddress from, ChainAddress to, Asset asset, string amount, DateTimeOffset time)
    {
        var hash = EthereumTxHash.Parse(chain, Hex("tx:" + label, 32, chain == Chain.Ethereum));
        var kind = asset.Kind == AssetKind.Native ? EthereumTransferKind.Native : EthereumTransferKind.Token;
        return new ChainMovement(
            new ChainTransactionRef(chain, hash.Value), from, to, AssetAmount.Parse(asset, amount), 21_000_000,
            "0x" + Hex("block:" + label, 32, false), time)
        {
            Movement = new AccountSeed(hash, kind, 0),
        };
    }

    private static EthereumTransfer Transfer(ChainMovement movement)
    {
        var chain = movement.Transaction.Chain;
        return new EthereumTransfer(
            movement.Movement!.Transaction,
            movement.Movement.Kind,
            EthereumAddress.Parse(chain, movement.From!.Value),
            EthereumAddress.Parse(chain, movement.To.Value),
            movement.Amount,
            0,
            new EthereumBlockReference(movement.Height!.Value, movement.BlockHash, movement.Time!.Value));
    }

    /// <summary>An address on an account chain derived from a label: the same label is the same key on both.</summary>
    private static ChainAddress Account(Chain chain, string label)
    {
        var hex = Hex("account:" + label, 20, false);
        return chain == Chain.Tron
            ? EthereumAddress.Parse(Chain.Tron, "41" + hex).ToChainAddress()
            : EthereumAddress.Parse("0x" + hex).ToChainAddress();
    }

    private static GraphNode BitcoinNode(BitcoinAddress address, params TrailEndHint[] ends) => new(
        "address:" + address.Value, GraphNodeKind.Address, [address], 1, null, null, null, false, null, null,
        NodeExpansion.Expanded, ends, new Dictionary<GraphLayout, NodePosition>());

    private static GraphNode AccountNode(ChainAddress address, params TrailEndHint[] ends) => new(
        $"account:{address.Chain}:{address.Value}", GraphNodeKind.Address, [], 1, null, null, null, false, null, null,
        NodeExpansion.Expanded, ends, new Dictionary<GraphLayout, NodePosition>())
    {
        Chain = address.Chain,
        Accounts = [address],
    };

    /// <summary>An <c>OP_RETURN</c> script that pushes a memo.</summary>
    private static string Script(string memo)
    {
        var data = Encoding.ASCII.GetBytes(memo);
        var push = data.Length < 0x4c ? [(byte)data.Length] : new byte[] { 0x4c, (byte)data.Length };
        return Convert.ToHexStringLower([0x6a, .. push, .. data]);
    }

    private static string Hex(string label, int bytes, bool prefixed)
    {
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label)).AsSpan(0, bytes));
        return prefixed ? "0x" + hex : hex;
    }

    /// <summary>A cross-chain search that finds what the test gives and values it by fixed rates in SEK.</summary>
    private sealed class FakeCrossChain : ICrossChainLookup
    {
        /// <summary>Gets the rates per whole coin, by symbol.</summary>
        public Dictionary<string, decimal> Rates { get; } = new()
        {
            ["BTC"] = 600_000m,
            ["ETH"] = 30_000m,
            ["USDT"] = 1m,
        };

        public Task<CrossChainArrivals> FindAsync(
            ICase @case, TraceGraph graph, LookupMode mode, CancellationToken cancellationToken = default)
        {
            var values = CrossChainPayments.All(graph)
                .Where(payment => Rates.ContainsKey(payment.Amount.Asset.Symbol))
                .ToDictionary(
                    payment => payment.Key,
                    payment => Rates[payment.Amount.Asset.Symbol] * (decimal)payment.Amount.Units
                        / (decimal)BigInteger.Pow(10, payment.Amount.Asset.Decimals));
            return Task.FromResult(CrossChainArrivals.Empty with { Values = values, Currency = "SEK" });
        }
    }

    /// <summary>Links that are only recorded, or refused.</summary>
    private sealed class FakeLinks : ICrossChainLinks
    {
        public List<CrossChainLinkDraft> Linked { get; } = [];

        public List<(string Analysis, long Decision, string? Reason)> Unlinked { get; } = [];

        public bool Refuse { get; set; }

        public CrossChainLink Link(ICaseTransaction transaction, CrossChainLinkDraft draft)
        {
            if (Refuse)
            {
                throw new ArgumentException("The coin already continues on another chain.");
            }

            Linked.Add(draft);
            return new CrossChainLink(Linked.Count, draft.From, draft.To, draft.Reason ?? string.Empty, 0, Start)
            {
                FromTransfer = draft.FromTransfer,
                Origin = draft.Origin,
            };
        }

        public int UnlinkMadeBy(ICaseTransaction transaction, string analysis, long decision, string? reason)
        {
            Unlinked.Add((analysis, decision, reason));
            return 1;
        }
    }
}
