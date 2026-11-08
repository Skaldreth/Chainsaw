using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The label library on this computer (CLAUDE.md, <em>Etiketter smittar</em>): it gathers the labels from every case
/// that is opened, also a colleague's, and gives them back to the cases that have the same addresses. It is encrypted
/// with a key from the vault and needs an unlocked vault.
/// </summary>
/// <remarks>
/// <para>
/// Everything follows (the user's decision 2026-11-07, <em>Allt följer med</em>): the case number, the reason, the
/// note and the whole operator profile (name, title and unit) go with a label into other cases, marked
/// <em>från ett annat ärende</em>, where it is a judgement, never a fact. The other cases that hold it stay in the
/// library. A label marked <see cref="LabelEntry.DoNotSpread"/>, a label on a victim, and every label on an address or
/// cluster near a victim's address, never enters the library.
/// </para>
/// <para>The methods are synchronous and thread-safe; call them off the UI thread.</para>
/// </remarks>
public interface ILabelLibrary
{
    /// <summary>
    /// Takes in the labels of an open case that may spread, on any chain, replacing what the library had from that
    /// case. A label on or near an address labelled as a victim stays out. A label the user set in this case (own or
    /// from an open source) and then took back is withdrawn: it leaves the library, is never given again, and its
    /// copies in other cases are taken back there the next time those cases are given labels
    /// (<see cref="ShareInto(ICase, IReadOnlyCollection{ChainAddress})"/>). Any other label taken back here (a copy,
    /// a sanction hit, an analysis's label) only leaves the library from this case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>What changed.</returns>
    /// <exception cref="Security.Contracts.VaultException">The vault is locked.</exception>
    LibraryAbsorption Absorb(ICase @case);

    /// <summary>
    /// Gives the case the library's labels for the addresses, sealed in the case as
    /// <see cref="LabelSource.OtherCase"/> with one line in the journal, with who set it, when, why, the note and the
    /// case it was set in (<see cref="LabelOrigin.CaseNumber"/>). A label the case already has, or has taken
    /// back, is not given again. Copies of labels that were withdrawn where they were set are taken back, with a
    /// line in the journal that says nothing about the other case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The case's addresses, for example every address in the graph.</param>
    /// <returns>The labels that came in.</returns>
    /// <exception cref="Security.Contracts.VaultException">The vault is locked.</exception>
    IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>
    /// Gives the case the library's labels for addresses on any chain (Bitcoin addresses and Ethereum accounts), as
    /// <see cref="ShareInto(ICase, IReadOnlyCollection{BitcoinAddress})"/> does for Bitcoin. The same text on two
    /// chains is two addresses.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The case's addresses, each with its chain.</param>
    /// <returns>The labels that came in.</returns>
    /// <exception cref="Security.Contracts.VaultException">The vault is locked.</exception>
    IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<ChainAddress> addresses);

    /// <summary>Lists the library's labels, for the operator's own view of it.</summary>
    /// <returns>The labels.</returns>
    /// <exception cref="Security.Contracts.VaultException">The vault is locked.</exception>
    IReadOnlyList<LibraryLabel> List();
}

/// <summary>A label in the library.</summary>
/// <param name="Subject">What it is on.</param>
/// <param name="Kind">What it says.</param>
/// <param name="Name">The name, or <see langword="null"/>.</param>
/// <param name="Origin">
/// Where it first came from, with the reason and the note when the case it came from had them; this stays in the
/// library.
/// </param>
/// <param name="CaseNumbers">The case numbers of the cases that hold it; this stays in the library.</param>
public sealed record LibraryLabel(
    LabelSubject Subject, LabelKind Kind, string? Name, LabelOrigin Origin, IReadOnlyList<string> CaseNumbers);

/// <summary>What absorbing a case changed in the library.</summary>
/// <param name="Taken">The case's labels that are in the library now.</param>
/// <param name="Removed">Labels that left the library because no case holds them any more.</param>
public sealed record LibraryAbsorption(int Taken, int Removed);
