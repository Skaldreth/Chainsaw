using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A chain as a chip at the top of the page Källor; one that is not built yet is toned.</summary>
/// <param name="name">The chain's name.</param>
/// <param name="chain">The chain, or <see langword="null"/> for one Chainsaw does not know yet (Solana).</param>
/// <param name="isBuilt">Whether Chainsaw has a sort of API for it.</param>
public sealed partial class SourceChainChip(string name, Chain? chain, bool isBuilt) : ObservableObject
{
    /// <summary>Gets the chain's name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the chain, or <see langword="null"/>.</summary>
    public Chain? Chain { get; } = chain;

    /// <summary>Gets whether Chainsaw has a sort of API for it.</summary>
    public bool IsBuilt { get; } = isBuilt;

    /// <summary>Gets the words on hovering.</summary>
    public string Tip => IsBuilt ? Name : $"{Name} · {SettingsViewModel.NotBuiltText}";

    /// <summary>Gets whether the page shows it.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Gets its dot: the one used answers, none answers, or it is off.</summary>
    [ObservableProperty]
    public partial EndpointDot Dot { get; set; } = EndpointDot.Off;
}

/// <summary>
/// The settings' page Källor (sketch KL1a, decided 2026-11-07): the chains as chips, each built chain's mode
/// (Snabbast, Prioriterad or Av), its endpoints with their latest probe, the form that adds one (only https, the sort of
/// API, a key from the vault, and <em>Prova</em>), and <em>Prova alla nu</em>. The endpoints are tried once when
/// Chainsaw is unlocked and the exit allows lookups; never before. It knows nothing of WPF.
/// </summary>
public sealed partial class SourcesViewModel : ObservableObject
{
    /// <summary>What the key's list says for no key.</summary>
    public const string NoKeyText = "Ingen";

    /// <summary>What the page says when a change could not be written.</summary>
    public const string SaveFailedText = "Källorna gick inte att spara. Ändringen gäller tills Chainsaw stängs.";

    /// <summary>What a round says when nothing could be sent.</summary>
    public const string NotSentText = "Inget provades: utgången tillåter inga uppslag nu";

    /// <summary>The chains as the page lists them, built or not; Solana is not a chain Chainsaw knows yet.</summary>
    private static readonly (string Name, Chain? Chain)[] ChainNames =
    [
        ("Bitcoin", Chain.Bitcoin), ("Ethereum", Chain.Ethereum), ("TRON", Chain.Tron), ("Solana", null),
    ];

    /// <summary>The modes, their words and what they do.</summary>
    private static readonly (SourceMode Mode, string Label, string Hint)[] Modes =
    [
        (SourceMode.Fastest, "Snabbast",
            "Provas vid start genom utgången, med kedjans höjd. Den snabbaste som svarar rätt används."),
        (SourceMode.Priority, "Prioriterad", "I ordningen nedan. Nästa tas när en inte svarar eller svarar fel."),
        (SourceMode.Off, "Av", "Inga uppslag görs för kedjan."),
    ];

    /// <summary>The chains' endpoints.</summary>
    private readonly IChainSources _sources;

    /// <summary>The way out, whether lookups can go now.</summary>
    private readonly ExitWay _way;

    /// <summary>The vault, which must be unlocked before anything is tried.</summary>
    private readonly IVault _vault;

    /// <summary>The API keys, whose services the form offers.</summary>
    private readonly IApiKeyStore _keys;

    /// <summary>The UI thread, where changes are shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The time zone the probe's time is shown in.</summary>
    private readonly TimeZoneInfo _timeZone;

    /// <summary>Whether the round at the start has been made (or the chains were off).</summary>
    private bool _startDone;

    /// <summary>Whether the round at the start is running.</summary>
    private bool _startRunning;

    /// <summary>Counts the form's attempts, so that a late answer is not shown in a newer form.</summary>
    private int _tries;

    /// <summary>Creates the page and makes the round at the start when it may.</summary>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="way">The way out.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="keys">The API keys.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="timeZone">The time zone of the probe's time; the computer's when <see langword="null"/>.</param>
    public SourcesViewModel(
        IChainSources sources, ExitWay way, IVault vault, IApiKeyStore keys, IUiThread ui, TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(way);
        ArgumentNullException.ThrowIfNull(vault);
        _sources = sources;
        _way = way;
        _vault = vault;
        _keys = keys;
        _ui = ui;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        Chips = [.. ChainNames.Select(c => new SourceChainChip(
            c.Name, c.Chain, c.Chain is { } chain && sources.Chains.Contains(chain)))];
        ModeChoices = [.. Modes.Select(mode => new SettingsChoice(mode.Label, mode.Mode))];
        sources.Changed += (_, chain) => ui.Post(() => ShowChain(chain));
        way.Changed += (_, _) => _ = ProbeAtStartAsync();
        vault.StateChanged += (_, _) => ui.Post(() => _ = ProbeAtStartAsync());
        ShowChip(Chips[0]);
        _ = ProbeAtStartAsync();
    }

    /// <summary>Gets the chains as chips.</summary>
    public IReadOnlyList<SourceChainChip> Chips { get; }

    /// <summary>Gets the modes.</summary>
    public IReadOnlyList<SettingsChoice> ModeChoices { get; }

    /// <summary>Gets the shown chain's endpoints.</summary>
    public ObservableCollection<EndpointRowViewModel> Rows { get; } = [];

    /// <summary>Gets the sorts of API the form offers for the shown chain.</summary>
    public ObservableCollection<SettingsChoice> ApiChoices { get; } = [];

    /// <summary>Gets the keys the form offers: none, and the services in the vault.</summary>
    public ObservableCollection<SettingsChoice> KeyChoices { get; } = [];

    /// <summary>Gets the chip shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChainName), nameof(IsBuilt), nameof(IsNotBuilt), nameof(NotBuiltText))]
    public partial SourceChainChip Chip { get; private set; } = null!;

    /// <summary>Gets the shown chain's name.</summary>
    public string ChainName => Chip.Name;

    /// <summary>Gets whether the shown chain has sources.</summary>
    public bool IsBuilt => Chip.IsBuilt;

    /// <summary>Gets whether the shown chain is not built yet.</summary>
    public bool IsNotBuilt => !Chip.IsBuilt;

    /// <summary>Gets what the page says for a chain that is not built.</summary>
    public string NotBuiltText => $"{Chip.Name} får sina källor när kedjan byggs.";

    /// <summary>Gets the shown chain's mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeHint), nameof(IsPriority), nameof(IsOff), nameof(OffText), nameof(CanProbe))]
    public partial SourceMode Mode { get; private set; }

    /// <summary>Gets what the mode does.</summary>
    public string ModeHint => Modes.First(mode => mode.Mode == Mode).Hint;

    /// <summary>Gets whether the shown chain is in Prioriterad, so that Alt+↑ and Alt+↓ move an endpoint.</summary>
    public bool IsPriority => Mode == SourceMode.Priority;

    /// <summary>Gets whether the shown chain is off, so that the list is toned.</summary>
    public bool IsOff => Mode == SourceMode.Off;

    /// <summary>Gets what the page says when the chain is off.</summary>
    public string OffText => $"Inga uppslag för {ChainName}. Det som finns i ärendet visas, och grafen säger Av.";

    /// <summary>Gets when the endpoints were last tried, or that they were not.</summary>
    [ObservableProperty]
    public partial string ProbeText { get; private set; } = string.Empty;

    /// <summary>Gets whether a round is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProbe))]
    public partial bool IsProbing { get; private set; }

    /// <summary>Gets whether <em>Prova alla nu</em> can be pressed.</summary>
    public bool CanProbe => IsBuilt && !IsOff && !IsProbing;

    /// <summary>Gets what went wrong last, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether there is something wrong to show.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether the form that adds an endpoint is out.</summary>
    [ObservableProperty]
    public partial bool IsAdding { get; private set; }

    /// <summary>Gets or sets the address written in the form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddressProblem), nameof(HasAddressProblem), nameof(CanAdd), nameof(CanTry))]
    public partial string NewAddress { get; set; } = string.Empty;

    /// <summary>Gets why the address cannot be used (<em>Bara https.</em>), or empty when empty or good.</summary>
    public string AddressProblem =>
        NewAddress.Trim().Length == 0 || SourceAddress.TryRead(NewAddress, out _, out var problem)
            ? string.Empty
            : problem!;

    /// <summary>Gets whether the address has a problem to show.</summary>
    public bool HasAddressProblem => AddressProblem.Length > 0;

    /// <summary>Gets whether the address can be tried.</summary>
    public bool CanTry => SourceAddress.TryRead(NewAddress, out _, out _) && !IsTrying;

    /// <summary>Gets whether the endpoint can be added.</summary>
    public bool CanAdd => SourceAddress.TryRead(NewAddress, out _, out _);

    /// <summary>Gets whether <em>Prova</em> is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTry))]
    public partial bool IsTrying { get; private set; }

    /// <summary>Gets what <em>Prova</em> answered: the height, the block's hash and the time, or why not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTryText))]
    public partial string TryText { get; private set; } = string.Empty;

    /// <summary>Gets whether <em>Prova</em> has something to show.</summary>
    public bool HasTryText => TryText.Length > 0;

    /// <summary>Gets whether <em>Prova</em> got a right answer.</summary>
    [ObservableProperty]
    public partial bool TryAnswered { get; private set; }

    /// <summary>Shows another chain's sources.</summary>
    /// <param name="chip">The chain's chip.</param>
    [RelayCommand]
    public void ShowChip(SourceChainChip chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        foreach (var each in Chips)
        {
            each.IsSelected = ReferenceEquals(each, chip);
        }

        Chip = chip;
        Rows.Clear();
        CancelAdd();
        ApiChoices.Clear();
        if (chip.Chain is { } chain && chip.IsBuilt)
        {
            foreach (var api in _sources.Apis(chain))
            {
                ApiChoices.Add(new SettingsChoice(api.Name, api.Code));
            }

            ShowChain(chain);
        }

        foreach (var each in Chips)
        {
            ShowDot(each);
        }

        OnPropertyChanged(nameof(CanProbe));
    }

    /// <summary>Chooses the shown chain's mode; it applies to the next lookup.</summary>
    /// <param name="choice">The mode's option.</param>
    [RelayCommand]
    public Task ChooseModeAsync(SettingsChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return Save(chain => _sources.SetModeAsync(chain, (SourceMode)choice.Value));
    }

    /// <summary>Switches an endpoint on or off.</summary>
    /// <param name="row">The endpoint.</param>
    [RelayCommand]
    public Task ToggleAsync(EndpointRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Save(chain => _sources.SetEnabledAsync(chain, row.Id, !row.IsOn));
    }

    /// <summary>Removes an endpoint (×).</summary>
    /// <param name="row">The endpoint.</param>
    [RelayCommand]
    public Task RemoveAsync(EndpointRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Save(chain => _sources.RemoveAsync(chain, row.Id));
    }

    /// <summary>Moves an endpoint up one place (Alt+↑ in Prioriterad).</summary>
    /// <param name="row">The endpoint.</param>
    [RelayCommand]
    public Task MoveUpAsync(EndpointRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Save(chain => _sources.MoveAsync(chain, row.Id, -1));
    }

    /// <summary>Moves an endpoint down one place (Alt+↓ in Prioriterad).</summary>
    /// <param name="row">The endpoint.</param>
    [RelayCommand]
    public Task MoveDownAsync(EndpointRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Save(chain => _sources.MoveAsync(chain, row.Id, 1));
    }

    /// <summary>Tries the shown chain's endpoints now (<em>Prova alla nu</em>), through the exit.</summary>
    /// <returns>A task that completes when the round has ended.</returns>
    [RelayCommand]
    public async Task ProbeAllAsync()
    {
        if (Chip.Chain is not { } chain || !CanProbe)
        {
            return;
        }

        IsProbing = true;
        ProbeText = "Provar …";
        try
        {
            var run = await _sources.ProbeAsync(chain).ConfigureAwait(true);
            ShowChain(chain);
            if (run == ProbeRun.NotSent)
            {
                ProbeText = NotSentText;
            }
        }
        finally
        {
            IsProbing = false;
        }
    }

    /// <summary>Folds out the form that adds an endpoint, or folds it in.</summary>
    [RelayCommand]
    public void ToggleAdd()
    {
        if (IsAdding)
        {
            CancelAdd();
            return;
        }

        if (!IsBuilt)
        {
            return;
        }

        NewAddress = string.Empty;
        TryText = string.Empty;
        SettingsChoice.Mark(ApiChoices, ApiChoices.FirstOrDefault()?.Value ?? string.Empty);
        KeyChoices.Clear();
        KeyChoices.Add(new SettingsChoice(NoKeyText, string.Empty));
        foreach (var service in KeyServices())
        {
            KeyChoices.Add(new SettingsChoice(service, service));
        }

        SettingsChoice.Mark(KeyChoices, string.Empty);
        IsAdding = true;
    }

    /// <summary>Folds the form in without adding (Avbryt, Esc).</summary>
    [RelayCommand]
    public void CancelAdd()
    {
        _tries++;
        IsAdding = false;
        IsTrying = false;
        TryText = string.Empty;
        TryAnswered = false;
    }

    /// <summary>Chooses the sort of API in the form.</summary>
    /// <param name="choice">The sort.</param>
    [RelayCommand]
    public void ChooseApi(SettingsChoice choice) => SettingsChoice.Mark(ApiChoices, choice.Value);

    /// <summary>Chooses the key in the form.</summary>
    /// <param name="choice">The key's service, or none.</param>
    [RelayCommand]
    public void ChooseKey(SettingsChoice choice) => SettingsChoice.Mark(KeyChoices, choice.Value);

    /// <summary>
    /// Tries the address in the form (<em>Prova</em>): the chain's height, the block's hash and the time, through the
    /// exit. Nothing is kept.
    /// </summary>
    /// <returns>A task that completes when the answer is shown.</returns>
    [RelayCommand]
    public async Task TryAsync()
    {
        if (Chip.Chain is not { } chain || !SourceAddress.TryRead(NewAddress, out var address, out _) || IsTrying)
        {
            return;
        }

        var attempt = ++_tries;
        IsTrying = true;
        TryText = "Provar …";
        TryAnswered = false;
        var probe = await _sources.TryAsync(chain, address!, ChosenApi, ChosenKey).ConfigureAwait(true);
        if (attempt != _tries)
        {
            return;
        }

        IsTrying = false;
        TryAnswered = probe.Outcome == ProbeOutcome.Answered;
        TryText = TryAnswered ? AnswerText(probe) : probe.Problem ?? "Svarade inte.";
    }

    /// <summary>Adds the address in the form last in the list, switched on (Lägg till, Enter).</summary>
    /// <returns>A task that completes when the change is written.</returns>
    [RelayCommand]
    public async Task AddAsync()
    {
        if (Chip.Chain is not { } chain || !SourceAddress.TryRead(NewAddress, out var address, out _))
        {
            return;
        }

        var (_, saved) = _sources.Add(chain, address!, ChosenApi, ChosenKey);
        CancelAdd();
        await Saved(saved).ConfigureAwait(true);
    }

    /// <summary>Says what a right answer to <em>Prova</em> was: <c>Höjd 921 404 · 0000…3f2a · 38 ms</c>.</summary>
    /// <param name="probe">The answer.</param>
    /// <returns>The words.</returns>
    public static string AnswerText(EndpointProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var height = (probe.Height ?? 0).ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
        var hash = probe.BlockHash is { Length: > 12 } full ? $"{full[..8]}…{full[^4..]}" : probe.BlockHash ?? "–";
        var milliseconds = (int)Math.Round(probe.Duration?.TotalMilliseconds ?? 0);
        return string.Create(CultureInfo.InvariantCulture, $"Höjd {height} · {hash} · {milliseconds} ms");
    }

    /// <summary>Gets the sort of API chosen in the form.</summary>
    private string ChosenApi => (string)(ApiChoices.FirstOrDefault(c => c.IsSelected)?.Value ?? string.Empty);

    /// <summary>Gets the key's service chosen in the form, or <see langword="null"/> for none.</summary>
    private string? ChosenKey =>
        KeyChoices.FirstOrDefault(c => c.IsSelected)?.Value is string { Length: > 0 } service ? service : null;

    /// <summary>
    /// Tries every built chain that is not off, once, when the vault is unlocked and the exit allows lookups (CLAUDE.md:
    /// <em>Snabbast</em> tries them at the start, through the exit). A round in which nothing could be sent is made
    /// again the next time both hold.
    /// </summary>
    /// <returns>A task that completes when the round has ended.</returns>
    public async Task ProbeAtStartAsync()
    {
        if (_startDone || _startRunning || _vault.State != VaultState.Unlocked || !_way.IsLive)
        {
            return;
        }

        _startRunning = true;
        try
        {
            var runs = new List<ProbeRun>();
            foreach (var chain in _sources.Chains)
            {
                runs.Add(await _sources.ProbeAsync(chain).ConfigureAwait(true));
            }

            _startDone = !runs.Contains(ProbeRun.NotSent);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A round that fails is made again the next time the exit or the vault changes.
        }
        finally
        {
            _startRunning = false;
        }
    }

    /// <summary>Makes a change to the shown chain and says when it could not be written.</summary>
    /// <param name="change">The change.</param>
    /// <returns>A task that completes when it is written.</returns>
    private Task Save(Func<Chain, Task> change) =>
        Chip.Chain is { } chain && IsBuilt ? Saved(change(chain)) : Task.CompletedTask;

    /// <summary>Waits for a write and shows its failure.</summary>
    /// <param name="saved">The write.</param>
    /// <returns>A task that completes when it is written or has failed.</returns>
    private async Task Saved(Task saved)
    {
        try
        {
            await saved.ConfigureAwait(true);
            ErrorText = string.Empty;
        }
        catch (IOException)
        {
            ErrorText = SaveFailedText;
        }
    }

    /// <summary>Shows a chain's sources when it is the one shown, and its chip's dot.</summary>
    /// <param name="chain">The chain.</param>
    private void ShowChain(Chain chain)
    {
        foreach (var chip in Chips.Where(chip => chip.Chain == chain))
        {
            ShowDot(chip);
        }

        if (Chip.Chain != chain || !Chip.IsBuilt)
        {
            return;
        }

        var view = _sources.Get(chain);
        Mode = view.Mode;
        SettingsChoice.Mark(ModeChoices, view.Mode);
        ShowRows(view);
        ProbeText = view.ProbedAt is { } at
            ? "Provade " + TimeZoneInfo.ConvertTime(at, _timeZone).ToString("HH:mm", CultureInfo.InvariantCulture)
            : "Inte provade än";
    }

    /// <summary>Shows the rows, keeping each endpoint's row so that moving one keeps its focus.</summary>
    /// <param name="view">The chain's sources.</param>
    private void ShowRows(ChainSourcesView view)
    {
        var ids = view.Endpoints.Select(endpoint => endpoint.Endpoint.Id).ToList();
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Rows[i].Id))
            {
                Rows.RemoveAt(i);
            }
        }

        for (var index = 0; index < view.Endpoints.Count; index++)
        {
            var endpoint = view.Endpoints[index];
            var at = IndexOf(endpoint.Endpoint.Id);
            EndpointRowViewModel row;
            if (at < 0)
            {
                row = new EndpointRowViewModel(endpoint.Endpoint.Id);
                Rows.Insert(index, row);
            }
            else
            {
                row = Rows[at];
                if (at != index)
                {
                    Rows.Move(at, index);
                }
            }

            row.Show(endpoint, view.Mode, index, view.Endpoints.Count);
        }
    }

    /// <summary>Finds an endpoint's row.</summary>
    /// <param name="id">The endpoint.</param>
    /// <returns>Its place, or -1.</returns>
    private int IndexOf(Guid id)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Shows a chip's dot: grey when off, not built or not tried, green when the one used answered right, yellow when
    /// it lags behind, red when none can be used.
    /// </summary>
    /// <param name="chip">The chip.</param>
    private void ShowDot(SourceChainChip chip)
    {
        if (chip.Chain is not { } chain || !chip.IsBuilt)
        {
            chip.Dot = EndpointDot.Off;
            return;
        }

        var view = _sources.Get(chain);
        if (view.Mode == SourceMode.Off)
        {
            chip.Dot = EndpointDot.Off;
        }
        else if (view.InUse is not { } used)
        {
            chip.Dot = EndpointDot.Failed;
        }
        else
        {
            chip.Dot = used.Probe?.Outcome switch
            {
                ProbeOutcome.Answered => EndpointDot.Answered,
                ProbeOutcome.Behind => EndpointDot.Behind,
                _ => EndpointDot.Untried,
            };
        }
    }

    /// <summary>Lists the services of the keys in the vault, or none when it cannot be read.</summary>
    /// <returns>The services.</returns>
    private IEnumerable<string> KeyServices()
    {
        try
        {
            return [.. _keys.List().Select(key => key.Service)];
        }
        catch (VaultException)
        {
            return [];
        }
    }
}
