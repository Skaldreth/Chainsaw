using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Settings;
using Chainsaw.Modules.Attribution.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's lookups at the suppliers of attribution (Chainabuse first): an icon over the chosen node asks the
/// suppliers that are on about its address, and what they report becomes labels in the case (the user's decision
/// 2026-11-08: a button for now, since a standard key gives 10 calls a month; automatic later).
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The icon's name over the node.</summary>
    public const string SuppliersToolText = "Fråga om rapporter";

    /// <summary>What the toast says when a supplier found nothing about the address.</summary>
    public const string NothingReportedText = "Inga rapporter om adressen.";

    /// <summary>What the toast says when the lookup failed before any supplier answered.</summary>
    public const string SuppliersFailedText = "Det gick inte att fråga om rapporter.";

    /// <summary>The suppliers, or <see langword="null"/> where none are wired (tests).</summary>
    private readonly IAttributionProviders? _suppliers;

    /// <summary>Which suppliers are on, or <see langword="null"/> where none are wired.</summary>
    private readonly AttributionSwitches? _switches;

    /// <summary>Whether a lookup is under way, so that a second click does not spend another call.</summary>
    private bool _asking;

    /// <summary>
    /// Gets whether the suppliers can be asked about a node: one is on, the node is one address on a chain one of them
    /// knows (a cluster is not asked about, so that the calls are not spent on every address in it), Chainsaw is
    /// online and no lookup is under way. The icon is not shown otherwise (principle 7).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when the icon is shown.</returns>
    private bool CanAskSuppliers(NodeView node)
    {
        if (_suppliers is null || _switches is null || _asking || IsOffline || node.Node.Addresses.Count != 1)
        {
            return false;
        }

        var chain = node.Node.Addresses[0].Chain;
        var enabled = _switches.Enabled;
        return _suppliers.Providers.Any(
            supplier => enabled.Contains(supplier.Name, StringComparer.OrdinalIgnoreCase)
                && supplier.Chains.Contains(chain));
    }

    /// <summary>
    /// Asks the suppliers that are on about the chosen node's address, then shows the new labels in the graph and says
    /// in a toast what came back, honestly when nothing was sent.
    /// </summary>
    /// <returns>A task that completes when the answer is shown.</returns>
    private async Task AskSuppliersAsync()
    {
        if (Scene?.Node(SelectedNode) is not { } node || _session.Current is not { } open
            || _suppliers is null || _switches is null || !CanAskSuppliers(node))
        {
            return;
        }

        _asking = true;
        ShowNodeTools();
        try
        {
            var answers = await _suppliers.LookupAsync(open, node.Node.Addresses[0], _switches.Enabled);
            if (_session.Current != open)
            {
                // The case was closed meanwhile: what came back is that case's, and is not said over another.
                return;
            }

            if (answers.Any(answer => answer.Added.Count > 0))
            {
                await RefreshAsync();
            }

            foreach (var answer in answers)
            {
                var (kind, text) = Said(answer);
                Tell(kind, text);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed meanwhile; the call is fire and forget, so the fault is said here or never.
            // A raw message is never shown, since it may name a path or a host.
            if (_session.Current == open)
            {
                Tell(ToastKind.Error, SuppliersFailedText);
            }
        }
        finally
        {
            _asking = false;
            ShowNodeTools();
        }
    }

    /// <summary>R, or the icon: asks the suppliers about the chosen node when they can be asked.</summary>
    public void AskSuppliers() => _ = AskSuppliersAsync();

    /// <summary>Gives what a toast says about one supplier's answer.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The toast's kind and text.</returns>
    private (ToastKind Kind, string Text) Said(ProviderLookup answer)
    {
        var name = _suppliers?.Providers.FirstOrDefault(supplier => supplier.Name == answer.Provider)?.DisplayName
            ?? answer.Provider;
        if (answer.Status != ProviderLookupStatus.Answered)
        {
            return (ToastKind.Warning, $"{name}: {answer.Problem}");
        }

        if (answer.Reports == 0)
        {
            return (ToastKind.Information, $"{name}: {NothingReportedText}");
        }

        var labels = string.Join(", ", answer.Added.Select(label => label.Name));
        var reports = answer.Reports.ToString(CultureInfo.InvariantCulture);
        return answer.Added.Count == 0
            ? (ToastKind.Information, $"{name}: {reports} rapporter, redan i ärendet.")
            : (ToastKind.Success, $"{name}: {reports} rapporter. Rapporterad som {labels}.");
    }
}
