namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What a lookup says when a source refuses the API key, so that the shell can offer to change the key.</summary>
public static class SourceKeyTexts
{
    /// <summary>How a problem that the API key is refused begins.</summary>
    public const string KeyRefusedLead = "Källan godtar inte API-nyckeln";

    /// <summary>Tells whether a lookup's problem says that a source refused its API key.</summary>
    /// <param name="problem">The problem, in Swedish.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool SaysKeyRefused(string? problem) =>
        problem?.Contains(KeyRefusedLead, StringComparison.Ordinal) == true;
}
