using System.Globalization;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// The tracing methods: rules for how much of the traced money goes on through a transaction. The rules are
/// conventions, not facts; the chain does not say whose money went where (principle 2), so the method is always
/// shown with what it gives.
/// </summary>
public enum TracingMethodKind
{
    /// <summary>
    /// First in, first out: the transaction's inputs are laid end to end in their order and cut into the outputs in
    /// their order, the fee last. The traced units keep their place, so the money stays together.
    /// </summary>
    Fifo,

    /// <summary>
    /// Haircut: the traced share of the inputs is spread over every output and the fee in proportion to their
    /// amounts. Nothing is lost, but the money is thinned out over every way it can have gone.
    /// </summary>
    Haircut,
}

/// <summary>A tracing method and its version; a later version never silently changes what an earlier gave.</summary>
/// <param name="Kind">The method.</param>
/// <param name="Version">The version, from 1; raised whenever the method may give other amounts.</param>
public sealed record TracingMethodId(TracingMethodKind Kind, int Version)
{
    /// <summary>Gets the method.</summary>
    public TracingMethodKind Kind { get; } = Enum.IsDefined(Kind)
        ? Kind
        : throw new ArgumentOutOfRangeException(nameof(Kind), "Unknown tracing method.");

    /// <summary>Gets the version, from 1.</summary>
    public int Version { get; } = Version >= 1
        ? Version
        : throw new ArgumentOutOfRangeException(nameof(Version), "A version starts at 1.");

    /// <summary>Gets the method's name as the user sees it, for example <c>FIFO</c>.</summary>
    public string Name => Kind switch
    {
        TracingMethodKind.Fifo => "FIFO",
        TracingMethodKind.Haircut => "haircut",
        _ => Kind.ToString(),
    };

    /// <summary>Gives the name and version as the user sees them, for example <c>FIFO v1</c>.</summary>
    /// <returns>The name, a space, <c>v</c> and the version.</returns>
    public override string ToString() => Name + " v" + Version.ToString(CultureInfo.InvariantCulture);
}
