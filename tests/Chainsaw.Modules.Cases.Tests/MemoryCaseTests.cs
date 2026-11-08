using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>A case in memory: the tables of the modules that seal into one chain, sealing and nothing of a real case.
/// </summary>
public sealed class MemoryCaseTests : IDisposable
{
    private static readonly SealedChain Raw = new("t.raw", "t_raw", ["kind", "body"]);

    private readonly IMemoryCase _case = new MemoryCases([new Tables(), new OtherTables()]).Create(Raw.Name);

    public void Dispose() => _case.Dispose();

    [Fact]
    public void Records_are_sealed_in_order_and_told_from_a_mark()
    {
        _case.Write(transaction => transaction.AppendSealed(Raw.Name, ["a", new byte[] { 1 }]));
        var mark = _case.Mark();

        var second = _case.Write(transaction => transaction.AppendSealed(Raw.Name, ["b", new byte[] { 2 }]));

        Assert.Equal(2, second.Sequence);
        Assert.Equal([2L], _case.SealedSince(Raw.Name, mark));
        Assert.Equal([1L, 2L], _case.SealedSince(Raw.Name, 0));
        Assert.Empty(_case.SealedSince("other", 0));
        Assert.Equal("b", _case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT kind FROM t_raw WHERE sequence = 2");
            return (string)command.ExecuteScalar()!;
        }));
    }

    [Fact]
    public void A_write_that_fails_seals_nothing()
    {
        var mark = _case.Mark();

        Assert.Throws<InvalidOperationException>(() => _case.Write<int>(transaction =>
        {
            transaction.AppendSealed(Raw.Name, ["a", new byte[] { 1 }]);
            throw new InvalidOperationException();
        }));

        Assert.Empty(_case.SealedSince(Raw.Name, mark));
        Assert.Equal(1, _case.Write(transaction => transaction.AppendSealed(Raw.Name, ["c", null]).Sequence));
    }

    [Fact]
    public void It_keeps_no_journal_and_refuses_an_unknown_chain_and_work_once_closed()
    {
        Assert.Throws<NotSupportedException>(() => _case.AppendJournal(new JournalDraft("x", "y")));
        Assert.Throws<ArgumentException>(() => _case.Write(transaction => transaction.AppendSealed("nej", ["a"])));
        Assert.Empty(_case.ReadJournal());

        _case.Close();

        Assert.True(_case.IsClosed);
        Assert.Equal(CaseError.Closed,
            Assert.Throws<CaseException>(() => _case.Read(_ => 0)).Error);
    }

    [Fact]
    public void Only_the_tables_of_the_modules_that_seal_into_the_chain_are_made()
    {
        Assert.Throws<ArgumentException>(() =>
            _case.Write(transaction => transaction.AppendSealed(OtherTables.Chain.Name, ["a"])));
        Assert.Equal(0L, _case.Read(reader =>
        {
            using var command = reader.CreateCommand(
                "SELECT count(*) FROM sqlite_master WHERE name = 'o_raw'");
            return (long)command.ExecuteScalar()!;
        }));
    }

    private sealed class Tables : ICaseSchemaContributor
    {
        public string Module => "t";

        public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(1, "t", "CREATE TABLE t_raw (sequence INTEGER PRIMARY KEY, kind TEXT, body BLOB, hash BLOB NOT NULL);"),
        ];

        public IReadOnlyList<SealedChain> SealedChains { get; } = [Raw];
    }

    private sealed class OtherTables : ICaseSchemaContributor
    {
        public static readonly SealedChain Chain = new("o.raw", "o_raw", ["kind"]);

        public string Module => "o";

        public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(1, "o", "CREATE TABLE o_raw (sequence INTEGER PRIMARY KEY, kind TEXT, hash BLOB NOT NULL);"),
        ];

        public IReadOnlyList<SealedChain> SealedChains { get; } = [Chain];
    }
}
