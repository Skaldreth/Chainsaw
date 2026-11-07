using System.Text.Json.Serialization;

namespace Chainsaw.Modules.Cases;

/// <summary>The details of a <c>case.anchors-replaced</c> journal entry.</summary>
/// <param name="Problem">What was wrong (<see cref="AnchorProblem"/>).</param>
/// <param name="Anchors">The anchors the case no longer reached.</param>
internal sealed record AnchorDetails(
    [property: JsonPropertyName("problem")] string Problem,
    [property: JsonPropertyName("anchors")] IReadOnlyList<AnchorDetail> Anchors);

/// <summary>One anchor that the case no longer reached.</summary>
/// <param name="Chain">The chain's name.</param>
/// <param name="Sequence">The anchored sequence number.</param>
/// <param name="Hash">The anchored hash in lower-case hex.</param>
internal sealed record AnchorDetail(
    [property: JsonPropertyName("chain")] string Chain,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("hash")] string Hash);
