using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Requests.Contracts;

/// <summary>What a request asks for, which decides its template and who it is addressed to.</summary>
public enum RequestKind
{
    /// <summary>
    /// A request for information to the service where the money left the chain (an exchange, an ATM, a card): who is
    /// behind the deposit address, their logins and their withdrawals.
    /// </summary>
    Information,

    /// <summary>
    /// A request to freeze a token at an address, addressed to the token's issuer (for example Tether for USDT), who
    /// can freeze it; never to the exchange (sketch TK1a).
    /// </summary>
    Freeze,
}

/// <summary>The language a request is written in; the templates have one text per language.</summary>
public enum RequestLanguage
{
    /// <summary>Swedish, for a recipient in Sweden.</summary>
    Swedish,

    /// <summary>English, for a recipient abroad.</summary>
    English,
}

/// <summary>What sort of recipient the request is addressed to, after the graph's sorts of cash-outs (GR3c).</summary>
public enum RequestTargetKind
{
    /// <summary>An exchange with customer checks.</summary>
    Exchange,

    /// <summary>An exchange without customer checks.</summary>
    NoKycExchange,

    /// <summary>A nested service, which trades through another exchange's accounts.</summary>
    Nested,

    /// <summary>An OTC desk or a P2P market.</summary>
    OtcP2P,

    /// <summary>A crypto ATM.</summary>
    Atm,

    /// <summary>A crypto card.</summary>
    Card,

    /// <summary>A token's issuer, who can freeze the token at an address.</summary>
    TokenIssuer,
}

/// <summary>Who the request is addressed to and the addresses it concerns.</summary>
/// <param name="Name">The recipient's name, from the label that made it a cash-out, for example <c>Börs A</c>.</param>
/// <param name="Kind">What sort of recipient it is.</param>
/// <param name="Chain">The chain the addresses are on.</param>
/// <param name="Addresses">
/// The addresses the request concerns: the deposit addresses that received the money, or the address holding the
/// token for a freeze; at least one, in the order they are written.
/// </param>
public sealed record RequestTarget(
    string Name, RequestTargetKind Kind, Chain Chain, IReadOnlyList<ChainAddress> Addresses)
{
    /// <summary>Gets the recipient's name.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("A recipient has a name.", nameof(Name))
        : Name.Trim();

    /// <summary>Gets the addresses the request concerns.</summary>
    public IReadOnlyList<ChainAddress> Addresses { get; } = Addresses is { Count: > 0 }
        ? Addresses
        : throw new ArgumentException("A request concerns at least one address.", nameof(Addresses));

    /// <summary>
    /// Gets the key that ties exports to this recipient in the case: the chain and the first address, so that the
    /// list of exits can say which have a request exported.
    /// </summary>
    public string Key => KeyOf(Addresses[0]);

    /// <summary>Gives the key of a recipient whose first address is the given one.</summary>
    /// <param name="address">The first address.</param>
    /// <returns>The key, for example <c>Bitcoin:bc1q…</c>.</returns>
    public static string KeyOf(ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.ToString();
    }
}

/// <summary>
/// One deposit to the recipient's address: a transfer on the chain, so every value is a fact (principle 2), and each
/// can be checked against the chain with any tool by its transaction, height and hash (principle 1).
/// </summary>
/// <param name="Transaction">The transaction's id, written out in full.</param>
/// <param name="To">The address that received it.</param>
/// <param name="Amount">The amount that came in, in its asset.</param>
/// <param name="Time">The block's time, or <see langword="null"/> while the transaction is unconfirmed.</param>
/// <param name="BlockHeight">The block's height, or <see langword="null"/> while unconfirmed.</param>
/// <param name="BlockHash">The block's hash, or <see langword="null"/> when the case does not have it.</param>
/// <param name="IsTraced">
/// Whether traced money came in with it, by the chosen method (a conclusion); deposits that are not traced may be
/// taken along all the same, since they go to the same customer.
/// </param>
public sealed record RequestDeposit(
    string Transaction,
    ChainAddress To,
    AssetAmount Amount,
    DateTimeOffset? Time,
    long? BlockHeight,
    string? BlockHash,
    bool IsTraced)
{
    /// <summary>Gets the transaction's id.</summary>
    public string Transaction { get; } = string.IsNullOrWhiteSpace(Transaction)
        ? throw new ArgumentException("A deposit names its transaction.", nameof(Transaction))
        : Transaction;

    /// <summary>Gets the address that received it.</summary>
    public ChainAddress To { get; } = To ?? throw new ArgumentNullException(nameof(To));
}

/// <summary>
/// The trail to the recipient in one sentence: the steps from the start, never a victim's name, and the method the
/// traced amounts were counted by. It is a conclusion and the document says so (principle 2).
/// </summary>
/// <param name="Steps">The steps from the start to the recipient, at least two; written by the caller without names
/// of persons, as shortened addresses and the services' names.</param>
/// <param name="Method">The tracing method and its version, for example <c>FIFO v1</c>.</param>
public sealed record RequestTrail(IReadOnlyList<string> Steps, string Method)
{
    /// <summary>Gets the steps from the start to the recipient.</summary>
    public IReadOnlyList<string> Steps { get; } = Steps is { Count: >= 2 }
        ? Steps
        : throw new ArgumentException("A trail has a start and an end.", nameof(Steps));

    /// <summary>Gets the tracing method.</summary>
    public string Method { get; } = string.IsNullOrWhiteSpace(Method)
        ? throw new ArgumentException("A trail names its method.", nameof(Method))
        : Method;

    /// <summary>Gets how many hops the trail has.</summary>
    public int Hops => Steps.Count - 1;

    /// <summary>
    /// Gets the swaps on the trail where the traced value followed into another asset (a judgement by an analysis);
    /// empty when it went through none.
    /// </summary>
    public IReadOnlyList<RequestSwap> Swaps { get; init; } = [];
}

/// <summary>
/// A swap on a request's trail: the traced value followed through it into another asset by an analysis, a judgement
/// that the request says with its name, version and share, never a fact.
/// </summary>
/// <param name="Transaction">The swap's transaction.</param>
/// <param name="Pill">What was swapped for what, for example <c>₮ → Ξ</c>.</param>
/// <param name="Share">The traced share of what went in, for example <c>62,2 %</c>.</param>
/// <param name="Analysis">The analysis with its version, for example <c>swap-value v1</c>.</param>
public sealed record RequestSwap(string Transaction, string Pill, string Share, string Analysis);

/// <summary>
/// What a freeze asks for: the token, its issuer and how much is left at the address by the case's history (a
/// conclusion from what has been fetched, not the issuer's own balance).
/// </summary>
/// <param name="Issuer">The issuer, for example <c>Tether</c>.</param>
/// <param name="Left">How much of the token is left at the address by the case's history.</param>
public sealed record RequestHolding(string Issuer, AssetAmount Left)
{
    /// <summary>Gets the issuer.</summary>
    public string Issuer { get; } = string.IsNullOrWhiteSpace(Issuer)
        ? throw new ArgumentException("A holding names its issuer.", nameof(Issuer))
        : Issuer;
}

/// <summary>
/// What a request is made from, chosen in the view Begäran: only what the request needs, never the victim's name,
/// the case's title or notes (CLAUDE.md, BG1b). The case number, the operator and the time are added when it is
/// composed.
/// </summary>
/// <param name="Kind">What it asks for.</param>
/// <param name="Language">The language it is written in.</param>
/// <param name="Target">Who it is addressed to and the addresses it concerns.</param>
/// <param name="Deposits">The deposits taken along, in time order; at least one for a request for information.</param>
/// <param name="Trail">The trail to the recipient, or <see langword="null"/> when it is left out.</param>
/// <param name="TimeZone">The time zone the local times are written in, the computer's own in the program.</param>
public sealed record RequestDraft(
    RequestKind Kind,
    RequestLanguage Language,
    RequestTarget Target,
    IReadOnlyList<RequestDeposit> Deposits,
    RequestTrail? Trail,
    TimeZoneInfo TimeZone)
{
    /// <summary>Gets the recipient.</summary>
    public RequestTarget Target { get; } = Target ?? throw new ArgumentNullException(nameof(Target));

    /// <summary>Gets the deposits taken along.</summary>
    public IReadOnlyList<RequestDeposit> Deposits { get; } =
        Deposits ?? throw new ArgumentNullException(nameof(Deposits));

    /// <summary>Gets the time zone.</summary>
    public TimeZoneInfo TimeZone { get; } = TimeZone ?? throw new ArgumentNullException(nameof(TimeZone));

    /// <summary>
    /// Gets what a freeze asks to have frozen; <see langword="null"/> for a request for information, and required for
    /// a freeze.
    /// </summary>
    public RequestHolding? Holding { get; init; }

    /// <summary>
    /// Gets when the request is made, the time its preview showed, so that the file says what the user saw; when
    /// <see langword="null"/>, it is made now.
    /// </summary>
    public DateTimeOffset? MadeUtc { get; init; }

    /// <summary>
    /// Gets the key an export is sealed under: the recipient's, and for a freeze also the token, since one address can
    /// hold two tokens that are frozen apart.
    /// </summary>
    public string ExportKey => Holding is { } holding ? ExportKeyOf(Target, holding) : Target.Key;

    /// <summary>Gives the key a freeze of a token at a recipient's address is sealed under.</summary>
    /// <param name="target">The recipient.</param>
    /// <param name="holding">The token and what is left.</param>
    /// <returns>For example <c>Ethereum:0x…|USDT (0xdac1…)</c>.</returns>
    public static string ExportKeyOf(RequestTarget target, RequestHolding holding)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(holding);
        return $"{target.Key}|{holding.Left.Asset}";
    }
}
