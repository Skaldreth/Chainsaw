using System.Text;

namespace Chainsaw.SharedKernel.Text;

/// <summary>
/// Puts a secret that a person types (the master key, a case's sharing password) into Unicode normalization form C
/// before a key is derived from it, so that the same text typed with different input methods (an <c>å</c> as one
/// character or as <c>a</c> and a combining ring) gives the same key.
/// </summary>
/// <remarks>
/// The normalized text is written into a pinned character array, never into a <see cref="string"/>, so that the caller
/// can clear it with <see cref="Clear"/> when the key has been derived. A lone surrogate, which can neither be
/// normalized nor encoded, becomes U+FFFD first, as a lenient UTF-8 encoder makes it, so that no typed text is refused.
/// No secret from before the normalization can hold one: the strict UTF-8 encoding inside PBKDF2 refused it then.
/// </remarks>
public static class SecretText
{
    /// <summary>The normalization form every typed secret is derived in.</summary>
    public const NormalizationForm Form = NormalizationForm.FormC;

    /// <summary>What a lone surrogate becomes, as in the UTF-8 encoder.</summary>
    private const char Replacement = '\uFFFD';

    /// <summary>Gives a secret in normalization form C, in a pinned array that the caller clears.</summary>
    /// <param name="secret">The secret as typed.</param>
    /// <returns>The normalized secret; exactly as long as it is.</returns>
    public static char[] Normalize(ReadOnlySpan<char> secret)
    {
        var wellFormed = GC.AllocateArray<char>(secret.Length, pinned: true);
        try
        {
            ReplaceLoneSurrogates(secret, wellFormed);
            ReadOnlySpan<char> text = wellFormed;
            var buffer = GC.AllocateArray<char>(text.GetNormalizedLength(Form), pinned: true);
            if (!text.TryNormalize(buffer, out var written, Form))
            {
                Clear(buffer);
                throw new InvalidOperationException("A well-formed text could not be normalized.");
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
        finally
        {
            Clear(wellFormed);
        }
    }

    /// <summary>Tells whether a secret is already in normalization form C.</summary>
    /// <param name="secret">The secret as typed.</param>
    /// <returns>
    /// <see langword="true"/> when normalizing it changes nothing; <see langword="false"/> also for a lone surrogate.
    /// </returns>
    public static bool IsNormalized(ReadOnlySpan<char> secret)
    {
        try
        {
            return secret.IsNormalized(Form);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Overwrites a secret's characters with zeros.</summary>
    /// <param name="secret">The characters, or <see langword="null"/>.</param>
    public static void Clear(char[]? secret)
    {
        if (secret is not null)
        {
            Array.Clear(secret);
        }
    }

    /// <summary>Copies a text, with every surrogate that has no partner made U+FFFD.</summary>
    /// <param name="source">The text.</param>
    /// <param name="destination">As long as the text.</param>
    private static void ReplaceLoneSurrogates(ReadOnlySpan<char> source, Span<char> destination)
    {
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (char.IsHighSurrogate(character) && index + 1 < source.Length && char.IsLowSurrogate(source[index + 1]))
            {
                destination[index] = character;
                destination[index + 1] = source[index + 1];
                index++;
            }
            else
            {
                destination[index] = char.IsSurrogate(character) ? Replacement : character;
            }
        }
    }
}
