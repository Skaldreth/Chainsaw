namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// What a check of the way out saw (sketch UT3b): the address the sources see lookups come from, and its country, as
/// <c>1.1.1.1/cdn-cgi/trace</c> answered through the way out. It says nothing about a case and is never kept in one; a
/// check of an exit is kept in the vault with the exit.
/// </summary>
/// <param name="ExitId">The exit the question left through, or <see cref="Guid.Empty"/> through Windows.</param>
/// <param name="Route">The sort of way out the question took.</param>
/// <param name="Address">The address the answer saw, as it wrote it.</param>
/// <param name="CountryCode">
/// The country the answer placed the address in, as two upper-case letters (ISO 3166-1), or an empty text when the
/// answer did not say.
/// </param>
/// <param name="CheckedAt">When the answer came, in UTC.</param>
public sealed record ExitSighting(
    Guid ExitId, ExitRoute Route, string Address, string CountryCode, DateTimeOffset CheckedAt)
{
    /// <summary>Gets whether the answer placed the address in a country.</summary>
    public bool HasCountry => CountryCode.Length > 0;
}
