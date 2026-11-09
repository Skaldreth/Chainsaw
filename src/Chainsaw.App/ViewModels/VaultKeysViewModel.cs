using System.Collections.ObjectModel;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The page <em>Nycklar</em> under the chains on Källor (sketch NY1a): a row per API key in the vault, with who uses it
/// and how it is sent, and <em>Lägg till nyckel …</em>. A key is never shown again in clear; only its last four
/// characters, so that two keys can be told apart.
/// </summary>
public sealed partial class VaultKeysViewModel : ObservableObject
{
    /// <summary>What the page says when the vault cannot be read or written.</summary>
    public const string FailedText = "Valvet gick inte att läsa eller skriva. Lås upp och försök igen.";

    /// <summary>What the form says without a service or a key.</summary>
    public const string MissingText = "Skriv tjänsten och nyckeln.";

    /// <summary>What a row says when nothing uses its key.</summary>
    public const string UnusedText = "Används inte av någon endpoint";

    /// <summary>How many characters of a key are shown, at its end.</summary>
    private const int TailLength = 4;

    /// <summary>The vault's keys.</summary>
    private readonly IApiKeyStore _keys;

    /// <summary>The chains' endpoints, to say which use a key.</summary>
    private readonly IChainSources _sources;

    /// <summary>The suppliers of attribution, to say which use a key.</summary>
    private readonly IReadOnlyList<AttributionProviderInfo> _suppliers;

    /// <summary>Creates the page.</summary>
    /// <param name="keys">The vault's keys.</param>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="suppliers">The suppliers of attribution; none in some tests.</param>
    public VaultKeysViewModel(
        IApiKeyStore keys, IChainSources sources, IReadOnlyList<AttributionProviderInfo>? suppliers = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(sources);
        _keys = keys;
        _sources = sources;
        _suppliers = suppliers ?? [];
    }

    /// <summary>Gets the keys, in the order of their services.</summary>
    public ObservableCollection<VaultKeyRow> Rows { get; } = [];

    /// <summary>Gets or sets whether the form <em>Lägg till nyckel …</em> is folded out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormClosed), nameof(HasListError))]
    public partial bool IsFormOpen { get; set; }

    /// <summary>Gets whether the form is folded in, so that <em>Lägg till nyckel …</em> shows.</summary>
    public bool IsFormClosed => !IsFormOpen;

    /// <summary>Gets whether a problem is said above the list rather than in the form.</summary>
    public bool HasListError => HasError && !IsFormOpen;

    /// <summary>Gets or sets the service the new key is saved under, such as <c>Etherscan</c>.</summary>
    [ObservableProperty]
    public partial string NewService { get; set; } = string.Empty;

    /// <summary>Gets what went wrong, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasListError))]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>Gets whether there is something wrong to say.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>Gets the line under the heading, such as <c>2 i valvet</c>.</summary>
    public string Sub => Rows.Count + " i valvet";

    /// <summary>Gets or sets whether the row in the middle column is chosen.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Reads the keys again, off the UI thread.</summary>
    /// <returns>A task that completes when the rows are shown.</returns>
    public async Task ShowAsync()
    {
        try
        {
            var rows = await Task.Run(ReadRows).ConfigureAwait(true);
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
            }

            Error = string.Empty;
        }
        catch (VaultException)
        {
            Rows.Clear();
            Error = FailedText;
        }

        OnPropertyChanged(nameof(Sub));
    }

    /// <summary>Folds out the form with Etherscan's service filled in when no key has it yet.</summary>
    [RelayCommand]
    public void OpenForm()
    {
        NewService = Rows.Any(row => row.Service == "Etherscan") ? string.Empty : "Etherscan";
        Error = string.Empty;
        IsFormOpen = true;
    }

    /// <summary>Folds the form in without saving.</summary>
    [RelayCommand]
    public void CloseForm()
    {
        IsFormOpen = false;
        Error = string.Empty;
    }

    /// <summary>Saves the written key in the vault under the service, replacing a key it had, off the UI thread.</summary>
    /// <param name="entry">The masked field, which is taken and cleared.</param>
    /// <returns>A task that completes when the vault has answered.</returns>
    public async Task SaveAsync(ISecretEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var written = entry.Take();
        try
        {
            var service = NewService.Trim();
            var secret = new string(written).Trim();
            if (service.Length == 0 || secret.Length == 0)
            {
                Error = MissingText;
                return;
            }

            await Task.Run(() => _keys.Save(new ApiKey(service, string.Empty, secret))).ConfigureAwait(true);
            IsFormOpen = false;
            await ShowAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is VaultException or IOException or ArgumentException)
        {
            Error = FailedText;
        }
        finally
        {
            Secret.Clear(written);
        }
    }

    /// <summary>Opens the form to replace a key, with its service filled in.</summary>
    /// <param name="row">The key.</param>
    [RelayCommand]
    public void Change(VaultKeyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        NewService = row.Service;
        Error = string.Empty;
        IsFormOpen = true;
    }

    /// <summary>Removes a key from the vault, off the UI thread; endpoints that used it stop asking.</summary>
    /// <param name="row">The key.</param>
    /// <returns>A task that completes when the vault has answered.</returns>
    [RelayCommand]
    public async Task RemoveAsync(VaultKeyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        try
        {
            await Task.Run(() => _keys.Remove(row.Service)).ConfigureAwait(true);
            await ShowAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is VaultException or IOException)
        {
            Error = FailedText;
        }
    }

    /// <summary>Reads each key with who uses it, how it is sent and its last characters.</summary>
    /// <returns>The rows, by service.</returns>
    private List<VaultKeyRow> ReadRows()
    {
        var endpoints = _sources.Chains
            .SelectMany(chain => _sources.Get(chain).Endpoints.Select(view => view.Endpoint))
            .Where(endpoint => endpoint.ApiKey is not null)
            .ToList();
        var rows = new List<VaultKeyRow>();
        foreach (var info in _keys.List().OrderBy(info => info.Service, StringComparer.OrdinalIgnoreCase))
        {
            var secret = _keys.Find(info.Service)?.Secret ?? string.Empty;
            var users = endpoints.Where(endpoint => SameService(endpoint.ApiKey, info.Service)).ToList();
            var suppliers = _suppliers.Where(supplier => SameService(supplier.KeyService, info.Service)).ToList();
            rows.Add(new VaultKeyRow(
                info.Service, UsedBy(users, suppliers), Way(users, suppliers), Tail(secret))
            {
                IsUsed = users.Count + suppliers.Count > 0,
            });
        }

        return rows;
    }

    /// <summary>Says who uses a key: the chains and how many endpoints, and the suppliers by name.</summary>
    /// <param name="users">The endpoints.</param>
    /// <param name="suppliers">The suppliers.</param>
    /// <returns>For example <c>Ethereum · 1 endpoint</c>.</returns>
    private static string UsedBy(IReadOnlyList<SourceEndpoint> users, IReadOnlyList<AttributionProviderInfo> suppliers)
    {
        var parts = users.GroupBy(endpoint => endpoint.Chain)
            .Select(group => group.Key + " · " + group.Count() + (group.Count() == 1 ? " endpoint" : " endpoints"))
            .Concat(suppliers.Select(supplier => "Etiketter · " + supplier.DisplayName))
            .ToList();
        return parts.Count == 0 ? UnusedText : string.Join(", ", parts);
    }

    /// <summary>Says how the key is sent by the first that uses it.</summary>
    /// <param name="users">The endpoints.</param>
    /// <param name="suppliers">The suppliers.</param>
    /// <returns><c>I adressen</c>, <c>Inloggning</c>, <c>I ett huvud</c>, or empty when nothing uses it.</returns>
    private string Way(IReadOnlyList<SourceEndpoint> users, IReadOnlyList<AttributionProviderInfo> suppliers)
    {
        if (users.Count == 0)
        {
            return suppliers.Count == 0 ? string.Empty : "I ett huvud";
        }

        var first = users[0];
        var sort = _sources.Apis(first.Chain).FirstOrDefault(api => api.Code == first.Api);
        if (sort is { KeyInAddress: true })
        {
            return "I adressen";
        }

        return sort is { LocalOnly: true } ? "Inloggning" : "I ett huvud";
    }

    /// <summary>Tells whether a key's service is the one named, whatever its case.</summary>
    /// <param name="named">The service named by an endpoint or supplier.</param>
    /// <param name="service">The key's service.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool SameService(string? named, string service) =>
        string.Equals(named?.Trim(), service, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gives a key's last characters behind dots, or only dots for a short key.</summary>
    /// <param name="secret">The key.</param>
    /// <returns>For example <c>••••9k2D</c>.</returns>
    internal static string Tail(string secret) =>
        secret.Length <= TailLength * 2 ? "••••" : "••••" + secret[^TailLength..];
}

/// <summary>A key in the vault on the page <em>Nycklar</em>.</summary>
/// <param name="Service">The service it is stored under.</param>
/// <param name="UsedBy">Who uses it, such as <c>Ethereum · 1 endpoint</c>.</param>
/// <param name="Way">How it is sent, or empty when nothing uses it.</param>
/// <param name="Tail">Its last characters behind dots.</param>
public sealed record VaultKeyRow(string Service, string UsedBy, string Way, string Tail)
{
    /// <summary>Gets whether something uses the key.</summary>
    public bool IsUsed { get; init; }

    /// <summary>Gets whether to show how it is sent.</summary>
    public bool HasWay => Way.Length > 0;
}
