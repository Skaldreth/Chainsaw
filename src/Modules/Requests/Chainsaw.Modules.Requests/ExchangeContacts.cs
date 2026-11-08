using Chainsaw.Modules.Requests.Contracts;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// The verified ways for law enforcement to reach exchanges and issuers. A way is added only with its source (the
/// recipient's own page) and the day it was checked, and never from memory: a wrong address in a request is worse
/// than none, so the catalogue starts empty and the view says so honestly (CLAUDE.md, BG1b).
/// </summary>
/// <param name="known">The ways; the program's catalogue when built by the container.</param>
internal sealed class ExchangeContacts(IReadOnlyList<ExchangeContact> known)
{
    /// <summary>Creates the program's catalogue.</summary>
    public ExchangeContacts()
        : this(Catalogue)
    {
    }

    /// <summary>
    /// Gets the program's catalogue. It is empty: no way had been checked against its source when the module was
    /// built (2026-11-08), and none is entered without one.
    /// </summary>
    public static IReadOnlyList<ExchangeContact> Catalogue { get; } = [];

    /// <summary>Gives the ways to reach a recipient, by its name, ignoring case and spaces at the ends.</summary>
    /// <param name="name">The recipient's name.</param>
    /// <returns>The ways, or an empty list.</returns>
    public IReadOnlyList<ExchangeContact> For(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var wanted = name.Trim();
        return [.. known.Where(contact =>
            string.Equals(contact.Recipient, wanted, StringComparison.OrdinalIgnoreCase))];
    }
}
