using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Chainsaw.App.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The empty graph that guides (sketch GR6c). Only wiring and the nodes that are built from the view model's lists;
/// what happens lives in <see cref="GraphStartViewModel"/>.
/// </summary>
public partial class GraphStartView : UserControl
{
    /// <summary>How far below the sonar's ring the line under Nytt ärende stands.</summary>
    private const double HintBelowRing = 30;

    /// <summary>The model shown.</summary>
    private GraphStartViewModel? _model;

    /// <summary>Creates the view.</summary>
    public GraphStartView()
    {
        InitializeComponent();
        FirstNewCase.Content = NewCaseNode();
        Course.ItemsSource = CourseDots();
        GhostExpand.Child = Ghost("Expandera", "E på en aktör", "Icon.Plus", "Brush.TextMuted",
            "Hämtar aktörens tx genom utgången och ritar motparterna. Belopp och spårad andel står på linjerna.");
        GhostExit.Child = Ghost("Hitta uttaget", "Blått är uttag", "Icon.Kind.Bank", "Brush.Kind.CashOut",
            "Följ pengarna tills de når en blå nod: en börs eller ett annat uttag. Där finns den som tar ut pengarna.");
        GhostWatch.Child = Ghost("Bevaka", "B på en aktör", "Icon.Eye", "Brush.TextMuted",
            "Säger till när pengarna rör sig. B, eller högerklicka och välj Bevaka …");
        DataContextChanged += (_, _) => Attach(DataContext as GraphStartViewModel);
    }

    /// <summary>Puts the keyboard in the start point's field, when it is shown.</summary>
    public void FocusField()
    {
        if (_model?.IsNoStartPoint == true)
        {
            StartPointBox.Focus();
            StartPointBox.CaretIndex = StartPointBox.Text.Length;
        }
    }

    /// <summary>Follows a new model: its recent cases and its exit.</summary>
    /// <param name="model">The model, or <see langword="null"/>.</param>
    private void Attach(GraphStartViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        if (model is null)
        {
            return;
        }

        model.PropertyChanged += OnModelChanged;
        ShowRing(model);
        ShowExit(model);
    }

    /// <summary>Builds the ring again when the cases change, and colours the exit's dots when it changes.</summary>
    /// <param name="sender">The model.</param>
    /// <param name="e">What changed.</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (e.PropertyName == nameof(GraphStartViewModel.Recent))
        {
            ShowRing(_model);
        }
        else if (e.PropertyName == nameof(GraphStartViewModel.IsOnline))
        {
            ShowExit(_model);
        }
    }

    /// <summary>Puts Nytt ärende in the middle of the ring, the latest cases around it and Alla ärenden last.</summary>
    /// <param name="model">The model.</param>
    private void ShowRing(GraphStartViewModel model)
    {
        Ring.Children.Clear();
        var middle = new Grid();
        middle.Children.Add(new SonarRing
        {
            Width = 248, Height = 82, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1.5),
            BorderBrush = Brush("Brush.Accent"),
        });
        middle.Children.Add(NewCaseNode());
        var hint = new TextBlock
        {
            Text = "Börja en ny spårning, eller öppna ett ärende runt om.", FontSize = 13,
            Foreground = Brush("Brush.TextMuted"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,

            // Below the sonar's ring, moved only where it is drawn, so that the node stays the ring's middle.
            RenderTransform = new TranslateTransform(0, HintBelowRing),
        };
        middle.Children.Add(hint);
        Ring.Children.Add(middle);
        var template = (DataTemplate)FindResource("RecentCase");
        foreach (var item in model.Recent)
        {
            Ring.Children.Add(new ContentPresenter { Content = item, ContentTemplate = template });
        }

        Ring.Children.Add(AllCasesNode());
    }

    /// <summary>Colours the exit's dots: green while lookups can be made, grey offline, and the start point quiet.</summary>
    /// <param name="model">The model.</param>
    private void ShowExit(GraphStartViewModel model)
    {
        var dot = model.IsOnline ? Brush("Brush.Success") : Brush("Brush.TextFaint");
        var text = model.IsOnline ? Brush("Brush.TextDim") : Brush("Brush.TextMuted");
        FootDot.Fill = dot;
        SendDot.Fill = dot;
        FootLine.Foreground = text;
        SendLine.Foreground = text;
        var ring = model.IsOnline ? Brush("Brush.Accent") : Brush("Brush.BorderHover");
        StartSonar.BorderBrush = ring;
        StartFrame.Stroke = ring;
        FootLine.ToolTip = model.IsOnline
            ? "Alla uppslag går genom den här utgången"
            : "Anslut en utgång i titelraden, eller välj Windows i inställningarna";
    }

    /// <summary>Makes the node Nytt ärende, the step the user stands at.</summary>
    /// <returns>The node.</returns>
    private Button NewCaseNode()
    {
        var node = new Button
        {
            Style = (Style)FindResource("Button.GuideNode.Current"),
            Content = NodeContent("Nytt ärende", "Ctrl+N", "Icon.Plus", "Brush.Accent", dashed: false, round: true),
            ToolTip = "Skapa ett ärende: en krypterad fil för utredningen",
        };
        node.SetBinding(Button.CommandProperty, nameof(GraphStartViewModel.NewCaseCommand));
        AutomationProperties.SetName(node, "Nytt ärende");
        return node;
    }

    /// <summary>Makes the node Alla ärenden, which asks for every case through <c>/open</c>.</summary>
    /// <returns>The node.</returns>
    private Button AllCasesNode()
    {
        var node = new Button
        {
            Style = (Style)FindResource("Button.GuideNode"),
            Content = NodeContent("Alla ärenden …", "/open", "Icon.Folder", "Brush.TextMuted", dashed: true, round: false),
            ToolTip = "Alla ärenden i mappen. Samma som /open i kommandoraden.",
        };
        node.SetBinding(Button.CommandProperty, nameof(GraphStartViewModel.AllCasesCommand));
        AutomationProperties.SetName(node, "Alla ärenden");
        return node;
    }

    /// <summary>Makes a later step, faint, with its explanation on hover.</summary>
    /// <param name="title">The step's name.</param>
    /// <param name="line">The line under it, for example its key.</param>
    /// <param name="icon">The icon's resource key.</param>
    /// <param name="ink">The colour's resource key.</param>
    /// <param name="tip">What the step does.</param>
    /// <returns>The node's content.</returns>
    private FrameworkElement Ghost(string title, string line, string icon, string ink, string tip)
    {
        var frame = new Grid { ToolTip = tip, Background = Brushes.Transparent };
        frame.Children.Add(new Rectangle
        {
            RadiusX = 10, RadiusY = 10, Stroke = Brush("Brush.BorderHover"), StrokeThickness = 1.5,
            StrokeDashArray = [3, 3],
        });
        var content = NodeContent(title, line, icon, ink, dashed: true, round: icon != "Icon.Kind.Bank");
        content.Margin = new Thickness(10, 0, 12, 0);
        content.VerticalAlignment = VerticalAlignment.Center;
        frame.Children.Add(content);
        return frame;
    }

    /// <summary>Makes a node's inside: the icon in its frame, the title and the line under it.</summary>
    /// <param name="title">The title.</param>
    /// <param name="line">The line under it.</param>
    /// <param name="icon">The icon's resource key.</param>
    /// <param name="ink">The colour's resource key.</param>
    /// <param name="dashed">Whether the icon's frame is dashed.</param>
    /// <param name="round">Whether the icon's frame is round (an actor) or square (a service or a file).</param>
    /// <returns>The inside.</returns>
    private DockPanel NodeContent(string title, string line, string icon, string ink, bool dashed, bool round)
    {
        var colour = Brush(ink);
        var iconFrame = new Grid { Style = (Style)FindResource("Grid.NodeIcon") };
        iconFrame.Children.Add(new Rectangle
        {
            RadiusX = round ? 13 : 7, RadiusY = round ? 13 : 7, Stroke = colour, StrokeThickness = 1.5,
            StrokeDashArray = dashed ? [2, 2] : null,
        });
        iconFrame.Children.Add(new GridIcon
        {
            Size = 15, Data = (Geometry)FindResource(icon), Foreground = colour,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(iconFrame, Dock.Left);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("Brush.Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(new TextBlock
        {
            Text = line, FontSize = 12, Foreground = Brush("Brush.TextDim"), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var panel = new DockPanel();
        panel.Children.Add(iconFrame);
        panel.Children.Add(words);
        return panel;
    }

    /// <summary>Makes the course as five dots with names, the first (Ärende) where the user stands.</summary>
    /// <returns>The dots.</returns>
    private List<FrameworkElement> CourseDots()
    {
        var dots = new List<FrameworkElement>();
        for (var index = 0; index < GraphStartViewModel.Course.Count; index++)
        {
            if (index > 0)
            {
                dots.Add(new Line
                {
                    X2 = 44, Stroke = Brush("Brush.BorderHover"), StrokeThickness = 1.5, StrokeDashArray = [0, 3],
                    StrokeDashCap = PenLineCap.Round, Margin = new Thickness(-14, 5, -14, 0),
                });
            }

            var current = index == 0;
            var dot = new StackPanel { Width = 76 };
            dot.Children.Add(new Ellipse
            {
                Width = 11, Height = 11, StrokeThickness = 1.5,
                Stroke = current ? Brush("Brush.Accent") : Brush("Brush.BorderHover"),
                StrokeDashArray = current ? null : [2, 2],
            });
            dot.Children.Add(new TextBlock
            {
                Text = GraphStartViewModel.Course[index], FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = current ? Brush("Brush.Text") : Brush("Brush.TextDim"),
            });
            dots.Add(dot);
        }

        return dots;
    }

    /// <summary>Finds a brush in the theme.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The brush.</returns>
    private Brush Brush(string key) => (Brush)FindResource(key);
}
