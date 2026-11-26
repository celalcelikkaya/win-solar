using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinSolar.App.Interop;
using WinSolar.App.Styling;
using WinSolar.Core.Scheduling;

namespace WinSolar.App.Notify;

/// <summary>
/// Win Solar's own notification card (spec "Heads-up before a switch"): small glass card, optional countdown ring,
/// quiet text buttons revealed on hover, never takes focus.
/// </summary>
internal sealed class NoticeCard : Window
{
    private const double RingSize = 40;
    private readonly Grid root;
    private readonly TranslateTransform move = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly TextBlock title;
    private readonly TextBlock line;
    private readonly Path? ring;
    private readonly FrameworkElement? actions;
    private readonly Button closeButton;
    private readonly bool quiet;
    private DispatcherTimer? timer;

    public NoticeCard(Theme glyph, string titleText, string lineText, bool withRing, bool quiet, params (string Label, bool Primary, Action Do)[] buttons)
    {
        this.quiet = quiet;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 320 + 32;
        FontSize = 13;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "Ink");

        // icon: glyph, optionally inside an emptying ring
        var iconGrid = new Grid { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
        var iconBg = new Ellipse();
        iconBg.SetResourceReference(Shape.FillProperty, "Card2");
        iconGrid.Children.Add(iconBg);
        if (withRing)
        {
            var track = new Ellipse { StrokeThickness = 2.5, Margin = new Thickness(1.25) };
            track.SetResourceReference(Shape.StrokeProperty, "Line");
            iconGrid.Children.Add(track);
            ring = new Path { StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ring.SetResourceReference(Shape.StrokeProperty, "Accent");
            iconGrid.Children.Add(ring);
        }

        var glyphEl = Ui.Glyph(glyph, 18);
        glyphEl.HorizontalAlignment = HorizontalAlignment.Center;
        glyphEl.VerticalAlignment = VerticalAlignment.Center;
        iconGrid.Children.Add(glyphEl);

        title = new TextBlock { Text = titleText, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
        line = Ui.Text(lineText, 12, null, "Ink2");
        var texts = new StackPanel { Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        if (lineText.Length > 0) texts.Children.Add(line);

        closeButton = Ui.Button("IconButton", Ui.IconInherit(Icons.Close, 12), () => Dismiss());
        closeButton.Width = closeButton.Height = 26;
        closeButton.VerticalAlignment = VerticalAlignment.Top;
        closeButton.Opacity = 0;

        var top = new DockPanel();
        DockPanel.SetDock(iconGrid, Dock.Left);
        DockPanel.SetDock(closeButton, Dock.Right);
        top.Children.Add(iconGrid);
        top.Children.Add(closeButton);
        top.Children.Add(texts);

        var stack = new StackPanel();
        stack.Children.Add(top);
        if (buttons.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(44, 6, 0, 0) };
            foreach (var (label, primary, act) in buttons)
                row.Children.Add(Ui.Button(primary ? "TextActionPrimary" : "TextAction", label, () => { act(); Dismiss(); }));
            actions = row;
            if (quiet) row.Visibility = Visibility.Collapsed;
            stack.Children.Add(row);
        }

        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 8, 10), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Bg");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        var shadow = new Border { CornerRadius = new CornerRadius(16), Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.25 } };
        shadow.SetResourceReference(Border.BackgroundProperty, "Bg");

        root = new Grid { Margin = new Thickness(16), RenderTransformOrigin = new Point(0.5, 0.5) };
        root.RenderTransform = new TransformGroup { Children = new TransformCollection { scale, move } };
        root.Children.Add(shadow);
        root.Children.Add(card);
        Content = root;

        MouseEnter += (_, _) => Reveal(true);
        MouseLeave += (_, _) => Reveal(false);
        SourceInitialized += (_, _) => Native.MakeNoActivate(new WindowInteropHelper(this).Handle);
        SetRing(1);
    }

    /// <summary>Raised when the card goes away for any reason. Argument: true when it tucked into the tray.</summary>
    public event Action<bool>? Gone;

    public bool IsGone { get; private set; }

    /// <summary>Called when the card should leave on its own after a while (null = stays).</summary>
    public TimeSpan? AutoHide { get; init; }

    /// <summary>Quiet cards tuck into the tray instead of fading out.</summary>
    public bool TuckWhenIdle { get; init; }

    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        root.Opacity = 0;
        Show();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease });
        Arm(AutoHide);
    }

    public void MoveTo(double left, double top)
    {
        Left = left;
        Top = top;
    }

    public void SetTitle(string text) => title.Text = text;

    /// <summary>Fraction of the 5 minutes left, 1 = full ring.</summary>
    public void SetRing(double fraction)
    {
        if (ring is null) return;
        fraction = Math.Clamp(fraction, 0, 1);
        const double r = RingSize / 2 - 1.25, c = RingSize / 2;
        if (fraction >= 0.999)
        {
            ring.Data = new EllipseGeometry(new Point(c, c), r, r);
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r) };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        ring.Data = new PathGeometry([figure]);
    }

    public void Dismiss() => Leave(tuck: false);

    private void Arm(TimeSpan? after)
    {
        timer?.Stop();
        if (after is null) return;
        var t = new DispatcherTimer { Interval = after.Value };
        t.Tick += (_, _) => { t.Stop(); Leave(tuck: TuckWhenIdle); };
        timer = t;
        t.Start();
    }

    private void Reveal(bool on)
    {
        closeButton.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(180)));
        if (quiet && actions is not null) actions.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) timer?.Stop();
        else if (AutoHide is not null) Arm(TimeSpan.FromSeconds(4));
    }

    private void Leave(bool tuck)
    {
        if (IsGone) return;
        IsGone = true;
        timer?.Stop();
        var duration = TimeSpan.FromMilliseconds(tuck ? 700 : 220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();
        if (tuck)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.25, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, duration) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(60, duration) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(90, duration) { EasingFunction = ease });
        }
        else
        {
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, duration) { EasingFunction = ease });
        }

        root.BeginAnimation(OpacityProperty, fade);
        Gone?.Invoke(tuck);
    }
}
