using Chainsaw.App.Exits;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The first-run setup (sketch FS1a), shown when there is no vault: one thing at a time on the lock screen's field.
/// Welcome gives the operator profile, Master key creates the vault, Exit chooses the way out (sketch FS2a: Windows,
/// chosen from the start, or VPN, with the route as a picture under the cards and a WireGuard file under VPN), and
/// First case creates a case through Cases (or is skipped). The master key is never a property: it is
/// taken from the fields as characters, given to the vault off the UI thread and cleared.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    /// <summary>The steps' names, in their order.</summary>
    private static readonly string[] StepNames = ["Välkommen", "Huvudnyckel", "Utgång", "Första ärendet"];

    /// <summary>The vault the master key creates.</summary>
    private readonly IVault _vault;

    /// <summary>Where the operator profile is stored.</summary>
    private readonly IOperatorProfileStore _profiles;

    /// <summary>Where the exit is stored.</summary>
    private readonly IExitStore _exits;

    /// <summary>Creates the first case and makes it the window's open case.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>The way out and the exit's state, for the route; none in some tests.</summary>
    private readonly ExitWay? _way;

    /// <summary>The settings, where the way out chosen is applied and saved; none in some tests.</summary>
    private readonly SettingsViewModel? _settings;

    /// <summary>The connection to the exit, which the file's row connects; none in some tests.</summary>
    private readonly IExitConnection? _connection;

    /// <summary>The exit stored in this setup, or <see langword="null"/>.</summary>
    private Guid? _exitId;

    /// <summary>Creates the setup at its first step.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="profiles">The operator profile's store.</param>
    /// <param name="exits">The exits' store.</param>
    /// <param name="workflow">Creates the first case.</param>
    /// <param name="way">The way out and the exit's state, for the route; none in some tests.</param>
    /// <param name="settings">The settings, where the way out chosen is applied and saved; none in some tests.</param>
    /// <param name="connection">The connection to the exit, which the file's row connects; none in some tests.</param>
    public FirstRunViewModel(
        IVault vault,
        IOperatorProfileStore profiles,
        IExitStore exits,
        CaseWorkflow workflow,
        ExitWay? way = null,
        SettingsViewModel? settings = null,
        IExitConnection? connection = null)
    {
        _vault = vault;
        _profiles = profiles;
        _exits = exits;
        _workflow = workflow;
        _way = way;
        _settings = settings;
        _connection = connection;
        if (way is not null)
        {
            way.Changed += (_, _) => ShowRoute();
        }
    }

    /// <summary>Raised when the setup is done and the shell can be shown.</summary>
    public event EventHandler? Completed;

    /// <summary>Gets the step shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLabel), nameof(Dots), nameof(CanGoBack), nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsMasterKey), nameof(IsExit), nameof(IsFirstCase))]
    public partial FirstRunStep Step { get; private set; } = FirstRunStep.Welcome;

    /// <summary>Gets or sets the operator's name; it stands as *utförd av* in the journal.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator's title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator's organisation.</summary>
    [ObservableProperty]
    public partial string Organization { get; set; } = string.Empty;

    /// <summary>Gets or sets the first case's number.</summary>
    [ObservableProperty]
    public partial string CaseNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the first case's title; it may be left empty.</summary>
    [ObservableProperty]
    public partial string CaseTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the first case's start point; it may be left empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartPoint), nameof(HasStartPoint))]
    public partial string StartPointText { get; set; } = string.Empty;

    /// <summary>Gets what Chains recognises in the start point, for the line under the field.</summary>
    public StartPointLine StartPoint => new(Links.StartPointText.Parse(StartPointText));

    /// <summary>Gets whether a start point is written, so that the line under the field is shown.</summary>
    public bool HasStartPoint => StartPointText.Trim().Length > 0;

    /// <summary>
    /// Gets the answer from creating the first case, or <see langword="null"/>; the shell shows it after the setup,
    /// so that a title or start point that could not be added is said.
    /// </summary>
    public SharedKernel.Commands.CommandReply? CreationReply { get; private set; }

    /// <summary>Takes the answer from creating the first case, so that it is shown once.</summary>
    /// <returns>The answer, or <see langword="null"/> when there is none or it was taken.</returns>
    public SharedKernel.Commands.CommandReply? TakeCreationReply()
    {
        var reply = CreationReply;
        CreationReply = null;
        return reply;
    }

    /// <summary>Gets why the last step did not go on, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether an error is shown.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether a step is working (creating the vault, storing the exit, creating the case).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets the name of the exit that was stored, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExit), nameof(NeedsExitFile))]
    public partial string ExitName { get; private set; } = string.Empty;

    /// <summary>Gets the name of the WireGuard file the exit was stored from, without its folder.</summary>
    [ObservableProperty]
    public partial string ExitFileName { get; private set; } = string.Empty;

    /// <summary>Gets whether an exit was stored in this setup.</summary>
    public bool HasExit => ExitName.Length > 0;

    /// <summary>
    /// Gets the way out chosen on the step Utgång (sketch FS2a); Windows from the start (the user's decision
    /// 2026-11-08), since the computer often has its own protection.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsChosen), nameof(IsVpnChosen), nameof(NeedsExitFile))]
    public partial ExitRoute Route { get; private set; } = ExitRoute.Windows;

    /// <summary>Gets whether the card Windows is marked.</summary>
    public bool IsWindowsChosen => Route == ExitRoute.Windows;

    /// <summary>Gets whether the card VPN is marked, so that the WireGuard file is asked for under it.</summary>
    public bool IsVpnChosen => Route == ExitRoute.Vpn;

    /// <summary>Gets whether the dashed field for a WireGuard file is shown: VPN, and no exit stored yet.</summary>
    public bool NeedsExitFile => IsVpnChosen && !HasExit;

    /// <summary>Gets whether lookups can go the way chosen now (the route is whole and green).</summary>
    [ObservableProperty]
    public partial bool IsRouteLive { get; private set; }

    /// <summary>
    /// Gets the middle of the route: Windows, the exit's name, <em>Offline</em> or <em>Ingen utgång</em>.
    /// </summary>
    [ObservableProperty]
    public partial string RouteName { get; private set; } = WindowsText;

    /// <summary>Gets the route's verdict in its colour, such as <em>Datorn når internet.</em></summary>
    [ObservableProperty]
    public partial string RouteVerdict { get; private set; } = string.Empty;

    /// <summary>Gets what the verdict goes on to say.</summary>
    [ObservableProperty]
    public partial string RouteDetail { get; private set; } = string.Empty;

    /// <summary>Gets whether the stored exit is connected or being connected (the round button is on).</summary>
    [ObservableProperty]
    public partial bool IsExitOn { get; private set; }

    /// <summary>Gets the stored exit's state under its name: <em>Ansluten</em> or <em>Inte ansluten</em>.</summary>
    [ObservableProperty]
    public partial string ExitStateText { get; private set; } = ExitRowViewModel.NotConnectedText;

    /// <summary>Gets whether the stored exit can be connected here.</summary>
    public bool CanConnectExit => _connection is not null;

    /// <summary>Gets whether the vault already exists, so the master key step only says so.</summary>
    public bool IsMasterKeyChosen => _vault.State != VaultState.NotCreated;

    /// <summary>Gets the line above the step: <c>Steg 2 av 4 · Huvudnyckel</c>.</summary>
    public string StepLabel => $"Steg {(int)Step} av {StepNames.Length} · {StepNames[(int)Step - 1]}";

    /// <summary>Gets the dashes at the top, one per step.</summary>
    public IReadOnlyList<StepDot> Dots =>
        StepNames.Select((name, index) => new StepDot(name, index < (int)Step)).ToList();

    /// <summary>Gets whether there is a step to go back to.</summary>
    public bool CanGoBack => Step > FirstRunStep.Welcome;

    /// <summary>
    /// Gets the main button's text; on the step Utgång it says honestly when Chainsaw goes on without lookups.
    /// </summary>
    public string NextLabel => Step switch
    {
        FirstRunStep.FirstCase => "Skapa ärendet",
        FirstRunStep.Exit when IsRouteLive => "Fortsätt",
        FirstRunStep.Exit when IsWindowsChosen => "Fortsätt offline",
        FirstRunStep.Exit => "Fortsätt utan utgång",
        _ => "Nästa",
    };

    /// <summary>What the middle of the route says through Windows.</summary>
    private const string WindowsText = "Windows";

    /// <summary>What the middle of the route says with VPN and no exit stored.</summary>
    private const string NoExitText = "Ingen utgång";

    /// <summary>Gets whether the welcome step is shown.</summary>
    public bool IsWelcome => Step == FirstRunStep.Welcome;

    /// <summary>Gets whether the master key step is shown.</summary>
    public bool IsMasterKey => Step == FirstRunStep.MasterKey;

    /// <summary>Gets whether the exit step is shown.</summary>
    public bool IsExit => Step == FirstRunStep.Exit;

    /// <summary>Gets whether the first case step is shown.</summary>
    public bool IsFirstCase => Step == FirstRunStep.FirstCase;

    /// <summary>
    /// Goes on from the step shown: checks the name, creates the vault from the two master key fields, or creates the
    /// first case. The master key fields are read only on the master key step, and are then always emptied and their
    /// characters cleared.
    /// </summary>
    /// <param name="masterKey">The master key field.</param>
    /// <param name="repeat">The field where the master key is written again.</param>
    /// <returns>A task that completes when the step is done or has said why not.</returns>
    public async Task NextAsync(ISecretEntry masterKey, ISecretEntry repeat)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorText = string.Empty;
        switch (Step)
        {
            case FirstRunStep.Welcome:
                await LeaveWelcomeAsync();
                break;
            case FirstRunStep.MasterKey:
                await ChooseMasterKeyAsync(masterKey, repeat);
                break;
            case FirstRunStep.Exit:
                _settings?.ChooseRoute(Route);
                Step = FirstRunStep.FirstCase;
                break;
            case FirstRunStep.FirstCase:
                await CreateCaseAsync();
                break;
        }
    }

    /// <summary>Goes back one step.</summary>
    public void Back()
    {
        if (CanGoBack && !IsBusy)
        {
            ErrorText = string.Empty;
            Step--;
            OnPropertyChanged(nameof(IsMasterKeyChosen));
        }
    }

    /// <summary>
    /// Chooses the way out on the step Utgång (the cards Windows and VPN). It is applied and saved at once, so that
    /// the route under the cards shows what holds.
    /// </summary>
    /// <param name="route">The way out.</param>
    public void ChooseRoute(ExitRoute route)
    {
        if (IsBusy || !Enum.IsDefined(route))
        {
            return;
        }

        Route = route;
        _settings?.ChooseRoute(route);
        ShowRoute();
    }

    /// <summary>
    /// Connects the stored exit, or disconnects it (the round button on the file's row), off the UI thread; a failure
    /// is shown by the route.
    /// </summary>
    /// <returns>A task that completes when the attempt has ended.</returns>
    public async Task ToggleExitAsync()
    {
        if (_connection is not { } connection || _exitId is null)
        {
            return;
        }

        try
        {
            if (IsExitOn)
            {
                await Task.Run(connection.DisconnectAsync);
            }
            else
            {
                await Task.Run(() => connection.ConnectAsync(CancellationToken.None));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorText = "Utgången gick inte att ansluta. Den kan anslutas senare i titelraden.";
        }
    }

    /// <summary>Takes the stored exit away again (× on the file's row): it is removed from the vault.</summary>
    /// <returns>A task that completes when it is removed, or why not is shown.</returns>
    public async Task RemoveExitAsync()
    {
        if (_exitId is not { } id || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _exits.RemoveAsync(id));
            _exitId = null;
            ExitName = string.Empty;
            ExitFileName = string.Empty;
            ShowRoute();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorText = "Utgången gick inte att ta bort.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Leaves the exit for later: Chainsaw works offline until an exit is added.</summary>
    public void Later()
    {
        if (!IsBusy)
        {
            ErrorText = string.Empty;
            Step = FirstRunStep.FirstCase;
        }
    }

    /// <summary>Skips the first case and ends the setup.</summary>
    public void Skip()
    {
        if (!IsBusy)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stores a WireGuard file as the exit, through Network, encrypted in the vault, and selects it. Nothing is
    /// connected here: the tunnel connects when the user asks.
    /// </summary>
    /// <param name="path">The file the user chose.</param>
    /// <returns>A task that completes when the exit is stored or the reason is shown.</returns>
    public async Task AddExitAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var name = ExitNames.FromFile(path);
            var (exit, verdict) = await Task.Run(() => StoreExit(path, name));
            if (exit is null)
            {
                ErrorText = $"WireGuard-filen gick inte att använda: {WireGuardCheck.TextFor(verdict)}.";
                return;
            }

            _exitId = exit.Id;
            ExitName = exit.Name;
            ExitFileName = Path.GetFileName(path);
            await SelectExitAsync(exit);
            ShowRoute();
        }
        catch (ArgumentException)
        {
            ErrorText = "WireGuard-filen gick inte att använda. Den ska vara en WireGuard-konfiguration med en "
                + "rad DNS =.";
        }
        catch (VaultException)
        {
            ErrorText = "Valvet är låst. Lås upp och lägg till utgången igen.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = "Filen gick inte att läsa.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorText = "Utgången gick inte att spara.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Draws the route under the cards (sketch FS2a): Windows while the computer reaches the internet, or the stored
    /// exit while it is connected; else red and dashed with what holds instead.
    /// </summary>
    private void ShowRoute()
    {
        var status = _way?.Status;
        var mine = status is not null && _exitId is not null && status.ExitId == _exitId;
        IsExitOn = mine && status!.State is ExitState.Connected or ExitState.Connecting;
        ExitStateText = mine && status!.State == ExitState.Connected
            ? ExitRowViewModel.ConnectedText
            : ExitRowViewModel.NotConnectedText;
        if (IsWindowsChosen)
        {
            var online = _way?.IsOnline ?? true;
            IsRouteLive = online;
            RouteName = online ? WindowsText : ExitPanelViewModel.NoRouteText;
            (RouteVerdict, RouteDetail) = online
                ? ("Datorn når internet.", "Uppslagen går genom datorns nät, med dess proxy och skydd. "
                    + "Källorna ser datorns eller organisationens adress.")
                : ("Datorn når inte internet.", "Inga uppslag görs förrän den gör det. Allt som redan finns i ett "
                    + "ärende går att arbeta med.");
        }
        else
        {
            IsRouteLive = mine && status!.State == ExitState.Connected;
            RouteName = HasExit ? ExitName : NoExitText;
            (RouteVerdict, RouteDetail) = IsRouteLive
                ? ("Ansluten.", $"Uppslagen går bara genom {ExitName}. Källorna ser utgångens adress, inte datorns.")
                : ("Inga uppslag.", "Utan ansluten utgång hämtas inget nytt. Lägg till en WireGuard-fil nu eller "
                    + "senare i Inställningar.");
        }

        OnPropertyChanged(nameof(NextLabel));
    }

    /// <summary>
    /// Reads, checks and stores a WireGuard file in the vault, and clears its text; it becomes a string only for
    /// <see cref="IExitStore.Add"/>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="name">The exit's name.</param>
    /// <returns>The stored exit, or <see langword="null"/> and what the check found.</returns>
    private (ExitInfo? Exit, WireGuardVerdict Verdict) StoreExit(string path, string name)
    {
        var text = WireGuardFile.Read(path);
        if (text is null)
        {
            return (null, WireGuardVerdict.TooLarge);
        }

        try
        {
            var verdict = WireGuardCheck.Check(text);
            return verdict == WireGuardVerdict.Valid
                ? (_exits.Add(name, string.Empty, ExitKind.WireGuard, new string(text)), verdict)
                : (null, verdict);
        }
        finally
        {
            Secret.Clear(text);
        }
    }

    /// <summary>
    /// Selects the stored exit. A failure here is not the file's fault: the exit is stored, and is chosen later in
    /// the settings, so the setup goes on rather than inviting a second copy.
    /// </summary>
    /// <param name="exit">The stored exit.</param>
    /// <returns>A task that completes when the exit is selected or the failure is passed over.</returns>
    private async Task SelectExitAsync(ExitInfo exit)
    {
        try
        {
            await Task.Run(() => _exits.SelectAsync(exit.Id));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Stored but not selected; the exit's settings show it.
        }
    }

    /// <summary>Checks the name, stores the profile when the vault already exists, and goes on.</summary>
    /// <returns>A task that completes when the step is done.</returns>
    private async Task LeaveWelcomeAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorText = "Skriv ditt namn. Det står som utförd av i journalen.";
            return;
        }

        if (IsMasterKeyChosen && !await SaveProfileAsync())
        {
            return;
        }

        Step = FirstRunStep.MasterKey;
        OnPropertyChanged(nameof(IsMasterKeyChosen));
    }

    /// <summary>
    /// Creates the vault with the master key, when both fields agree and the key is long enough, and stores the
    /// profile in it. When the vault already exists, the fields are only emptied.
    /// </summary>
    /// <param name="masterKeyEntry">The master key field.</param>
    /// <param name="repeatEntry">The repeat field.</param>
    /// <returns>A task that completes when the step is done.</returns>
    private async Task ChooseMasterKeyAsync(ISecretEntry masterKeyEntry, ISecretEntry repeatEntry)
    {
        var masterKey = masterKeyEntry.Take();
        var repeat = repeatEntry.Take();
        IsBusy = true;
        try
        {
            if (!IsMasterKeyChosen && !await CreateVaultAsync(masterKey, repeat))
            {
                return;
            }
        }
        finally
        {
            Secret.Clear(masterKey);
            Secret.Clear(repeat);
            IsBusy = false;
        }

        OnPropertyChanged(nameof(IsMasterKeyChosen));
        if (await SaveProfileAsync())
        {
            Step = FirstRunStep.Exit;
            ShowRoute();
        }
    }

    /// <summary>Creates the vault, after checking the two fields.</summary>
    /// <param name="masterKey">The master key.</param>
    /// <param name="repeat">The master key written again.</param>
    /// <returns><see langword="true"/> when the vault was created.</returns>
    private async Task<bool> CreateVaultAsync(char[] masterKey, char[] repeat)
    {
        if (!MasterKeyRules.IsLongEnough(masterKey))
        {
            ErrorText = $"Huvudnyckeln ska ha minst {MasterKeyRules.MinimumLength} tecken.";
            return false;
        }

        if (!masterKey.AsSpan().SequenceEqual(repeat))
        {
            ErrorText = "Huvudnycklarna är inte lika. Skriv samma nyckel två gånger.";
            return false;
        }

        try
        {
            await Task.Run(() => _vault.Create(masterKey));
            return true;
        }
        catch (VaultException exception)
        {
            ErrorText = exception.Error == VaultError.AlreadyCreated
                ? "Det finns redan ett valv på den här datorn."
                : "Valvet gick inte att skapa.";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = "Valvet gick inte att spara. Kontrollera att mappen går att skriva i.";
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorText = "Valvet gick inte att skapa.";
            return false;
        }
    }

    /// <summary>Stores the operator profile in the vault, off the UI thread.</summary>
    /// <returns><see langword="true"/> when it was stored.</returns>
    private async Task<bool> SaveProfileAsync()
    {
        var profile = new OperatorProfile(Name.Trim(), Title.Trim(), Organization.Trim());
        IsBusy = true;
        try
        {
            await Task.Run(() => _profiles.Save(profile));
            return true;
        }
        catch (VaultException)
        {
            ErrorText = "Valvet är låst, så profilen sparades inte.";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = "Profilen gick inte att spara i valvet.";
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorText = "Profilen gick inte att spara i valvet.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Creates the first case with its title and start point through <see cref="CaseWorkflow"/>, which makes it the
    /// open case, and ends the setup.
    /// </summary>
    /// <returns>A task that completes when the case is created or the reason is shown.</returns>
    private async Task CreateCaseAsync()
    {
        var number = CaseNumber.Trim();
        if (number.Length == 0)
        {
            ErrorText = "Skriv ett ärendenummer, eller hoppa över.";
            return;
        }

        if (!CaseNumberRules.IsValid(number))
        {
            ErrorText = CaseTexts.InvalidCaseNumber;
            return;
        }

        if (!CaseTitleRules.IsValid(CaseTitle))
        {
            ErrorText = CaseTexts.InvalidTitle;
            return;
        }

        var startPoint = StartPoint.StartPoint;
        if (HasStartPoint && !startPoint.IsRecognised)
        {
            ErrorText = "Startpunkten känns inte igen som en adress eller en tx.";
            return;
        }

        var request = new NewCaseRequest(
            number, CaseTitleRules.Normalize(CaseTitle), HasStartPoint ? [startPoint] : []);
        IsBusy = true;
        CaseCreation creation;
        try
        {
            creation = await _workflow.CreateAsync(request, sharingPassword: null);
        }
        finally
        {
            IsBusy = false;
        }

        if (creation.Exists)
        {
            // Shown in the shell once it is shown, also after a lock that cut the creation short.
            CreationReply = creation.Reply;
        }

        if (!creation.IsCreated)
        {
            ErrorText = creation.Reply.Text;
            return;
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }
}
