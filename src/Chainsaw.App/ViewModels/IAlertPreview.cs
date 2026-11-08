namespace Chainsaw.App.ViewModels;

/// <summary>
/// Shows the ways a hit is said, so that the user can see and hear them in the settings (sketch BV4a): the message in
/// the middle of the screen (<em>Prova</em>) and the coin swoosh (<em>Lyssna</em>). Both say only that a watch has
/// hit, never what or where.
/// </summary>
public interface IAlertPreview
{
    /// <summary>Shows the message in the middle of the screen, as a preview.</summary>
    void ShowMessage();

    /// <summary>Plays the coin swoosh.</summary>
    void PlaySound();

    /// <summary>Closes the preview's message, as when Chainsaw locks, so that nothing stays over the lock.</summary>
    void CloseMessage();
}
