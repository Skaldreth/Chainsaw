using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Seals the links to other chains, by hand or from an approved suggestion, with the same rules: the target is on
/// another chain than the money, and one coin or movement continues in one place at a time, so that its amount is
/// never on two lines at once.
/// </summary>
internal sealed class CrossChainLinks : ICrossChainLinks
{
    /// <summary>The longest reason the user may give.</summary>
    public const int MaximumReasonLength = 1_000;

    /// <inheritdoc/>
    public CrossChainLink Link(ICaseTransaction transaction, CrossChainLinkDraft draft)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Origin);
        var reason = draft.Reason?.Trim() ?? string.Empty;
        CheckReason(reason);
        return (draft.From, draft.FromTransfer) switch
        {
            ({ } coin, null) => SealCoin(transaction, coin, draft.To, reason, draft.Origin),
            (null, { } movement) => SealMovement(transaction, movement, draft.To, reason, draft.Origin),
            _ => throw new ArgumentException("A link leaves from a coin or a movement, exactly one.", nameof(draft)),
        };
    }

    /// <inheritdoc/>
    public int UnlinkMadeBy(ICaseTransaction transaction, string analysis, long decision, string? reason)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is not null)
        {
            CheckReason(reason);
        }

        var made = GraphStateStore.ReadLinkHistory(transaction)
            .Where(entry => !entry.IsTakenBack && entry.Link.Origin is { } origin
                && origin.Decision == decision && origin.Analysis == analysis)
            .Select(entry => entry.Link)
            .ToList();
        foreach (var link in made)
        {
            Unlink(transaction, link, reason);
        }

        return made.Count;
    }

    /// <summary>Seals a link from a coin on a UTXO chain, after the rules are checked.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="from">The coin.</param>
    /// <param name="to">The other chain's transaction.</param>
    /// <param name="reason">The checked reason, empty when none is given.</param>
    /// <param name="origin">The approved suggestion, or <see langword="null"/> when made by hand.</param>
    /// <returns>The link.</returns>
    /// <exception cref="ArgumentException">The target is on the coin's chain, or the coin is linked already.</exception>
    public static CrossChainLink SealCoin(
        ICaseTransaction transaction, Outpoint from, ChainTransactionRef to, string reason, CrossChainOrigin? origin)
    {
        if (to.Chain == from.Txid.Chain)
        {
            throw new ArgumentException("A link to another chain leads off the coin's chain.", nameof(to));
        }

        // One coin continues in one place; another link is made after the first is taken back, so that the coin's
        // amount is never on two lines at once.
        if (GraphStateStore.Read(transaction).Links.Any(link => link.From == from))
        {
            throw new ArgumentException(
                "The coin already continues on another chain; take that link back first.", nameof(from));
        }

        var journal = transaction.AppendJournal(new JournalDraft(
            TracingJournalKinds.CrossChainLinked,
            string.Format(CultureInfo.InvariantCulture, "Fortsätter på annan kedja: {0} → {1}", from, to)
                + OriginText(origin),
            JsonSerializer.Serialize(new
            {
                from = from.ToString(), chain = to.Chain.ToString(), to = to.Id, reason, origin = origin?.ToString(),
            })));
        return GraphStateStore.SealLink(transaction, from, to, reason, journal, origin);
    }

    /// <summary>Seals a link from a movement on an account chain, after the rules are checked.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="from">The movement.</param>
    /// <param name="to">The other chain's transaction.</param>
    /// <param name="reason">The checked reason, empty when none is given.</param>
    /// <param name="origin">The approved suggestion, or <see langword="null"/> when made by hand.</param>
    /// <returns>The link.</returns>
    /// <exception cref="ArgumentException">The target is on the movement's chain, or it is linked already.</exception>
    public static CrossChainLink SealMovement(
        ICaseTransaction transaction, AccountSeed from, ChainTransactionRef to, string reason, CrossChainOrigin? origin)
    {
        if (to.Chain == from.Transaction.Chain)
        {
            throw new ArgumentException("A link to another chain leads off the movement's chain.", nameof(to));
        }

        if (GraphStateStore.Read(transaction).Links.Any(link => link.FromTransfer == from))
        {
            throw new ArgumentException(
                "The movement already continues on another chain; take that link back first.", nameof(from));
        }

        var text = MovementText(from);
        var journal = transaction.AppendJournal(new JournalDraft(
            TracingJournalKinds.CrossChainLinked,
            string.Format(CultureInfo.InvariantCulture, "Fortsätter på annan kedja: {0} → {1}", text, to)
                + OriginText(origin),
            JsonSerializer.Serialize(new
            {
                from = text, chain = to.Chain.ToString(), to = to.Id, reason, origin = origin?.ToString(),
            })));
        return GraphStateStore.SealLink(transaction, from, to, reason, journal, origin);
    }

    /// <summary>Seals that a link in force is taken back, with a line in the journal.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="stored">The link as stored.</param>
    /// <param name="reason">The checked reason, or <see langword="null"/>.</param>
    public static void Unlink(ICaseTransaction transaction, CrossChainLink stored, string? reason)
    {
        var journal = transaction.AppendJournal(new JournalDraft(
            TracingJournalKinds.CrossChainUnlinked,
            string.Format(
                CultureInfo.InvariantCulture,
                "Länken till annan kedja togs tillbaka: {0} → {1}",
                stored.FromText,
                stored.To),
            JsonSerializer.Serialize(new { link = stored.Sequence, reason })));
        GraphStateStore.SealUnlink(transaction, stored, reason, journal);
    }

    /// <summary>
    /// Writes an account movement for the journal with its chain, since Ethereum's and BSC's hashes look alike, for
    /// example <c>Bsc token:0x…#3</c>.
    /// </summary>
    /// <param name="from">The movement.</param>
    /// <returns>The text.</returns>
    public static string MovementText(AccountSeed from) => string.Create(
        CultureInfo.InvariantCulture,
        $"{from.Transaction.Chain} {from.Kind.ToString().ToLowerInvariant()}:{from.Transaction.Value}#{from.Position}");

    /// <summary>Checks a reason's length.</summary>
    /// <param name="reason">The reason.</param>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    public static void CheckReason(string reason)
    {
        if (reason is null || reason.Length > MaximumReasonLength)
        {
            throw new ArgumentException("A reason is at most 1 000 characters.", nameof(reason));
        }
    }

    /// <summary>Writes where a link came from for the journal's line, when it was not made by hand.</summary>
    /// <param name="origin">The origin, or <see langword="null"/>.</param>
    /// <returns>For example <c> (godkänt förslag, cross-chain v1)</c>, or nothing.</returns>
    private static string OriginText(CrossChainOrigin? origin) =>
        origin is null ? string.Empty : $" (godkänt förslag, {origin.Analysis})";
}
