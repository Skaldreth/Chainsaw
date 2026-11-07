using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The label library on this computer (CLAUDE.md, <em>Etiketter smittar</em>): it gathers the labels from every case
/// that is opened, also a colleague's, and gives them back to the cases that have the same addresses. It is encrypted
/// with a key from the vault and needs an unlocked vault.
/// </summary>
/// <remarks>
/// <para>
/// Secrecy: the library keeps the case number, the reason and the note, since it is the operator's own, but none of
/// them goes on into another case. There a label states only who set it, when, what it says and its original source,
/// marked <em>från ett annat ärende</em>. A label marked <see cref="LabelEntry.DoNotSpread"/>, and every label on a
/// victim, never enters the library.
/// </para>
/// <para>The methods are synchronous and thread-safe; call them off the UI thread.</para>
/// </remarks>
public interface ILabelLibrary
{
    /// <summary>
    /// Takes in the labels of an open case that may spread, replacing what the library had from that case, so that a
    /// label taken back or marked <em>Sprid inte</em> there leaves the library unless another case still holds it.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>What changed.</returns>
    /// <exception cref="Security.Contracts.VaultException">The vault is locked.</exception>
    LibraryAbsorption Absorb(ICase @case);

    /// <summary>
    /// Gives the case the library's labels for the addresses, sealed in the case as
    /// <see cref="LabelSource.OtherCase"/> with one line in the journal. A label the case already has, or has taken
    /// back, is not given again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The case's addresses, for example every address in the graph.</param>
    /// <returns>The labels that came in.</returns>
    /// <exception cref="Security.Contracts.VaultException">The vault is locked.</exception>
    IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

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
