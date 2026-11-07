using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Tests;

/// <summary>The toaster (sketch TO1a), without WPF.</summary>
public sealed class ToasterTests
{
    private static Toaster NewToaster() => new(new InlineUi());

    [Fact]
    public void A_toast_is_added_at_the_bottom()
    {
        var toaster = NewToaster();

        toaster.Show(ToastKind.Success, "Första");
        toaster.Show(ToastKind.Information, "Andra");

        Assert.Equal(["Första", "Andra"], toaster.Toasts.Select(toast => toast.Title));
    }

    [Fact]
    public void At_most_five_stand_and_a_sixth_fades_the_oldest()
    {
        var toaster = NewToaster();

        for (var index = 1; index <= 6; index++)
        {
            toaster.Show(ToastKind.Information, $"Toast {index}");
        }

        Assert.True(toaster.Toasts[0].IsLeaving);
        Assert.Equal(Toaster.MaximumShown, toaster.Toasts.Count(toast => !toast.IsLeaving));
    }

    [Theory]
    [InlineData(ToastKind.Error, true)]
    [InlineData(ToastKind.Warning, true)]
    [InlineData(ToastKind.Success, false)]
    [InlineData(ToastKind.Information, false)]
    public void Errors_and_warnings_stay_until_closed(ToastKind kind, bool sticky)
    {
        var toaster = NewToaster();

        toaster.Show(kind, "Något");

        Assert.Equal(sticky, toaster.Toasts[0].IsSticky);
    }

    [Fact]
    public void Closing_starts_the_way_out_and_the_view_removes_it()
    {
        var toaster = NewToaster();
        toaster.Show(ToastKind.Error, "Fel");
        var toast = toaster.Toasts[0];

        toast.CloseCommand.Execute(null);
        Assert.True(toast.IsLeaving);
        toaster.Remove(toast);

        Assert.Empty(toaster.Toasts);
    }

    [Fact]
    public void The_action_runs_and_closes_the_toast()
    {
        var toaster = NewToaster();
        var ran = 0;
        toaster.Show(new ToastRequest(ToastKind.Error, "Fel", "", "Försök igen", () => ran++));
        var toast = toaster.Toasts[0];

        Assert.True(toast.HasAction);
        toast.RunCommand.Execute(null);

        Assert.Equal(1, ran);
        Assert.True(toast.IsLeaving);
    }

    [Fact]
    public void An_empty_title_is_not_shown()
    {
        var toaster = NewToaster();

        toaster.Show(ToastKind.Information, "  ");

        Assert.Empty(toaster.Toasts);
    }
}
