namespace Chainsaw.App.ViewModels;

/// <summary>
/// A field where the user types a secret, such as the master key. The view model takes the secret only when it needs
/// it, as characters it clears as soon as it is done, so that the secret never becomes a string or a property.
/// </summary>
public interface ISecretEntry
{
    /// <summary>Takes what is written and empties the field.</summary>
    /// <returns>
    /// The characters, in an array the caller owns and must clear (see <see cref="Secret.Clear"/>); empty when nothing
    /// was written.
    /// </returns>
    char[] Take();
}
