using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Commands;

/// <summary>Says what <see cref="ICaseVerifier"/> found, in plain Swedish, one line per thing that is wrong.</summary>
public static class VerificationText
{
    /// <summary>Describes a verification.</summary>
    /// <param name="verification">The result.</param>
    /// <returns>A done answer when everything holds, otherwise an error with where each chain breaks.</returns>
    public static CommandReply Describe(CaseVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.IsIntact)
        {
            var records = verification.Chains.Sum(chain => chain.RecordCount);
            return CommandReply.Done(
                $"Inget har ändrats: {verification.Chains.Count} kedjor och {records} poster stämmer.");
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
        CaseProblem.BodyHashMismatch =>
            "Ett sparat svar har en kontrollsumma (SHA-256) som inte stämmer med svarets innehåll; "
            + "svaret förseglades så och går inte att läsa.",
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
