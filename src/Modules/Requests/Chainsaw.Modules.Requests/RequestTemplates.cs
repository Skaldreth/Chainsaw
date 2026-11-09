using System.Globalization;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// Who and when a request is made: added to the draft when it is composed, so that the preview and the file say the
/// same.
/// </summary>
/// <param name="CaseNumber">The case's number; never its title, which may name the victim.</param>
/// <param name="Operator">The operator who makes it, or <see langword="null"/> when the profile is not set.</param>
/// <param name="Utc">When it is made.</param>
internal sealed record RequestContext(string CaseNumber, OperatorProfile? Operator, DateTimeOffset Utc);

/// <summary>
/// The templates: a request for information to the place where the money left the chain, and a freeze to a token's
/// issuer, in the words of <see cref="RequestTexts"/>. They take only what the draft holds, so nothing else of the
/// case can reach the file (principle 4).
/// </summary>
internal static class RequestTemplates
{
    /// <summary>How times are written: the full date and the second, which an exchange searches its logs by.</summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>How the time a request is made is written.</summary>
    private const string MadeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The fewest decimals an amount is written with.</summary>
    private const int LeastDecimals = 2;

    /// <summary>The characters a Windows file name cannot hold.</summary>
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Composes a request from its template.</summary>
    /// <param name="draft">What it is made from.</param>
    /// <param name="context">Who makes it and when.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentException">
    /// A request for information has no deposits, or a freeze has no holding.
    /// </exception>
    public static RequestDocument Compose(RequestDraft draft, RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(context);
        var words = RequestTexts.Of(draft.Language);
        var blocks = draft.Kind == RequestKind.Freeze
            ? Freeze(draft, context, words)
            : Information(draft, context, words);
        var title = blocks[0].Text;
        return new RequestDocument(title, blocks, RequestMarkdown.Write(blocks), FileName(draft, context, words))
        {
            MadeUtc = context.Utc,
        };
    }

    /// <summary>Writes an amount exactly, in the language's decimal separator, for example <c>0,215 BTC</c>.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>The text.</returns>
    public static string Amount(AssetAmount amount, RequestTexts words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var parts = amount.ToDecimalString().Split('.');
        var fraction = (parts.Length > 1 ? parts[1].TrimEnd('0') : string.Empty).PadRight(LeastDecimals, '0');
        return parts[0] + words.DecimalSeparator + fraction + " " + amount.Asset.Symbol;
    }

    /// <summary>The blocks of a request for information.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="context">Who and when.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>The blocks.</returns>
    private static List<RequestBlock> Information(RequestDraft draft, RequestContext context, RequestTexts words)
    {
        if (draft.Deposits.Count == 0)
        {
            throw new ArgumentException("A request for information takes at least one deposit.", nameof(draft));
        }

        var blocks = Head($"{words.InformationTitle} · {draft.Target.Name}", context, draft.TimeZone, words);
        blocks.Add(new RequestBlock(RequestBlockKind.Heading, words.AskHeading));
        blocks.Add(new RequestBlock(RequestBlockKind.Paragraph, words.InformationAsk));
        blocks.Add(new RequestBlock(RequestBlockKind.Heading, words.RecipientHeading));
        blocks.Add(RequestBlock.Table(
            [words.AddressColumn, words.ChainColumn, words.KindColumn],
            [.. draft.Target.Addresses.Select(address => (IReadOnlyList<string>)
                [address.Value, address.Chain.ToString(), words.KindName(draft.Target.Kind)])],
            0));
        blocks.Add(new RequestBlock(RequestBlockKind.Note, words.AssessmentNote));
        AddDeposits(blocks, draft, words);
        AddTrail(blocks, draft, words);
        blocks.Add(new RequestBlock(RequestBlockKind.Note, words.Closing));
        return blocks;
    }

    /// <summary>The blocks of a freeze, addressed to the token's issuer.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="context">Who and when.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>The blocks.</returns>
    private static List<RequestBlock> Freeze(RequestDraft draft, RequestContext context, RequestTexts words)
    {
        var holding = draft.Holding
            ?? throw new ArgumentException("A freeze names the token and its issuer.", nameof(draft));
        var token = holding.Left.Asset.Symbol;
        var blocks = Head($"{words.FreezeTitle} · {holding.Issuer} ({token})", context, draft.TimeZone, words);
        blocks.Add(new RequestBlock(RequestBlockKind.Heading, words.AskHeading));
        blocks.Add(new RequestBlock(
            RequestBlockKind.Paragraph, string.Format(CultureInfo.InvariantCulture, words.FreezeAsk, token)));
        blocks.Add(new RequestBlock(RequestBlockKind.Heading, words.HoldingHeading));
        blocks.Add(RequestBlock.Table(
            [words.AddressColumn, words.ChainColumn, words.TokenColumn, words.LeftColumn],
            [.. draft.Target.Addresses.Select(address => (IReadOnlyList<string>)
                [address.Value, address.Chain.ToString(), Token(holding.Left.Asset), Amount(holding.Left, words)])],
            0));
        blocks.Add(new RequestBlock(RequestBlockKind.Note, words.LeftNote));
        if (draft.Deposits.Count > 0)
        {
            AddDeposits(blocks, draft, words);
        }

        AddTrail(blocks, draft, words);
        blocks.Add(new RequestBlock(RequestBlockKind.Note, words.Closing));
        return blocks;
    }

    /// <summary>The title and the line of who made it and when.</summary>
    /// <param name="title">The title.</param>
    /// <param name="context">Who and when.</param>
    /// <param name="zone">The local time zone.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>The first blocks.</returns>
    private static List<RequestBlock> Head(string title, RequestContext context, TimeZoneInfo zone, RequestTexts words)
    {
        var local = TimeZoneInfo.ConvertTime(context.Utc, zone);
        var localText = local.ToString(MadeFormat, CultureInfo.InvariantCulture);
        var utcText = context.Utc.UtcDateTime.ToString(MadeFormat, CultureInfo.InvariantCulture);
        var made = $"{words.CaseWord} {context.CaseNumber} · {words.MadeWord} {localText} ({utcText} UTC)";
        if (context.Operator is { } person)
        {
            var who = string.Join(", ", new[] { person.Name, person.Title, person.Organization }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            made += $" {words.ByWord} {who}";
        }

        return [new RequestBlock(RequestBlockKind.Title, title), new RequestBlock(RequestBlockKind.Note, made)];
    }

    /// <summary>Adds the deposits as a table: times local and in UTC, amounts, addresses, txs and blocks.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    /// <param name="words">The language's words.</param>
    private static void AddDeposits(List<RequestBlock> blocks, RequestDraft draft, RequestTexts words)
    {
        blocks.Add(new RequestBlock(
            RequestBlockKind.Heading,
            string.Create(CultureInfo.InvariantCulture, $"{words.DepositsHeading} · {draft.Deposits.Count}")));
        var rows = draft.Deposits
            .OrderBy(deposit => deposit.Time ?? DateTimeOffset.MaxValue)
            .Select(deposit => (IReadOnlyList<string>)
            [
                LocalTime(deposit.Time, draft.TimeZone, words),
                UtcTime(deposit.Time, words),
                Amount(deposit.Amount, words),
                deposit.To.Value,
                deposit.Transaction,
                deposit.BlockHeight?.ToString(CultureInfo.InvariantCulture) ?? words.Unconfirmed,
                deposit.BlockHash ?? (deposit.BlockHeight is null ? words.Unconfirmed : words.Unknown),
            ])
            .ToList();
        blocks.Add(RequestBlock.Table(
            [
                words.LocalColumn, words.UtcColumn, words.AmountColumn, words.ToColumn, words.TransactionColumn,
                words.HeightColumn, words.HashColumn,
            ],
            rows,
            3, 4, 6));
    }

    /// <summary>Adds the trail in one sentence, when it is taken along.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    /// <param name="words">The language's words.</param>
    private static void AddTrail(List<RequestBlock> blocks, RequestDraft draft, RequestTexts words)
    {
        if (draft.Trail is not { } trail)
        {
            return;
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Heading, words.TrailHeading));
        var sentence = string.Join(" → ", trail.Steps) + ": "
            + string.Format(CultureInfo.InvariantCulture, words.TrailText, trail.Hops, trail.Method);
        foreach (var swap in trail.Swaps)
        {
            sentence += " " + string.Format(
                CultureInfo.InvariantCulture, words.SwapText, swap.Pill, swap.Transaction, swap.Analysis, swap.Share);
            sentence += " " + PriceSentence(words, swap);
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Paragraph, sentence));
    }

    /// <summary>
    /// Says whether a swap's price was tested, and by how much the values differed, so that a request never says the
    /// value follows without saying whether its price was tested.
    /// </summary>
    /// <param name="words">The language's words.</param>
    /// <param name="swap">The swap.</param>
    /// <returns>The sentence.</returns>
    private static string PriceSentence(RequestTexts words, RequestSwap swap) => swap.PriceGap is { } gap
        ? string.Format(CultureInfo.InvariantCulture, words.SwapPriceTestedText, swap.PriceCheck, gap)
        : string.Format(CultureInfo.InvariantCulture, words.SwapPriceUntestedText, swap.PriceCheck);

    /// <summary>Writes a block's time in the local zone, with its offset; or that it is unconfirmed.</summary>
    /// <param name="time">The time.</param>
    /// <param name="zone">The zone.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>For example <c>2026-10-28 21:14:05 +01:00</c>.</returns>
    internal static string LocalTime(DateTimeOffset? time, TimeZoneInfo zone, RequestTexts words) =>
        time is { } at
            ? TimeZoneInfo.ConvertTime(at, zone).ToString(TimeFormat + " zzz", CultureInfo.InvariantCulture)
            : words.Unconfirmed;

    /// <summary>Writes a block's time in UTC; or that it is unconfirmed.</summary>
    /// <param name="time">The time.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>For example <c>2026-10-28 20:14:05</c>.</returns>
    internal static string UtcTime(DateTimeOffset? time, RequestTexts words) =>
        time is { } at ? at.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture) : words.Unconfirmed;

    /// <summary>Names a token with its contract, so that the issuer knows which it is.</summary>
    /// <param name="asset">The token.</param>
    /// <returns>For example <c>USDT (0xdac1…)</c>.</returns>
    private static string Token(Asset asset) =>
        asset.Contract is { } contract ? $"{asset.Symbol} ({contract})" : asset.Symbol;

    /// <summary>
    /// Names the file: the case number, the kind, the recipient and the day, without the characters Windows refuses.
    /// </summary>
    /// <param name="draft">The draft.</param>
    /// <param name="context">Who and when.</param>
    /// <param name="words">The language's words.</param>
    /// <returns>For example <c>2026-0117 Begäran Börs A 2026-11-07.md</c>.</returns>
    private static string FileName(RequestDraft draft, RequestContext context, RequestTexts words)
    {
        var day = TimeZoneInfo.ConvertTime(context.Utc, draft.TimeZone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = draft.Kind == RequestKind.Freeze && draft.Holding is { } holding
            ? $"{context.CaseNumber} {words.FreezeFileWord} {holding.Issuer} {day}"
            : $"{context.CaseNumber} {words.FileWord} {draft.Target.Name} {day}";
        return SafeFileName(name);
    }

    /// <summary>
    /// Makes a file's name of a text: without the characters Windows refuses, single spaces, and <c>.md</c> last.
    /// </summary>
    /// <param name="name">The name without its extension.</param>
    /// <returns>The file's name.</returns>
    internal static string SafeFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var safe = new string([.. name.Select(c => char.IsControl(c) || Forbidden.Contains(c) ? ' ' : c)]);
        return string.Join(' ', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ') + ".md";
    }
}
