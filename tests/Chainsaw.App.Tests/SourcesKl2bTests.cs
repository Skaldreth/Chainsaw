using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The page Källor after sketch KL2b: the chains in a middle column with their mode and how many are on, the endpoint
/// in use as a card, the reserves by latency or in the user's order, those switched off folded together, the drag by
/// the handle, and the suppliers of attribution under Etiketter with their switch and key.
/// </summary>
public sealed class SourcesKl2bTests
{
    private static readonly Guid MempoolId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid BlockstreamId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid LanId = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid OffId = Guid.Parse("00000000-0000-0000-0000-0000000000a4");

    private readonly ThreeChainSources _sources = new();

    private readonly TestVault _vault = new(VaultState.Unlocked);

    private readonly KeyStore _keys = new();

    private readonly AttributionSwitches _switches = new();

    [Fact]
    public void A_chains_row_says_its_mode_and_how_many_are_on_and_one_not_built_says_so()
    {
        var page = Create();

        Assert.Equal("Snabbast · 3 av 4", page.Chips[0].Sub);
        Assert.Equal(SettingsViewModel.NotBuiltText, page.Chips[3].Sub);
    }

    [Fact]
    public void Snabbast_shows_the_one_used_as_the_card_and_the_reserves_by_latency()
    {
        _sources.Probes[MempoolId] = Probe(MempoolId, ProbeOutcome.Answered, 146);
        _sources.Probes[BlockstreamId] = Probe(BlockstreamId, ProbeOutcome.Answered, 539);
        _sources.Probes[LanId] = Probe(LanId, ProbeOutcome.Behind, 12) with { BlocksBehind = 3 };

        var page = Create();

        Assert.Equal(MempoolId, page.InUseRow!.Id);
        Assert.Equal([LanId, BlockstreamId], page.Reserves.Select(r => r.Id));
        Assert.Equal(("RESERVER", "efter svarstid"), (page.ReservesTitle, page.ReservesHint));
        Assert.Equal(EndpointNote.Behind, page.Reserves[0].NoteKind);
        Assert.Equal("3 block efter", page.Reserves[0].Note);
        Assert.Equal([OffId], page.Disabled.Select(r => r.Id));
        Assert.Equal("Avstängda · 1", page.DisabledTitle);
        Assert.True(page.Disabled[0].HasKey);
        Assert.False(page.IsDisabledOpen);

        page.ToggleDisabled();

        Assert.True(page.IsDisabledOpen);
    }

    [Fact]
    public async Task Prioriterad_keeps_the_users_order_numbers_those_on_and_drags_one_to_be_used()
    {
        var page = Create();
        await page.ChooseModeAsync(page.ModeChoices[1]);

        Assert.Equal("Prioriterad", page.ModeName);
        Assert.Equal(("RESERVER", "i din ordning, dra i greppet"), (page.ReservesTitle, page.ReservesHint));
        Assert.Equal(["1", "2", "3"], new[] { page.InUseRow! }.Concat(page.Reserves).Select(r => r.Position));
        Assert.Equal(string.Empty, page.Disabled[0].Position);

        var lan = page.Reserves.Single(r => r.Id == LanId);
        await page.MoveToAsync(lan, page.InUseRow!);

        Assert.Equal([LanId, MempoolId, BlockstreamId, OffId], _sources.Endpoints.Select(e => e.Id));
        Assert.Same(lan, page.InUseRow);
    }

    [Fact]
    public async Task Alt_arrows_move_past_those_switched_off()
    {
        _sources.Endpoints.Insert(1, _sources.Endpoints[3]);
        _sources.Endpoints.RemoveAt(4);
        var page = Create();
        await page.ChooseModeAsync(page.ModeChoices[1]);
        var blockstream = page.Rows.Single(r => r.Id == BlockstreamId);

        await page.MoveUpAsync(blockstream);

        Assert.Equal([BlockstreamId, MempoolId, OffId, LanId], _sources.Endpoints.Select(e => e.Id));
    }

    [Fact]
    public async Task Av_leaves_no_card_and_says_the_endpoints_are_not_used()
    {
        var page = Create();
        await page.ChooseModeAsync(page.ModeChoices[2]);

        Assert.Null(page.InUseRow);
        Assert.Equal(("ENDPOINTS", "används inte medan läget är Av"), (page.ReservesTitle, page.ReservesHint));
        Assert.Equal("Av · 3 av 4", page.Chips[0].Sub);
    }

    [Fact]
    public void A_failing_reserve_says_so_in_red_and_the_hash_is_shown_only_when_measured()
    {
        _sources.Probes[MempoolId] =
            Probe(MempoolId, ProbeOutcome.Answered, 146) with { BlockHash = new string('0', 60) + "3f2a" };
        _sources.Probes[LanId] =
            new EndpointProbe(LanId, ProbeOutcome.Failed, null, null, null, DateTimeOffset.UnixEpoch, "503");

        var page = Create();

        Assert.Equal("Höjd 921 404 · 00000000…3f2a", page.InUseRow!.AnswerText);
        var lan = page.Reserves.Single(r => r.Id == LanId);
        Assert.Equal(EndpointNote.Failed, lan.NoteKind);
        Assert.Equal(EndpointRowViewModel.FailedText, lan.Note);
        Assert.Equal("Svarade inte rätt: 503", lan.AnswerText);
        Assert.Equal("–", lan.MillisecondsNumber);
    }

    [Fact]
    public void Chainabuse_stands_under_etiketter_off_by_default_and_shows_instead_of_a_chain()
    {
        var page = Create();

        var supplier = Assert.Single(page.Suppliers);
        Assert.True(page.HasSuppliers);
        Assert.Equal("Chainabuse", supplier.Name);
        Assert.False(supplier.IsOn);
        Assert.Equal(AttributionSupplierViewModel.OffText, supplier.Sub);
        Assert.Contains("TRM Labs", supplier.About, StringComparison.Ordinal);
        Assert.Contains("10 anrop i månaden", supplier.Note, StringComparison.Ordinal);

        page.ShowSupplier(supplier);

        Assert.True(page.IsSupplierShown);
        Assert.False(page.IsChainShown);
        Assert.True(supplier.IsSelected);
        Assert.DoesNotContain(page.Chips, chip => chip.IsSelected);

        page.ShowChip(page.Chips[0]);

        Assert.False(supplier.IsSelected);
        Assert.True(page.IsChainShown);
    }

    [Fact]
    public void The_switch_is_kept_in_the_settings_and_read_by_whoever_asks_the_suppliers()
    {
        var page = Create();
        var rig = new GateRig(VaultState.Unlocked);
        var settings = new SettingsViewModel(
            new AppSettings(), rig.SettingsStore, rig.Way, rig.ExitPanel, rig.AutoLock, rig.Prices, rig.Profiles,
            rig.Vault, rig.Cases, rig.Build, page, _switches);
        var supplier = page.Suppliers[0];

        supplier.Toggle();

        Assert.True(supplier.IsOn);
        Assert.Equal(["chainabuse"], _switches.Enabled);
        Assert.Equal(["chainabuse"], settings.Current.AttributionProviders);
        Assert.Equal("På · ingen nyckel", supplier.Sub);
        Assert.Equal(EndpointDot.Behind, supplier.Dot);

        supplier.Toggle();

        Assert.Empty(settings.Current.AttributionProviders);
    }

    [Fact]
    public async Task The_key_is_saved_to_the_vault_from_the_masked_field_and_never_shown_again()
    {
        var page = Create();
        var supplier = page.Suppliers[0];
        _switches.Set("chainabuse", true);
        var entry = new TestEntry("  secret-key  ");

        await supplier.SaveKeyAsync(entry);

        Assert.Equal(new ApiKey("Chainabuse", string.Empty, "secret-key"), _keys.Saved.Single());
        Assert.True(entry.AllCleared);
        Assert.True(supplier.HasKey);
        Assert.Equal(AttributionSupplierViewModel.OnText, supplier.Sub);
        Assert.Equal(EndpointDot.Answered, supplier.Dot);

        await supplier.RemoveKeyAsync();

        Assert.False(supplier.HasKey);
        Assert.Empty(_keys.Saved);
    }

    [Fact]
    public async Task An_empty_key_or_a_locked_vault_is_said()
    {
        var page = Create();
        var supplier = page.Suppliers[0];

        await supplier.SaveKeyAsync(new TestEntry("   "));
        Assert.Equal(AttributionSupplierViewModel.KeyEmptyText, supplier.KeyError);

        _keys.Locked = true;
        await supplier.SaveKeyAsync(new TestEntry("k"));
        Assert.Equal(AttributionSupplierViewModel.KeyFailedText, supplier.KeyError);
        Assert.False(supplier.HasKey);
    }

    [Fact]
    public void The_settings_file_keeps_the_suppliers_that_are_on()
    {
        var store = new AppSettingsStore(
            Path.Combine(Path.GetTempPath(), "chainsaw-suppliers-" + Guid.NewGuid().ToString("N")));
        try
        {
            store.Save(new AppSettings { AttributionProviders = ["chainabuse"] });

            Assert.Equal(["chainabuse"], store.Load().AttributionProviders);
            Assert.Empty(new AppSettings().AttributionProviders);
            Assert.True(new AttributionSwitches(store.Load().AttributionProviders).IsOn("Chainabuse"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(store.FilePath)!, recursive: true);
        }
    }

    [Fact]
    public void A_row_that_sends_a_key_says_whether_it_is_missing_saved_or_refused()
    {
        _keys.Saved.Add(new ApiKey("bc", string.Empty, "blockchair-secret-1234"));
        var page = Create();
        var row = Row(page, OffId);
        var plain = Row(page, MempoolId);

        Assert.Equal((EndpointKey.Saved, "••••1234", false), (row.Key, row.KeyTail, row.IsKeyFieldOpen));
        Assert.Equal(EndpointKey.None, plain.Key);

        row.ShowKey("bc", "blockchair-secret-1234", "Invalid API Key (#err2)");
        Assert.Equal(EndpointKey.Refused, row.Key);
        Assert.Contains("Invalid API Key (#err2)", row.KeyText, StringComparison.Ordinal);
        Assert.True(row.IsKeyFieldOpen);

        row.ShowKey("Etherscan", null, null);
        Assert.Equal(EndpointKey.Missing, row.Key);
        Assert.Contains("etherscan.io", row.KeyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_written_on_the_row_is_saved_under_its_service_and_switches_its_endpoints_on()
    {
        var page = Create();
        var row = Row(page, OffId);
        Assert.Equal(EndpointKey.Missing, row.Key);

        await page.SaveKeyAsync(row, new TestEntry("  S3cretKey  "));

        Assert.Equal(("bc", "S3cretKey"), (_keys.Saved[0].Service, _keys.Saved[0].Secret));
        Assert.Contains("bc", _sources.EnabledForKeys);
        Assert.Equal(EndpointKey.Saved, Row(page, OffId).Key);
    }

    [Fact]
    public async Task An_address_pasted_as_a_key_or_a_locked_vault_saves_nothing_and_says_why()
    {
        var page = Create();
        var row = Row(page, OffId);

        await page.SaveKeyAsync(row, new TestEntry("0xeb931257765e313079f2db87a966d80ca5cf8ac6"));
        Assert.Equal(ApiKeyText.AddressText, row.KeyError);
        Assert.False(ApiKeyText.IsAddress("9BG68EH6QDVNRVWUNSVUGJNIA38TMUFR6X"));

        _keys.Locked = true;
        await page.SaveKeyAsync(row, new TestEntry("abc"));
        Assert.Equal(ApiKeyText.FailedText, row.KeyError);
        Assert.Empty(_keys.Saved);
    }

    [Fact]
    public async Task A_key_removed_on_the_row_leaves_the_vault_and_the_row_asks_for_one()
    {
        _keys.Saved.Add(new ApiKey("bc", string.Empty, "S3cretKey"));
        var page = Create();

        await page.RemoveKeyAsync(Row(page, OffId));

        Assert.Empty(_keys.Saved);
        Assert.Equal(EndpointKey.Missing, Row(page, OffId).Key);
    }

    private static EndpointRowViewModel Row(SourcesViewModel page, Guid id) => page.Rows.Single(row => row.Id == id);

    private SourcesViewModel Create() =>
        new(_sources, new ExitWay(new TestExitRoute(), new TestExitConnection(), new InlineUi()), _vault, _keys,
            new InlineUi(), TimeZoneInfo.Utc, new Suppliers(), _switches);

    private static EndpointProbe Probe(Guid id, ProbeOutcome outcome, int milliseconds) =>
        new(id, outcome, 921_404, null, TimeSpan.FromMilliseconds(milliseconds), DateTimeOffset.UnixEpoch, null);

    /// <summary>Chainabuse, as the module describes it; nothing is asked.</summary>
    private sealed class Suppliers : IAttributionProviders
    {
        public IReadOnlyList<AttributionProviderInfo> Providers { get; } =
            [new("chainabuse", "Chainabuse", "Chainabuse", [Chain.Bitcoin])];

        public Task<IReadOnlyList<ProviderLookup>> LookupAsync(
            ICase @case, ChainAddress address, IReadOnlyCollection<string> enabled,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The vault's API keys in memory, which can be locked.</summary>
    private sealed class KeyStore : IApiKeyStore
    {
        public List<ApiKey> Saved { get; } = [];

        public bool Locked { get; set; }

        public IReadOnlyList<ApiKeyInfo> List()
        {
            ThrowIfLocked();
            return [.. Saved.Select(key => new ApiKeyInfo(key.Service, key.Identifier))];
        }

        public ApiKey? Find(string service) => Saved.FirstOrDefault(key => key.Service == service);

        public void Save(ApiKey key)
        {
            ThrowIfLocked();
            Saved.RemoveAll(each => each.Service == key.Service);
            Saved.Add(key);
        }

        public bool Remove(string service)
        {
            ThrowIfLocked();
            return Saved.RemoveAll(key => key.Service == service) > 0;
        }

        private void ThrowIfLocked()
        {
            if (Locked)
            {
                throw new VaultException(VaultError.Locked);
            }
        }
    }

    /// <summary>Bitcoin with four endpoints, the last switched off and with a key; probes the test sets.</summary>
    private sealed class ThreeChainSources : IChainSources
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
            new(BlockstreamId, Chain.Bitcoin, "Blockstream", new Uri("https://blockstream.info/api/"), "esplora", null,
                true),
            new(LanId, Chain.Bitcoin, "Egen nod", new Uri("https://esplora.lan/api/"), "esplora", null, true)
            {
                Local = true,
            },
            new(OffId, Chain.Bitcoin, "Blockchair", new Uri("https://api.blockchair.com/bitcoin/"), "esplora", "bc",
                false),
        ];

        public Dictionary<Guid, EndpointProbe> Probes { get; } = [];

        public SourceMode Mode { get; private set; }

        public IReadOnlyList<Chain> Chains { get; } = [Chain.Bitcoin];

        public event EventHandler<Chain>? Changed;

        public IReadOnlyList<SourceApiInfo> Apis(Chain chain) => [new SourceApiInfo("esplora", "Esplora", chain)];

        public ChainSourcesView Get(Chain chain)
        {
            var first = Mode == SourceMode.Off
                ? null
                : Endpoints.Where(e => e.Enabled && Probes.GetValueOrDefault(e.Id)?.Outcome is not
                        (ProbeOutcome.Behind or ProbeOutcome.Failed))
                    .Select(e => (Guid?)e.Id).FirstOrDefault();
            return new ChainSourcesView(chain, Mode,
                [.. Endpoints.Select(e => new EndpointView(e, Probes.GetValueOrDefault(e.Id), false, e.Id == first))],
                null);
        }

        public Task SetModeAsync(Chain chain, SourceMode mode) => Change(() => Mode = mode);

        public (SourceEndpoint Endpoint, Task Saved) Add(
            Chain chain, Uri address, string api, string? apiKey, bool local = false) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Chain chain, Guid id) => Change(() => Endpoints.RemoveAll(e => e.Id == id));

        public Task SetEnabledAsync(Chain chain, Guid id, bool enabled) => Change(() =>
        {
            var index = Endpoints.FindIndex(e => e.Id == id);
            Endpoints[index] = Endpoints[index] with { Enabled = enabled };
        });

        public List<string> EnabledForKeys { get; } = [];

        public Task EnableForKeyAsync(string service)
        {
            EnabledForKeys.Add(service);
            return Task.CompletedTask;
        }

        public Task MoveAsync(Chain chain, Guid id, int steps) => Change(() =>
        {
            var index = Endpoints.FindIndex(e => e.Id == id);
            var endpoint = Endpoints[index];
            Endpoints.RemoveAt(index);
            Endpoints.Insert(Math.Clamp(index + steps, 0, Endpoints.Count), endpoint);
        });

        public Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProbeRun.Probed);

        public Task<EndpointProbe> TryAsync(
            Chain chain, Uri address, string api, string? apiKey, bool local = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private Task Change(Action change)
        {
            change();
            Changed?.Invoke(this, Chain.Bitcoin);
            return Task.CompletedTask;
        }
    }
}
