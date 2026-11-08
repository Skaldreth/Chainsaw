using System.Xml;
using System.Xml.Linq;
using Chainsaw.Modules.Attribution.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// What the readers of sanction lists share: a reader that refuses DTDs and external entities, and a way to read an
/// entry's parts one at a time with a ceiling, so that a crafted file with millions of ids in one entry cannot take
/// unbounded memory (the review of etapp 8, M6).
/// </summary>
internal static class SanctionXml
{
    /// <summary>
    /// The most parts one entry may have, counting the elements read one at a time: far above any real entry (the
    /// largest have some hundreds), far below what would fill the memory.
    /// </summary>
    public const int MaximumPartsPerEntry = 20_000;

    /// <summary>Gives the settings every list is read with.</summary>
    /// <returns>A reader's settings without DTDs, external entities or comments.</returns>
    public static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = SanctionLists.MaximumLength,
    };

    /// <summary>Gives the local name of a document's root element, or none when it is not XML.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The root's local name, or <see langword="null"/>.</returns>
    public static string? RootName(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, Settings());
            return reader.MoveToContent() == XmlNodeType.Element ? reader.LocalName : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the parts of the element the reader stands on, one small element at a time: each child, and for a
    /// child whose name is one of <paramref name="containers"/> each of its children instead. The reader ends after
    /// the element.
    /// </summary>
    /// <param name="reader">The reader, on the element's start.</param>
    /// <param name="containers">
    /// The children whose own children are read one at a time (for example <c>idList</c>).
    /// </param>
    /// <returns>Each part with the container it was in, or <see langword="null"/> for a direct child.</returns>
    /// <exception cref="SanctionListException">
    /// <see cref="SanctionListError.TooLarge"/> when the element has more than <see cref="MaximumPartsPerEntry"/>
    /// parts.
    /// </exception>
    public static IEnumerable<(string? Container, XElement Part)> Parts(
        XmlReader reader, IReadOnlySet<string> containers)
    {
        var count = 0;
        foreach (var (container, part) in Children(reader, null, containers))
        {
            if (++count > MaximumPartsPerEntry)
            {
                throw new SanctionListException(SanctionListError.TooLarge);
            }

            yield return (container, part);
        }
    }

    /// <summary>Gives a child element's trimmed text.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The child's local name.</param>
    /// <returns>The text, or <see langword="null"/> when there is no such child or it is empty.</returns>
    public static string? Child(XElement parent, string name)
    {
        var text = Elements(parent, name).FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Gives an attribute's trimmed value.</summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The value, or <see langword="null"/> when there is none or it is empty.</returns>
    public static string? Attribute(XElement element, string name)
    {
        var text = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Gives the child elements with a local name, whatever their namespace.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The local name.</param>
    /// <returns>The elements.</returns>
    public static IEnumerable<XElement> Elements(XElement parent, string name) =>
        parent.Elements().Where(element => element.Name.LocalName == name);

    /// <summary>Walks the children of the element the reader stands on, descending into the containers.</summary>
    /// <param name="reader">The reader, on the element's start.</param>
    /// <param name="container">The container being walked, or <see langword="null"/> for the entry itself.</param>
    /// <param name="containers">The names of the containers.</param>
    /// <returns>Each part with its container.</returns>
    private static IEnumerable<(string? Container, XElement Part)> Children(
        XmlReader reader, string? container, IReadOnlySet<string> containers)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            yield break;
        }

        var depth = reader.Depth;
        reader.Read();
        while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            if (container is null && containers.Contains(reader.LocalName))
            {
                var name = reader.LocalName;
                foreach (var inner in Children(reader, name, containers))
                {
                    yield return inner;
                }

                continue;
            }

            yield return (container, (XElement)XNode.ReadFrom(reader));
        }

        reader.Read();
    }
}
