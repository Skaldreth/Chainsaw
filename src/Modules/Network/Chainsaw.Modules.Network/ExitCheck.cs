using System.Net;
using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Checks the way out by asking Cloudflare's trace through <see cref="IExitHttp"/>, and keeps the latest answer for
/// each exit with the exit in the vault.
/// </summary>
/// <param name="http">The way out chosen now.</param>
/// <param name="records">The exits in the vault, where the answers are kept.</param>
/// <param name="connection">
/// The connection, whose lock an answer is kept under, in turn with a removal, so that a removed exit's answer is never
/// written back.
/// </param>
internal sealed class ExitCheck(IExitHttp http, ExitRecords records, ExitConnection connection) : IExitCheck
{
    /// <summary>The trace's line with the asker's address.</summary>
    private const string AddressKey = "ip";

    /// <summary>The trace's line with the address's country.</summary>
    private const string CountryKey = "loc";

    /// <summary>The length of a country code.</summary>
    private const int CountryCodeLength = 2;

    /// <inheritdoc />
    public async Task<ExitSighting> CheckAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.SendAsync(
            ExitRequest.Get(IExitCheck.TraceUri) with { Accept = "text/plain" }, cancellationToken);
        if (!response.IsSuccess
            || Read(Encoding.UTF8.GetString(response.Body.Span)) is not { } answer)
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        var sighting = new ExitSighting(
            response.ExitId, response.Route, answer.Address, answer.Country, response.ReceivedAt);
        if (response.Route == ExitRoute.Vpn && response.ExitId != Guid.Empty)
        {
            await KeepAsync(sighting).ConfigureAwait(false);
        }

        return sighting;
    }

    /// <inheritdoc />
    public ExitSighting? FindLast(Guid exitId)
    {
        try
        {
            return records.FindSighting(exitId);
        }
        catch (VaultException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the trace's answer: lines <c>key=value</c>, of which <c>ip</c> must be an address and <c>loc</c>, when
    /// it is two letters, is the country. Anything else in the answer is left.
    /// </summary>
    /// <param name="text">The answer.</param>
    /// <returns>The address and the country (or an empty text), or <see langword="null"/> without an address.</returns>
    internal static (string Address, string Country)? Read(string text)
    {
        var values = text.Split('\n')
            .Select(line => line.Trim().Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0].Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First()[1].Trim(), StringComparer.Ordinal);
        if (!values.TryGetValue(AddressKey, out var address) || !IPAddress.TryParse(address, out var parsed))
        {
            return null;
        }

        var country = values.GetValueOrDefault(CountryKey, string.Empty).ToUpperInvariant();
        var isCountry = country.Length == CountryCodeLength && country.All(char.IsAsciiLetterUpper);
        return (parsed.ToString(), isCountry ? country : string.Empty);
    }

    /// <summary>
    /// Keeps the answer with its exit, while the exit is still in the vault, under the same lock as a removal.
    /// </summary>
    /// <param name="sighting">The answer.</param>
    /// <returns>A task that completes when the answer is kept, or left.</returns>
    private async Task KeepAsync(ExitSighting sighting)
    {
        try
        {
            await connection.ChangeExitsAsync(
                leaveConnected: _ => true,
                change: () =>
                {
                    if (records.Find(sighting.ExitId) is not null)
                    {
                        records.SaveSighting(sighting);
                    }

                    return true;
                }).ConfigureAwait(false);
        }
        catch (VaultException)
        {
            // The vault locked meanwhile: the answer is still shown, only not kept.
        }
    }
}
