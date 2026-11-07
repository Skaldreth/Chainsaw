#if DEBUG
using System.Buffers.Binary;
using System.Numerics;

namespace Chainsaw.App.TestData;

/// <summary>A made-up block: its header's parts and its hash.</summary>
/// <param name="Height">The height.</param>
/// <param name="Hash">The hash, as shown.</param>
/// <param name="Previous">The previous block's hash, as shown.</param>
/// <param name="MerkleRoot">The merkle root of its transactions, as shown.</param>
/// <param name="Time">The block's time.</param>
/// <param name="Header">The 80 bytes, as <c>/block/:hash/header</c> gives them.</param>
/// <param name="Transactions">How many transactions it has, a coinbase always among them.</param>
public sealed record TestBlock(
    long Height, string Hash, string Previous, string MerkleRoot, DateTimeOffset Time, byte[] Header, int Transactions);

/// <summary>Where a made-up coin was spent.</summary>
/// <param name="By">The spending transaction.</param>
/// <param name="Input">The spending input.</param>
public sealed record TestSpend(TestTransaction By, int Input);

/// <summary>
/// A made-up Bitcoin chain for the test case: transactions built in the scenario's words (pay, spend, the rest back),
/// in blocks with real headers (each hash meets the easiest target, as on a test network), so that the Esplora source
/// can check transactions against their bytes and blocks against their headers as for the real chain.
/// </summary>
public sealed class TestChain
{
    /// <summary>The fee a made-up transaction pays when the scenario names none, in satoshi.</summary>
    public const long DefaultFee = 2_000;

    /// <summary>The time between blocks.</summary>
    public static readonly TimeSpan BlockTime = TimeSpan.FromMinutes(10);

    /// <summary>The easiest target a header can name (regtest's), so that a hash is found in a few tries.</summary>
    private const uint EasiestBits = 0x207F_FFFF;

    /// <summary>The block version every made-up header has.</summary>
    private const int BlockVersion = 0x2000_0000;

    /// <summary>The header's length.</summary>
    private const int HeaderLength = 80;

    /// <summary>The transactions by label.</summary>
    private readonly Dictionary<string, TestTransaction> _byLabel = new(StringComparer.Ordinal);

    /// <summary>The transactions by txid.</summary>
    private readonly Dictionary<string, TestTransaction> _byTxid = new(StringComparer.Ordinal);

    /// <summary>The transactions in the order they were added.</summary>
    private readonly List<TestTransaction> _transactions = [];

    /// <summary>Who spent each coin, by txid and output.</summary>
    private readonly Dictionary<(string Txid, int Index), TestSpend> _spends = [];

    /// <summary>The blocks by height, once sealed.</summary>
    private readonly Dictionary<long, TestBlock> _blocks = [];

    /// <summary>The blocks by hash, once sealed.</summary>
    private readonly Dictionary<string, TestBlock> _blocksByHash = new(StringComparer.Ordinal);

    /// <summary>Creates an empty chain.</summary>
    /// <param name="firstHeight">The lowest block the chain has.</param>
    /// <param name="firstTime">The first block's time.</param>
    public TestChain(long firstHeight, DateTimeOffset firstTime)
    {
        FirstHeight = firstHeight;
        FirstTime = firstTime;
    }

    /// <summary>Gets the lowest block.</summary>
    public long FirstHeight { get; }

    /// <summary>Gets the first block's time.</summary>
    public DateTimeOffset FirstTime { get; }

    /// <summary>Gets the newest block, once sealed.</summary>
    public long Tip { get; private set; }

    /// <summary>Gets the transactions in the order they were added.</summary>
    public IReadOnlyList<TestTransaction> Transactions => _transactions;

    /// <summary>Gives a block's time.</summary>
    /// <param name="height">The height.</param>
    /// <returns>The time.</returns>
    public DateTimeOffset TimeOf(long height) => FirstTime + ((height - FirstHeight) * BlockTime);

    /// <summary>Gives a transaction by the scenario's label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="KeyNotFoundException">The scenario has no such transaction.</exception>
    public TestTransaction this[string label] => _byLabel[label];

    /// <summary>Finds a transaction by txid.</summary>
    /// <param name="txid">The txid.</param>
    /// <returns>The transaction, or <see langword="null"/>.</returns>
    public TestTransaction? Find(string txid) => _byTxid.GetValueOrDefault(txid);

    /// <summary>Finds who spent a coin.</summary>
    /// <param name="txid">The coin's transaction.</param>
    /// <param name="index">The coin's output.</param>
    /// <returns>The spend, or <see langword="null"/> while it is unspent.</returns>
    public TestSpend? SpentBy(string txid, int index) => _spends.GetValueOrDefault((txid, index));

    /// <summary>Finds a block by height.</summary>
    /// <param name="height">The height.</param>
    /// <returns>The block, or <see langword="null"/>.</returns>
    public TestBlock? BlockAt(long height) => _blocks.GetValueOrDefault(height);

    /// <summary>Finds a block by hash.</summary>
    /// <param name="hash">The hash, as shown.</param>
    /// <returns>The block, or <see langword="null"/>.</returns>
    public TestBlock? Block(string hash) => _blocksByHash.GetValueOrDefault(hash);

    /// <summary>Gives an address's transactions, the newest first, as Esplora orders them.</summary>
    /// <param name="script">The address's script.</param>
    /// <returns>The transactions.</returns>
    public IReadOnlyList<TestTransaction> History(string script) =>
    [
        .. _transactions
            .Select((transaction, order) => (Transaction: transaction, Order: order))
            .Where(item => item.Transaction.Outputs.Any(output => output.To.Script == script)
                || item.Transaction.Inputs.Any(input => input.Spent?.To.Script == script))
            .OrderByDescending(item => item.Transaction.Height)
            .ThenByDescending(item => item.Order)
            .Select(item => item.Transaction),
    ];

    /// <summary>Adds new coins to a block, as a miner's reward.</summary>
    /// <param name="label">The scenario's name for it.</param>
    /// <param name="height">The block.</param>
    /// <param name="outputs">Where the coins go.</param>
    /// <returns>The coinbase.</returns>
    public TestTransaction Coinbase(string label, long height, params TestOutput[] outputs) =>
        Add(new TestTransaction(label, height, [new TestInput(null, 0, null)], outputs));

    /// <summary>Gives fresh money to an address, as a coinbase in a block of its own before the story.</summary>
    /// <param name="to">The address.</param>
    /// <param name="value">The amount in satoshi.</param>
    /// <param name="height">The block.</param>
    /// <returns>The coinbase, whose output 0 is the money.</returns>
    public TestTransaction Fund(TestAddress to, long value, long height) =>
        Coinbase("fund:" + to.Label, height, new TestOutput(to, value));

    /// <summary>Starts a transaction that spends coins; finish it with <see cref="TestDraft.Done"/>.</summary>
    /// <param name="label">The scenario's name for it.</param>
    /// <param name="height">The block.</param>
    /// <returns>The draft.</returns>
    public TestDraft Tx(string label, long height) => new(this, label, height);

    /// <summary>
    /// Seals the chain: writes a block for every height from the first to the tip, each with a header whose hash meets
    /// its target and names the block before it.
    /// </summary>
    /// <param name="tip">The newest block.</param>
    /// <exception cref="InvalidOperationException">A transaction is above the tip or below the first block.</exception>
    public void Seal(long tip)
    {
        if (_transactions.Any(transaction => transaction.Height > tip || transaction.Height < FirstHeight))
        {
            throw new InvalidOperationException("A made-up transaction lies outside the chain.");
        }

        Tip = tip;
        var previous = TestTransaction.DisplayHash("chainsaw-testdata:before"u8);
        for (var height = FirstHeight; height <= tip; height++)
        {
            var block = MakeBlock(height, previous);
            _blocks[height] = block;
            _blocksByHash[block.Hash] = block;
            previous = block.Hash;
        }
    }

    /// <summary>Adds a finished transaction and notes which coins it spends.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The same transaction.</returns>
    /// <exception cref="InvalidOperationException">The label is taken, or a coin is spent twice.</exception>
    internal TestTransaction Add(TestTransaction transaction)
    {
        if (!_byLabel.TryAdd(transaction.Label, transaction))
        {
            throw new InvalidOperationException($"The label {transaction.Label} is taken.");
        }

        _byTxid.Add(transaction.Txid, transaction);
        _transactions.Add(transaction);
        for (var index = 0; index < transaction.Inputs.Count; index++)
        {
            var input = transaction.Inputs[index];
            if (!input.IsCoinbase && !_spends.TryAdd((input.PreviousTxid!, input.PreviousIndex), new(transaction, index)))
            {
                throw new InvalidOperationException($"{transaction.Label} spends a coin that is already spent.");
            }
        }

        return transaction;
    }

    /// <summary>Makes one block: a coinbase of its own unless the scenario gave it one, and a header that meets the
    /// easiest target.</summary>
    /// <param name="height">The height.</param>
    /// <param name="previous">The previous block's hash.</param>
    /// <returns>The block.</returns>
    private TestBlock MakeBlock(long height, string previous)
    {
        var txids = _transactions.Where(transaction => transaction.Height == height)
            .OrderBy(transaction => transaction.IsCoinbase ? 0 : 1)
            .Select(transaction => transaction.Txid)
            .ToList();
        if (!_transactions.Any(transaction => transaction.Height == height && transaction.IsCoinbase))
        {
            txids.Insert(0, TestTransaction.DisplayHash(TestAddress.Hash("coinbase:" + height)));
        }

        var merkleRoot = MerkleRoot(txids);
        var time = TimeOf(height);
        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), BlockVersion);
        TestTransaction.Internal(previous).CopyTo(header, 4);
        TestTransaction.Internal(merkleRoot).CopyTo(header, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68, 4), (uint)time.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72, 4), EasiestBits);
        var target = new BigInteger(0x7F_FFFF) << (8 * (0x20 - 3));
        for (uint nonce = 0; ; nonce++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76, 4), nonce);
            var hash = TestTransaction.DisplayHash(header);
            if (new BigInteger(Convert.FromHexString(hash), isUnsigned: true, isBigEndian: true) <= target)
            {
                return new TestBlock(height, hash, previous, merkleRoot, time, header, txids.Count);
            }
        }
    }

    /// <summary>Gives the merkle root of a block's txids, as Bitcoin computes it.</summary>
    /// <param name="txids">The txids, as shown, the coinbase first.</param>
    /// <returns>The root, as shown.</returns>
    private static string MerkleRoot(IReadOnlyList<string> txids)
    {
        var level = txids.Select(TestTransaction.Internal).ToList();
        while (level.Count > 1)
        {
            if (level.Count % 2 == 1)
            {
                level.Add(level[^1]);
            }

            level = [.. Enumerable.Range(0, level.Count / 2).Select(pair =>
            {
                var both = level[2 * pair].Concat(level[(2 * pair) + 1]).ToArray();
                return TestTransaction.Internal(TestTransaction.DisplayHash(both));
            })];
        }

        var root = level[0].ToArray();
        Array.Reverse(root);
        return Convert.ToHexStringLower(root);
    }
}

/// <summary>
/// A made-up transaction being written: the coins it spends, what it pays, and the rest back to an address after the
/// fee, so that the scenario reads as what happens with the money.
/// </summary>
/// <param name="chain">The chain it goes into.</param>
/// <param name="label">The scenario's name for it.</param>
/// <param name="height">The block.</param>
public sealed class TestDraft(TestChain chain, string label, long height)
{
    /// <summary>The inputs so far.</summary>
    private readonly List<TestInput> _inputs = [];

    /// <summary>The outputs so far.</summary>
    private readonly List<TestOutput> _outputs = [];

    /// <summary>Spends an output of an earlier transaction.</summary>
    /// <param name="previous">The earlier transaction's label.</param>
    /// <param name="index">The output.</param>
    /// <returns>The draft.</returns>
    public TestDraft From(string previous, int index = 0)
    {
        var spent = chain[previous];
        _inputs.Add(new TestInput(spent.Txid, index, spent.Outputs[index]));
        return this;
    }

    /// <summary>Pays an amount to an address.</summary>
    /// <param name="to">The address.</param>
    /// <param name="value">The amount in satoshi.</param>
    /// <returns>The draft.</returns>
    public TestDraft Pay(TestAddress to, long value)
    {
        _outputs.Add(new TestOutput(to, value));
        return this;
    }

    /// <summary>Pays what is left after the outputs so far and the fee.</summary>
    /// <param name="to">The address that gets the rest.</param>
    /// <param name="fee">The fee in satoshi.</param>
    /// <returns>The draft.</returns>
    public TestDraft Rest(TestAddress to, long fee = TestChain.DefaultFee) =>
        Pay(to, _inputs.Sum(input => input.Spent!.Value) - _outputs.Sum(output => output.Value) - fee);

    /// <summary>Finishes the transaction and adds it to the chain.</summary>
    /// <returns>The transaction.</returns>
    /// <exception cref="InvalidOperationException">It spends nothing, pays nothing or pays more than it spends.</exception>
    public TestTransaction Done()
    {
        if (_inputs.Count == 0 || _outputs.Count == 0 || _outputs.Any(output => output.Value < 0)
            || _outputs.Sum(output => output.Value) > _inputs.Sum(input => input.Spent!.Value))
        {
            throw new InvalidOperationException($"{label} does not add up.");
        }

        return chain.Add(new TestTransaction(label, height, [.. _inputs], [.. _outputs]));
    }
}
#endif
