namespace Chainsaw.App.ViewModels;

/// <summary>The steps of the first-run setup (sketch FS1a), in their order.</summary>
public enum FirstRunStep
{
    /// <summary>Name, title and organisation for the operator profile.</summary>
    Welcome = 1,

    /// <summary>The master key, twice; it creates the vault.</summary>
    MasterKey = 2,

    /// <summary>A WireGuard file for the exit, or later.</summary>
    Exit = 3,

    /// <summary>The first case, which can be skipped.</summary>
    FirstCase = 4,
}

/// <summary>A dash at the top of the form: a step, filled once it is reached.</summary>
/// <param name="Name">The step's name, shown on hover.</param>
/// <param name="IsReached">Whether the step is the current one or behind it.</param>
public sealed record StepDot(string Name, bool IsReached);
