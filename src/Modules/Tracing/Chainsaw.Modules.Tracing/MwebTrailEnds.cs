using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Ends the trail where money goes into Litecoin's shielded part, MWEB: a peg-in, an output to a witness program of
/// version 9, read from the output's script (<see cref="BitcoinScriptType.MwebPegIn"/>), and the extension block's own
/// address that each block's integration transaction holds (<see cref="BitcoinScriptType.MwebHogAddress"/>). Inside,
/// amounts and owners do not show, so the trail ends with the reason <see cref="TrailEndReason.PrivacyCoin"/> (the
/// shielded pool). Which peg-outs may belong to a peg-in is a proposal for a later round, never drawn here.
/// </summary>
internal sealed class MwebTrailEnds : ITrailEndClassifier
{
    /// <summary>The classifier's name and version, as a hint names its source.</summary>
    public const string Source = "mweb v1";

    /// <summary>What a peg-in's hint says.</summary>
    public const string PegInText =
        "Pengarna gick in i Litecoins skyddade pool (MWEB) genom en peg-in: en output till ett vittnesprogram av "
        + "version 9, läst ur skriptet. Belopp och ägare syns inte där, så spåret tar slut.";

    /// <summary>What the extension block's own address's hint says.</summary>
    public const string HogText =
        "Adressen är Litecoins skyddade pool (MWEB) själv, som blockets integrationstx (HogEx) håller: vittnesprogram "
        + "av version 8, läst ur skriptet. Det som går in där syns inte, så spåret tar slut.";

    /// <inheritdoc/>
    public IReadOnlyList<TrailEndHint> ClassifyAddress(ICase @case, BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Type switch
        {
            BitcoinScriptType.MwebPegIn => [new TrailEndHint(TrailEndReason.PrivacyCoin, Source, PegInText)],
            BitcoinScriptType.MwebHogAddress => [new TrailEndHint(TrailEndReason.PrivacyCoin, Source, HogText)],
            _ => [],
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrailEndHint> ClassifyTransaction(ICase @case, BitcoinTransaction transaction) => [];
}
