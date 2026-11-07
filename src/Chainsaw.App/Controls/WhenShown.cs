using System.Windows.Input;

namespace Chainsaw.App.Controls;

/// <summary>
/// A command that only runs while a condition holds, such as the shell's shortcuts only while the shell is shown, so
/// that no key reaches what the lock screen hides.
/// </summary>
/// <param name="inner">The command.</param>
/// <param name="condition">Whether it may run now.</param>
public sealed class WhenShown(ICommand inner, Func<bool> condition) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => condition() && inner.CanExecute(parameter);

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            inner.Execute(parameter);
        }
    }
}
