using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NowPlaying.Controls;

/// <summary>
/// Builders for the rows of the app window, like those of the Windows 11 Settings app: a card with an icon, a title,
/// a short description and its control on the right. The pages are built in code with them, so that every row looks
/// the same.
/// </summary>
internal static class Ui
{
    public static Border Card(string glyph, string title, string? description = null, UIElement? control = null) =>
        new() { Style = (Style)Application.Current.FindResource("Card"), Child = Row(glyph, title, description, control) };

    /// <summary>The inside of a card: icon, title and description, control on the right.</summary>
    public static DockPanel Row(string glyph, string title, string? description = null, UIElement? control = null) =>
        Row(glyph, Texts(title, description), control);

    public static DockPanel Row(string glyph, StackPanel texts, UIElement? control)
    {
        var dock = new DockPanel { LastChildFill = true };
        if (glyph.Length > 0)
        {
            var icon = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("CardIcon") };
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
        }
        if (control is not null)
        {
            if (control is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(16, 0, 0, 0);
            }
            DockPanel.SetDock(control, Dock.Right);
            dock.Children.Add(control);
        }
        dock.Children.Add(texts);
        return dock;
    }

    public static StackPanel Texts(string title, string? description = null)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(description))
            texts.Children.Add(Description(description));
        return texts;
    }

    public static TextBlock Description(string text) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("CardDescription") };

    /// <summary>
    /// One card of several rows split by thin lines, as the settings of ScreenClip and PageCompose: the first row with
    /// its icon, the others aligned with its title.
    /// </summary>
    public static Border Group(params DockPanel[] rows)
    {
        var panel = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var divider = new System.Windows.Shapes.Rectangle { Height = 1 };
                divider.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "DividerStrokeColorDefaultBrush");
                panel.Children.Add(divider);
            }
            rows[i].Margin = i == 0 ? new Thickness(16, 12, 16, 12) : new Thickness(54, 12, 16, 12);
            panel.Children.Add(rows[i]);
        }
        var card = new Border { Style = (Style)Application.Current.FindResource("Card"), Child = panel, Padding = new Thickness(0) };
        return card;
    }

    /// <summary>A card under another one, indented to its title (a detail of the row above).</summary>
    public static Border SubCard(string title, UIElement control)
    {
        var card = Card("", title, null, control);
        card.Padding = new Thickness(54, 10, 16, 10);
        card.MinHeight = 52;
        return card;
    }

    public static TextBlock Section(string title) =>
        new() { Text = title, Style = (Style)Application.Current.FindResource("SectionTitle") };

    public static TextBlock PageTitle(string title) =>
        new() { Text = title, Style = (Style)Application.Current.FindResource("PageTitle") };

    public static CheckBox Toggle(bool isOn, Action<bool> changed, string automationName)
    {
        var toggle = new CheckBox { IsChecked = isOn, Style = (Style)Application.Current.FindResource("ToggleSwitch") };
        System.Windows.Automation.AutomationProperties.SetName(toggle, automationName);
        // Checked / Unchecked rather than Click: UI Automation (Narrator, the tests) toggles without a click.
        void Toggled(object sender, RoutedEventArgs e)
        {
            if (s_quiet != toggle)
                changed(toggle.IsChecked == true);
        }
        toggle.Checked += Toggled;
        toggle.Unchecked += Toggled;
        return toggle;
    }

    private static CheckBox? s_quiet;

    /// <summary>Shows a setting changed elsewhere (a menu of a widget) on its switch, without calling back.</summary>
    public static void ShowToggle(CheckBox toggle, bool isOn)
    {
        s_quiet = toggle;
        toggle.IsChecked = isOn;
        s_quiet = null;
    }

    /// <summary>A toggle with its label on its left ("Artist ◯").</summary>
    public static StackPanel LabeledToggle(string label, bool isOn, Action<bool> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 2, 0, 2) };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        row.Children.Add(text);
        row.Children.Add(Toggle(isOn, changed, label));
        return row;
    }

    /// <summary>
    /// A slider with its value written on its right ("280 px"); a double click puts it back to
    /// <paramref name="defaultValue"/> (asked by the author).
    /// </summary>
    public static StackPanel Slider(int min, int max, int value, int defaultValue, string unit, Action<int> changed, string automationName, double width = 200) =>
        Slider(min, max, value, () => defaultValue, unit, changed, automationName, width);

    /// <summary>A slider whose default value depends on another setting (the opacity of each look of the floating widget).</summary>
    public static StackPanel Slider(int min, int max, int value, Func<int> defaultValue, string unit, Action<int> changed, string automationName, double width = 200)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, Width = width, IsSnapToTickEnabled = true, TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Strings.Format("SliderResetHint", defaultValue(), unit),
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, automationName);
        slider.ToolTipOpening += (_, _) => slider.ToolTip = Strings.Format("SliderResetHint", defaultValue(), unit);
        slider.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            slider.Value = Math.Clamp(defaultValue(), min, max);
        };
        var label = new TextBlock { Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = $"{value} {unit}" };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            label.Text = $"{v} {unit}";
            changed(v);
        };
        row.Children.Add(slider);
        row.Children.Add(label);
        return row;
    }

    /// <summary>Moves the slider of a <see cref="Slider"/> row (its value is then written as when dragged).</summary>
    public static void SetSlider(StackPanel row, int value)
    {
        if (row.Children[0] is Slider slider)
            slider.Value = value;
    }

    public static ComboBox Choice<T>(IEnumerable<(T Value, string Text)> items, T selected, Action<T> changed, double width = 260)
    {
        var box = new ComboBox { MinWidth = width };
        foreach (var (value, text) in items)
        {
            box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
            if (EqualityComparer<T>.Default.Equals(value, selected))
                box.SelectedIndex = box.Items.Count - 1;
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: T value })
                changed(value);
        };
        return box;
    }

    public static Button Button(string text, Action click, bool accent = false, string glyph = "")
    {
        object content = text;
        if (glyph.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("IconText"), Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = text });
            content = row;
        }
        var button = new Button { Content = content, MinWidth = 96 };
        if (accent)
            button.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>A few exclusive choices side by side ("Left · Near the clock"), the chosen one in the accent color.</summary>
internal sealed class Segmented : Border
{
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal };
    private readonly string _group = Guid.NewGuid().ToString("N");
    private bool _setting;

    public Segmented()
    {
        SetResourceReference(BackgroundProperty, "ControlFillColorDefaultBrush");
        SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _panel;
    }

    public event EventHandler<object>? Changed;

    public static Segmented Of<T>(IEnumerable<(T Value, string Text)> items, T selected, Action<T> changed) where T : notnull
    {
        var segmented = new Segmented();
        foreach (var (value, text) in items)
        {
            var button = new RadioButton
            {
                Content = text,
                Tag = value,
                GroupName = segmented._group,
                IsChecked = EqualityComparer<T>.Default.Equals(value, selected),
                Style = (Style)Application.Current.FindResource("SegmentButton"),
            };
            button.Checked += (_, _) =>
            {
                if (!segmented._setting)
                {
                    changed(value);
                    segmented.Changed?.Invoke(segmented, value);
                }
            };
            segmented._panel.Children.Add(button);
        }
        return segmented;
    }

    public void Select(object value)
    {
        _setting = true;
        foreach (var button in _panel.Children.OfType<RadioButton>())
            button.IsChecked = Equals(button.Tag, value);
        _setting = false;
    }
}

/// <summary>A checkerboard, as image editors show transparency (behind the overlay preview).</summary>
internal static class Checkerboard
{
    public static Brush Create()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xE9, 0xE9, 0xEE)), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        var dark = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xDE));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(10, 10, 10, 10))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 20, 20), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    /// <summary>The stream scene of the mock-up: a dusk over hills, for the preview "on a scene".</summary>
    public static Brush Scene()
    {
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x1B, 0x23, 0x50), 0),
            new(Color.FromRgb(0x4A, 0x3F, 0x84), 0.45),
            new(Color.FromRgb(0xC4, 0x70, 0x8A), 0.78),
            new(Color.FromRgb(0xF0, 0xA7, 0x7F), 1),
        }, 90);
        brush.Freeze();
        return brush;
    }
}
