using System.Security;
using System.Text;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// A small synthetic file in the form of the EU's consolidated list of financial sanctions (the FSF export in XML):
/// made-up entities with synthetic addresses, never the real list.
/// </summary>
internal sealed class EuFile(string generationDate = "2026-11-06T19:01:41.347+01:00")
{
    private readonly StringBuilder _entities = new();

    /// <summary>Adds an entity whose address stands in an identification's number.</summary>
    public EuFile Identified(string reference, string name, string programme, string number, string type = "Other") =>
        Entity(reference, name, programme, Identification(number, "other", type));

    /// <summary>Adds an entity whose address stands in a remark in free text.</summary>
    public EuFile Remarked(string reference, string name, string programme, string remark) =>
        Entity(reference, name, programme, $"    <remark>{Escape(remark)}</remark>\n");

    /// <summary>Adds an entity with a passport and no address.</summary>
    public EuFile Plain(string reference, string name, string programme) =>
        Entity(reference, name, programme, Identification("X1234567", "passport", "Passport"));

    /// <summary>Adds an entity with many identifications, to see the ceiling per entity.</summary>
    public EuFile Many(string reference, int count) =>
        Entity(reference, "MÅNGA", "TEST", string.Concat(
            Enumerable.Range(0, count).Select(index => Identification("N" + index, "id", "Id"))));

    public string Xml(bool withDate = true) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<export xmlns=\"http://eu.europa.ec/fpi/fsd/export\""
        + (withDate ? $" generationDate=\"{generationDate}\"" : string.Empty)
        + " globalFileId=\"123\">\n"
        + _entities
        + "</export>\n";

    public byte[] Bytes(bool withDate = true) => Encoding.UTF8.GetBytes(Xml(withDate));

    public MemoryStream Stream(bool withDate = true) => new(Bytes(withDate));

    private static string Identification(string number, string code, string description) =>
        $"    <identification number=\"{Escape(number)}\" identificationTypeCode=\"{code}\" "
        + $"identificationTypeDescription=\"{Escape(description)}\" logicalId=\"1\"/>\n";

    private EuFile Entity(string reference, string name, string programme, string parts)
    {
        _entities
            .Append($"  <sanctionEntity designationDetails=\"\" euReferenceNumber=\"{reference}\" logicalId=\"7\">\n")
            .Append($"    <regulation regulationType=\"amendment\" programme=\"{programme}\" logicalId=\"8\"/>\n")
            .Append("    <subjectType code=\"enterprise\" classificationCode=\"E\"/>\n")
            .Append($"    <nameAlias wholeName=\"ALIAS {Escape(name)}\" strong=\"false\" logicalId=\"9\"/>\n")
            .Append($"    <nameAlias wholeName=\"{Escape(name)}\" strong=\"true\" logicalId=\"10\"/>\n")
            .Append(parts)
            .Append("  </sanctionEntity>\n");
        return this;
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
