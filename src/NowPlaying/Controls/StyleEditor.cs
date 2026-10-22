using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NowPlaying.Core;

namespace NowPlaying.Controls;

/// <summary>
/// The style rows shared by the widget and the overlays: color (glass, cover, custom), size, corners, text, the parts
/// shown, the visualizer. Each change is written into the style at once and <see cref="Changed"/> is raised.
/// </summary>
internal sealed class StyleEditor : StackPanel
{
    // The swatches of the mock-up.
    private static readonly string[] Swatches = ["#5B6CF0", "#E5484D", "#F5A524", "#30A46C", "#12A594", "#8E4EC6", "#1B1B1F", "#F2F2F2"];

    private readonly SurfaceStyle _style;
    private readonly bool _glassOpacity;
    private readonly Border _customRow;
    private readonly Border _opacityRow;
    private readonly StackPanel _opacity;
    private readonly TextBox _hex;
    private readonly WrapPanel _swatches = new() { VerticalAlignment = VerticalAlignment.Center };

    // The choices of the floating widget: its glass blurred (frosted) or not (clear).
    private enum Look
    {
        Frosted,
        Clear,
        Cover,
        Custom,
    }

    /// <param name="defaults">The default style, given back by a double click on a slider.</param>
    /// <param name="elements">The parts that can be hidden: their label, whether shown, and how to change it.</param>
    /// <param name="glassOpacity">The opacity applies to the glass too (the veil of the floating widget).</param>
    /// <param name="blur">
    /// The floating widget: the glass offered twice, blurred ("Glass") or not ("Transparent"), in one list with the
    /// other colors; how to read and set the blur.
    /// </param>
    /// <param name="afterColor">A row of that widget shown right under the color and its opacity (the shadow of the floating widget).</param>
    /// <param name="colorHint">The key of the text under "Color", when that surface needs its own.</param>
    /// <param name="defaultOpacity">The opacity a double click gives back, when it depends on the look chosen.</param>
    public StyleEditor(SurfaceStyle style, SurfaceStyle defaults, bool overlay, IEnumerable<(string Label, Func<bool> Get, Action<bool> Set)> elements, bool withVisualizer,
        bool glassOpacity = false, (Func<bool> Get, Action<bool> Set)? blur = null, UIElement? afterColor = null, string? colorHint = null,
        Func<int>? defaultOpacity = null)
    {
        _style = style;
        _glassOpacity = glassOpacity;

        UIElement colors;
        if (blur is { } frost)
        {
            var current = style.ColorMode switch
            {
                ColorMode.Cover => Look.Cover,
                ColorMode.Custom => Look.Custom,
                _ => frost.Get() ? Look.Frosted : Look.Clear,
            };
            colors = Segmented.Of(
                [
                    (Look.Frosted, Strings.Get("ColorModeFrosted")),
                    (Look.Clear, Strings.Get("ColorModeGlass")),
                    (Look.Cover, Strings.Get("ColorModeCover")),
                    (Look.Custom, Strings.Get("ColorModeCustom")),
                ],
                current, look =>
                {
                    style.ColorMode = look switch { Look.Cover => ColorMode.Cover, Look.Custom => ColorMode.Custom, _ => ColorMode.Glass };
                    frost.Set(look == Look.Frosted);
                    ShowColorRows();
                    LookChanged?.Invoke(this, EventArgs.Empty);
                    Raise();
                });
        }
        else
            colors = Segmented.Of(
                [
                    (ColorMode.Glass, Strings.Get(overlay ? "ColorModeFrosted" : "ColorModeGlass")),
                    (ColorMode.Cover, Strings.Get("ColorModeCover")),
                    (ColorMode.Custom, Strings.Get("ColorModeCustom")),
                ],
                style.ColorMode, mode =>
                {
                    style.ColorMode = mode;
                    ShowColorRows();
                    Raise();
                });
        Children.Add(Ui.Card("", Strings.Get("StyleColor"), Strings.Get(colorHint ?? (blur is null ? "StyleColorHint" : "FloatingColorHint")), colors));

        foreach (string color in Swatches)
        {
            var swatch = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 6, 0), Tag = color, Cursor = System.Windows.Input.Cursors.Hand,
                Template = SwatchTemplate(color),
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, color);
            swatch.Click += (_, _) => SetCustom(color);
            _swatches.Children.Add(swatch);
        }
        _hex = new TextBox { Width = 92, Text = style.CustomColor, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(_hex, Strings.Get("StyleCustomColor"));
        _hex.TextChanged += (_, _) =>
        {
            if (Rgb.TryParse(_hex.Text, out var rgb) && rgb.ToHex() != _style.CustomColor)
            {
                _style.CustomColor = rgb.ToHex();
                MarkSwatch();
                Raise();
            }
        };
        var customControls = new StackPanel { Orientation = Orientation.Horizontal };
        customControls.Children.Add(_swatches);
        customControls.Children.Add(_hex);
        _customRow = Ui.SubCard(Strings.Get("StyleCustomColor"), customControls);
        Children.Add(_customRow);

        _opacity = Ui.Slider(20, 100, style.Opacity, defaultOpacity ?? (() => defaults.Opacity), "%", v => { style.Opacity = v; Raise(); },
            Strings.Get("StyleOpacity"));
        _opacityRow = Ui.SubCard(Strings.Get("StyleOpacity"), _opacity);
        Children.Add(_opacityRow);
        if (afterColor is not null)
            Children.Add(afterColor);

        Children.Add(Ui.Card("", Strings.Get("StyleWidth"), null,
            Ui.Slider(style.WidthRange.Min, style.WidthRange.Max, style.Width, defaults.Width, "px", v => { style.Width = v; Raise(); }, Strings.Get("StyleWidth"))));
        Children.Add(Ui.Card("", Strings.Get("StyleCorners"), null,
            Ui.Slider(style.RadiusRange.Min, style.RadiusRange.Max, style.CornerRadius, defaults.CornerRadius, "px", v => { style.CornerRadius = v; Raise(); }, Strings.Get("StyleCorners"))));

        var text = new StackPanel { Orientation = Orientation.Horizontal };
        var fonts = SurfaceStyle.Fonts.Contains(style.Font) ? SurfaceStyle.Fonts : [style.Font, .. SurfaceStyle.Fonts];
        var fontBox = Ui.Choice(fonts.Select(f => (f, f == SurfaceStyle.DefaultFont ? "Segoe UI Variable" : f)), style.Font,
            f => { style.Font = f; Raise(); }, 180);
        foreach (ComboBoxItem item in fontBox.Items)
            item.FontFamily = new FontFamily((string)item.Tag + ", Segoe UI");
        System.Windows.Automation.AutomationProperties.SetName(fontBox, Strings.Get("StyleFont"));
        text.Children.Add(fontBox);
        var size = Ui.Slider(style.FontSizeRange.Min, style.FontSizeRange.Max, style.FontSize, defaults.FontSize, "px", v => { style.FontSize = v; Raise(); },
            Strings.Get("StyleFontSize"), 120);
        size.Margin = new Thickness(14, 0, 0, 0);
        text.Children.Add(size);
        Children.Add(Ui.Card("", Strings.Get("StyleText"), Strings.Get("StyleTextHint"), text));

        var parts = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, get, set) in elements)
            parts.Children.Add(Ui.LabeledToggle(label, get(), on => { set(on); Raise(); }));
        Children.Add(Ui.Card("", Strings.Get("StyleElements"), null, parts));

        if (withVisualizer)
            Children.Add(Ui.Card("", Strings.Get("StyleVisualizer"), Strings.Get("StyleVisualizerHint"),
                Segmented.Of(
                    [
                        (VisualizerMode.Spectrum, Strings.Get("VisualizerSpectrum")),
                        (VisualizerMode.Animation, Strings.Get("VisualizerAnimation")),
                    ],
                    style.Visualizer, mode => { style.Visualizer = mode; Raise(); })));

        ShowColorRows();
        MarkSwatch();
    }

    public event EventHandler? Changed;

    /// <summary>Another look chosen (the floating widget): its page gives it its own opacity and shadow.</summary>
    public event EventHandler? LookChanged;

    /// <summary>Shows the opacity of the style, changed outside the editor.</summary>
    public void ShowOpacity() => Ui.SetSlider(_opacity, _style.Opacity);

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private void SetCustom(string color)
    {
        _style.CustomColor = color;
        _hex.Text = color; // raises through TextChanged only when different
        MarkSwatch();
        Raise();
    }

    private void ShowColorRows()
    {
        _customRow.Visibility = _style.ColorMode == ColorMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        _opacityRow.Visibility = _style.ColorMode == ColorMode.Glass && !_glassOpacity ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MarkSwatch()
    {
        foreach (Button swatch in _swatches.Children)
            swatch.Opacity = string.Equals((string)swatch.Tag, _style.CustomColor, StringComparison.OrdinalIgnoreCase) ? 1 : 0.55;
    }

    private static ControlTemplate SwatchTemplate(string color)
    {
        var template = new ControlTemplate(typeof(Button));
        var ellipse = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        ellipse.SetValue(System.Windows.Shapes.Shape.FillProperty, new BrushConverter().ConvertFromString(color));
        ellipse.SetValue(System.Windows.Shapes.Shape.StrokeProperty, new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)));
        ellipse.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.0);
        template.VisualTree = ellipse;
        return template;
    }
}
