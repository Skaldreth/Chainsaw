using System.Text;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// A small synthetic SDN list in the form of OFAC's <c>sdn.xml</c>: made-up entries with synthetic addresses, never
/// the real list.
/// </summary>
internal sealed class SdnFile(string publishDate = "11/05/2026")
{
    private readonly StringBuilder _entries = new();

    public SdnFile Entry(string uid, string lastName, string program, params (string Type, string Number)[] ids)
    {
        _entries.Append("  <sdnEntry>\n")
            .Append("    <uid>").Append(uid).Append("</uid>\n")
            .Append("    <lastName>").Append(lastName).Append("</lastName>\n")
            .Append("    <sdnType>Entity</sdnType>\n")
            .Append("    <programList><program>").Append(program).Append("</program></programList>\n")
            .Append("    <idList>\n");
        var idUid = 1;
        foreach (var (type, number) in ids)
        {
            _entries.Append("      <id><uid>").Append(uid).Append(idUid++).Append("</uid><idType>").Append(type)
                .Append("</idType><idNumber>").Append(number).Append("</idNumber></id>\n");
        }

        _entries.Append("    </idList>\n  </sdnEntry>\n");
        return this;
    }

    public SdnFile Xbt(string uid, string lastName, string address) =>
        Entry(uid, lastName, "CYBER2", ("Digital Currency Address - XBT", address));

    public string Xml() =>
        "<?xml version=\"1.0\" standalone=\"yes\"?>\n"
        + "<sdnList xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://tempuri.org/sdnList.xsd\">\n"
        + "  <publshInformation><Publish_Date>" + publishDate + "</Publish_Date><Record_Count>2</Record_Count>"
        + "</publshInformation>\n"
        + _entries
        + "</sdnList>\n";

    public byte[] Bytes() => Encoding.UTF8.GetBytes(Xml());

    public MemoryStream Stream() => new(Bytes());
}
