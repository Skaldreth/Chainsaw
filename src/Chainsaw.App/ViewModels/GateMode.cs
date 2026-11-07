namespace Chainsaw.App.ViewModels;

/// <summary>What the window shows: the first-run setup, the lock screen or the shell.</summary>
public enum GateMode
{
    /// <summary>There was no vault: the first-run setup (sketch FS1a).</summary>
    FirstRun,

    /// <summary>The vault is locked: the lock screen (sketch ID7), and nothing else.</summary>
    Locked,

    /// <summary>The vault is open: the shell (sketch SH1b).</summary>
    Unlocked,
}
