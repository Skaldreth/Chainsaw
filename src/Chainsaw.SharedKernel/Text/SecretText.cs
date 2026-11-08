using System.Text;

namespace Chainsaw.SharedKernel.Text;

/// <summary>
/// Puts a secret that a person types (the master key, a case's sharing password) into Unicode normalization form C
/// before a key is derived from it, so that the same text typed with different input methods (an <c>å</c> as one
/// character or as <c>a</c> and a combining ring) gives the same key.
/// </summary>
/// <remarks>
/// The normalized text is written into a pinned character array, never into a <see cref="string"/>, so that the caller
/// can clear it with <see cref="Clear"/> when the key has been derived.
/// </remarks>
public static class SecretText
{
    /// <summary>The normalization form every typed secret is derived in.</summary>
    public const NormalizationForm Form = NormalizationForm.FormC;

    /// <summary>Gives a secret in normalization form C, in a pinned array that the caller clears.</summary>
    /// <param name="secret">The secret as typed.</param>
    /// <returns>The normalized secret; exactly as long as it is.</returns>
    /// <exception cref="ArgumentException">The text holds invalid UTF-16 (a lone surrogate).</exception>
    public static char[] Normalize(ReadOnlySpan<char> secret)
    {
        var buffer = GC.AllocateArray<char>(secret.GetNormalizedLength(Form), pinned: true);
        if (!secret.TryNormalize(buffer, out var written, Form))
        {
            Clear(buffer);
            throw new ArgumentException("The secret could not be normalized.", nameof(secret));
        }

        if (written == buffer.Length)
        {
            return buffer;
        }

        var exact = GC.AllocateArray<char>(written, pinned: true);
        buffer.AsSpan(0, written).CopyTo(exact);
        Clear(buffer);
        return exact;
    }

    /// <summary>Tells whether a secret is already in normalization form C.</summary>
    /// <param name="secret">The secret as typed.</param>
    /// <returns><see langword="true"/> when normalizing it changes nothing.</returns>
    public static bool IsNormalized(ReadOnlySpan<char> secret) => secret.IsNormalized(Form);

    /// <summary>Overwrites a secret's characters with zeros.</summary>
    /// <param name="secret">The characters, or <see langword="null"/>.</param>
    public static void Clear(char[]? secret)
    {
        if (secret is not null)
        {
            Array.Clear(secret);
        }
    }
}
