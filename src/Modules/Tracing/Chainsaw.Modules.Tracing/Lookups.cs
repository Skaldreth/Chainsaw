using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>What the module says about a lookup, written once for every chain's source.</summary>
internal static class Lookups
{
    /// <summary>Says what went wrong with a lookup, for the user.</summary>
    /// <param name="status">How it was answered.</param>
    /// <param name="problem">The source's own text, if any.</param>
    /// <returns>The text.</returns>
    public static string Problem(LookupStatus status, string? problem) => problem ?? status switch
    {
        LookupStatus.Offline => "Offline: ingen utgång är ansluten och ärendet har inte uppgiften.",
        LookupStatus.NotInCase => "Ärendet har inte uppgiften; expandera för att hämta den.",
        LookupStatus.NotFound => "Källan känner inte till den.",
        _ => "Uppslaget misslyckades.",
    };

    /// <summary>
    /// Gives why an answer may be outdated, when it came from the case instead of the source; such an answer leaves
    /// the expansion incomplete.
    /// </summary>
    /// <typeparam name="T">What was looked up.</typeparam>
    /// <param name="lookup">The answer.</param>
    /// <returns>The text, or <see langword="null"/> when the answer is current.</returns>
    public static string? Outdated<T>(BitcoinLookup<T> lookup)
        where T : class => lookup.Status == LookupStatus.FromCaseMayBeOutdated
        ? lookup.Problem ?? "Svaret kommer ur ärendet och kan ha ändrats."
        : null;

    /// <summary>Joins two texts for the user, either of which may be missing, each once.</summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>Both, one, or <see langword="null"/>.</returns>
    public static string? Join(string? first, string? second)
    {
        if (first is null || second is null || first.Contains(second, StringComparison.Ordinal))
        {
            return first ?? second;
        }

        return $"{first} {second}";
    }
}
