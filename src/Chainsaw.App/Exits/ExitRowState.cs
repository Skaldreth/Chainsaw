namespace Chainsaw.App.Exits;

/// <summary>Where one exit in the panel stands, shown as its dot (sketch UT1a).</summary>
public enum ExitRowState
{
    /// <summary>Not connected: a grey dot.</summary>
    Off,

    /// <summary>Being connected: a yellow dot and <em>Ansluter …</em>.</summary>
    Connecting,

    /// <summary>Connected: a green dot and <em>Ansluten</em>.</summary>
    Connected,

    /// <summary>Failed: a red dot and the reason under the name.</summary>
    Failed,
}
