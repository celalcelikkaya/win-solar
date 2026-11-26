using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinSolar.Core.Scheduling;

namespace WinSolar.App.Styling;

/// <summary>Swaps the light/dark brush dictionary so every DynamicResource follows the applied Windows theme.</summary>
public static class ThemeManager
{
    private static Theme? current;

    public static void Apply(Theme theme)
    {
        if (current == theme) return;
        current = theme;
        var name = theme == Theme.Light ? "Light" : "Dark";
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/WinSolar;component/Theme/{name}.xaml", UriKind.Absolute) };
        System.Windows.Application.Current.Resources.MergedDictionaries[0] = dict;
    }
}

/// <summary>Stroke icons on a 24×24 grid (Lucide shapes, as in the demo).</summary>
public static class Icons
{
    private static Geometry G(string d)
    {
        var g = Geometry.Parse(d);
        g.Freeze();
        return g;
    }

    public static readonly Geometry Pin = G("M20,10 C20,16 12,22 12,22 C12,22 4,16 4,10 A8,8 0 0 1 20,10 Z M9,10 A3,3 0 1 0 15,10 A3,3 0 1 0 9,10");
    public static readonly Geometry Chevron = G("M6,9 L12,15 L18,9");
    public static readonly Geometry Sliders = G("M21,4 H14 M10,4 H3 M21,12 H12 M8,12 H3 M21,20 H16 M12,20 H3 M14,2 V6 M8,10 V14 M16,18 V22");
    public static readonly Geometry Pushpin = G("M12,17 V22 M9,3 H15 L14,10 L17,13 V15 H7 V13 L10,10 Z");
    public static readonly Geometry Sun = G("M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12 M12,2 V4 M12,20 V22 M4.93,4.93 L6.34,6.34 M17.66,17.66 L19.07,19.07 M2,12 H4 M20,12 H22 M6.34,17.66 L4.93,19.07 M19.07,4.93 L17.66,6.34");
    public static readonly Geometry Moon = G("M12,3 A6,6 0 0 0 21,12 A9,9 0 1 1 12,3 Z");
    public static readonly Geometry Auto = G("M12,3 L13.9,8.1 L19,10 L13.9,11.9 L12,17 L10.1,11.9 L5,10 L10.1,8.1 Z");
    public static readonly Geometry Sunrise = G("M12,2 V10 M4.93,10.93 L6.34,12.34 M2,18 H4 M20,18 H22 M19.07,10.93 L17.66,12.34 M22,22 H2 M8,6 L12,2 L16,6 M16,18 A4,4 0 0 0 8,18");
    public static readonly Geometry Sunset = G("M12,10 V2 M4.93,10.93 L6.34,12.34 M2,18 H4 M20,18 H22 M19.07,10.93 L17.66,12.34 M22,22 H2 M16,6 L12,10 L8,6 M16,18 A4,4 0 0 0 8,18");
    public static readonly Geometry Timer = G("M4,14 A8,8 0 1 0 20,14 A8,8 0 1 0 4,14 M12,10 V14 M10,2 H14");
    public static readonly Geometry Clock = G("M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 M12,7 V12 L15,14");
    public static readonly Geometry Bell = G("M6,8 A6,6 0 0 1 18,8 C18,15 21,17 21,17 H3 C3,17 6,15 6,8 M10.3,21 A1.94,1.94 0 0 0 13.7,21");
    public static readonly Geometry Close = G("M18,6 L6,18 M6,6 L18,18");
    public static readonly Geometry Expand = G("M8,3 H5 A2,2 0 0 0 3,5 V8 M21,8 V5 A2,2 0 0 0 19,3 H16 M3,16 V19 A2,2 0 0 0 5,21 H8 M16,21 H19 A2,2 0 0 0 21,19 V16");
    public static readonly Geometry Search = G("M4,11 A7,7 0 1 0 18,11 A7,7 0 1 0 4,11 M21,21 L16.7,16.7");
    public static readonly Geometry Locate = G("M5,12 A7,7 0 1 0 19,12 A7,7 0 1 0 5,12 M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22");
    public static readonly Geometry Power = G("M12,2 V12 M18.36,6.64 A9,9 0 1 1 5.64,6.64");

    public static Geometry For(Theme theme) => theme == Theme.Light ? Sun : Moon;
}

/// <summary>Small factory helpers so code-built views read like markup.</summary>
public static class Ui
{
    public static FrameworkElement Icon(Geometry data, double size = 16, string brushKey = "Ink2")
    {
        var path = new Path
        {
            Data = data,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, brushKey);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>An icon whose stroke follows the parent's Foreground (for buttons).</summary>
    public static FrameworkElement IconInherit(Geometry data, double size = 16)
    {
        var path = new Path
        {
            Data = data,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1),
        });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, string brushKey = "Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    public static Border Card(UIElement child, Thickness? padding = null, double radius = 8)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(12, 10, 12, 10) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }

    public static Button Button(string styleKey, object content, Action onClick)
    {
        var b = new Button { Content = content };
        b.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(Orientation orientation, double gap, params UIElement[] children)
    {
        var p = new StackPanel { Orientation = orientation };
        for (var i = 0; i < children.Length; i++)
        {
            if (i > 0 && children[i] is FrameworkElement fe)
                fe.Margin = orientation == Orientation.Horizontal ? new Thickness(gap, 0, 0, 0) : new Thickness(0, gap, 0, 0);
            p.Children.Add(children[i]);
        }

        return p;
    }

    /// <summary>A sun or moon disc used in the hero, cards and OSD.</summary>
    public static FrameworkElement Glyph(Theme theme, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (theme == Theme.Light)
        {
            canvas.Children.Add(new Path { Data = Icons.Sun, Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00)), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            var core = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00)) };
            Canvas.SetLeft(core, 8);
            Canvas.SetTop(core, 8);
            canvas.Children.Add(core);
        }
        else
        {
            canvas.Children.Add(new Path { Data = Icons.Moon, Fill = new SolidColorBrush(Color.FromRgb(0xB9, 0xC6, 0xFF)) });
        }

        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
