using System.Collections.ObjectModel;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>Which list of counterparties a typed address or a Ctrl+click in the graph goes to.</summary>
public enum WatchPartyList
{
    /// <summary>The receivers of money going out (1:n).</summary>
    Receivers,

    /// <summary>The senders of money coming in (n:1).</summary>
    Senders,
}

/// <summary>
/// A counterparty in the form, as a chip: one address, or every address of a node in the graph (a cluster), so that
/// money to any of them counts.
/// </summary>
/// <param name="Addresses">The addresses, at least one.</param>
/// <param name="Text">What the chip says: the node's name in the graph, or the address shortened.</param>
public sealed record WatchPartyChip(IReadOnlyList<ChainAddress> Addresses, string Text)
{
    /// <summary>Gets the first address, for the chip's tooltip.</summary>
    public ChainAddress Address => Addresses[0];

    /// <summary>Gets the chip's tooltip: the addresses it stands for.</summary>
    public string ToolTip => Addresses.Count == 1
        ? Address.Value
        : $"{Address.Value} och {Addresses.Count - 1} till i noden";
}

/// <summary>One choice of a segmented row in the form.</summary>
/// <param name="label">What it says.</param>
/// <param name="value">What it stands for.</param>
public sealed partial class WatchChoice(string label, object? value) : ObservableObject
{
    /// <summary>Gets what it says.</summary>
    public string Label { get; } = label;

    /// <summary>Gets what it stands for.</summary>
    public object? Value { get; } = value;

    /// <summary>Gets or sets whether it is chosen.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}

/// <summary>
/// The form of a watch (sketches BV1a and BV2a): the same for <em>Bevaka …</em> on a node in the graph, <em>Ny
/// bevakning …</em> in the view (with an address of its own) and <em>Ändra</em>. Direction in, out or both; receivers
/// (1:n) and senders (n:1) as chips, added by address or with Ctrl+click in the graph, where an empty list means all;
/// the asset on Ethereum; an optional minimum; and how often the watch asks the source. Enter saves, Esc cancels. It
/// knows nothing of WPF.
/// </summary>
public sealed partial class WatchFormViewModel : ObservableObject
{
    /// <summary>What the form says when an address is not one the chain knows.</summary>
    public const string NotAnAddressText = "Ingen adress på kedjan.";

    /// <summary>What the form says when a counterparty is the watched node.</summary>
    public const string SelfText = "Noden själv kan inte vara motpart.";

    /// <summary>What the form says when a minimum cannot be read.</summary>
    public const string MinimumProblemText = "Minsta belopp är ett positivt belopp, till exempel 0,5.";

    /// <summary>What the minimum says on Ethereum without a chosen asset.</summary>
    public const string MinimumNeedsAssetText = "Välj en tillgång först";

    /// <summary>The watches.</summary>
    private readonly WatchBoard _board;

    /// <summary>The node being watched, or <see langword="null"/> while the address is typed.</summary>
    private WatchSubject? _subject;

    /// <summary>The watch being changed, or <see langword="null"/>.</summary>
    private string? _changing;

    /// <summary>Names an address in a chip.</summary>
    private Func<ChainAddress, string> _name = WatchWords.Short;

    /// <summary>Creates the form.</summary>
    /// <param name="board">The watches.</param>
    public WatchFormViewModel(WatchBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        _board = board;
        Paces = [.. Enum.GetValues<WatchPace>().Select(pace => new WatchChoice(WatchWords.Pace(pace), pace))];
        Directions =
        [
            new WatchChoice("In", WatchDirection.In),
            new WatchChoice("Ut", WatchDirection.Out),
            new WatchChoice("Båda", WatchDirection.Both),
        ];
        Assets =
        [
            new WatchChoice("Alla", null),
            new WatchChoice("ETH", SharedKernel.Asset.Ether),
            new WatchChoice("₮ USDT", KnownTokens.EthereumUsdt),
            new WatchChoice("USDC", KnownTokens.EthereumUsdc),
        ];
    }

    /// <summary>Raised on the UI thread after a watch was saved, with it.</summary>
    public event EventHandler<Watch>? Saved;

    /// <summary>Gets the directions to choose from.</summary>
    public IReadOnlyList<WatchChoice> Directions { get; }

    /// <summary>Gets how often the watch can ask the source.</summary>
    public IReadOnlyList<WatchChoice> Paces { get; }

    /// <summary>Gets the assets to choose from on Ethereum.</summary>
    public IReadOnlyList<WatchChoice> Assets { get; }

    /// <summary>Gets the receivers.</summary>
    public ObservableCollection<WatchPartyChip> Receivers { get; } = [];

    /// <summary>Gets the senders.</summary>
    public ObservableCollection<WatchPartyChip> Senders { get; } = [];

    /// <summary>Gets whether the form is open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets whether the address is typed (Ny bevakning) rather than taken from a node.</summary>
    [ObservableProperty]
    public partial bool IsManual { get; private set; }

    /// <summary>Gets the form's title.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>Gets or sets the typed address of a new watch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChainName), nameof(IsEthereum), nameof(IsBitcoin), nameof(MinimumHint))]
    public partial string AddressText { get; set; } = string.Empty;

    /// <summary>Gets the watched direction.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsReceivers), nameof(ShowsSenders))]
    public partial WatchDirection Direction { get; private set; } = WatchDirection.Both;

    /// <summary>Gets the list a typed address or a Ctrl+click adds to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PicksReceivers), nameof(PicksSenders))]
    public partial WatchPartyList Picking { get; private set; } = WatchPartyList.Receivers;

    /// <summary>Gets or sets the address typed to add as a counterparty.</summary>
    [ObservableProperty]
    public partial string PartyText { get; set; } = string.Empty;

    /// <summary>Gets or sets the minimum as typed; empty for none.</summary>
    [ObservableProperty]
    public partial string MinimumInput { get; set; } = string.Empty;

    /// <summary>Gets the chosen asset on Ethereum, or <see langword="null"/> for all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanHaveMinimum), nameof(MinimumHint))]
    public partial Asset? Asset { get; private set; }

    /// <summary>Gets how often the watch asks the source; at every new block by default.</summary>
    [ObservableProperty]
    public partial WatchPace Pace { get; private set; } = WatchPace.EveryBlock;

    /// <summary>Gets what went wrong, or an empty text.</summary>
    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets whether the form is being saved.</summary>
    [ObservableProperty]
    public partial bool IsSaving { get; private set; }

    /// <summary>Gets the chain of the watch, or <see langword="null"/> while no address is known.</summary>
    public Chain? Chain => _subject?.Anchor.Chain ?? ChainOf(AddressText);

    /// <summary>Gets the chain's name for the form's chip.</summary>
    public string ChainName => Chain?.ToString() ?? string.Empty;

    /// <summary>Gets whether the watch is on Ethereum, where the asset is chosen.</summary>
    public bool IsEthereum => Chain == SharedKernel.Chain.Ethereum;

    /// <summary>Gets whether the watch is on Bitcoin, where BTC is the only asset.</summary>
    public bool IsBitcoin => Chain == SharedKernel.Chain.Bitcoin;

    /// <summary>Gets whether receivers are shown: money going out is watched.</summary>
    public bool ShowsReceivers => Direction != WatchDirection.In;

    /// <summary>Gets whether senders are shown: money coming in is watched.</summary>
    public bool ShowsSenders => Direction != WatchDirection.Out;

    /// <summary>Gets whether a Ctrl+click adds a receiver.</summary>
    public bool PicksReceivers => Picking == WatchPartyList.Receivers;

    /// <summary>Gets whether a Ctrl+click adds a sender.</summary>
    public bool PicksSenders => Picking == WatchPartyList.Senders;

    /// <summary>Gets whether a minimum can be given: on Bitcoin, or on Ethereum with one asset chosen.</summary>
    public bool CanHaveMinimum => !IsEthereum || Asset is not null;

    /// <summary>Gets the minimum's placeholder.</summary>
    public string MinimumHint => CanHaveMinimum ? "Valfritt" : MinimumNeedsAssetText;

    /// <summary>Gets the save button's text.</summary>
    public string SaveText => _changing is null ? "Bevaka" : "Spara";

    /// <summary>Gets the watched node, while the form is open from a node or for a change.</summary>
    public WatchSubject? Subject => _subject;

    /// <summary>Opens the form for a node in the graph.</summary>
    /// <param name="subject">The node.</param>
    /// <param name="name">Names an address: its node's name in the graph, or the address shortened.</param>
    public void OpenFor(WatchSubject subject, Func<ChainAddress, string>? name = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Reset(subject, null, name, $"Bevaka {subject.Name}");
        IsManual = false;
    }

    /// <summary>Opens the form for a new watch on an address that is typed (Ny bevakning …).</summary>
    public void OpenNew()
    {
        Reset(null, null, null, "Ny bevakning");
        IsManual = true;
    }

    /// <summary>Opens the form to change a watch.</summary>
    /// <param name="watch">The watch.</param>
    /// <param name="name">Names an address.</param>
    public void OpenChange(Watch watch, Func<ChainAddress, string>? name = null)
    {
        ArgumentNullException.ThrowIfNull(watch);
        Reset(watch.Subject, watch.Id, name, $"Ändra {watch.Subject.Name}");
        IsManual = false;
        ChooseDirection(watch.Filter.Direction);
        foreach (var group in watch.Filter.Receivers.GroupBy(party => _name(party)))
        {
            Receivers.Add(new WatchPartyChip([.. group], group.Key));
        }

        foreach (var group in watch.Filter.Senders.GroupBy(party => _name(party)))
        {
            Senders.Add(new WatchPartyChip([.. group], group.Key));
        }

        ChooseAsset(watch.Filter.Asset);
        ChoosePace(watch.Filter.Pace);
        MinimumInput = watch.Filter.Minimum is { } minimum ? Graph.AmountText.Number(minimum) : string.Empty;
    }

    /// <summary>Chooses the direction.</summary>
    /// <param name="direction">The direction.</param>
    [RelayCommand]
    public void ChooseDirection(WatchDirection direction)
    {
        Direction = direction;
        WatchChoiceMark(Directions, direction);
        Picking = direction == WatchDirection.In ? WatchPartyList.Senders
            : direction == WatchDirection.Out ? WatchPartyList.Receivers : Picking;
    }

    /// <summary>Chooses the asset on Ethereum; <see langword="null"/> for all.</summary>
    /// <param name="asset">The asset.</param>
    [RelayCommand]
    public void ChooseAsset(Asset? asset)
    {
        Asset = asset;
        WatchChoiceMark(Assets, asset);
        if (!CanHaveMinimum)
        {
            MinimumInput = string.Empty;
        }
    }

    /// <summary>Chooses how often the watch asks the source.</summary>
    /// <param name="pace">The pace.</param>
    [RelayCommand]
    public void ChoosePace(WatchPace pace)
    {
        Pace = pace;
        WatchChoiceMark(Paces, pace);
    }

    /// <summary>Makes a list the one a typed address or a Ctrl+click adds to.</summary>
    /// <param name="list">The list.</param>
    [RelayCommand]
    public void Pick(WatchPartyList list) => Picking = list;

    /// <summary>Adds the typed address to the list being picked for.</summary>
    [RelayCommand]
    public void AddParty()
    {
        if (Chain is not { } chain || !IsAddress(chain, PartyText.Trim()))
        {
            Problem = NotAnAddressText;
            return;
        }

        if (Add(new ChainAddress(chain, PartyText.Trim()), null))
        {
            PartyText = string.Empty;
        }
    }

    /// <summary>Adds an address to the list being picked for.</summary>
    /// <param name="address">The address.</param>
    /// <param name="text">The node's name, or <see langword="null"/> for the address shortened.</param>
    /// <returns><see langword="true"/> when it was added.</returns>
    public bool Add(ChainAddress address, string? text)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Add([address], text ?? _name(address));
    }

    /// <summary>
    /// Adds a node from the graph (Ctrl+click) to the list being picked for, with every address it has on the chain,
    /// so that a cluster counts as a whole.
    /// </summary>
    /// <param name="addresses">The node's addresses.</param>
    /// <param name="text">The node's name.</param>
    /// <returns><see langword="true"/> when it was added.</returns>
    public bool Add(IReadOnlyList<ChainAddress> addresses, string text)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var mine = addresses.Where(address => address.Chain == Chain).Distinct().ToList();
        if (mine.Count == 0)
        {
            Problem = NotAnAddressText;
            return false;
        }

        if ((_subject?.Anchor ?? ManualAddress()) is { } self && mine.Contains(self))
        {
            Problem = SelfText;
            return false;
        }

        var list = (Picking == WatchPartyList.Receivers && ShowsReceivers) || !ShowsSenders ? Receivers : Senders;
        var fresh = mine.Where(address => list.All(chip => !chip.Addresses.Contains(address))).ToList();
        if (fresh.Count > 0)
        {
            list.Add(new WatchPartyChip(fresh, text));
        }

        Problem = string.Empty;
        return true;
    }

    /// <summary>Takes a counterparty out of its list.</summary>
    /// <param name="chip">The chip.</param>
    [RelayCommand]
    public void RemoveParty(WatchPartyChip? chip)
    {
        if (chip is not null)
        {
            Receivers.Remove(chip);
            Senders.Remove(chip);
        }
    }

    /// <summary>Closes the form without saving.</summary>
    [RelayCommand]
    public void Cancel()
    {
        IsOpen = false;
        Problem = string.Empty;
    }

    /// <summary>Saves the watch through the Monitoring module and closes the form.</summary>
    /// <returns>A task that completes when it is saved or the problem is shown.</returns>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!IsOpen || IsSaving)
        {
            return;
        }

        if (_board.Case is not { } openCase || BuildFilter() is not { } filter || SubjectToSave() is not { } subject)
        {
            return;
        }

        IsSaving = true;
        try
        {
            Watch? saved = null;
            var source = _board.Source;
            var changing = _changing;
            var written = await _board.WriteAsync(async () => saved = changing is null
                ? await source.AddAsync(openCase, subject, filter)
                : await source.ChangeAsync(openCase, changing, filter));
            if (!written)
            {
                Problem = WatchBoard.FailedText;
                return;
            }

            IsOpen = false;
            if (saved is not null)
            {
                Saved?.Invoke(this, saved);
            }
        }
        catch (ArgumentException)
        {
            Problem = "Bevakningen går inte ihop; kontrollera adresserna och beloppet.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Gives the filter the form holds, or says what is wrong.</summary>
    /// <returns>The filter, or <see langword="null"/> with <see cref="Problem"/> set.</returns>
    public WatchFilter? BuildFilter()
    {
        if (Chain is not { } chain)
        {
            Problem = NotAnAddressText;
            return null;
        }

        var asset = chain == SharedKernel.Chain.Ethereum ? Asset : null;
        AssetAmount? minimum = null;
        var typed = MinimumInput.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(' '.ToString(), string.Empty, StringComparison.Ordinal).Replace(',', '.');
        if (typed.Length > 0)
        {
            var counted = asset ?? (chain == SharedKernel.Chain.Bitcoin ? SharedKernel.Asset.Bitcoin : null);
            try
            {
                minimum = counted is null ? null : AssetAmount.Parse(counted, typed);
            }
            catch (FormatException)
            {
                minimum = null;
            }

            if (minimum is not { Units.Sign: > 0 })
            {
                Problem = MinimumProblemText;
                return null;
            }
        }

        Problem = string.Empty;
        return new WatchFilter(
            chain,
            Direction,
            ShowsReceivers ? [.. Receivers.SelectMany(chip => chip.Addresses)] : [],
            ShowsSenders ? [.. Senders.SelectMany(chip => chip.Addresses)] : [],
            asset,
            minimum)
        {
            Pace = Pace,
        };
    }

    /// <summary>Gives the chain an address is on, by its form and checksum.</summary>
    /// <param name="text">The address.</param>
    /// <returns>The chain, or <see langword="null"/>.</returns>
    public static Chain? ChainOf(string? text)
    {
        var trimmed = text?.Trim();
        if (BitcoinAddress.TryParse(trimmed, out _))
        {
            return SharedKernel.Chain.Bitcoin;
        }

        return EthereumAddress.TryParse(trimmed, out _) ? SharedKernel.Chain.Ethereum : null;
    }

    /// <summary>Says whether a text is an address on a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsAddress(Chain chain, string text) => ChainOf(text) == chain;

    /// <summary>Marks the chosen choice of a row.</summary>
    /// <param name="choices">The row.</param>
    /// <param name="value">The chosen value.</param>
    private static void WatchChoiceMark(IEnumerable<WatchChoice> choices, object? value)
    {
        foreach (var choice in choices)
        {
            choice.IsChosen = Equals(choice.Value, value);
        }
    }

    /// <summary>Gives the typed address, when it is one.</summary>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private ChainAddress? ManualAddress() =>
        ChainOf(AddressText) is { } chain ? new ChainAddress(chain, AddressText.Trim()) : null;

    /// <summary>Gives the node to save the watch for, or says what is wrong.</summary>
    /// <returns>The node, or <see langword="null"/> with <see cref="Problem"/> set.</returns>
    private WatchSubject? SubjectToSave()
    {
        if (_subject is { } subject)
        {
            return subject;
        }

        if (ManualAddress() is not { } address)
        {
            Problem = NotAnAddressText;
            return null;
        }

        return new WatchSubject(WatchSubjectKind.Address, address, WatchWords.Short(address));
    }

    /// <summary>Empties the form and opens it.</summary>
    /// <param name="subject">The node, or <see langword="null"/> for a typed address.</param>
    /// <param name="changing">The watch being changed, or <see langword="null"/>.</param>
    /// <param name="name">Names an address.</param>
    /// <param name="title">The title.</param>
    private void Reset(WatchSubject? subject, string? changing, Func<ChainAddress, string>? name, string title)
    {
        _subject = subject;
        _changing = changing;
        _name = name ?? WatchWords.Short;
        Title = title;
        AddressText = string.Empty;
        PartyText = string.Empty;
        MinimumInput = string.Empty;
        Problem = string.Empty;
        Receivers.Clear();
        Senders.Clear();
        ChooseDirection(WatchDirection.Both);
        Picking = WatchPartyList.Receivers;
        ChooseAsset(null);
        ChoosePace(WatchPace.EveryBlock);
        OnPropertyChanged(nameof(Chain));
        OnPropertyChanged(nameof(ChainName));
        OnPropertyChanged(nameof(IsEthereum));
        OnPropertyChanged(nameof(IsBitcoin));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(Subject));
        IsOpen = true;
    }
}
