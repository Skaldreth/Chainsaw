using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// What a Solana transaction moved: the transfers its parsed instructions made, between the owners of the accounts
/// involved, checked against what the chain recorded in its balances before and after.
/// </summary>
/// <param name="Transfers">The movements, in the order the transaction ran them.</param>
/// <param name="Problems">
/// What the instructions do not explain, in Swedish: balances that a program changed without an instruction the node
/// parses. The movements given are still the ones the instructions made; what else moved is not shown, never guessed.
/// Empty when the instructions explain every balance.
/// </param>
/// <param name="Skipped">How many movements were not counted, for decimals Chainsaw cannot count in.</param>
internal sealed record SolanaMoves(
    IReadOnlyList<EthereumTransfer> Transfers, IReadOnlyList<string> Problems, int Skipped)
{
    /// <summary>Gets whether the instructions explain every change in the balances.</summary>
    public bool IsComplete => Problems.Count == 0;
}

/// <summary>
/// Reads a Solana transaction's movements from the transfers its instructions made, each one as the chain ran it
/// (principle 2): the System program's transfers of lamports, the Token programs' transfers, mints and burns, and the
/// lamports a closed token account handed over. The balances before and after are the check: the instructions are
/// replayed on the balances before, and when the result is not the balances after for an asset, something else moved
/// it (a program changing lamports itself, a fee withheld by Token-2022); the movements read stay, the gap is said,
/// and a close's amount, which only the replay gives, is left out of that asset.
/// </summary>
/// <remarks>
/// <para>
/// Each movement is between the <em>owners</em> of the accounts: a token account's lamports and tokens are its
/// owner's, as the balances or the instruction that made it name the owner, so that rent paid into a token account and
/// handed back when it closes never makes the token account an actor of its own. A movement between two accounts of one
/// owner is not a movement between parties and is left out.
/// </para>
/// <para>
/// Wrapped SOL is SOL: a transfer of it moves the accounts' lamports, and it is given as SOL, never as a token.
/// </para>
/// </remarks>
internal static class SolanaMovements
{
    /// <summary>Reads a transaction's movements.</summary>
    /// <param name="raw">The transaction.</param>
    /// <param name="block">Its block, as every movement in it carries it.</param>
    /// <returns>The movements, and what the instructions do not explain.</returns>
    public static SolanaMoves Read(SolanaTransactionRaw raw, EthereumBlockReference block)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(block);
        return raw.Failed ? new SolanaMoves([], [], 0) : new Replay(raw).Run(block);
    }

    /// <summary>
    /// Gives an SPL token by its mint: Chainsaw's own known token, or one shown by a short form of its mint, since the
    /// chain does not name a token and a name a source gave would say nothing about what it is.
    /// </summary>
    /// <param name="mint">The mint.</param>
    /// <param name="decimals">Its decimals.</param>
    /// <returns>The token.</returns>
    public static Asset Token(string mint, int decimals) =>
        KnownTokens.Canonical(Asset.Token(Chain.Solana, mint, "SPL " + mint[..4] + "…", decimals));

    /// <summary>The instructions replayed on the balances before, and the movements they make.</summary>
    private sealed class Replay
    {
        /// <summary>The transaction.</summary>
        private readonly SolanaTransactionRaw _raw;

        /// <summary>Each account's lamports as the replay goes, from the balances before less the fee.</summary>
        private readonly Dictionary<EthereumAddress, BigInteger> _lamports = [];

        /// <summary>Each token account's amount as the replay goes, from the balances before.</summary>
        private readonly Dictionary<EthereumAddress, BigInteger> _tokens = [];

        /// <summary>Each token account's mint, owner and decimals, from the balances and the instructions.</summary>
        private readonly Dictionary<EthereumAddress, (string Mint, EthereumAddress? Owner, int? Decimals)> _accounts =
            [];

        /// <summary>The movements, with whether the amount came from the replay alone (a close).</summary>
        private readonly List<(EthereumAddress From, EthereumAddress To, Asset Asset, BigInteger Units, bool Replayed)>
            _moved = [];

        /// <summary>How many movements were not counted.</summary>
        private int _skipped;

        /// <summary>What the instructions could not be read for.</summary>
        private readonly List<string> _problems = [];

        /// <summary>Prepares the replay of a transaction that ran.</summary>
        /// <param name="raw">The transaction.</param>
        public Replay(SolanaTransactionRaw raw)
        {
            _raw = raw;
            for (var index = 0; index < raw.Accounts.Count; index++)
            {
                _lamports[raw.Accounts[index]] = raw.Pre[index] - (index == 0 ? raw.Fee : BigInteger.Zero);
            }

            foreach (var balance in raw.PreTokens.Concat(raw.PostTokens))
            {
                Know(raw.Accounts[balance.AccountIndex], balance.Mint, balance.Owner, balance.Decimals);
            }

            foreach (var hint in raw.Hints)
            {
                Know(hint.Account, hint.Mint, hint.Owner, null);
            }

            foreach (var balance in raw.PreTokens)
            {
                _tokens[raw.Accounts[balance.AccountIndex]] = balance.Amount;
            }
        }

        /// <summary>
        /// Replays the instructions, checks the result against the balances after and gives the movements.
        /// </summary>
        /// <param name="block">The transaction's block.</param>
        /// <returns>The movements.</returns>
        public SolanaMoves Run(EthereumBlockReference block)
        {
            foreach (var move in _raw.Moves)
            {
                Apply(move);
            }

            var unexplainedSol = !SolExplained();
            if (unexplainedSol)
            {
                _problems.Add("Instruktionerna förklarar inte all SOL i tx:en (ett program flyttade lamports själv); "
                    + "bara de överföringar instruktionerna anger visas.");
            }

            foreach (var mint in UnexplainedMints())
            {
                _problems.Add($"Instruktionerna förklarar inte all {Symbol(mint)} i tx:en; bara de överföringar "
                    + "instruktionerna anger visas.");
            }

            var transfers = _moved
                .Where(each => !(each.Replayed && unexplainedSol))
                .Select((each, position) => new EthereumTransfer(
                    _raw.Hash,
                    each.Asset.Kind == AssetKind.Native ? EthereumTransferKind.Native : EthereumTransferKind.Token,
                    each.From,
                    each.To,
                    new AssetAmount(each.Asset, each.Units),
                    position,
                    block))
                .ToList();
            return new SolanaMoves(transfers, _problems, _skipped);
        }

        /// <summary>Takes in what a token account is, the first time each part is known.</summary>
        /// <param name="account">The token account.</param>
        /// <param name="mint">Its mint.</param>
        /// <param name="owner">Its owner, or <see langword="null"/>.</param>
        /// <param name="decimals">Its token's decimals, or <see langword="null"/>.</param>
        private void Know(EthereumAddress account, string mint, EthereumAddress? owner, int? decimals)
        {
            var known = _accounts.GetValueOrDefault(account, (mint, null, null));
            _accounts[account] = (known.Mint, known.Owner ?? owner, known.Decimals ?? decimals);
        }

        /// <summary>Plays one instruction's movement on the balances and records it.</summary>
        /// <param name="move">The movement.</param>
        private void Apply(SolanaMove move)
        {
            switch (move.Kind)
            {
                case SolanaMoveKind.Lamports:
                    MoveLamports(move.From, move.To, move.Amount!.Value, replayed: false);
                    break;
                case SolanaMoveKind.Close:
                    MoveLamports(move.From, move.To, _lamports.GetValueOrDefault(move.From), replayed: true);
                    _tokens[move.From] = BigInteger.Zero;
                    break;
                default:
                    MoveToken(move);
                    break;
            }
        }

        /// <summary>Moves lamports between two accounts in the replay, and records it between their owners.</summary>
        /// <param name="from">The account they leave.</param>
        /// <param name="to">The account that receives them.</param>
        /// <param name="lamports">How many.</param>
        /// <param name="replayed">Whether the amount came from the replay alone.</param>
        private void MoveLamports(EthereumAddress from, EthereumAddress to, BigInteger lamports, bool replayed)
        {
            _lamports[from] = _lamports.GetValueOrDefault(from) - lamports;
            _lamports[to] = _lamports.GetValueOrDefault(to) + lamports;
            Record(OwnerOf(from), OwnerOf(to), Asset.Sol, lamports, replayed);
        }

        /// <summary>Moves, creates or destroys a token in the replay, and records it between the owners.</summary>
        /// <param name="move">The movement.</param>
        private void MoveToken(SolanaMove move)
        {
            var account = move.Kind == SolanaMoveKind.Mint ? move.To : move.From;
            var mint = move.Mint ?? _accounts.GetValueOrDefault(account).Mint;
            var units = move.Amount!.Value;
            if (mint is null)
            {
                _problems.Add("En tokenöverföring i tx:en gäller ett konto vars token inte går att läsa.");
                return;
            }

            if (move.Kind != SolanaMoveKind.Mint)
            {
                _tokens[move.From] = _tokens.GetValueOrDefault(move.From) - units;
            }

            if (move.Kind != SolanaMoveKind.Burn)
            {
                _tokens[move.To] = _tokens.GetValueOrDefault(move.To) + units;
            }

            if (mint == KnownTokens.SolanaWrappedSolMint && move.Kind == SolanaMoveKind.Token)
            {
                // Wrapped SOL is the accounts' own lamports: the transfer moves them, and it is SOL.
                MoveLamports(move.From, move.To, units, replayed: false);
                return;
            }

            var decimals = move.Decimals ?? _accounts.GetValueOrDefault(account).Decimals;
            if (decimals is not { } places || places > Asset.MaximumDecimals)
            {
                _skipped++;
                return;
            }

            var from = move.Kind == SolanaMoveKind.Mint ? move.From : OwnerOf(move.From);
            var to = move.Kind == SolanaMoveKind.Burn ? move.To : OwnerOf(move.To);
            Record(from, to, Token(mint, places), units, replayed: false);
        }

        /// <summary>Records a movement between two parties; one between an owner and itself is not one.</summary>
        /// <param name="from">The party that gave.</param>
        /// <param name="to">The party that received.</param>
        /// <param name="asset">The asset.</param>
        /// <param name="units">How much.</param>
        /// <param name="replayed">Whether the amount came from the replay alone.</param>
        private void Record(EthereumAddress from, EthereumAddress to, Asset asset, BigInteger units, bool replayed)
        {
            if (from != to && units.Sign > 0)
            {
                _moved.Add((from, to, asset, units, replayed));
            }
        }

        /// <summary>Gives an account's owner: a token account's owner, any other account itself.</summary>
        /// <param name="account">The account.</param>
        /// <returns>The owner.</returns>
        private EthereumAddress OwnerOf(EthereumAddress account) =>
            _accounts.TryGetValue(account, out var known) && known.Owner is { } owner ? owner : account;

        /// <summary>Says whether the replay ends at every account's lamports after.</summary>
        /// <returns><see langword="true"/> when it does.</returns>
        private bool SolExplained() =>
            Enumerable.Range(0, _raw.Accounts.Count)
                .All(index => _lamports.GetValueOrDefault(_raw.Accounts[index]) == _raw.Post[index]);

        /// <summary>Gives the mints (not wrapped SOL) whose token accounts the replay does not end at.</summary>
        /// <returns>The mints.</returns>
        private IEnumerable<string> UnexplainedMints()
        {
            var after = _raw.PostTokens.ToDictionary(balance => _raw.Accounts[balance.AccountIndex], b => b.Amount);
            return _accounts
                .Where(pair => pair.Value.Mint != KnownTokens.SolanaWrappedSolMint
                    && _tokens.GetValueOrDefault(pair.Key) != after.GetValueOrDefault(pair.Key))
                .Select(pair => pair.Value.Mint)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
        }

        /// <summary>Gives a mint's symbol for a text.</summary>
        /// <param name="mint">The mint.</param>
        /// <returns>The known token's symbol, or the short form of the mint.</returns>
        private string Symbol(string mint) => Token(mint, _accounts.Values.First(each => each.Mint == mint)
            .Decimals ?? 0).Symbol;
    }
}
