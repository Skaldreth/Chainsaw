using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>What a toast says (sketch TO1a): each sort has its own colour and icon; none uses the accent.</summary>
public enum ToastKind
{
    /// <summary>Something failed; stays until closed.</summary>
    Error,

    /// <summary>Something the user should know before going on; stays until closed.</summary>
    Warning,

    /// <summary>Something was done; fades out by itself.</summary>
    Success,

    /// <summary>Something happened; fades out by itself.</summary>
    Information,
}

/// <summary>A toast to show: its sort, title, an optional line under it and an optional action.</summary>
/// <param name="Kind">The sort.</param>
/// <param name="Title">The title, one short sentence.</param>
/// <param name="Text">The line under the title; empty for none.</param>
/// <param name="ActionText">The action's word, such as <em>Försök igen</em>; <see langword="null"/> for none.</param>
/// <param name="Action">What the action does; the toast closes after it.</param>
public sealed record ToastRequest(
    ToastKind Kind, string Title, string Text = "", string? ActionText = null, Action? Action = null);

/// <summary>One toast in the stack (sketch TO1a), as the view shows it.</summary>
public sealed partial class Toast : ObservableObject
{
    /// <summary>The toaster the toast belongs to.</summary>
    private readonly Toaster _owner;

    /// <summary>Creates a toast.</summary>
    /// <param name="owner">The toaster.</param>
    /// <param name="request">What it says.</param>
    internal Toast(Toaster owner, ToastRequest request)
    {
        _owner = owner;
        Request = request;
    }

    /// <summary>Gets what the toast says.</summary>
    public ToastRequest Request { get; }

    /// <summary>Gets the sort.</summary>
    public ToastKind Kind => Request.Kind;

    /// <summary>Gets the title.</summary>
    public string Title => Request.Title;

    /// <summary>Gets the line under the title.</summary>
    public string Text => Request.Text;

    /// <summary>Gets whether there is a line under the title.</summary>
    public bool HasText => Request.Text.Length > 0;

    /// <summary>Gets the action's word.</summary>
    public string ActionText => Request.ActionText ?? string.Empty;

    /// <summary>Gets whether the toast has an action.</summary>
    public bool HasAction => Request is { ActionText: not null, Action: not null };

    /// <summary>Gets whether the toast stays until closed: errors and warnings do, the others fade out by themselves.</summary>
    public bool IsSticky => Kind is ToastKind.Error or ToastKind.Warning;

    /// <summary>Gets whether the toast is on its way out; the view fades it and then removes it.</summary>
    [ObservableProperty]
    public partial bool IsLeaving { get; private set; }

    /// <summary>Closes the toast (its cross, or its time ran out).</summary>
    [RelayCommand]
    public void Close() => _owner.Dismiss(this);

    /// <summary>Does the toast's action and closes it.</summary>
    [RelayCommand]
    private void Run()
    {
        Close();
        Request.Action?.Invoke();
    }

    /// <summary>Starts the toast's way out.</summary>
    internal void Leave() => IsLeaving = true;
}

/// <summary>
/// The toaster (sketch TO1a): one shared way for every part of Chainsaw to say something, at the lower right. The newest
/// comes in at the bottom and pushes the older up; at most <see cref="MaximumShown"/> stand, and a new one beyond that
/// fades the oldest out. Without WPF: the view slides, counts down and fades.
/// </summary>
public sealed class Toaster
{
    /// <summary>The most toasts that stand at once.</summary>
    public const int MaximumShown = 5;

    /// <summary>How long a toast that is not sticky stands before it fades out by itself.</summary>
    public static readonly TimeSpan TimeShown = TimeSpan.FromSeconds(6);

    /// <summary>The UI thread, since any part may say something from any thread.</summary>
    private readonly IUiThread _ui;

    /// <summary>Creates the toaster.</summary>
    /// <param name="ui">The UI thread.</param>
    public Toaster(IUiThread ui) => _ui = ui;

    /// <summary>Gets the toasts, the oldest first, including those on their way out.</summary>
    public ObservableCollection<Toast> Toasts { get; } = [];

    /// <summary>Shows a toast; beyond <see cref="MaximumShown"/>, the oldest that still stands fades out.</summary>
    /// <param name="request">What it says.</param>
    public void Show(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return;
        }

        _ui.Post(() =>
        {
            Toasts.Add(new Toast(this, request));
            var standing = Toasts.Where(toast => !toast.IsLeaving).ToList();
            foreach (var oldest in standing.Take(Math.Max(0, standing.Count - MaximumShown)))
            {
                oldest.Leave();
            }
        });
    }

    /// <summary>Shows a toast of a sort with a title and an optional line.</summary>
    /// <param name="kind">The sort.</param>
    /// <param name="title">The title.</param>
    /// <param name="text">The line under it.</param>
    public void Show(ToastKind kind, string title, string text = "") => Show(new ToastRequest(kind, title, text));

    /// <summary>Starts a toast's way out.</summary>
    /// <param name="toast">The toast.</param>
    public void Dismiss(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        toast.Leave();
    }

    /// <summary>Takes a toast away, once the view has faded it out.</summary>
    /// <param name="toast">The toast.</param>
    public void Remove(Toast toast) => Toasts.Remove(toast);

    /// <summary>Takes every toast away at once, as when Chainsaw locks, so that nothing stands over the lock screen.</summary>
    public void Clear() => Toasts.Clear();
}
