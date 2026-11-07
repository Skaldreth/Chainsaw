namespace Chainsaw.App.ViewModels;

/// <summary>The background process as Chainsaw sees it: whether it runs, and starting and stopping it.</summary>
public interface IBackgroundProcess
{
    /// <summary>Gets whether the program is next to Chainsaw's.</summary>
    bool Exists { get; }

    /// <summary>Gets the command that starts it, for Windows to run at sign-in.</summary>
    string Command { get; }

    /// <summary>Says whether it runs for this Windows user.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    bool IsRunning();

    /// <summary>Starts it, quietly; one that already runs ends at once by itself.</summary>
    /// <returns><see langword="false"/> when it could not be started.</returns>
    bool Start();

    /// <summary>Asks it to stop.</summary>
    void Stop();
}

/// <summary>Starting the background process with Windows, for this user only.</summary>
public interface IAutostart
{
    /// <summary>Says whether it is set to start with Windows.</summary>
    /// <returns><see langword="true"/> when it is.</returns>
    bool IsSet();

    /// <summary>Sets it to start with Windows, or not.</summary>
    /// <param name="command">The command, or <see langword="null"/> to take it away.</param>
    void Set(string? command);
}
