using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>What to trace and how.</summary>
/// <param name="Method">The tracing method; the newest registered version is used.</param>
/// <param name="Seeds">The coins the traced money starts in, for example the victim's payments.</param>
public sealed record TraceRequest(TracingMethodKind Method, IReadOnlyList<TraceSeed> Seeds);

/// <summary>A coin the traced money starts in.</summary>
/// <param name="Coin">The coin; its transaction must be in the graph.</param>
/// <param name="Amount">
/// How much of it is traced, counted from the coin's start; <see langword="null"/> for the whole coin.
/// </param>
public sealed record TraceSeed(Outpoint Coin, AssetAmount? Amount = null);
