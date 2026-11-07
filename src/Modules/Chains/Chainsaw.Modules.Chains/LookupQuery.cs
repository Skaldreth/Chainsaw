using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>One kind of lookup: what to ask, how to read the answer and when the answer can no longer change.</summary>
/// <typeparam name="T">What is looked up.</typeparam>
/// <param name="Kind">What is asked (<see cref="ResponseKinds"/>).</param>
/// <param name="Subject">What it is asked about; with the kind it finds the answer in the case again.</param>
/// <param name="Path">The path under the source's base address.</param>
/// <param name="Parse">Reads and checks a fresh response; throws <see cref="EsploraFormatException"/>.</param>
/// <param name="Index">What a fresh answer adds to the indexes.</param>
/// <param name="FindStored">Finds the newest answer in the case and reads it, or gives <see langword="null"/>.</param>
/// <param name="SettledAt">
/// The height of the shallowest block the answer rests on, when every part of it is in a block; once that block is
/// deep enough the answer cannot change. <see langword="null"/> when it can still change.
/// </param>
/// <param name="NeverChanges">Whether the answer can never change (a block by its hash).</param>
internal sealed record LookupQuery<T>(
    string Kind,
    string Subject,
    string Path,
    Func<byte[], T> Parse,
    Func<T, ResponseIndex> Index,
    Func<ICaseReader, (T Value, StoredResponse Response)?> FindStored,
    Func<T, long?> SettledAt,
    bool NeverChanges = false)
    where T : class;
