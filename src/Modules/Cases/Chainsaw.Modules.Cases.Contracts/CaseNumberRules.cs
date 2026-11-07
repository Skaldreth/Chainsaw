namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// Which case numbers are accepted. The number becomes the file name (<c>&lt;case number&gt;.cut</c>), so it must be
/// a safe file name on Windows: letters, digits, space, <c>-</c>, <c>_</c> and <c>.</c>, no path, no reserved name.
/// </summary>
public static class CaseNumberRules
{
    /// <summary>The longest case number that is accepted.</summary>
    public const int MaximumLength = 64;

    /// <summary>Names that Windows reserves for devices, whatever extension follows them.</summary>
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Checks whether a case number can be used.</summary>
    /// <param name="caseNumber">The case number as the user wrote it.</param>
    /// <returns><see langword="true"/> when the number is accepted as it is.</returns>
    public static bool IsValid(string? caseNumber)
    {
        if (string.IsNullOrEmpty(caseNumber) || caseNumber.Length > MaximumLength)
        {
            return false;
        }

        // Leading or trailing dots and spaces are dropped by Windows, so two numbers could name the same file.
        if (caseNumber[0] is '.' or ' ' || caseNumber[^1] is '.' or ' ')
        {
            return false;
        }

        foreach (var character in caseNumber)
        {
            if (!char.IsLetterOrDigit(character) && character is not (' ' or '-' or '_' or '.'))
            {
                return false;
            }
        }

        var stem = caseNumber.Split('.')[0];
        return !ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}
