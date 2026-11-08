using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Exits;

/// <summary>A country an exit can stand in: its code (ISO 3166-1 alpha-2) and its Swedish name.</summary>
/// <param name="Code">Two upper-case letters, as the check's answer gives them.</param>
/// <param name="Name">The country's name in Swedish.</param>
public sealed record Country(string Code, string Name)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Where an exit stands (sketch UT3b): a country from the list and a city, which is optional, kept together in the
/// exit's place as <c>Sverige · Stockholm</c>. A place written before the list, or by hand, is kept as it is and only
/// read as a country when it starts with one.
/// </summary>
public static class ExitPlace
{
    /// <summary>What stands between the country and the city.</summary>
    public const string Separator = " · ";

    /// <summary>What a row says in the column Plats when the exit has none.</summary>
    public const string NoPlaceText = "Ingen plats angiven";

    /// <summary>
    /// The countries offered, by name: the EU and EEA, and the other countries where VPN services commonly have exits.
    /// </summary>
    public static IReadOnlyList<Country> Countries { get; } = ByName(
        [
            new("AL", "Albanien"), new("AR", "Argentina"), new("AU", "Australien"), new("BE", "Belgien"),
            new("BR", "Brasilien"), new("BG", "Bulgarien"), new("CA", "Kanada"), new("CH", "Schweiz"),
            new("CL", "Chile"), new("CY", "Cypern"), new("CZ", "Tjeckien"), new("DE", "Tyskland"),
            new("DK", "Danmark"), new("EE", "Estland"), new("ES", "Spanien"), new("FI", "Finland"),
            new("FR", "Frankrike"), new("GB", "Storbritannien"), new("GR", "Grekland"), new("HK", "Hongkong"),
            new("HR", "Kroatien"), new("HU", "Ungern"), new("IE", "Irland"), new("IL", "Israel"),
            new("IS", "Island"), new("IT", "Italien"), new("JP", "Japan"), new("LI", "Liechtenstein"),
            new("LT", "Litauen"), new("LU", "Luxemburg"), new("LV", "Lettland"), new("MD", "Moldavien"),
            new("MT", "Malta"), new("MX", "Mexiko"), new("NL", "Nederländerna"), new("NO", "Norge"),
            new("NZ", "Nya Zeeland"), new("PL", "Polen"), new("PT", "Portugal"), new("RO", "Rumänien"),
            new("RS", "Serbien"), new("SE", "Sverige"), new("SG", "Singapore"), new("SI", "Slovenien"),
            new("SK", "Slovakien"), new("TR", "Turkiet"), new("UA", "Ukraina"), new("US", "USA"),
            new("ZA", "Sydafrika"),
        ]);

    /// <summary>Orders countries by name as Swedish orders them (Ö last).</summary>
    /// <param name="countries">The countries.</param>
    /// <returns>The countries by name.</returns>
    private static Country[] ByName(Country[] countries)
    {
        var swedish = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("sv-SE"), false);
        return [.. countries.OrderBy(country => country.Name, swedish)];
    }

    /// <summary>Finds a country by its code.</summary>
    /// <param name="code">Two letters, in any case.</param>
    /// <returns>The country, or <see langword="null"/> when it is not in the list.</returns>
    public static Country? ByCode(string code) =>
        Countries.FirstOrDefault(country => country.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gives a country's name for its code, or the code itself when the list does not have it.</summary>
    /// <param name="code">Two letters.</param>
    /// <returns>The name.</returns>
    public static string NameOf(string code) => ByCode(code)?.Name ?? code.ToUpperInvariant();

    /// <summary>Reads the country a place starts with.</summary>
    /// <param name="place">The exit's place.</param>
    /// <returns>The country, or <see langword="null"/> when the place does not start with one in the list.</returns>
    public static Country? CountryOf(string place)
    {
        var first = place.Split(Separator.Trim(), 2)[0].Trim();
        return Countries.FirstOrDefault(
            country => country.Name.Equals(first, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Reads the city of a place that starts with a country, or the whole place when it does not.</summary>
    /// <param name="place">The exit's place.</param>
    /// <returns>The city, or an empty text.</returns>
    public static string CityOf(string place)
    {
        if (CountryOf(place) is null)
        {
            return place.Trim();
        }

        var parts = place.Split(Separator.Trim(), 2);
        return parts.Length == 2 ? parts[1].Trim() : string.Empty;
    }

    /// <summary>Writes a place from a country and a city, within the limit.</summary>
    /// <param name="country">The country, or <see langword="null"/> for none.</param>
    /// <param name="city">The city; may be empty.</param>
    /// <returns>For example <c>Sverige · Stockholm</c>, <c>Sverige</c> or <c>Stockholm</c>.</returns>
    public static string Compose(Country? country, string city)
    {
        var parts = new[] { country?.Name ?? string.Empty, city.Trim() }.Where(part => part.Length > 0);
        var place = string.Join(Separator, parts);
        return place.Length > ExitRules.MaximumPlaceLength ? place[..ExitRules.MaximumPlaceLength].Trim() : place;
    }
}
