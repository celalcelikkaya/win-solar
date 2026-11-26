using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.App.SettingsUi;
using WinSolar.App.Styling;
using WinSolar.Core;
using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Light;
using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Sun;

namespace WinSolar.App.Flyout;

/// <summary>
/// The tray flyout: opens next to the taskbar on whichever edge it is, slides in from that edge, hides when you click
/// elsewhere unless pinned. Content mirrors the approved demo.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private const double ContentWidth = 360;
    private const double ArcRadius = 92;
    private static readonly double ArcLength = Math.PI * ArcRadius;
    private static readonly Brush White = Frozen(Colors.White);

    private readonly EngineHost host;
    private readonly Grid root;
    private readonly ScrollViewer mainScroll;
    private readonly DispatcherTimer minuteTimer;

    // header
    private readonly TextBlock placeName;
    private readonly Button pinButton;

    // hero
    private readonly Dictionary<Phase, Border> skies = [];
    private readonly ContentControl envContent = new();
    private Border envChip = null!;
    private readonly TextBlock heroClock;
    private readonly Canvas stars = new();
    private readonly Path progressArc;
    private readonly TranslateTransform bodyPos = new();
    private readonly Canvas sunBody;
    private readonly Canvas moonBody;
    private readonly ContentControl prevTime = new();
    private readonly ContentControl nextTime = new();
    private readonly TextBlock bigText;
    private readonly TextBlock captionText;
    private double shownProgress;
    private double targetProgress;
    private Phase? shownPhase;

    // body
    private readonly ContentControl bannerHost = new();
    private string bannerKey = "";
    private readonly Border segThumb;
    private readonly TranslateTransform segThumbPos = new();
    private readonly Button[] segButtons = new Button[3];
    private readonly TextBlock hint;
    private readonly (ContentControl Icon, TextBlock Label, TextBlock Value, TextBlock Sub)[] tiles = new (ContentControl, TextBlock, TextBlock, TextBlock)[4];
    private readonly Canvas dayCanvas = new();
    private readonly TextBlock daysToggleText;
    private readonly RotateTransform daysChevron = new();
    private readonly Grid daysGrid = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private string daysKey = "";
    private bool daysOpen;

    // sheet
    private readonly Grid sheetLayer = new() { Visibility = Visibility.Collapsed };
    private readonly Border sheetBackdrop;
    private readonly Border sheet;
    private readonly TranslateTransform sheetSlide = new();
    private readonly TextBlock sheetTitle;
    private readonly StackPanel sheetBody = new() { Margin = new Thickness(12, 2, 12, 16) };
    private readonly ScrollViewer sheetScroll;

    private DateTime lastHidden = DateTime.MinValue;
    private bool hiding;
    private Edge edge = Edge.Bottom;

    public FlyoutWindow(EngineHost host)
    {
        this.host = host;
        // An opaque window that DWM rounds, borders and shadows: a per-pixel transparent window with a WPF drop
        // shadow had to be re-rendered and copied back from the GPU on every frame of the slide (~28 fps measured).
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        SetResourceReference(BackgroundProperty, "Bg");
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = ContentWidth;
        FontSize = 13;
        UseLayoutRounding = true;
        Title = "Win Solar";
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "Ink");

        // ----- header -----
        placeName = Ui.Text("", 13, FontWeights.SemiBold);
        placeName.MaxWidth = 200;
        var placeButton = Ui.Button("FlatButton",
            Ui.Row(Orientation.Horizontal, 6, Ui.IconInherit(Icons.Pin, 15), placeName, Ui.IconInherit(Icons.Chevron, 12)),
            () => OpenSheet("Location", body => SettingsUi.LocationSheet.Build(body, Context)));
        placeButton.HorizontalAlignment = HorizontalAlignment.Left;
        pinButton = Ui.Button("IconButton", Ui.IconInherit(Icons.Pushpin, 16), TogglePin);
        pinButton.ToolTip = "Keep open when clicking outside";
        var settingsButton = Ui.Button("IconButton", Ui.IconInherit(Icons.Sliders, 17), () => ShowSettings());
        settingsButton.ToolTip = "Settings";
        var header = new Grid { Margin = new Thickness(8, 8, 8, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(placeButton);
        Grid.SetColumn(pinButton, 1);
        header.Children.Add(pinButton);
        Grid.SetColumn(settingsButton, 2);
        header.Children.Add(settingsButton);

        // ----- hero -----
        var hero = BuildHero(out heroClock, out progressArc, out sunBody, out moonBody, out bigText, out captionText);

        // ----- mode switch -----
        var segGrid = new Grid();
        for (var i = 0; i < 3; i++) segGrid.ColumnDefinitions.Add(new ColumnDefinition());
        segThumb = new Border { CornerRadius = new CornerRadius(6), RenderTransform = segThumbPos };
        segThumb.SetResourceReference(Border.BackgroundProperty, "Accent");
        segGrid.Children.Add(segThumb);
        var modes = new[] { (Mode.Auto, "Auto", Icons.Auto), (Mode.Light, "Light", Icons.Sun), (Mode.Dark, "Dark", Icons.Moon) };
        for (var i = 0; i < 3; i++)
        {
            var (mode, label, icon) = modes[i];
            var b = Ui.Button("FlatButton", Ui.Row(Orientation.Horizontal, 6, Ui.IconInherit(icon, 15), new TextBlock { Text = label, FontWeight = FontWeights.SemiBold }),
                () => host.Engine.SetMode(mode));
            b.Padding = new Thickness(6, 8, 6, 8);
            Grid.SetColumn(b, i);
            segGrid.Children.Add(b);
            segButtons[i] = b;
        }

        segGrid.SizeChanged += (_, _) => PlaceThumb(animate: false);
        var seg = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(3), Child = segGrid };
        seg.SetResourceReference(Border.BackgroundProperty, "Card2");
        seg.SetResourceReference(Border.BorderBrushProperty, "Line");
        hint = Ui.Text("", 12, null, "Ink2");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(0, -4, 0, 0);

        // ----- tiles -----
        var tileGrid = new Grid();
        tileGrid.ColumnDefinitions.Add(new ColumnDefinition());
        tileGrid.ColumnDefinitions.Add(new ColumnDefinition());
        tileGrid.RowDefinitions.Add(new RowDefinition());
        tileGrid.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < 4; i++)
        {
            var icon = new ContentControl();
            var iconBox = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(6), Child = icon, VerticalAlignment = VerticalAlignment.Center };
            iconBox.SetResourceReference(Border.BackgroundProperty, "Card2");
            var label = Ui.Text("", 11.5, null, "Ink2");
            var value = Ui.Text("", 14, FontWeights.Bold);
            var sub = Ui.Text("", 11, FontWeights.SemiBold, "Accent");
            var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(label);
            texts.Children.Add(value);
            texts.Children.Add(sub);
            var tileContent = new DockPanel();
            tileContent.Children.Add(iconBox);
            tileContent.Children.Add(texts);
            var card = Ui.Card(tileContent, new Thickness(10, 9, 10, 9));
            card.Margin = new Thickness(i % 2 == 0 ? 0 : 3, i < 2 ? 0 : 3, i % 2 == 0 ? 3 : 0, i < 2 ? 3 : 0);
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            tileGrid.Children.Add(card);
            tiles[i] = (icon, label, value, sub);
        }

        // ----- today bar -----
        var legend = Ui.Row(Orientation.Horizontal, 10, Legend("Light", Color.FromRgb(0xFF, 0xC3, 0x5C)), Legend("Dark", Color.FromRgb(0x33, 0x40, 0x7A)));
        var todayHead = new DockPanel();
        DockPanel.SetDock(legend, Dock.Right);
        todayHead.Children.Add(legend);
        todayHead.Children.Add(Ui.Text("Today", 12.5, FontWeights.SemiBold));
        var track = new Grid { Height = 10, Margin = new Thickness(0, 10, 0, 6) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Frozen(Color.FromRgb(0x33, 0x40, 0x7A)) });
        track.Children.Add(dayCanvas);
        dayCanvas.SizeChanged += (_, _) => { if (host.Last is { } s) RenderDay(s, DateTimeOffset.Now); };
        var ticks = new Grid();
        for (var i = 0; i < 4; i++) ticks.ColumnDefinitions.Add(new ColumnDefinition());
        string[] tickLabels = ["00", "06", "12", "18"];
        for (var i = 0; i < 4; i++)
        {
            var t = Ui.Text(tickLabels[i], 10.5, null, "Ink2");
            Grid.SetColumn(t, i);
            ticks.Children.Add(t);
        }

        var end = Ui.Text("24", 10.5, null, "Ink2");
        end.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(end, 3);
        ticks.Children.Add(end);
        var todayCard = Ui.Card(Ui.Row(Orientation.Vertical, 0, todayHead, track, ticks));

        // ----- next 7 days -----
        daysToggleText = Ui.Text("Show", 12, null, "Ink2");
        var chevron = Ui.Icon(Icons.Chevron, 14);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = daysChevron;
        var daysHeadRight = Ui.Row(Orientation.Horizontal, 4, daysToggleText, chevron);
        var daysHead = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(daysHeadRight, Dock.Right);
        daysHead.Children.Add(daysHeadRight);
        daysHead.Children.Add(Ui.Text("Next 7 days", 12.5, FontWeights.SemiBold));
        var daysButton = Ui.Button("FlatButton", daysHead, ToggleDays);
        daysButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        daysButton.Padding = new Thickness(2);
        var daysCard = Ui.Card(Ui.Row(Orientation.Vertical, 0, daysButton, daysGrid), new Thickness(10, 8, 10, 8));

        // ----- main column -----
        var main = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };
        foreach (var el in new UIElement[] { bannerHost, hero, seg, hint, tileGrid, todayCard, daysCard })
        {
            if (el is FrameworkElement fe && el != bannerHost) fe.Margin = new Thickness(0, main.Children.Count == 0 ? 0 : 10, 0, 0);
            main.Children.Add(el);
        }

        hint.Margin = new Thickness(0, 6, 0, 0);
        mainScroll = new ScrollViewer { Content = main };
        mainScroll.SetResourceReference(StyleProperty, "ThinScroll");

        var content = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(mainScroll);

        // ----- sheet -----
        sheetBackdrop = new Border();
        sheetBackdrop.SetResourceReference(Border.BackgroundProperty, "Backdrop");
        sheetBackdrop.MouseLeftButtonDown += (_, _) => CloseSheet();
        sheetTitle = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        sheetTitle.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        var sheetClose = Ui.Button("IconButton", Ui.IconInherit(Icons.Close, 16), CloseSheet);
        var sheetHead = new DockPanel { Margin = new Thickness(16, 12, 10, 6) };
        DockPanel.SetDock(sheetClose, Dock.Right);
        sheetHead.Children.Add(sheetClose);
        sheetHead.Children.Add(sheetTitle);
        sheetScroll = new ScrollViewer { Content = sheetBody };
        sheetScroll.SetResourceReference(StyleProperty, "ThinScroll");
        var sheetContent = new DockPanel();
        DockPanel.SetDock(sheetHead, Dock.Top);
        sheetContent.Children.Add(sheetHead);
        sheetContent.Children.Add(sheetScroll);
        sheet = new Border
        {
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = sheetContent,
            RenderTransform = sheetSlide,
        };
        sheet.SetResourceReference(Border.BackgroundProperty, "Bg");
        sheetLayer.Children.Add(sheetBackdrop);
        sheetLayer.Children.Add(sheet);

        // ----- frame (corners, border and shadow come from DWM) -----
        root = new Grid();
        root.Children.Add(content);
        root.Children.Add(sheetLayer);
        Content = root;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.RoundCorners(hwnd);
            Native.MakeLayered(hwnd);
            Native.SetAlpha(hwnd, 0);
        };

        minuteTimer = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        minuteTimer.Stop();

        host.Changed += Render;
        Deactivated += (_, _) => { if (!Pinned) HideFlyout(); };
        PreviewKeyDown += OnKey;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { CompositionTarget.Rendering += Animate; minuteTimer.Start(); StartStars(); }
            else { CompositionTarget.Rendering -= Animate; minuteTimer.Stop(); StopStars(); }
        };
    }

    private bool Pinned => !host.Store.Settings.HideOnClickOutside;

    /// <summary>Everything the sheets need.</summary>
    public SheetContext Context { get; set; } = null!;

    public bool IsShownOnRight => IsVisible && edge != Edge.Left;

    public double VisibleWidth => ContentWidth;

    // ---------------- show / hide ----------------

    public void Toggle()
    {
        if (IsVisible && !hiding) HideFlyout();
        else if ((DateTime.UtcNow - lastHidden).TotalMilliseconds > 300) ShowFlyout();
    }

    public void ShowFlyout()
    {
        if (IsVisible && !hiding) { Activate(); return; }
        var wasVisible = IsVisible; // re-opened while the hide motion was still running
        hiding = false;
        Refresh();
        shownProgress = 0; // the sun sweeps along the arc on every open (after the slide, see Animate)
        DrawBody();
        var (e, wa) = TaskbarEdge.Get();
        edge = e;
        mainScroll.MaxHeight = Math.Max(200, wa.Height - 2 * Gap - 60);
        if (!wasVisible)
        {
            if (hwnd != IntPtr.Zero) Native.SetAlpha(hwnd, 0);
            Show();
        }

        UpdateLayout(); // lay out once before the first frame, not during the motion
        anchor = new Point(
            edge == Edge.Left ? wa.Left + Gap : wa.Right - ActualWidth - Gap,
            edge == Edge.Top ? wa.Top + Gap : wa.Bottom - ActualHeight - Gap);
        if (TryFindResource("Line") is SolidColorBrush line && TryFindResource("Bg") is SolidColorBrush bg)
        {
            // DWM ignores alpha: blend the translucent line colour over the background ourselves.
            byte Mix(byte over, byte under) => (byte)Math.Round(under + (over - under) * line.Color.A / 255.0);
            Native.SetBorderColor(hwnd, Color.FromRgb(Mix(line.Color.R, bg.Color.R), Mix(line.Color.G, bg.Color.G), Mix(line.Color.B, bg.Color.B)));
        }
        Activate();
        PlaceThumb(animate: false);
        StartMotion(showing: true);
    }

    public void HideFlyout()
    {
        if (!IsVisible || hiding) return;
        hiding = true;
        lastHidden = DateTime.UtcNow;
        StartMotion(showing: false);
    }

    // ---------------- motion: the window itself slides and fades; nothing inside is re-rendered ----------------

    private const double Gap = 12;
    private const double SlideDistance = 28;
    private const double ShowMs = 300, FadeInMs = 200, HideMs = 150;
    private readonly System.Diagnostics.Stopwatch motionClock = new();
    private IntPtr hwnd;
    private Point anchor;
    private bool moving;
    private bool motionShows;

    private void StartMotion(bool showing)
    {
        motionShows = showing;
        motionClock.Restart();
        if (!moving) { moving = true; CompositionTarget.Rendering += StepMotion; }
        StepMotion(null, EventArgs.Empty);
    }

    private void StepMotion(object? sender, EventArgs e)
    {
        var ms = motionClock.Elapsed.TotalMilliseconds;
        double offset, alpha;
        bool done;
        if (motionShows)
        {
            var t = Math.Min(1, ms / ShowMs);
            offset = Math.Pow(1 - t, 3); // cubic ease-out
            alpha = 1 - Math.Pow(1 - Math.Min(1, ms / FadeInMs), 3);
            done = t >= 1;
        }
        else
        {
            var t = Math.Min(1, ms / HideMs);
            offset = t * t * t; // cubic ease-in
            alpha = 1 - offset;
            done = t >= 1;
        }

        var (dx, dy) = edge switch { Edge.Left => (-1, 0), Edge.Right => (1, 0), Edge.Top => (0, -1), _ => (0, 1) };
        var scale = VisualTreeHelper.GetDpi(this);
        Native.MoveWindow(hwnd,
            (int)Math.Round((anchor.X + dx * SlideDistance * offset) * scale.DpiScaleX),
            (int)Math.Round((anchor.Y + dy * SlideDistance * offset) * scale.DpiScaleY));
        Native.SetAlpha(hwnd, alpha);
        if (!done) return;

        moving = false;
        CompositionTarget.Rendering -= StepMotion;
        if (motionShows || !hiding) return;
        hiding = false;
        Hide();
        CloseSheet(animate: false);
    }

    public void ShowSettings()
    {
        ShowFlyout();
        OpenSheet("Settings", body => SettingsUi.SettingsSheet.Build(body, Context));
    }

    private void TogglePin()
    {
        host.Store.UpdateSettings(s => s with { HideOnClickOutside = !s.HideOnClickOutside });
        Refresh();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (sheetLayer.Visibility == Visibility.Visible) CloseSheet();
        else HideFlyout();
    }

    // ---------------- sheets ----------------

    public void OpenSheet(string title, Action<StackPanel> build)
    {
        sheetTitle.Text = title;
        RebuildSheet(build);
        if (sheetLayer.Visibility == Visibility.Visible) return;
        sheetLayer.Visibility = Visibility.Visible;
        sheet.MaxHeight = Math.Max(240, root.ActualHeight * 0.94);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        sheetSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(root.ActualHeight, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        sheetBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>Rebuilds the open sheet in place, keeping its scroll position.</summary>
    public void RebuildSheet(Action<StackPanel> build)
    {
        var offset = sheetScroll.VerticalOffset;
        sheetBody.Children.Clear();
        build(sheetBody);
        sheetScroll.UpdateLayout();
        sheetScroll.ScrollToVerticalOffset(offset);
    }

    public void CloseSheet() => CloseSheet(animate: true);

    private void CloseSheet(bool animate)
    {
        if (sheetLayer.Visibility != Visibility.Visible) return;
        if (!animate)
        {
            sheetLayer.Visibility = Visibility.Collapsed;
            return;
        }

        var anim = new DoubleAnimation(Math.Max(200, sheet.ActualHeight), TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        anim.Completed += (_, _) => sheetLayer.Visibility = Visibility.Collapsed;
        sheetSlide.BeginAnimation(TranslateTransform.YProperty, anim);
        sheetBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
    }

    // ---------------- rendering ----------------

    private void Refresh()
    {
        if (host.Last is { } s) Render(s);
    }

    private void Render(EngineSnapshot s)
    {
        var now = DateTimeOffset.Now;
        var settings = host.Store.Settings;
        var tz = s.Place.TimeZoneId;
        var h12 = settings.Hour12;
        string Time(DateTimeOffset? t) => t is { } v ? Format.Time(v, tz, h12) : "--:--";
        string When(DateTimeOffset t) => Format.When(t, now, tz, h12);
        var sc = s.Schedule;

        placeName.Text = s.Place.Name;
        pinButton.SetResourceReference(ForegroundProperty, Pinned ? "Accent" : "Ink2");

        // hero
        SetPhase(sc.Phase);
        var env = s.Light.Source switch
        {
            LightSource.Sensor when s.Light.Lux is { } lux => HeroRow(Ui.Icon(Icons.Bulb, 13, "OnHero"), $"{lux:0} lx"),
            LightSource.Weather when s.Light.Weather is { } w => HeroRow(Ui.Icon(w.Storm ? Icons.Storm : w.CloudCover < 25 ? Icons.Sun : Icons.Cloud, 13, "OnHero"), LightModel.WeatherLabel(w)),
            LightSource.WeatherUnavailable => HeroRow(Ui.Icon(Icons.Cloud, 13, "OnHero"), "Weather unavailable"),
            _ => null,
        };
        envContent.Content = env;
        envChip.Visibility = env is null ? Visibility.Collapsed : Visibility.Visible;
        heroClock.Text = Time(now);
        prevTime.Content = sc.Prev is { } p ? HeroRow(Ui.Icon(KindIcon(p.Kind), 14, "OnHero"), Time(p.At)) : HeroRow(null, "--:--");
        nextTime.Content = sc.Next is { } n ? HeroRow(null, Time(n.At), Ui.Icon(KindIcon(n.Kind), 14, "OnHero")) : HeroRow(null, "--:--");
        var minutesLeft = sc.Next is { } nx ? Math.Max(0, (int)Math.Ceiling((nx.At - now).TotalMinutes)) : 0;
        if (sc.Next is { } next)
        {
            bigText.Text = minutesLeft == 0 ? "Now" : Format.Duration(minutesLeft);
            var label = s.Mode == Mode.Auto ? $"Until {next.Theme}"
                : next.Kind == TransitionKind.Sunset ? "Until sunset"
                : next.Kind == TransitionKind.Sunrise ? "Until sunrise" : "Until the scheduled time";
            captionText.Text = $"{label} · {When(next.At)}";
            if (s.Mode == Mode.Auto && s.Light.Source == LightSource.Weather && next.ShiftMinutes != 0 && s.Light.Weather is { } wx)
                captionText.Text += $" · {LightModel.WeatherLabel(wx).ToLowerInvariant()} sky, {Math.Abs(next.ShiftMinutes)} min {(next.ShiftMinutes < 0 ? "before" : "after")} {(next.Kind == TransitionKind.Sunset ? "sunset" : "sunrise")}";
        }
        else
        {
            bigText.Text = sc.Today.Polar == PolarState.Day ? "Midnight sun" : "Polar night";
            captionText.Text = sc.Today.Polar == PolarState.Day ? "The sun does not set today" : "The sun does not rise today";
        }

        // room light mode: the room decides, so there is no countdown to a sun switch
        if (s.Mode == Mode.Auto && s.Hold is null && s.Light is { Source: LightSource.Sensor, Lux: { } roomLux })
        {
            var th = host.Light.Thresholds;
            bigText.Text = roomLux <= th.DarkBelow ? "Dark room" : roomLux >= th.LightAbove ? "Bright room" : "Dim room";
            captionText.Text = s.Applied == Core.Scheduling.Theme.Dark
                ? $"Light when the room is above {th.LightAbove:0} lx"
                : $"Dark when the room is below {th.DarkBelow:0} lx";
        }

        targetProgress = sc.Prev is { } a && sc.Next is { } b ? Math.Clamp((now - a.At) / (b.At - a.At), 0, 1) : 0.5;
        var showSun = sc.Theme == Core.Scheduling.Theme.Light || sc.Phase == Phase.Dawn;
        sunBody.Visibility = showSun ? Visibility.Visible : Visibility.Collapsed;
        moonBody.Visibility = showSun ? Visibility.Collapsed : Visibility.Visible;
        stars.Opacity = sc.Phase == Phase.Night ? 1 : sc.Phase == Phase.Dusk ? 0.55 : 0;

        // mode switch + hint
        for (var i = 0; i < 3; i++) segButtons[i].SetResourceReference(ForegroundProperty, (int)s.Mode == i ? "OnAccent" : "Ink2");
        PlaceThumb(animate: true);
        hint.Text = s.Mode == Mode.Auto
            ? s.Light.Source == LightSource.Sensor ? "Follows the room light"
            : settings.Source != ScheduleSource.Fixed ? "Follows sunrise and sunset" : $"Light at {Time(sc.TodayLight?.At)}, Dark at {Time(sc.TodayDark?.At)}"
            : s.ManualUntil is { } until ? $"{s.Mode} until {When(until)}, then back to Auto" : $"{s.Mode} until you change it";

        // tiles
        var t = sc.Today;
        var dayLen = t.Sunrise is { } r && t.Sunset is { } st ? Format.Duration((int)Math.Round((st - r).TotalMinutes))
            : t.Polar == PolarState.Day ? "24 h" : t.Polar == PolarState.Night ? "0 min" : "--";
        if (settings.Source != ScheduleSource.Fixed)
        {
            SetTile(0, Icons.Sunrise, "Sunrise", Time(t.Sunrise), settings.OffsetSunriseMin != 0 || sc.TodayLight?.ShiftMinutes is not (null or 0) ? $"Light at {Time(sc.TodayLight?.At)}" : "");
            SetTile(1, Icons.Sunset, "Sunset", Time(t.Sunset), settings.OffsetSunsetMin != 0 || sc.TodayDark?.ShiftMinutes is not (null or 0) ? $"Dark at {Time(sc.TodayDark?.At)}" : "");
            SetTile(2, Icons.Sun, "Solar noon", Time(t.Noon), "");
            SetTile(3, Icons.Timer, "Day length", dayLen, "");
        }
        else
        {
            SetTile(0, Icons.Sun, "Light at", Time(sc.TodayLight?.At), "Fixed time");
            SetTile(1, Icons.Moon, "Dark at", Time(sc.TodayDark?.At), "Fixed time");
            SetTile(2, Icons.Sunrise, "Sunrise", Time(t.Sunrise), "");
            SetTile(3, Icons.Sunset, "Sunset", Time(t.Sunset), "");
        }

        RenderDay(s, now);
        RenderDays(s, now, h12);
        RenderBanner(s, now, minutesLeft, When);
    }

    private void RenderBanner(EngineSnapshot s, DateTimeOffset now, int minutesLeft, Func<DateTimeOffset, string> when)
    {
        var engine = host.Engine;
        string key;
        Func<UIElement?> build;
        if (s.WaitingForFullScreen)
        {
            var due = s.Mode == Mode.Auto ? (s.Hold?.Theme ?? s.Schedule.Theme) : s.Applied;
            key = $"wait{due}";
            build = () => Banner("WarnSoft", "WarnLine", Icons.Expand, $"{due} theme is due. It switches when you leave full screen.");
        }
        else if (s.BatteryDark && s.Mode == Mode.Auto)
        {
            var percent = s.Power.Percent is { } p ? $" ({p}%)" : "";
            key = $"bat{percent}";
            build = () => Banner("AccentSoft", "AccentLine", Icons.Bolt, $"Dark while on battery{percent}. Your schedule comes back when you plug in.");
        }
        else if (s.Mode == Mode.Auto && s.Hold is null && s.Light is { Source: LightSource.Sensor, Pending: { } pending })
        {
            key = $"room{pending.Theme}";
            build = () => Banner("AccentSoft", "AccentLine", Icons.Bulb, $"The room is getting {(pending.Theme == Core.Scheduling.Theme.Dark ? "darker" : "brighter")}. Checking that it lasts…");
        }
        else if (s.HeadsUp is { } hu)
        {
            key = $"hu{hu.At}{minutesLeft}";
            build = () => Banner("AccentSoft", "AccentLine", Icons.Bell, $"Switching to {hu.Theme} in {Format.Duration(minutesLeft)}",
                ("Snooze 15 min", true, engine.Snooze), ("Skip", false, engine.Skip), ("Switch now", false, engine.SwitchNow));
        }
        else if (s.Hold is { Kind: not HoldKind.SwitchNow } hold)
        {
            var text = hold.Kind switch
            {
                HoldKind.Pause => $"Switching paused until {when(hold.Until)}",
                HoldKind.Skip => $"Switch skipped. {hold.Theme} stays until {when(hold.Until)}",
                _ => $"Switch postponed to {when(hold.Until)}",
            };
            key = $"hold{hold}";
            build = () => Banner("AccentSoft", "AccentLine", Icons.Clock, text, (hold.Kind == HoldKind.Pause ? "Resume now" : "Switch now", true, engine.Resume));
        }
        else
        {
            key = "";
            build = () => null;
        }

        if (key == bannerKey) return;
        bannerKey = key;
        var banner = build();
        bannerHost.Content = banner;
        bannerHost.Margin = banner is null ? new Thickness(0) : new Thickness(0, 0, 0, 10);
        if (banner is FrameworkElement fe)
        {
            fe.RenderTransform = new TranslateTransform(0, -6);
            fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
            fe.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase() });
        }
    }

    private static Border Banner(string bgKey, string lineKey, Geometry icon, string text, params (string Label, bool Primary, Action Do)[] actions)
    {
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        var top = new DockPanel();
        var iconEl = Ui.Icon(icon, 15, "Ink");
        iconEl.VerticalAlignment = VerticalAlignment.Top;
        iconEl.Margin = new Thickness(0, 1, 0, 0);
        top.Children.Add(iconEl);
        top.Children.Add(message);
        var stack = new StackPanel();
        stack.Children.Add(top);
        if (actions.Length > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (label, primary, act) in actions)
            {
                var b = Ui.Button(primary ? "AccentPill" : "Pill", label, act);
                b.Margin = new Thickness(0, 0, 6, 0);
                row.Children.Add(b);
            }

            stack.Children.Add(row);
        }

        var border = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, bgKey);
        border.SetResourceReference(Border.BorderBrushProperty, lineKey);
        return border;
    }

    private void SetTile(int i, Geometry icon, string label, string value, string sub)
    {
        var (iconHost, l, v, sb) = tiles[i];
        iconHost.Content = Ui.Icon(icon, 16, "Accent");
        l.Text = label;
        v.Text = value;
        sb.Text = sub;
        sb.Visibility = sub.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderDay(EngineSnapshot s, DateTimeOffset now)
    {
        var width = dayCanvas.ActualWidth;
        if (width <= 0) return;
        var zone = Zones.Find(s.Place.TimeZoneId);
        double Minutes(DateTimeOffset t)
        {
            var local = TimeZoneInfo.ConvertTime(t, zone);
            return local.Hour * 60 + local.Minute;
        }

        dayCanvas.Children.Clear();
        var segments = new List<(double From, double To)>();
        var sc = s.Schedule;
        if (sc.TodayLight is { } l && sc.TodayDark is { } d)
        {
            double a = Minutes(l.At), b = Minutes(d.At);
            if (a <= b) segments.Add((a, b));
            else { segments.Add((0, b)); segments.Add((a, 1440)); }
        }
        else if (sc.Today.Polar == PolarState.Day)
        {
            segments.Add((0, 1440));
        }

        foreach (var (from, to) in segments)
        {
            var rect = new Rectangle { Width = Math.Max(0, (to - from) / 1440 * width), Height = 10, RadiusX = 5, RadiusY = 5, Fill = Frozen(Color.FromRgb(0xFF, 0xC3, 0x5C)) };
            Canvas.SetLeft(rect, from / 1440 * width);
            dayCanvas.Children.Add(rect);
        }

        var marker = new Rectangle { Width = 3, Height = 18, RadiusX = 1.5, RadiusY = 1.5, StrokeThickness = 2 };
        marker.SetResourceReference(Shape.FillProperty, "Ink");
        marker.SetResourceReference(Shape.StrokeProperty, "Card");
        Canvas.SetLeft(marker, Minutes(now) / 1440 * width - 1.5);
        Canvas.SetTop(marker, -4);
        dayCanvas.Children.Add(marker);
    }

    private void ToggleDays()
    {
        daysOpen = !daysOpen;
        daysGrid.Visibility = daysOpen ? Visibility.Visible : Visibility.Collapsed;
        daysToggleText.Text = daysOpen ? "Hide" : "Show";
        daysChevron.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(daysOpen ? 180 : 0, TimeSpan.FromMilliseconds(250)));
        Refresh();
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible) return;
            var (_, wa) = TaskbarEdge.Get();
            UpdateLayout();
            if (edge == Edge.Top || moving) return;
            anchor = new Point(anchor.X, wa.Bottom - ActualHeight - Gap);
            Top = anchor.Y;
        }, DispatcherPriority.Loaded);
    }

    private void RenderDays(EngineSnapshot s, DateTimeOffset now, bool h12)
    {
        if (!daysOpen) return;
        var zone = Zones.Find(s.Place.TimeZoneId);
        var today = Zones.LocalDate(now, zone);
        var key = $"{today}{s.Place}{h12}";
        if (key == daysKey) return;
        daysKey = key;
        daysGrid.Children.Clear();
        daysGrid.RowDefinitions.Clear();
        daysGrid.ColumnDefinitions.Clear();
        daysGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var c = 0; c < 3; c++) daysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 7; i++)
        {
            daysGrid.RowDefinitions.Add(new RowDefinition());
            var date = today.AddDays(i);
            var sun = SunCalculator.ForDate(date, s.Place.Lat, s.Place.Lng);
            var weight = i == 0 ? FontWeights.Bold : FontWeights.Normal;
            void Cell(string text, int col, string brush = "Ink")
            {
                var tb = Ui.Text(text, 12, weight, brush);
                tb.Margin = new Thickness(col == 0 ? 0 : 14, 3, 0, 3);
                tb.HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                Grid.SetRow(tb, i);
                Grid.SetColumn(tb, col);
                daysGrid.Children.Add(tb);
            }

            Cell(date.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture), 0, i == 0 ? "Ink" : "Ink2");
            if (sun.Sunrise is { } r && sun.Sunset is { } st)
            {
                Cell(Format.Time(r, s.Place.TimeZoneId, h12), 1);
                Cell(Format.Time(st, s.Place.TimeZoneId, h12), 2);
                Cell(Format.Duration((int)Math.Round((st - r).TotalMinutes)), 3, "Ink2");
            }
            else
            {
                Cell(sun.Polar == PolarState.Day ? "Midnight sun" : "Polar night", 3, "Ink2");
            }
        }
    }

    private void PlaceThumb(bool animate)
    {
        var width = segThumb.ActualWidth;
        if (width <= 0 || host.Last is not { } s) return;
        var x = (int)s.Mode * width;
        if (animate) segThumbPos.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { segThumbPos.BeginAnimation(TranslateTransform.XProperty, null); segThumbPos.X = x; }
    }

    // ---------------- hero ----------------

    private Border BuildHero(out TextBlock clock, out Path progress, out Canvas sun, out Canvas moon, out TextBlock big, out TextBlock caption)
    {
        var grid = new Grid();
        AddSky(grid, Phase.Day, (0, "#3F8EF2"), (1, "#86C1FF"));
        AddSky(grid, Phase.Dawn, (0, "#4B5BC9"), (0.6, "#C9709B"), (1, "#F6A95E"));
        AddSky(grid, Phase.Dusk, (0, "#26306F"), (0.6, "#8B4F8F"), (1, "#EF8655"));
        AddSky(grid, Phase.Night, (0, "#0A1330"), (1, "#1A2A5C"));
        grid.Resources["OnHero"] = White;

        clock = new TextBlock { Foreground = White, FontWeight = FontWeights.SemiBold, FontSize = 12 };
        var clockBox = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 3, 8, 3), Background = Frozen(Color.FromArgb(0x2E, 0, 0, 0)), Child = clock, HorizontalAlignment = HorizontalAlignment.Right };
        envChip = new Border { Padding = new Thickness(2, 3, 0, 3), Child = envContent, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center }; // plain, no pill (Celal)
        // No phase chip ("Night", "Daytime"): the sky and the sun or moon already say it (Celal, 2026-11-27).
        envChip.HorizontalAlignment = HorizontalAlignment.Left;
        var top = new Grid();
        top.Children.Add(envChip);
        top.Children.Add(clockBox);

        var canvas = new Canvas { Width = 320, Height = 118 };
        for (var i = 0; i < StarSpots.Length; i++)
        {
            var (x, y, r) = StarSpots[i];
            var star = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = White, Tag = i };
            Canvas.SetLeft(star, x - r);
            Canvas.SetTop(star, y - r);
            stars.Children.Add(star);
        }

        canvas.Children.Add(stars);
        canvas.Children.Add(new Line { X1 = 12, Y1 = 106, X2 = 308, Y2 = 106, Stroke = Frozen(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
        var arc = Geometry.Parse("M 68,106 A 92,92 0 0 1 252,106");
        canvas.Children.Add(new Path { Data = arc, Stroke = Frozen(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2, StrokeDashArray = [1, 2.5], StrokeDashCap = PenLineCap.Round });
        progress = new Path { Data = arc, Stroke = White, StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashArray = [0, 1000] };
        canvas.Children.Add(progress);

        sun = new Canvas();
        sun.Children.Add(Disc(20, Color.FromArgb(0x38, 0xFF, 0xD1, 0x66)));
        sun.Children.Add(Disc(9.5, Color.FromRgb(0xFF, 0xD1, 0x66)));
        moon = new Canvas();
        moon.Children.Add(Disc(20, Color.FromArgb(0x38, 0xCF, 0xD8, 0xFF)));
        moon.Children.Add(new Path
        {
            Fill = Frozen(Color.FromRgb(0xEE, 0xF1, 0xFF)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(0, 0), 9.5, 9.5), new EllipseGeometry(new Point(5, -3), 8, 8)),
        });
        var body = new Canvas { RenderTransform = bodyPos };
        body.Children.Add(sun);
        body.Children.Add(moon);
        canvas.Children.Add(body);

        var times = new Grid { Margin = new Thickness(0, -4, 0, 0) };
        prevTime.HorizontalAlignment = HorizontalAlignment.Left;
        nextTime.HorizontalAlignment = HorizontalAlignment.Right;
        times.Children.Add(prevTime);
        times.Children.Add(nextTime);

        big = new TextBlock { Foreground = White, FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        caption = new TextBlock { Foreground = White, FontSize = 12, FontWeight = FontWeights.SemiBold, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        stack.Children.Add(top);
        stack.Children.Add(new Viewbox { Child = canvas, Margin = new Thickness(0, 2, 0, 0) });
        stack.Children.Add(times);
        stack.Children.Add(big);
        stack.Children.Add(caption);
        grid.Children.Add(stack);
        return new Border { CornerRadius = new CornerRadius(10), Child = grid, ClipToBounds = true };
    }

    private static readonly (double X, double Y, double R)[] StarSpots =
        [(30, 22, 1.3), (64, 48, 1), (102, 16, 1.1), (138, 40, 0.9), (196, 14, 1.2), (232, 44, 1), (270, 24, 1.4), (292, 58, 0.9), (46, 72, 0.8), (250, 78, 0.9)];

    private void AddSky(Grid grid, Phase phase, params (double Offset, string Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (offset, color) in stops) brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), offset));
        brush.Freeze();
        var sky = new Border { Background = brush, CornerRadius = new CornerRadius(10), Opacity = 0 };
        skies[phase] = sky;
        grid.Children.Add(sky);
    }

    private void SetPhase(Phase phase)
    {
        if (shownPhase == phase) return;
        var first = shownPhase is null;
        shownPhase = phase;
        foreach (var (p, sky) in skies)
        {
            var to = p == phase ? 1.0 : 0.0;
            if (first) { sky.Opacity = to; continue; }
            sky.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromSeconds(1.2)));
        }
    }

    private void StartStars()
    {
        foreach (UIElement star in stars.Children)
        {
            var i = (int)((FrameworkElement)star).Tag;
            star.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 0.25, TimeSpan.FromSeconds(1.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(i * 0.37 % 3),
                EasingFunction = new SineEase(),
            });
        }
    }

    private void StopStars()
    {
        foreach (UIElement star in stars.Children) star.BeginAnimation(OpacityProperty, null);
    }

    /// <summary>Eases the sun/moon along the arc (and sweeps it on open), like the demo.</summary>
    private void Animate(object? sender, EventArgs e)
    {
        if (moving) return; // the sweep starts once the window has arrived, so the slide never waits on a redraw
        if (Math.Abs(targetProgress - shownProgress) < 0.0005 && progressArc.StrokeDashArray.Count > 0) return;
        shownProgress += (targetProgress - shownProgress) * 0.07;
        DrawBody();
    }

    private void DrawBody()
    {
        var angle = Math.PI * (1 - shownProgress);
        bodyPos.X = 160 + ArcRadius * Math.Cos(angle);
        bodyPos.Y = 106 - ArcRadius * Math.Sin(angle);
        progressArc.StrokeDashArray = [shownProgress * ArcLength / progressArc.StrokeThickness, 1000];
    }

    // ---------------- helpers ----------------

    private static StackPanel HeroRow(FrameworkElement? before, string text, FrameworkElement? after = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (before is not null) { before.Margin = new Thickness(0, 0, 5, 0); before.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(before); }
        row.Children.Add(new TextBlock { Text = text, Foreground = White, FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        if (after is not null) { after.Margin = new Thickness(5, 0, 0, 0); after.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(after); }
        return row;
    }

    private static Geometry KindIcon(TransitionKind kind) => kind switch
    {
        TransitionKind.Sunset => Icons.Sunset,
        TransitionKind.Sunrise => Icons.Sunrise,
        _ => Icons.Clock,
    };

    private static Ellipse Disc(double r, Color color)
    {
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = Frozen(color) };
        Canvas.SetLeft(e, -r);
        Canvas.SetTop(e, -r);
        return e;
    }

    private static FrameworkElement Legend(string label, Color color)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Frozen(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(Ui.Text(label, 11.5, null, "Ink2"));
        return row;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
