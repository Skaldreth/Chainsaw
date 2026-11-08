using Chainsaw.Modules.Requests.Contracts;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// The words of the templates in one language. Every text a request holds is here, so that a language is added by
/// one more set of words and the templates never change (<see cref="RequestTemplates"/>).
/// </summary>
/// <param name="InformationTitle">The title of a request for information, before the recipient's name.</param>
/// <param name="FreezeTitle">The title of a freeze, before the issuer's name.</param>
/// <param name="FileWord">The word in an information request's file name.</param>
/// <param name="FreezeFileWord">The words in a freeze's file name.</param>
/// <param name="CaseWord">The word before the case number.</param>
/// <param name="MadeWord">The word before the time it was made.</param>
/// <param name="ByWord">The word before the operator's name.</param>
/// <param name="AskHeading">The heading of what is asked for.</param>
/// <param name="InformationAsk">What a request for information asks for.</param>
/// <param name="FreezeAsk">What a freeze asks for; <c>{0}</c> is the token.</param>
/// <param name="RecipientHeading">The heading of the recipient's addresses.</param>
/// <param name="HoldingHeading">The heading of the address a freeze concerns.</param>
/// <param name="DepositsHeading">The heading of the deposits.</param>
/// <param name="TrailHeading">The heading of the trail.</param>
/// <param name="TrailText">
/// The sentence after the trail's steps; <c>{0}</c> is the number of hops and <c>{1}</c> the method.
/// </param>
/// <param name="Closing">The quiet line at the end.</param>
/// <param name="AddressColumn">The column of an address.</param>
/// <param name="ChainColumn">The column of a chain.</param>
/// <param name="KindColumn">The column of the recipient's sort.</param>
/// <param name="TokenColumn">The column of the token.</param>
/// <param name="LeftColumn">The column of what is left.</param>
/// <param name="LocalColumn">The column of the local time.</param>
/// <param name="UtcColumn">The column of the time in UTC.</param>
/// <param name="AmountColumn">The column of the amount.</param>
/// <param name="ToColumn">The column of the receiving address.</param>
/// <param name="TransactionColumn">The column of the transaction.</param>
/// <param name="HeightColumn">The column of the block's height.</param>
/// <param name="HashColumn">The column of the block's hash.</param>
/// <param name="Unconfirmed">What stands for the time and height of an unconfirmed deposit.</param>
/// <param name="Unknown">What stands for a hash the case does not have.</param>
/// <param name="LeftNote">What the holding's amount rests on.</param>
/// <param name="AssessmentNote">That the recipient's kind and the trail are assessments, not facts.</param>
/// <param name="DecimalSeparator">The decimal separator of amounts.</param>
internal sealed record RequestTexts(
    string InformationTitle,
    string FreezeTitle,
    string FileWord,
    string FreezeFileWord,
    string CaseWord,
    string MadeWord,
    string ByWord,
    string AskHeading,
    string InformationAsk,
    string FreezeAsk,
    string RecipientHeading,
    string HoldingHeading,
    string DepositsHeading,
    string TrailHeading,
    string TrailText,
    string Closing,
    string AddressColumn,
    string ChainColumn,
    string KindColumn,
    string TokenColumn,
    string LeftColumn,
    string LocalColumn,
    string UtcColumn,
    string AmountColumn,
    string ToColumn,
    string TransactionColumn,
    string HeightColumn,
    string HashColumn,
    string Unconfirmed,
    string Unknown,
    string LeftNote,
    string AssessmentNote,
    string DecimalSeparator)
{
    /// <summary>The Swedish words.</summary>
    public static RequestTexts Swedish { get; } = new(
        InformationTitle: "Begäran om uppgifter",
        FreezeTitle: "Begäran om frysning",
        FileWord: "Begäran",
        FreezeFileWord: "Begäran om frysning",
        CaseWord: "Ärende",
        MadeWord: "upprättad",
        ByWord: "av",
        AskHeading: "Det vi ber om",
        InformationAsk: "Uppgifter om kunden eller kunderna bakom insättningsadressen: identitet och kundkontroll, "
            + "inloggningar med IP-adress och tid, och uttag från kontot med mottagare, tid och belopp.",
        FreezeAsk: "Att {0} på adressen nedan fryses i avvaktan på beslut om beslag, och uppgifter om adressen och "
            + "den som kontrollerar den.",
        RecipientHeading: "Insättningsadress",
        HoldingHeading: "Adressen",
        DepositsHeading: "Insättningar",
        TrailHeading: "Spåret dit",
        TrailText: "{0} hopp, {1}. Det spårade beloppet är en bedömning enligt metoden; varje tx går att "
            + "kontrollera på kedjan med vilket verktyg som helst.",
        Closing: "Hela txid, blockhöjd och blockhash står i tabellen. Exporterad ur Chainsaw; filens SHA-256 står i "
            + "ärendets journal.",
        AddressColumn: "Adress",
        ChainColumn: "Kedja",
        KindColumn: "Sort",
        TokenColumn: "Token",
        LeftColumn: "Kvar enligt ärendet",
        LocalColumn: "Tid (lokal)",
        UtcColumn: "Tid (UTC)",
        AmountColumn: "Belopp",
        ToColumn: "Till adress",
        TransactionColumn: "Tx",
        HeightColumn: "Block",
        HashColumn: "Blockhash",
        Unconfirmed: "obekräftad",
        Unknown: "saknas i ärendet",
        LeftNote: "Det som ligger kvar är räknat ur adressens historik så som den hämtats i ärendet, inte ett saldo "
            + "från utgivaren.",
        AssessmentNote: "Att adressen hör till mottagaren är en bedömning ur etiketter, och spåret dit är en bedömning "
            + "enligt spårningsmetoden; insättningarna i tabellen är fakta på kedjan.",
        DecimalSeparator: ",")
    {
        SwapText = "Genom swappen {0} i tx {1} följer värdet med enligt {2}: {3} av det som gick in var spårat, och "
            + "samma andel av det som kom ut räknas som spårat. Det är en bedömning, inte ett faktum på kedjan.",
        Kinds = new Dictionary<RequestTargetKind, string>
        {
            [RequestTargetKind.Exchange] = "Börs",
            [RequestTargetKind.NoKycExchange] = "Börs utan kundkontroll",
            [RequestTargetKind.Nested] = "Nästlad tjänst",
            [RequestTargetKind.OtcP2P] = "OTC eller P2P",
            [RequestTargetKind.Atm] = "Kryptoautomat",
            [RequestTargetKind.Card] = "Kryptokort",
            [RequestTargetKind.TokenIssuer] = "Utgivare av token",
        },
    };

    /// <summary>The English words.</summary>
    public static RequestTexts English { get; } = new(
        InformationTitle: "Request for information",
        FreezeTitle: "Request to freeze",
        FileWord: "Request",
        FreezeFileWord: "Freeze request",
        CaseWord: "Case",
        MadeWord: "made",
        ByWord: "by",
        AskHeading: "What we ask for",
        InformationAsk: "Information on the customer or customers behind the deposit address: identity and "
            + "customer due diligence, logins with IP address and time, and withdrawals from the account with "
            + "recipient, time and amount.",
        FreezeAsk: "That the {0} at the address below is frozen pending a decision on seizure, and information on "
            + "the address and who controls it.",
        RecipientHeading: "Deposit address",
        HoldingHeading: "The address",
        DepositsHeading: "Deposits",
        TrailHeading: "The trail",
        TrailText: "{0} hops, {1}. The traced amount is an assessment by the method; every transaction can be "
            + "checked on the chain with any tool.",
        Closing: "Full txid, block height and block hash are in the table. Exported from Chainsaw; the file's SHA-256 "
            + "is recorded in the case's journal.",
        AddressColumn: "Address",
        ChainColumn: "Chain",
        KindColumn: "Kind",
        TokenColumn: "Token",
        LeftColumn: "Left per the case",
        LocalColumn: "Time (local)",
        UtcColumn: "Time (UTC)",
        AmountColumn: "Amount",
        ToColumn: "To address",
        TransactionColumn: "Tx",
        HeightColumn: "Block",
        HashColumn: "Block hash",
        Unconfirmed: "unconfirmed",
        Unknown: "not in the case",
        LeftNote: "What is left is counted from the address's history as fetched in the case, not a balance from "
            + "the issuer.",
        AssessmentNote: "That the address belongs to the recipient is an assessment from labels, and the trail is an "
            + "assessment by the tracing method; the deposits in the table are facts on the chain.",
        DecimalSeparator: ".")
    {
        SwapText = "Through the swap {0} in tx {1} the value follows by {2}: {3} of what went in was traced, and the "
            + "same share of what came out is counted as traced. It is an assessment, not a fact on the chain.",
        Kinds = new Dictionary<RequestTargetKind, string>
        {
            [RequestTargetKind.Exchange] = "Exchange",
            [RequestTargetKind.NoKycExchange] = "Exchange without customer due diligence",
            [RequestTargetKind.Nested] = "Nested service",
            [RequestTargetKind.OtcP2P] = "OTC or P2P",
            [RequestTargetKind.Atm] = "Crypto ATM",
            [RequestTargetKind.Card] = "Crypto card",
            [RequestTargetKind.TokenIssuer] = "Token issuer",
        },
    };

    /// <summary>Gives the words of a language.</summary>
    /// <param name="language">The language.</param>
    /// <returns>Its words.</returns>
    public static RequestTexts Of(RequestLanguage language) => language switch
    {
        RequestLanguage.English => English,
        _ => Swedish,
    };

    /// <summary>Gets the names of the recipients' sorts in this language.</summary>
    public IReadOnlyDictionary<RequestTargetKind, string> Kinds { get; init; } =
        new Dictionary<RequestTargetKind, string>();

    /// <summary>
    /// Gets the sentence for a swap on the trail: <c>{0}</c> is what was swapped for what, <c>{1}</c> the
    /// transaction, <c>{2}</c> the analysis with its version and <c>{3}</c> the traced share.
    /// </summary>
    public string SwapText { get; init; } = string.Empty;

    /// <summary>Names a recipient's sort in this language.</summary>
    /// <param name="kind">The sort.</param>
    /// <returns>Its name.</returns>
    public string KindName(RequestTargetKind kind) => Kinds.TryGetValue(kind, out var name) ? name : kind.ToString();
}
