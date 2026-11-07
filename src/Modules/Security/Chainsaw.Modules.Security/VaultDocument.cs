namespace Chainsaw.Modules.Security;

/// <summary>
/// The vault file as it is stored: a readable header and two encrypted boxes. Nothing in it is secret except what the
/// boxes hold.
/// </summary>
/// <param name="Format">Always <see cref="FormatName"/>, so that a stray file is not taken for a vault.</param>
/// <param name="Version">The format version; a build refuses versions it does not know.</param>
/// <param name="Kdf">How the wrapping key is derived from the master key.</param>
/// <param name="DataKey">The data key, encrypted with the wrapping key.</param>
/// <param name="Content">The content (API keys, operator profile), encrypted with a key derived from the data key.</param>
internal sealed record VaultDocument(string Format, int Version, KdfParameters Kdf, SealedBox DataKey, SealedBox Content)
{
    /// <summary>The value of <see cref="Format"/>.</summary>
    public const string FormatName = "chainsaw-vault";

    /// <summary>The format version this build writes and reads. Before 1.0 the format may change freely.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The associated data of the wrapped data key, binding it to this format and version.</summary>
    public static ReadOnlySpan<byte> DataKeyAssociatedData => "chainsaw-vault/v1/data-key"u8;

    /// <summary>The associated data of the content, binding it to this format and version.</summary>
    public static ReadOnlySpan<byte> ContentAssociatedData => "chainsaw-vault/v1/content"u8;
}

/// <summary>The parameters of the master key derivation, stored so that a vault opens with what it was written with.</summary>
/// <param name="Algorithm">Always <see cref="AlgorithmName"/>.</param>
/// <param name="Iterations">The PBKDF2 iteration count.</param>
/// <param name="Salt">The random salt, new for every master key.</param>
internal sealed record KdfParameters(string Algorithm, int Iterations, byte[] Salt)
{
    /// <summary>The value of <see cref="Algorithm"/>.</summary>
    public const string AlgorithmName = "PBKDF2-SHA512";

    /// <summary>
    /// The highest iteration count a file may ask for, so that a damaged or hostile file cannot hang the unlock.
    /// </summary>
    public const int MaximumIterations = 10_000_000;
}

/// <summary>The first two fields of a vault file, read before the rest so that the version is checked first.</summary>
/// <param name="Format">The format name, or <see langword="null"/> when it is missing.</param>
/// <param name="Version">The format version, or 0 when it is missing.</param>
internal sealed record VaultHeader(string? Format, int Version);
