using System.Text.Json.Nodes;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The unencrypted header of a case file: the case id and the key envelopes. Envelopes of kinds this build does not
/// know are kept as they are, so that a file keeps them when it is saved here.
/// </summary>
/// <param name="CaseId">The case id, which the envelopes and the database key are bound to.</param>
/// <param name="Envelopes">The envelopes as JSON objects, each with a <c>kind</c>.</param>
internal sealed record CaseHeader(Guid CaseId, IReadOnlyList<JsonObject> Envelopes)
{
    /// <summary>Gives a header with other envelopes and the same case id.</summary>
    /// <param name="envelopes">The new envelopes.</param>
    /// <returns>The new header.</returns>
    public CaseHeader WithEnvelopes(IEnumerable<JsonObject> envelopes) => this with { Envelopes = [.. envelopes] };
}
