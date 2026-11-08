namespace Chainsaw.App.Exits;

/// <summary>Where an exit's check stands, shown in the column <em>Syns som</em> (sketch UT3b).</summary>
public enum ExitCheckState
{
    /// <summary>Never checked: <em>Inte kontrollerad</em> in grey.</summary>
    NotChecked,

    /// <summary>The trace is being asked through the exit: a spinner.</summary>
    Checking,

    /// <summary>The trace answered, in the country the place says or where the place names none: a tick.</summary>
    Seen,

    /// <summary>The trace answered from another country than the place says: a warning in yellow.</summary>
    Mismatch,

    /// <summary>The trace did not answer: <em>Kontrollen gick inte</em>; lookups still go through the exit.</summary>
    Failed,
}
