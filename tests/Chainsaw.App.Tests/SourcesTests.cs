using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The settings' page Källor (sketch KL1a, etapp 24): the chains as chips, the modes, the rows with their dot, time, ★
/// and yellow mark, the form that adds an endpoint (only https, Prova), the round at the start that waits for an
/// unlocked vault and an exit that allows lookups, and Av in the graph's corner.
/// </summary>
public sealed class SourcesTests
{
    private static readonly Guid MempoolId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LanId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private readonly TestChainSources _sources = new();

    private readonly TestExitConnection _exit = new();

    private readonly TestExitRoute _route = new();

    private readonly TestVault _vault = new(VaultState.Locked);

    private readonly TestApiKeys _keys = new();

    [Fact]
    public void The_page_comes_after_nätverk_with_the_chains_as_chips_and_those_not_built_toned()
    {
        var page = Create();

        Assert.Equal(SettingsPage.Sources, SettingsMenu.Groups[0].Entries[1].Page);
        Assert.Equal("Källor", SettingsMenu.For(SettingsPage.Sources).Name);
        Assert.Equal(["Bitcoin", "Ethereum", "TRON", "Solana"], page.Chips.Select(c => c.Name));
        Assert.Equal([true, false, false, false], page.Chips.Select(c => c.IsBuilt));
        Assert.True(page.Chips[0].IsSelected);
        Assert.Equal("Ethereum · Inte byggt än", page.Chips[1].Tip);

        page.ShowChip(page.Chips[1]);

        Assert.True(page.IsNotBuilt);
        Assert.Empty(page.Rows);
        Assert.False(page.CanProbe);
        Assert.Equal("Ethereum får sina källor när kedjan byggs.", page.NotBuiltText);
    }

    [Fact]
    public void The_rows_show_the_dot_the_time_the_star_and_the_mark_for_one_behind()
    {
        _sources.Probes[MempoolId] = Probe(MempoolId, ProbeOutcome.Answered, 182);
        _sources.Probes[LanId] = Probe(LanId, ProbeOutcome.Behind, 38) with { BlocksBehind = 2, Problem = "Höjd" };
        _sources.ProbedAt = new DateTimeOffset(2026, 11, 7, 9, 12, 0, TimeSpan.Zero);

        var page = Create();

        var mempool = page.Rows[0];
        var lan = page.Rows[1];
        Assert.Equal(EndpointDot.Answered, mempool.Dot);
        Assert.Equal("182 ms", mempool.MillisecondsText);
        Assert.True(mempool.IsInUse);
        Assert.Equal("Snabbast just nu, används", mempool.InUseTip);
        Assert.Equal(EndpointDot.Behind, lan.Dot);
        Assert.Equal("2 block efter", lan.Note);
        Assert.False(lan.IsInUse);
        Assert.Equal("182", mempool.MillisecondsNumber);
        Assert.Equal("Höjd 921 404", mempool.AnswerText);
        Assert.Equal("https://mempool.space/api", mempool.Address);
        Assert.Equal("Provade 09:12", page.ProbeText);
        Assert.Equal(EndpointDot.Answered, page.Chips[0].Dot);
    }

    [Fact]
    public async Task The_modes_change_the_chain_and_av_tones_the_list_and_says_so()
    {
        var page = Create();

        await page.ChooseModeAsync(page.ModeChoices[2]);

        Assert.Equal(SourceMode.Off, _sources.Mode);
        Assert.True(page.IsOff);
        Assert.False(page.CanProbe);
        Assert.True(page.ModeChoices[2].IsSelected);
        Assert.Contains("grafen säger Av", page.OffText, StringComparison.Ordinal);
        Assert.Equal(EndpointDot.Off, page.Chips[0].Dot);

        await page.ChooseModeAsync(page.ModeChoices[1]);

        Assert.True(page.IsPriority);
        Assert.All(page.Rows, row => Assert.True(row.IsPriority));
        Assert.Equal(["1", "2"], page.Rows.Select(r => r.Position));
        Assert.Equal("Används nu", page.Rows[0].InUseTip);
    }

    [Fact]
    public async Task Moving_keeps_each_endpoints_row_so_that_it_keeps_the_focus()
    {
        var page = Create();
        var lan = page.Rows[1];

        await page.MoveUpAsync(lan);

        Assert.Same(lan, page.Rows[0]);
        Assert.False(lan.CanMoveUp);
        Assert.True(lan.CanMoveDown);
        Assert.Equal([LanId, MempoolId], _sources.Endpoints.Select(e => e.Id));
    }

    [Fact]
    public async Task The_switch_and_the_cross_change_the_endpoint()
    {
        var page = Create();

        await page.ToggleAsync(page.Rows[1]);
        Assert.False(page.Rows[1].IsOn);
        Assert.Equal(EndpointDot.Off, page.Rows[1].Dot);
        Assert.Equal("Slå på", page.Rows[1].SwitchTip);

        await page.RemoveAsync(page.Rows[1]);
        Assert.Single(page.Rows);
    }

    [Fact]
    public async Task A_change_that_cannot_be_written_is_said()
    {
        var page = Create();
        _sources.SaveFailure = new IOException("disk");

        await page.ToggleAsync(page.Rows[0]);

        Assert.Equal(SourcesViewModel.SaveFailedText, page.ErrorText);
    }

    [Fact]
    public async Task A_problem_is_said_once_in_the_toaster()
    {
        var toaster = new Toaster(new InlineUi());
        var page = Create(toaster);
        _sources.SaveFailure = new IOException("disk");

        await page.ToggleAsync(page.Rows[0]);
        await page.ToggleAsync(page.Rows[0]);

        var toast = Assert.Single(toaster.Toasts);
        Assert.Equal(ToastKind.Error, toast.Kind);
        Assert.Equal(SourcesViewModel.SaveFailedText, toast.Title);
    }

    [Fact]
    public void The_form_takes_only_https_and_offers_the_vaults_keys()
    {
        _vault.Set(VaultState.Unlocked);
        _keys.Services.Add("esplora-lan");
        var page = Create();

        page.ToggleAdd();
        page.NewAddress = "http://esplora.lan/api";

        Assert.True(page.IsAdding);
        Assert.Equal("Bara https.", page.AddressProblem);
        Assert.False(page.CanAdd);
        Assert.False(page.CanTry);
        Assert.Equal([SourcesViewModel.NoKeyText, "esplora-lan"], page.KeyChoices.Select(c => c.Label));
        Assert.True(page.KeyChoices[0].IsSelected);
        Assert.Equal("Esplora", Assert.Single(page.ApiChoices).Label);

        page.NewAddress = "https://esplora.lan/api";
        Assert.Equal(string.Empty, page.AddressProblem);
        Assert.True(page.CanAdd);
    }

    [Fact]
    public async Task Prova_shows_the_height_the_hash_and_the_time_and_lägg_till_adds_it_with_its_key()
    {
        _keys.Services.Add("esplora-lan");
        var page = Create();
        page.ToggleAdd();
        page.NewAddress = "https://esplora.lan/api";
        page.ChooseKey(page.KeyChoices[1]);
        _sources.TryAnswer = new EndpointProbe(Guid.Empty, ProbeOutcome.Answered, 921_404,
            "00000000" + new string('a', 52) + "3f2a", TimeSpan.FromMilliseconds(38), DateTimeOffset.UnixEpoch, null);

        await page.TryAsync();

        Assert.True(page.TryAnswered);
        Assert.Equal("Höjd 921 404 · 00000000…3f2a · 38 ms", page.TryText);
        Assert.Empty(_sources.Added);

        await page.AddAsync();

        var added = Assert.Single(_sources.Added);
        Assert.Equal(
            ("https://esplora.lan/api/", "esplora", "esplora-lan"), (added.Address.AbsoluteUri, added.Api, added.Key));
        Assert.False(page.IsAdding);
        Assert.Equal(3, page.Rows.Count);
    }

    [Fact]
    public async Task Prova_that_sends_nothing_says_why()
    {
        var page = Create();
        page.ToggleAdd();
        page.NewAddress = "https://esplora.lan/api";
        _sources.TryAnswer = new EndpointProbe(
            Guid.Empty, ProbeOutcome.NotSent, null, null, null, DateTimeOffset.UnixEpoch, "Ingen utgång är ansluten.");

        await page.TryAsync();

        Assert.False(page.TryAnswered);
        Assert.Equal("Ingen utgång är ansluten.", page.TryText);
    }

    [Fact]
    public async Task Prova_that_throws_says_so_without_the_raw_message_and_can_be_tried_again()
    {
        var page = Create();
        page.ToggleAdd();
        page.NewAddress = "https://esplora.lan/api";
        _sources.TryFailure = new ArgumentException(@"C:\hemlig\sökväg");

        await page.TryAsync();

        Assert.False(page.IsTrying);
        Assert.False(page.TryAnswered);
        Assert.Equal(SourcesViewModel.ProbeFailedText, page.TryText);
    }

    [Fact]
    public void Nothing_is_tried_at_the_start_until_the_vault_is_unlocked_and_the_exit_allows_lookups()
    {
        Create();
        Assert.Equal(0, _sources.Rounds);

        _vault.Set(VaultState.Unlocked);
        Assert.Equal(0, _sources.Rounds);

        _exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));
        Assert.Equal(1, _sources.Rounds);

        _exit.Set(ExitStatus.NotConnected);
        _exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));
        Assert.Equal(1, _sources.Rounds);
    }

    [Fact]
    public void A_round_at_the_start_that_sent_nothing_is_made_again()
    {
        _vault.Set(VaultState.Unlocked);
        _sources.Run = ProbeRun.NotSent;
        Create();
        _route.Choose(ExitRoute.Windows);
        Assert.Equal(1, _sources.Rounds);

        _sources.Run = ProbeRun.Probed;
        _route.Choose(ExitRoute.Vpn);
        _route.Choose(ExitRoute.Windows);

        Assert.Equal(2, _sources.Rounds);
    }

    [Fact]
    public async Task Prova_alla_nu_tries_the_chain_and_says_when_nothing_could_be_sent()
    {
        var page = Create();
        _sources.Run = ProbeRun.NotSent;

        await page.ProbeAllAsync();

        Assert.Equal(1, _sources.Rounds);
        Assert.Equal(SourcesViewModel.NotSentText, page.ProbeText);
        Assert.False(page.IsProbing);
    }

    [Fact]
    public async Task Av_shows_in_the_graphs_corner_through_the_way_out()
    {
        var way = new ExitWay(_route, _exit, new InlineUi(), _sources);
        var told = 0;
        way.Changed += (_, _) => told++;

        await _sources.SetModeAsync(Chain.Bitcoin, SourceMode.Off);

        Assert.Contains(Chain.Bitcoin, way.ChainsOff);
        Assert.Equal(1, told);
        await _sources.SetModeAsync(Chain.Bitcoin, SourceMode.Fastest);
        Assert.Empty(way.ChainsOff);
    }

    [Fact]
    public void Closing_the_settings_folds_the_form_in()
    {
        var page = Create();
        var rig = new GateRig(VaultState.Unlocked);
        var settings = new SettingsViewModel(
            new AppSettings(), rig.SettingsStore, rig.Way, rig.ExitPanel, rig.AutoLock, rig.Prices, rig.Profiles,
            rig.Vault, rig.Cases, rig.Build, page);
        settings.Open(SettingsPage.Sources);
        page.ToggleAdd();

        Assert.True(settings.IsSourcesPage);
        settings.Close();

        Assert.False(page.IsAdding);
    }

    [Fact]
    public void The_chips_dot_is_red_when_nothing_can_be_used_and_grey_before_a_round()
    {
        var page = Create();
        Assert.Equal(EndpointDot.Untried, page.Chips[0].Dot);

        _sources.Probes[MempoolId] = Probe(MempoolId, ProbeOutcome.Failed, 182);
        _sources.Probes[LanId] = Probe(LanId, ProbeOutcome.Failed, 38);
        _sources.NoneUsable = true;
        page.ShowChip(page.Chips[0]);

        Assert.Equal(EndpointDot.Failed, page.Chips[0].Dot);
        Assert.DoesNotContain(page.Rows, row => row.IsInUse);
    }

    [Fact]
    public async Task The_mark_for_one_behind_shows_only_in_snabbast()
    {
        _sources.Probes[LanId] = Probe(LanId, ProbeOutcome.Behind, 38) with { BlocksBehind = 1 };
        var page = Create();
        Assert.True(page.Rows[1].HasNote);

        await page.ChooseModeAsync(page.ModeChoices[1]);

        Assert.False(page.Rows[1].HasNote);
        Assert.Equal(EndpointDot.Behind, page.Rows[1].Dot);
    }

    [Fact]
    public async Task Lokal_lets_an_address_on_a_private_network_be_added_and_tried_as_the_own_node()
    {
        var page = Create();
        page.ToggleAdd();
        page.NewAddress = "https://192.168.1.20/api";

        Assert.Equal(SourceAddress.LocalNeedsMarkText, page.AddressProblem);
        Assert.False(page.CanAdd);
        await page.AddAsync();
        Assert.Empty(_sources.Added);

        page.ToggleLocal();

        Assert.True(page.NewLocal);
        Assert.Empty(page.AddressProblem);
        Assert.True(page.CanAdd);
        _sources.TryAnswer = new EndpointProbe(Guid.Empty, ProbeOutcome.Answered, 921_404, "ab",
            TimeSpan.FromMilliseconds(4), DateTimeOffset.UnixEpoch, null);
        await page.TryAsync();
        Assert.Equal([true], _sources.TriedLocal);

        await page.AddAsync();

        Assert.True(Assert.Single(_sources.Added).Local);
        Assert.True(page.Rows[2].IsLocal);
        Assert.False(page.Rows[0].IsLocal);
    }

    [Fact]
    public void Lokal_refuses_a_global_address_and_is_off_again_in_a_new_form()
    {
        var page = Create();
        page.ToggleAdd();
        page.ToggleLocal();
        page.NewAddress = "https://8.8.8.8/";

        Assert.Equal(SourceAddress.LocalMustBeLocalText, page.AddressProblem);

        page.CancelAdd();
        page.ToggleAdd();

        Assert.False(page.NewLocal);
    }

    [Fact]
    public void An_endpoint_that_asked_to_wait_is_marked_and_one_block_behind_is_not()
    {
        _sources.Probes[MempoolId] = Probe(MempoolId, ProbeOutcome.Answered, 182) with { BlocksBehind = 1 };
        _sources.WaitUntil[LanId] = new DateTimeOffset(2026, 11, 7, 9, 12, 30, TimeSpan.Zero);

        var page = Create();

        Assert.False(page.Rows[0].HasNote);
        Assert.Equal(EndpointDot.Answered, page.Rows[0].Dot);
        Assert.Contains("ett block efter", page.Rows[0].DotTip, StringComparison.Ordinal);
        Assert.Equal(EndpointRowViewModel.WaitingText, page.Rows[1].Note);
        Assert.Contains("09:12:30", page.Rows[1].NoteTip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wait_for_a_source_is_told_while_it_lasts_and_goes_after()
    {
        var way = new ExitWay(_route, _exit, new InlineUi(), _sources);
        var told = 0;
        way.Changed += (_, _) => told++;

        _sources.WaitingUntil = DateTimeOffset.UnixEpoch;
        _sources.Tell();

        Assert.Contains(Chain.Bitcoin, way.ChainsWaiting);
        Assert.Equal("Väntar på källan · Bitcoin", way.WaitText);

        _sources.WaitingUntil = null;
        _sources.Tell();

        Assert.Empty(way.ChainsWaiting);
        Assert.Empty(way.WaitText);
        Assert.Equal(2, told);
    }

    private SourcesViewModel Create(Toaster? toaster = null) => new(
        _sources,
        new ExitWay(_route, _exit, new InlineUi()),
        _vault,
        _keys,
        new InlineUi(),
        TimeZoneInfo.Utc,
        toaster: toaster);

    private static EndpointProbe Probe(Guid id, ProbeOutcome outcome, int milliseconds) =>
        new(id, outcome, 921_404, null, TimeSpan.FromMilliseconds(milliseconds), DateTimeOffset.UnixEpoch, null);

    /// <summary>Bitcoin's sources in memory: two endpoints in Snabbast, probes the test sets.</summary>
    private sealed class TestChainSources : IChainSources
    {
        public event EventHandler<SourceEndpoint>? CertificateRefused
        {
            add { }
            remove { }
        }

        public Task<Chainsaw.Modules.Network.Contracts.NodeCertificate> ReadCertificateAsync(
            Chain chain, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task PinAsync(Chain chain, Guid id, string fingerprint, string by) => Task.CompletedTask;

        public Task UnpinAsync(Chain chain, Guid id, string by) => Task.CompletedTask;

        public List<SourceEndpoint> Endpoints { get; } =
        [
            new(MempoolId, Chain.Bitcoin, "mempool.space", new Uri("https://mempool.space/api/"), "esplora", null, true),
            new(LanId, Chain.Bitcoin, "esplora.lan", new Uri("https://esplora.lan/api/"), "esplora", null, true),
        ];

        public Dictionary<Guid, EndpointProbe> Probes { get; } = [];

        public Dictionary<Guid, DateTimeOffset> WaitUntil { get; } = [];

        public DateTimeOffset? WaitingUntil { get; set; }

        public void Tell() => Changed?.Invoke(this, Chain.Bitcoin);

        public DateTimeOffset? ProbedAt { get; set; }

        public bool NoneUsable { get; set; }

        public SourceMode Mode { get; private set; }

        public ProbeRun Run { get; set; } = ProbeRun.Probed;

        public Exception? TryFailure { get; set; }

        public int Rounds { get; private set; }

        public Exception? SaveFailure { get; set; }

        public EndpointProbe? TryAnswer { get; set; }

        public List<(Uri Address, string Api, string? Key, bool Local)> Added { get; } = [];

        public List<bool> TriedLocal { get; } = [];

        public IReadOnlyList<Chain> Chains { get; } = [Chain.Bitcoin];

        public event EventHandler<Chain>? Changed;

        public IReadOnlyList<SourceApiInfo> Apis(Chain chain) =>
            chain == Chain.Bitcoin ? [new SourceApiInfo("esplora", "Esplora", Chain.Bitcoin)] : [];

        public ChainSourcesView Get(Chain chain)
        {
            var first = Mode == SourceMode.Off || NoneUsable
                ? null
                : Endpoints.Where(e => e.Enabled && Probes.GetValueOrDefault(e.Id)?.Outcome != ProbeOutcome.Behind)
                    .Select(e => (Guid?)e.Id).FirstOrDefault();
            return new ChainSourcesView(chain, Mode,
                [.. Endpoints.Select(e => new EndpointView(e, Probes.GetValueOrDefault(e.Id), false, e.Id == first)
                {
                    WaitUntil = WaitUntil.TryGetValue(e.Id, out var until) ? until : null,
                })],
                ProbedAt)
            {
                WaitingUntil = WaitingUntil,
            };
        }

        public Task SetModeAsync(Chain chain, SourceMode mode) => Change(() => Mode = mode);

        public (SourceEndpoint Endpoint, Task Saved) Add(
            Chain chain, Uri address, string api, string? apiKey, bool local = false)
        {
            Added.Add((address, api, apiKey, local));
            var endpoint = new SourceEndpoint(Guid.NewGuid(), chain, address.Host, address, api, apiKey, true)
            {
                Local = local,
            };
            return (endpoint, Change(() => Endpoints.Add(endpoint)));
        }

        public Task RemoveAsync(Chain chain, Guid id) => Change(() => Endpoints.RemoveAll(e => e.Id == id));

        public Task SetEnabledAsync(Chain chain, Guid id, bool enabled) => Change(() =>
        {
            var index = Endpoints.FindIndex(e => e.Id == id);
            Endpoints[index] = Endpoints[index] with { Enabled = enabled };
        });

        public Task MoveAsync(Chain chain, Guid id, int steps) => Change(() =>
        {
            var index = Endpoints.FindIndex(e => e.Id == id);
            var endpoint = Endpoints[index];
            Endpoints.RemoveAt(index);
            Endpoints.Insert(Math.Clamp(index + steps, 0, Endpoints.Count), endpoint);
        });

        public Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default)
        {
            Rounds++;
            return Task.FromResult(Run);
        }

        public Task<EndpointProbe> TryAsync(
            Chain chain, Uri address, string api, string? apiKey, bool local = false,
            CancellationToken cancellationToken = default)
        {
            TriedLocal.Add(local);
            return TryFailure is null ? Task.FromResult(TryAnswer!) : Task.FromException<EndpointProbe>(TryFailure);
        }

        private Task Change(Action change)
        {
            change();
            Changed?.Invoke(this, Chain.Bitcoin);
            return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        }
    }

    /// <summary>The services of the keys in the vault.</summary>
    private sealed class TestApiKeys : IApiKeyStore
    {
        public List<string> Services { get; } = [];

        public IReadOnlyList<ApiKeyInfo> List() => [.. Services.Select(s => new ApiKeyInfo(s, string.Empty))];

        public ApiKey? Find(string service) => null;

        public void Save(ApiKey key) => throw new NotSupportedException();

        public bool Remove(string service) => throw new NotSupportedException();
    }
}
