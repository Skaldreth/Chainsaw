using System.Globalization;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// The words of a report (sketch RP1b), in Swedish: the report is an underlay for the user's own intelligence work,
/// not addressed to anyone abroad.
/// </summary>
internal static class ReportTexts
{
    /// <summary>The title, followed by the case number.</summary>
    public const string Title = "Underrättelse · ärende ";

    /// <summary>What the report is, at the end of the line of who made it.</summary>
    public const string Kind = "ur Chainsaw · underlag för underrättelse";

    /// <summary>The word before who made it.</summary>
    public const string By = "av";

    /// <summary>The word before when it was made.</summary>
    public const string Made = "Upprättad";

    /// <summary>The heading of the summary.</summary>
    public const string SummaryHeading = "Sammanfattning";

    /// <summary>The summary when no trail joins the actors marked.</summary>
    public const string NoTrails = "Inga spår mellan de markerade aktörerna. Markera fler i grafen.";

    /// <summary>The summary's sentence: the method, the number of trails and where the money went.</summary>
    public const string SummaryText = "Spårade pengar har följts med {0} i {1} spår: {2}.";

    /// <summary>The heading of a trail, followed by its start and end.</summary>
    public const string TrailHeading = "Spåret ";

    /// <summary>The line under a trail's table: the traced amount and the method, a conclusion.</summary>
    public const string TracedNote = "Spårat: {0} · {1}, en bedömning";

    /// <summary>The line under a trail's table when the traced amount is not known.</summary>
    public const string UntracedNote = "{0}, en bedömning";

    /// <summary>The value at the rate when the money moved, added to the traced amount.</summary>
    public const string ValueNote = " till kursen vid tx-tiden";

    /// <summary>The heading of the cash-outs.</summary>
    public const string CashOutHeading = "Uttag";

    /// <summary>A cash-out: where, when the traced money came in, how much, and that a request can be made.</summary>
    public const string CashOutItem = "{0}: insättning {1}, {2} spårat · underlag för en begäran";

    /// <summary>A sanctioned end among the cash-outs.</summary>
    public const string SanctionedItem = "{0}: insättning {1}, {2} spårat · sanktionerad";

    /// <summary>The heading of the conclusions.</summary>
    public const string ConclusionsHeading = "Slutsatser";

    /// <summary>The word before what a conclusion rests on.</summary>
    public const string Why = "Varför: ";

    /// <summary>The heading of the labels.</summary>
    public const string LabelsHeading = "Etiketter och källor";

    /// <summary>The heading of the check of the hash chain.</summary>
    public const string LedgerHeading = "Kontroll";

    /// <summary>The check when the case was found unchanged: how many chains, the journal's line and when.</summary>
    public const string LedgerIntact =
        "Topphash över ärendets {0} förseglade kedjor (SHA-256 av kedja:sekvens:hash, sorterade efter kedjan), "
        + "med journalen vid rad {1}, kontrollerad {2} ({3} UTC): inga ändringar.";

    /// <summary>The check when a chain was found broken, or the case unsound.</summary>
    public const string LedgerBroken =
        "Topphash över ärendets {0} förseglade kedjor, med journalen vid rad {1}, kontrollerad {2} ({3} UTC): "
        + "kontrollen fann ändringar i ärendet. Kör Verifiera innan rapporten används.";

    /// <summary>The mark after a step whose transfer could not be told traced, though its line carries traced money.
    /// </summary>
    public const string UntracedStep = " (ej spårad)";

    /// <summary>The check before it has run; an export always runs it.</summary>
    public const string LedgerNotChecked = "Hashkedjan kontrolleras när rapporten exporteras.";

    /// <summary>The last line: what is fact and what is a judgement (principle 2).</summary>
    public const string Closing =
        "Kedjan är fakta; kluster, etiketter, spårade belopp och slutsatser är bedömningar. Varje steg kan "
        + "kontrolleras mot kedjan med tx, blockhöjd och blockhash.";

    /// <summary>The word in the file's name.</summary>
    public const string FileWord = "underrättelse";

    /// <summary>The columns of a trail's table, without the chain's.</summary>
    public static readonly IReadOnlyList<string> Columns = ["Steg", "Tid (lokal)", "UTC", "Belopp", "Tx"];

    /// <summary>The chain's columns, added when <see cref="ReportPart.Chain"/> is on.</summary>
    public static readonly IReadOnlyList<string> ChainColumns = ["Block", "Blockhash"];

    /// <summary>Names how a trail ends, as its heading's tag says it.</summary>
    /// <param name="end">The end.</param>
    /// <returns>The tag, or an empty text for a trail that goes on.</returns>
    public static string Tag(ReportTrailEnd end) => end switch
    {
        ReportTrailEnd.CashOut => "Uttag funnet",
        ReportTrailEnd.Sanctioned => "Sanktionerad",
        ReportTrailEnd.Risk => "Spåret tar slut",
        ReportTrailEnd.Spent => "Spenderas",
        ReportTrailEnd.OtherChain => "Fortsätter på annan kedja",
        _ => string.Empty,
    };

    /// <summary>Gives the tone of a trail's tag.</summary>
    /// <param name="end">The end.</param>
    /// <returns>The tone.</returns>
    public static RequestTagTone Tone(ReportTrailEnd end) => end switch
    {
        ReportTrailEnd.CashOut => RequestTagTone.CashOut,
        ReportTrailEnd.Sanctioned or ReportTrailEnd.Risk => RequestTagTone.Risk,
        ReportTrailEnd.Spent => RequestTagTone.Spent,
        ReportTrailEnd.OtherChain => RequestTagTone.OtherChain,
        _ => RequestTagTone.None,
    };
}

/// <summary>
/// Composes a report from its draft (sketch RP1b), part by part in the order of <see cref="ReportPart"/>. It takes
/// only what the draft holds, so nothing else of the case can reach the file (principle 4).
/// </summary>
internal static class ReportTemplate
{
    /// <summary>How the time a report is made is written.</summary>
    private const string MadeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The Swedish words for amounts and times, shared with the requests.</summary>
    private static readonly RequestTexts Words = RequestTexts.Of(RequestLanguage.Swedish);

    /// <summary>Composes the report.</summary>
    /// <param name="draft">What it is made from.</param>
    /// <param name="context">Who makes it and when.</param>
    /// <returns>The document.</returns>
    public static RequestDocument Compose(ReportDraft draft, RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(context);
        var blocks = Head(draft, context);
        AddSummary(blocks, draft);
        AddTrails(blocks, draft);
        AddCashOuts(blocks, draft);
        AddConclusions(blocks, draft);
        AddLabels(blocks, draft);
        AddLedger(blocks, draft);
        blocks.Add(new RequestBlock(RequestBlockKind.Note, ReportTexts.Closing));
        return new RequestDocument(blocks[0].Text, blocks, RequestMarkdown.Write(blocks), FileName(draft, context))
        {
            MadeUtc = context.Utc,
        };
    }

    /// <summary>The title and the line of who made it and when; the case's number, never its title.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="context">Who and when.</param>
    /// <returns>The first blocks.</returns>
    private static List<RequestBlock> Head(ReportDraft draft, RequestContext context)
    {
        var made = $"{ReportTexts.Made} {Moment(context.Utc, draft.TimeZone)}";
        if (context.Operator is { } person)
        {
            var who = string.Join(", ", new[] { person.Name, person.Title, person.Organization }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            made += $" {ReportTexts.By} {who}";
        }

        return
        [
            new RequestBlock(RequestBlockKind.Title, ReportTexts.Title + context.CaseNumber),
            new RequestBlock(RequestBlockKind.Note, $"{made} {ReportTexts.Kind}"),
        ];
    }

    /// <summary>Adds the summary: the method, the trails and how much reached each end.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    private static void AddSummary(List<RequestBlock> blocks, ReportDraft draft)
    {
        if (!draft.Has(ReportPart.Summary))
        {
            return;
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Heading, ReportTexts.SummaryHeading));
        if (draft.Trails.Count == 0)
        {
            blocks.Add(new RequestBlock(RequestBlockKind.Paragraph, ReportTexts.NoTrails));
            return;
        }

        var ends = string.Join(", ", draft.Trails.Select(trail => trail.Traced.Count > 0
            ? $"{Amounts(trail.Traced)} till {trail.To}"
            : $"till {trail.To}"));
        blocks.Add(new RequestBlock(RequestBlockKind.Paragraph, string.Format(
            CultureInfo.InvariantCulture,
            ReportTexts.SummaryText,
            draft.Method,
            draft.Trails.Count,
            ends)));
    }

    /// <summary>Adds each trail: a heading with its end, the steps as a table and the traced amount under it.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    private static void AddTrails(List<RequestBlock> blocks, ReportDraft draft)
    {
        if (!draft.Has(ReportPart.Trail))
        {
            return;
        }

        var chain = draft.Has(ReportPart.Chain);
        IReadOnlyList<string> columns = chain ? [.. ReportTexts.Columns, .. ReportTexts.ChainColumns] : ReportTexts.Columns;
        int[] mono = chain ? [4, 6] : [4];
        foreach (var trail in draft.Trails)
        {
            blocks.Add(new RequestBlock(RequestBlockKind.Heading, $"{ReportTexts.TrailHeading}{trail.From} → {trail.To}")
            {
                Tag = ReportTexts.Tag(trail.End),
                TagTone = ReportTexts.Tone(trail.End),
            });
            var rows = trail.Steps.Select(step => Row(step, draft.TimeZone, chain)).ToList();
            blocks.Add(RequestBlock.Table(columns, rows, mono));
            blocks.Add(new RequestBlock(RequestBlockKind.Note, TracedNote(trail, draft)));
        }
    }

    /// <summary>Writes a step as a row: who to whom, the times, the amount, the transaction and its block.</summary>
    /// <param name="step">The step.</param>
    /// <param name="zone">The local zone.</param>
    /// <param name="chain">Whether the block's height and hash are written.</param>
    /// <returns>The row.</returns>
    private static IReadOnlyList<string> Row(ReportStep step, TimeZoneInfo zone, bool chain)
    {
        var row = new List<string>
        {
            $"{step.From} → {step.To}{(step.IsTraced ? string.Empty : ReportTexts.UntracedStep)}",
            RequestTemplates.LocalTime(step.Time, zone, Words),
            RequestTemplates.UtcTime(step.Time, Words),
            StepAmount(step.Amount),
            step.Transaction,
        };
        if (chain)
        {
            row.Add(step.BlockHeight?.ToString(CultureInfo.InvariantCulture) ?? Words.Unconfirmed);
            row.Add(step.BlockHash ?? (step.BlockHeight is null ? Words.Unconfirmed : Words.Unknown));
        }

        return row;
    }

    /// <summary>Writes the line under a trail: the traced amount, its value when known, and the method.</summary>
    /// <param name="trail">The trail.</param>
    /// <param name="draft">The draft.</param>
    /// <returns>The line.</returns>
    private static string TracedNote(ReportTrail trail, ReportDraft draft)
    {
        if (trail.Traced.Count == 0)
        {
            return string.Format(CultureInfo.InvariantCulture, ReportTexts.UntracedNote, draft.Method);
        }

        var amount = Amounts(trail.Traced);
        if (draft.Has(ReportPart.Sek) && trail.Value is { } value)
        {
            amount += $" · {value}{ReportTexts.ValueNote}";
        }

        return string.Format(CultureInfo.InvariantCulture, ReportTexts.TracedNote, amount, draft.Method);
    }

    /// <summary>Adds the cash-outs: where the traced money left the chain, and the sanctioned ends.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    private static void AddCashOuts(List<RequestBlock> blocks, ReportDraft draft)
    {
        var ends = draft.Trails.Where(trail => trail.End is ReportTrailEnd.CashOut or ReportTrailEnd.Sanctioned)
            .ToList();
        if (!draft.Has(ReportPart.CashOut) || ends.Count == 0)
        {
            return;
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Heading, ReportTexts.CashOutHeading));
        foreach (var trail in ends)
        {
            var format = trail.End == ReportTrailEnd.Sanctioned ? ReportTexts.SanctionedItem : ReportTexts.CashOutItem;
            var traced = trail.Traced.Count > 0 ? Amounts(trail.Traced) : Words.Unknown;
            blocks.Add(new RequestBlock(RequestBlockKind.Item, string.Format(
                CultureInfo.InvariantCulture,
                format,
                trail.To,
                RequestTemplates.LocalTime(trail.Reached, draft.TimeZone, Words),
                traced)));
        }
    }

    /// <summary>Adds the conclusions, each followed by why.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    private static void AddConclusions(List<RequestBlock> blocks, ReportDraft draft)
    {
        if (!draft.Has(ReportPart.Conclusions) || draft.Conclusions.Count == 0)
        {
            return;
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Heading, ReportTexts.ConclusionsHeading));
        foreach (var conclusion in draft.Conclusions)
        {
            blocks.Add(new RequestBlock(RequestBlockKind.Item, conclusion.Statement));
            blocks.Add(new RequestBlock(RequestBlockKind.Reason, ReportTexts.Why + conclusion.Why));
        }
    }

    /// <summary>Adds the labels and their sources.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    private static void AddLabels(List<RequestBlock> blocks, ReportDraft draft)
    {
        if (!draft.Has(ReportPart.Labels) || draft.Labels.Count == 0)
        {
            return;
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Heading, ReportTexts.LabelsHeading));
        foreach (var label in draft.Labels)
        {
            blocks.Add(new RequestBlock(RequestBlockKind.Item, $"{label.Text} · {label.Source}"));
        }
    }

    /// <summary>Adds the check of the hash chain and its top hash.</summary>
    /// <param name="blocks">The blocks, added to.</param>
    /// <param name="draft">The draft.</param>
    private static void AddLedger(List<RequestBlock> blocks, ReportDraft draft)
    {
        if (!draft.Has(ReportPart.Ledger))
        {
            return;
        }

        blocks.Add(new RequestBlock(RequestBlockKind.Heading, ReportTexts.LedgerHeading));
        if (draft.Ledger is not { } ledger)
        {
            blocks.Add(new RequestBlock(RequestBlockKind.Paragraph, ReportTexts.LedgerNotChecked));
            return;
        }

        var local = TimeZoneInfo.ConvertTime(ledger.CheckedUtc, draft.TimeZone)
            .ToString(MadeFormat, CultureInfo.InvariantCulture);
        var utc = ledger.CheckedUtc.UtcDateTime.ToString(MadeFormat, CultureInfo.InvariantCulture);
        var format = ledger.IsIntact ? ReportTexts.LedgerIntact : ReportTexts.LedgerBroken;
        blocks.Add(new RequestBlock(
            RequestBlockKind.Paragraph,
            string.Format(CultureInfo.InvariantCulture, format, ledger.Chains, ledger.JournalLine, local, utc)));
        blocks.Add(new RequestBlock(RequestBlockKind.Hash, ledger.Digest));
    }

    /// <summary>Writes a moment in the local zone with UTC in brackets.</summary>
    /// <param name="utc">The moment.</param>
    /// <param name="zone">The local zone.</param>
    /// <returns>For example <c>2026-11-08 14:40 (13:40 UTC)</c>.</returns>
    private static string Moment(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone).ToString(MadeFormat, CultureInfo.InvariantCulture);
        return $"{local} ({utc.UtcDateTime.ToString(MadeFormat, CultureInfo.InvariantCulture)} UTC)";
    }

    /// <summary>Writes an amount exactly, as a request does.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>0,25 BTC</c>.</returns>
    private static string Amount(AssetAmount amount) => RequestTemplates.Amount(amount, Words);

    /// <summary>
    /// Writes a step's amount, and for a token the chain and contract it is known by, since anyone can issue a token
    /// with the same symbol: <c>500,00 USDT (Ethereum 0xdac1…)</c>, written whole so that it can be checked.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The text.</returns>
    private static string StepAmount(AssetAmount amount) => amount.Asset.Contract is { } contract
        ? $"{Amount(amount)} ({amount.Asset.Chain} {contract})"
        : Amount(amount);

    /// <summary>Writes amounts of several assets, one after the other.</summary>
    /// <param name="amounts">The amounts, one per asset.</param>
    /// <returns>For example <c>0,25 BTC + 1 200,00 USDT</c>.</returns>
    private static string Amounts(IReadOnlyList<AssetAmount> amounts) => string.Join(" + ", amounts.Select(Amount));

    /// <summary>Names the file: the case number, the word and the day.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="context">Who and when.</param>
    /// <returns>For example <c>2026-0117 underrättelse 2026-11-08.md</c>.</returns>
    private static string FileName(ReportDraft draft, RequestContext context)
    {
        var day = TimeZoneInfo.ConvertTime(context.Utc, draft.TimeZone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return RequestTemplates.SafeFileName($"{context.CaseNumber} {ReportTexts.FileWord} {day}");
    }
}
