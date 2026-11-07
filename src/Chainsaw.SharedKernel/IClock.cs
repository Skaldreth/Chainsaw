namespace Chainsaw.SharedKernel;

/// <summary>
/// The time now, behind an interface so that every time Chainsaw records (fetched, journalled, monitored) can be
/// fixed in tests.
/// </summary>
public interface IClock
{
    /// <summary>Gets the current time in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
