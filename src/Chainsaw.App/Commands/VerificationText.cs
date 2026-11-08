using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Commands;

/// <summary>Says what <see cref="ICaseVerifier"/> found, in plain Swedish, one line per thing that is wrong.</summary>
public static class VerificationText
{
    /// <summary>
    /// What <c>/verify</c> says under a whole chain when this computer's anchor was replaced for a reason on this
    /// computer (missing or unreadable), AK1a.
    /// </summary>
    public const string AnchorNote =
        "Ankaret på den här datorn byttes när ärendet öppnades (case.anchors-replaced). Verifiera i ärendeväljaren "
        + "(Ctrl+O) säger varför.";

    /// <summary>
    /// What <c>/verify</c> says, as a warning, when the anchor was replaced because of the file: an older copy, a
    /// change elsewhere, or an anchor file put back from a copy.
    /// </summary>
    public const string AnchorWarning =
        "Men filen stämde inte med det den här datorn sparade senast (case.anchors-replaced): kanske en äldre kopia "
        + "eller ändringar på en annan dator. Verifiera i ärendeväljaren (Ctrl+O) säger varför.";

    /// <summary>Describes a verification.</summary>
    /// <param name="verification">The result.</param>
    /// <param name="replaced">
    /// The latest replacement of the anchors from the journal, which says whether a replaced anchor concerns the file
    /// (a warning) or only this computer; <see langword="null"/> when not known, which counts as concerning the file.
    /// </param>
    /// <returns>
    /// A done answer when everything holds, a warning when the chain is whole but the file did not match this computer,
    /// otherwise an error with where each chain breaks.
    /// </returns>
    public static CommandReply Describe(CaseVerification verification, AnchorReplacement? replaced = null)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var records = verification.Chains.Sum(chain => chain.RecordCount);
        var whole = $"{verification.Chains.Count} kedjor och {records} poster stämmer.";
        if (verification.IsIntact)
        {
            return CommandReply.Done($"Inget har ändrats: {whole}");
        }

        // A replaced anchor is no fault in the file (sketch AK1a): the chain is whole, and the anchor is said under.
        if (verification.Chains.All(chain => chain.Break is null)
            && verification.Problems.All(problem => problem == CaseProblem.AnchorsReplaced))
        {
            var first = $"Hashkedjan är hel: {whole}{Environment.NewLine}";
            return replaced is { ConcernsTheFile: false }
                ? CommandReply.Done(first + AnchorNote)
                : CommandReply.Warning(first + AnchorWarning);
        }

        var lines = verification.Problems.Select(For)
            .Concat(verification.Chains.Where(chain => chain.Break is not null).Select(For));
        return CommandReply.Failed(string.Join(Environment.NewLine, lines));
    }

    /// <summary>Says what is wrong with the case as a whole.</summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The line.</returns>
    public static string For(CaseProblem problem) => problem switch
    {
        CaseProblem.DatabaseUnsound => "Databasen i ärendet är skadad, så kedjorna gick inte att läsa.",
        CaseProblem.CaseInfoChanged =>
            "Ärendets uppgifter (nummer, id, tid eller journalens första rad) har ändrats.",
        CaseProblem.AnchorsUnreadable =>
            "Den här datorns ankare för ärendet gick inte att läsa, så en äldre kopia kunde inte upptäckas.",
        CaseProblem.AnchorsReplaced =>
            "Ärendet stämde inte med det den här datorn sparade senast; journalen säger vad "
            + "(case.anchors-replaced).",
        CaseProblem.TriggerAdded =>
            "Ärendet har en utlösare som Chainsaw inte lägger dit; den påverkar det som sparas härefter.",
        _ => "Ärendet har ett fel som den här versionen inte kan beskriva.",
    };

    /// <summary>Says where and why a chain breaks.</summary>
    /// <param name="chain">The chain's result, with a break.</param>
    /// <returns>The line.</returns>
    public static string For(ChainVerification chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var broken = chain.Break!;
        var where = broken.Sequence == 0
            ? $"Kedjan {chain.Chain} håller inte"
            : $"Kedjan {chain.Chain} bryts vid post {broken.Sequence}";
        return $"{where}: {Reason(broken.Reason)}.";
    }

    /// <summary>Says why a chain breaks.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The words.</returns>
    private static string Reason(ChainBreakReason reason) => reason switch
    {
        ChainBreakReason.RecordChanged => "posten eller en post före den har ändrats",
        ChainBreakReason.SequenceGap => "en post saknas eller har lagts till",
        ChainBreakReason.HeadMismatch => "poster i slutet har tagits bort eller lagts till utanför Chainsaw",
        ChainBreakReason.ProtectionMissing => "skyddet mot ändringar saknas eller har ändrats",
        ChainBreakReason.TableChanged => "tabellen saknas eller har ändrats",
        ChainBreakReason.RolledBack => "kedjan är kortare eller annan än när den här datorn sparade den senast",
        ChainBreakReason.Unregistered => "kedjan har ingen känd definition och kan inte kontrolleras",
        _ => "okänd orsak",
    };
}
