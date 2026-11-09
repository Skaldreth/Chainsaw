using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The Swedish texts for cases and start points, in one place for the setup, the panel and the commands.
/// </summary>
public static class CaseTexts
{
    /// <summary>What is said when the case number cannot be a file name.</summary>
    public static readonly string InvalidCaseNumber =
        "Ärendenumret får ha bokstäver, siffror, mellanslag, - _ och . och högst "
        + $"{CaseNumberRules.MaximumLength} tecken.";

    /// <summary>What is said when the title cannot be stored.</summary>
    public static readonly string InvalidTitle =
        $"Titeln får ha högst {CaseTitleRules.MaximumLength} tecken och inga styrtecken.";

    /// <summary>Gives the text for why a case could not be created, opened or closed.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The text.</returns>
    public static string For(CaseError error) => error switch
    {
        CaseError.AlreadyExists => "Det finns redan ett ärende med det numret.",
        CaseError.InvalidCaseNumber => "Ärendenumret går inte att använda som filnamn.",
        CaseError.NotFound => "Det finns inget ärende med det numret.",
        CaseError.NoAccess => "Ärendet går inte att öppna med det här valvet.",
        CaseError.Corrupt => "Ärendefilen är skadad eller har ändrats utanför Chainsaw.",
        CaseError.UnsupportedVersion => "Ärendet är skrivet av en nyare Chainsaw.",
        CaseError.InUse => "Ärendet används redan av en annan Chainsaw.",
        CaseError.Conflict => "Det finns en arbetskopia som inte hör till ärendefilen. Inget har ändrats.",
        CaseError.VaultLocked => "Valvet är låst, så inget gjordes med ärendet.",
        CaseError.NoOperatorProfile => "Operatörsprofilen saknas, så journalen kan inte säga vem som gjorde det.",
        CaseError.PasswordTooShort =>
            $"Ärendelösenordet ska ha minst {SharingPasswordRules.MinimumLength} tecken.",
        CaseError.WouldLoseAccess =>
            "Ditt valv har inget eget kuvert i ärendet, så lösenordet kan inte tas bort. Lägg till ditt kuvert först.",
        CaseError.Closed => "Ärendet är stängt.",
        CaseError.OutdatedFormat => "Ärendet är skrivet av en äldre Chainsaw, före 1.0, och kan inte öppnas.",
        CaseError.TooManyEnvelopes => "Ärendet har redan så många kuvert som en fil får ha. Inget ändrades.",
        CaseError.LockedWhileSaving => "Valvet låstes medan ärendet sparades. Det är sparat och stängt.",
        _ => "Det gick inte att göra det med ärendet.",
    };

    /// <summary>Gives a chain's short label, as in the sketch's tag under the start points.</summary>
    /// <param name="chain">The chain, or <see langword="null"/> when unknown.</param>
    /// <returns>The label, for example <c>BTC</c>.</returns>
    public static string ChainLabel(Chain? chain) => chain switch
    {
        Chain.Bitcoin => "BTC",
        Chain.Ethereum => "ETH",
        Chain.Tron => "TRON",
        Chain.Litecoin => "LTC",
        Chain.Bsc => "BSC",
        _ => "?",
    };

    /// <summary>Gives a chain's name in Swedish text.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The name, for example <c>TRON</c>.</returns>
    public static string ChainName(Chain chain) => chain switch
    {
        Chain.Bitcoin => "Bitcoin",
        Chain.Ethereum => "Ethereum",
        Chain.Tron => "TRON",
        Chain.Litecoin => "Litecoin",
        Chain.Bsc => "BSC",
        _ => chain.ToString(),
    };

    /// <summary>Says what a start point is: <c>Bitcoin-adress</c>, or that its chain is not built yet.</summary>
    /// <param name="startPoint">The recognised start point.</param>
    /// <returns>The text.</returns>
    public static string Describe(ChainStartPoint startPoint)
    {
        if (startPoint.Chain is not { } chain)
        {
            return "Känns inte igen som adress eller tx";
        }

        var what = ChainName(chain) + (startPoint.Kind == StartPointKind.Address ? "-adress" : "-tx");
        return startPoint.IsSupported ? what : what + " · inte byggt än";
    }

    /// <summary>Shortens an address or a txid for a line: the first eight and the last seven characters.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value as it is when it is short, otherwise shortened with an ellipsis.</returns>
    public static string Shorten(string value) =>
        value.Length <= 18 ? value : string.Concat(value.AsSpan(0, 8), "…", value.AsSpan(value.Length - 7));
}
