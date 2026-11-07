namespace Chainsaw.SharedKernel;

/// <summary>The computer's own clock.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Gets the current time in UTC from the system.</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
