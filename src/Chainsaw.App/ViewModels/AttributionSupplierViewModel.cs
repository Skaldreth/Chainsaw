using Chainsaw.App.Settings;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A supplier of attribution on the page Källor, under the heading <em>Etiketter</em> (the user's decision 2026-11-08):
/// in the middle column a row like a chain's, with its dot, its name and whether it is on; to the right its name, what
/// it reports, its switch (off by default), its API key in the vault and an honest note on how its lookups are made.
/// The key is written in a masked field, saved to the vault and never shown again in clear. There is no lookup here.
/// It knows nothing of WPF.
/// </summary>
public sealed partial class AttributionSupplierViewModel : ObservableObject
{
    /// <summary>What the row says under the name when the supplier is on.</summary>
    public const string OnText = "På";

    /// <summary>What the row says under the name when the supplier is off.</summary>
    public const string OffText = "Av";

    /// <summary>What the row adds when the supplier is on but has no key, so that nothing can be asked.</summary>
    public const string NoKeyText = "ingen nyckel";

    /// <summary>What the page says when the key could not be saved or removed, such as when the vault locked.</summary>
    public const string KeyFailedText = "Nyckeln gick inte att spara eller ta bort. Lås upp och försök igen.";

    /// <summary>What the page says when the key field was empty.</summary>
    public const string KeyEmptyText = "Skriv nyckeln först.";

    /// <summary>
    /// What each supplier Chainsaw knows reports, and how its lookups are made, by its name in the settings; a supplier
    /// not in the table shows neither.
    /// </summary>
    private static readonly Dictionary<string, (string About, string Note)> Texts =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["chainabuse"] = (
            "Rapporter om bedrägerier, från allmänheten och partner (TRM Labs)",
            "En vanlig nyckel ger 10 anrop i månaden; uppslag görs när du ber om det."),
    };

    /// <summary>The supplier as the module describes it.</summary>
    private readonly AttributionProviderInfo _info;

    /// <summary>The switches, written to the settings' file.</summary>
    private readonly AttributionSwitches _switches;

    /// <summary>The API keys in the vault.</summary>
    private readonly IApiKeyStore _keys;

    /// <summary>Creates the supplier's row and page.</summary>
    /// <param name="info">The supplier.</param>
    /// <param name="switches">The switches of the suppliers.</param>
    /// <param name="keys">The API keys in the vault.</param>
    public AttributionSupplierViewModel(
        AttributionProviderInfo info, AttributionSwitches switches, IApiKeyStore keys)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(switches);
        ArgumentNullException.ThrowIfNull(keys);
        _info = info;
        _switches = switches;
        _keys = keys;
        (About, Note) = Texts.GetValueOrDefault(info.Name, (string.Empty, string.Empty));
        switches.Changed += (_, _) => ShowState();
        ShowState();
    }

    /// <summary>Gets the supplier's stable name, such as <c>chainabuse</c>.</summary>
    public string Code => _info.Name;

    /// <summary>Gets the name the user reads, such as <c>Chainabuse</c>.</summary>
    public string Name => _info.DisplayName;

    /// <summary>Gets what it reports, in a short line.</summary>
    public string About { get; }

    /// <summary>Gets whether there is a line on what it reports.</summary>
    public bool HasAbout => About.Length > 0;

    /// <summary>Gets the honest note on how its lookups are made.</summary>
    public string Note { get; }

    /// <summary>Gets whether there is a note.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Gets what its switch does, on hovering.</summary>
    public string SwitchTip => IsOn ? "Stäng av" : "Slå på";

    /// <summary>Gets whether the page shows it.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Gets whether it is on, so that Chainsaw asks it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchTip))]
    public partial bool IsOn { get; private set; }

    /// <summary>Gets whether a key for it is stored in the vault; not while the vault is locked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoKey))]
    public partial bool HasKey { get; private set; }

    /// <summary>Gets whether the key field is shown, since no key is stored.</summary>
    public bool HasNoKey => !HasKey;

    /// <summary>Gets what the row says under the name: <c>På</c>, <c>På · ingen nyckel</c> or <c>Av</c>.</summary>
    [ObservableProperty]
    public partial string Sub { get; private set; } = string.Empty;

    /// <summary>Gets the row's dot: green when on with a key, yellow when on without one, off otherwise.</summary>
    [ObservableProperty]
    public partial EndpointDot Dot { get; private set; } = EndpointDot.Off;

    /// <summary>Gets what went wrong with the key last, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyError))]
    public partial string KeyError { get; private set; } = string.Empty;

    /// <summary>Gets whether there is something wrong with the key to show.</summary>
    public bool HasKeyError => KeyError.Length > 0;

    /// <summary>Switches the supplier on or off; the settings write it to their file.</summary>
    [RelayCommand]
    public void Toggle() => _switches.Set(Code, !IsOn);

    /// <summary>
    /// Saves the key written in the masked field to the vault under the supplier's service, off the UI thread, and
    /// empties the field. The key is never shown again; only that one is stored.
    /// </summary>
    /// <param name="entry">The masked field.</param>
    /// <returns>A task that completes when the vault has answered.</returns>
    public async Task SaveKeyAsync(ISecretEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var written = entry.Take();
        try
        {
            var secret = new string(written).Trim();
            if (secret.Length == 0)
            {
                KeyError = KeyEmptyText;
                return;
            }

            await Task.Run(() => _keys.Save(new ApiKey(_info.KeyService, string.Empty, secret))).ConfigureAwait(true);
            KeyError = string.Empty;
        }
        catch (Exception exception) when (exception is VaultException or IOException or ArgumentException)
        {
            KeyError = KeyFailedText;
        }
        finally
        {
            Secret.Clear(written);
            ShowState();
        }
    }

    /// <summary>Removes the key from the vault (<em>Ta bort</em>), off the UI thread.</summary>
    /// <returns>A task that completes when the vault has answered.</returns>
    [RelayCommand]
    public async Task RemoveKeyAsync()
    {
        try
        {
            await Task.Run(() => _keys.Remove(_info.KeyService)).ConfigureAwait(true);
            KeyError = string.Empty;
        }
        catch (Exception exception) when (exception is VaultException or IOException)
        {
            KeyError = KeyFailedText;
        }
        finally
        {
            ShowState();
        }
    }

    /// <summary>Reads again whether it is on and has a key, and shows its row.</summary>
    public void ShowState()
    {
        IsOn = _switches.IsOn(Code);
        HasKey = StoresKey();
        Sub = SubText(IsOn, HasKey);
        Dot = DotFor(IsOn, HasKey);
    }

    /// <summary>Says whether it is on, and when on without a key, that too.</summary>
    /// <param name="on">Whether it is on.</param>
    /// <param name="hasKey">Whether a key is stored.</param>
    /// <returns>The words.</returns>
    private static string SubText(bool on, bool hasKey)
    {
        if (!on)
        {
            return OffText;
        }

        return hasKey ? OnText : $"{OnText} · {NoKeyText}";
    }

    /// <summary>Gives the row's dot.</summary>
    /// <param name="on">Whether it is on.</param>
    /// <param name="hasKey">Whether a key is stored.</param>
    /// <returns>The dot.</returns>
    private static EndpointDot DotFor(bool on, bool hasKey)
    {
        if (!on)
        {
            return EndpointDot.Off;
        }

        return hasKey ? EndpointDot.Answered : EndpointDot.Behind;
    }

    /// <summary>Tells whether a key is stored under the supplier's service, without reading the key itself.</summary>
    /// <returns><see langword="true"/> when one is stored; never while the vault is locked.</returns>
    private bool StoresKey()
    {
        try
        {
            return _keys.List().Any(
                key => string.Equals(key.Service, _info.KeyService, StringComparison.OrdinalIgnoreCase));
        }
        catch (VaultException)
        {
            return false;
        }
    }
}
