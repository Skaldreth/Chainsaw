namespace Chainsaw.SharedKernel.Commands;

/// <summary>A command's short answer, shown above the command bar.</summary>
/// <param name="IsError">Whether the command could not do what was asked.</param>
/// <param name="Text">The answer in Swedish; empty when there is nothing to say.</param>
public sealed record CommandReply(bool IsError, string Text)
{
    /// <summary>Gets an answer that says nothing, for a command whose result is seen elsewhere.</summary>
    public static CommandReply None { get; } = new(false, string.Empty);

    /// <summary>Gives an answer for a command that did what was asked.</summary>
    /// <param name="text">What it did.</param>
    /// <returns>The answer.</returns>
    public static CommandReply Done(string text) => new(false, text);

    /// <summary>Gives an answer for a command that could not do what was asked.</summary>
    /// <param name="text">Why, briefly.</param>
    /// <returns>The answer.</returns>
    public static CommandReply Failed(string text) => new(true, text);
}
