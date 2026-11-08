using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// A case in memory (see <see cref="IMemoryCase"/>): it has the modules' tables, so that the chains' sources answer,
/// pace and seal their raw responses exactly as in a real case, and nothing more. It has no file, no journal and no
/// key, and is thrown away when disposed. It never touches a real case, the vault or the disk.
/// </summary>
internal sealed class MemoryCase : IMemoryCase
{
    /// <summary>The case number a case in memory goes by; it is never a file.</summary>
    internal const string MemoryCaseNumber = "minne";

    /// <summary>The one connection; SQLite in memory lives as long as it.</summary>
    private readonly SqliteConnection _connection;

    /// <summary>Lets one read or write at a time, as a real case does.</summary>
    private readonly Lock _gate = new();

    /// <summary>The sealed chains by name: their tables and content columns.</summary>
    private readonly Dictionary<string, SealedChain> _chains = new(StringComparer.Ordinal);

    /// <summary>Every record sealed, in order: its chain and number.</summary>
    private readonly List<(string Chain, long Sequence)> _sealed = [];

    /// <summary>Loads SQLite once per process.</summary>
    static MemoryCase()
    {
        Batteries_V2.Init();
    }

    /// <summary>Creates the case with the modules' tables.</summary>
    /// <param name="contributors">The modules' tables.</param>
    public MemoryCase(IEnumerable<ICaseSchemaContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        foreach (var contributor in contributors)
        {
            foreach (var step in contributor.Steps.OrderBy(step => step.Version))
            {
                using var command = _connection.CreateCommand();
                command.CommandText = step.Sql;
                command.ExecuteNonQuery();
            }

            foreach (var chain in contributor.SealedChains)
            {
                _chains[chain.Name] = chain;
            }
        }

        Info = new CaseInfo(Guid.NewGuid(), MemoryCaseNumber, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public CaseInfo Info { get; }

    /// <inheritdoc/>
    public string Title => string.Empty;

    /// <inheritdoc/>
    public string Path => string.Empty;

    /// <inheritdoc/>
    public bool IsClosed { get; private set; }

    /// <inheritdoc/>
    public bool HasSharingPassword => false;

    /// <inheritdoc/>
    public IReadOnlyList<string> RecoveryFingerprints => [];

    /// <inheritdoc/>
    public IReadOnlyList<long> SealedSince(string chain, int mark)
    {
        lock (_gate)
        {
            return [.. _sealed.Skip(mark).Where(each => each.Chain == chain).Select(each => each.Sequence)];
        }
    }

    /// <inheritdoc/>
    public int Mark()
    {
        lock (_gate)
        {
            return _sealed.Count;
        }
    }

    /// <inheritdoc/>
    public T Read<T>(Func<ICaseReader, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            EnsureOpen();
            using var transaction = _connection.BeginTransaction(deferred: true);
            return work(new Scope(this, transaction));
        }
    }

    /// <inheritdoc/>
    public T Write<T>(Func<ICaseTransaction, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            EnsureOpen();
            var before = _sealed.Count;
            using var transaction = _connection.BeginTransaction();
            try
            {
                var result = work(new Scope(this, transaction));
                transaction.Commit();
                return result;
            }
            catch
            {
                _sealed.RemoveRange(before, _sealed.Count - before);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// A case in memory keeps no journal; the case it is brought into does.
    /// </exception>
    public JournalEntry AppendJournal(JournalDraft draft) => throw NoJournal();

    /// <inheritdoc/>
    public bool SetTitle(string title) => throw new NotSupportedException();

    /// <inheritdoc/>
    public IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue) => [];

    /// <inheritdoc/>
    public void SetSharingPassword(ReadOnlySpan<char> password) => throw new NotSupportedException();

    /// <inheritdoc/>
    public void RemoveSharingPassword() => throw new NotSupportedException();

    /// <inheritdoc/>
    public void AddOperatorAccess() => throw new NotSupportedException();

    /// <inheritdoc/>
    public void Save()
    {
        // Nothing to save: it lives in memory only.
    }

    /// <inheritdoc/>
    public void Close()
    {
        lock (_gate)
        {
            IsClosed = true;
            _connection.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Close();

    /// <summary>Seals a record: the next number in its chain, and a hash over the previous hash and the content.
    /// </summary>
    /// <param name="transaction">The write.</param>
    /// <param name="chainName">The chain.</param>
    /// <param name="values">The content, in the chain's column order.</param>
    /// <returns>The record.</returns>
    private SealedRecord Append(SqliteTransaction transaction, string chainName, IReadOnlyList<object?> values)
    {
        if (!_chains.TryGetValue(chainName, out var chain) || values.Count != chain.ContentColumns.Count)
        {
            throw new ArgumentException("The chain is unknown, or the values do not match its columns.");
        }

        using var last = _connection.CreateCommand();
        last.Transaction = transaction;
        last.CommandText = $"SELECT sequence, hash FROM {chain.Table} ORDER BY sequence DESC LIMIT 1";
        long sequence = 1;
        var previous = new byte[32];
        using (var row = last.ExecuteReader())
        {
            if (row.Read())
            {
                sequence = row.GetInt64(0) + 1;
                previous = (byte[])row.GetValue(1);
            }
        }

        var hash = HashOf(previous, values);
        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        var columns = string.Join(", ", chain.ContentColumns);
        var names = string.Join(", ", chain.ContentColumns.Select((_, index) => "$v" + index));
        insert.CommandText = $"INSERT INTO {chain.Table} (sequence, {columns}, hash) VALUES ($sequence, {names}, $hash)";
        insert.Parameters.AddWithValue("$sequence", sequence);
        for (var index = 0; index < values.Count; index++)
        {
            insert.Parameters.AddWithValue("$v" + index, values[index] ?? DBNull.Value);
        }

        insert.Parameters.AddWithValue("$hash", hash);
        insert.ExecuteNonQuery();
        _sealed.Add((chainName, sequence));
        return new SealedRecord(chainName, sequence, Convert.ToHexStringLower(hash));
    }

    /// <summary>
    /// Hashes a record over the previous hash and its content. The chain in memory is never verified; the hash only
    /// keeps the table's form, and the response is sealed for real in the case it is brought into.
    /// </summary>
    /// <param name="previous">The previous hash.</param>
    /// <param name="values">The content.</param>
    /// <returns>The hash.</returns>
    private static byte[] HashOf(byte[] previous, IReadOnlyList<object?> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(previous);
        foreach (var value in values)
        {
            hash.AppendData(value switch
            {
                byte[] bytes => bytes,
                null => [],
                _ => Encoding.UTF8.GetBytes(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!),
            });
        }

        return hash.GetHashAndReset();
    }

    /// <summary>Refuses work once closed, as a real case does.</summary>
    /// <exception cref="CaseException">It is closed.</exception>
    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new CaseException(CaseError.Closed);
        }
    }

    /// <summary>The error for a journal line.</summary>
    /// <returns>The error.</returns>
    private static NotSupportedException NoJournal() =>
        new("A case in memory keeps no journal; the case it is brought into does.");

    /// <summary>A read or write in the case in memory.</summary>
    /// <param name="owner">The case.</param>
    /// <param name="transaction">The transaction.</param>
    private sealed class Scope(MemoryCase owner, SqliteTransaction transaction) : ICaseTransaction
    {
        /// <inheritdoc/>
        public DbCommand CreateCommand(string sql)
        {
            var command = owner._connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        /// <inheritdoc/>
        public JournalEntry AppendJournal(JournalDraft draft) => throw NoJournal();

        /// <inheritdoc/>
        public SealedRecord AppendSealed(string chain, IReadOnlyList<object?> values) =>
            owner.Append(transaction, chain, values);
    }
}
