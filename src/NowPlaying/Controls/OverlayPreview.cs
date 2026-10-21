using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NowPlaying.Core;
using NowPlaying.Services;

namespace NowPlaying.Controls;

/// <summary>
/// The overlay as OBS shows it (Overlay/overlay.css), drawn in WPF for the preview of the app window: the bar or the
/// card, in its style, with what plays. Its timer runs only while it is seen and something plays.
/// </summary>
internal sealed class OverlayPreview : Border
{
    private const int BarCount = 16;

    private readonly Border _cover = new();
    private readonly ImageBrush _coverImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly BarsView _bars = new(BarCount, 4, 3, 4) { VerticalAlignment = VerticalAlignment.Center, Height = 36 };
    private readonly Grid _progress = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _elapsed = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _length = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Border _track = new() { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly float[] _levels = new float[BarCount];
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };

    private OverlayLayout _layout;
    private OverlayStyle _style = OverlayStyle.DefaultCard();
    private MediaSnapshot? _media;
    private bool _spectrumHeld;

    public OverlayPreview()
    {
        _cover.Background = _coverImage;
        var trackGrid = new Grid();
        trackGrid.Children.Add(_track);
        trackGrid.Children.Add(_fill);
        _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(trackGrid, 1);
        Grid.SetColumn(_length, 2);
        _progress.Children.Add(_elapsed);
        _progress.Children.Add(trackGrid);
        _progress.Children.Add(_length);

        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => UpdateTimer();
        Unloaded += (_, _) => StopTimer();
        ClipToBounds = true;
    }

    /// <summary>The sound of the PC for the real spectrum.</summary>
    internal AudioSpectrum? Spectrum { get; set; }

    public void Show(OverlayLayout layout, OverlayStyle style, MediaSnapshot? media)
    {
        if (layout != _layout || style.Visualizer != _style.Visualizer)
            StopTimer();
        _layout = layout;
        _style = style;
        _media = media;
        Build();
        UpdateTimer();
    }

    private void Build()
    {
        var colors = SurfaceColors.Resolve(_style, _media?.CoverColor, onDarkSurface: true, overlay: true);
        var text = WidgetView.Brush(colors.Text);
        var secondary = WidgetView.Brush(colors.SecondaryText);
        bool bar = _layout == OverlayLayout.Bar;

        Width = _style.Width;
        Height = bar ? 76 : 150;
        CornerRadius = new CornerRadius(_style.CornerRadius);
        Background = WidgetView.Brush(colors.Background);
        Padding = bar ? new Thickness(12, 0, 18, 0) : new Thickness(18, 0, 22, 0);
        System.Windows.Documents.TextElement.SetFontFamily(this, new FontFamily(_style.Font + ", Segoe UI"));

        double size = bar ? 52 : 114;
        _cover.Width = _cover.Height = size;
        _cover.CornerRadius = new CornerRadius(bar ? 8 : 10);
        _cover.Margin = new Thickness(0, 0, bar ? 14 : 18, 0);
        _cover.Visibility = _style.ShowCover ? Visibility.Visible : Visibility.Collapsed;
        _coverImage.ImageSource = _media?.Cover;
        if (_media?.Cover is null)
            _cover.Background = WidgetView.Brush(colors.Track);
        else
            _cover.Background = _coverImage;

        _title.Text = _media?.Title ?? Strings.Get("NothingPlaying");
        _title.FontSize = bar ? _style.FontSize : _style.FontSize * 1.3;
        _title.Foreground = text;
        _artist.Text = _media?.Artist ?? "";
        _artist.FontSize = bar ? _style.FontSize * 0.82 : _style.FontSize;
        _artist.Foreground = secondary;
        _artist.Visibility = _style.ShowArtist ? Visibility.Visible : Visibility.Collapsed;
        _elapsed.Foreground = _length.Foreground = secondary;
        _track.Background = WidgetView.Brush(colors.Track);
        _fill.Background = text;
        _progress.Visibility = !bar && _style.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        _elapsed.Visibility = _length.Visibility = _style.ShowTimes ? Visibility.Visible : Visibility.Collapsed;
        _bars.Visibility = bar && _style.ShowVisualizer ? Visibility.Visible : Visibility.Collapsed;
        _bars.Fill = secondary;

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Detach(_title, _artist, _progress, _cover, _bars);
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_progress);
        var dock = new DockPanel();
        DockPanel.SetDock(_cover, Dock.Left);
        dock.Children.Add(_cover);
        DockPanel.SetDock(_bars, Dock.Right);
        _bars.Margin = new Thickness(14, 0, 0, 0);
        dock.Children.Add(_bars);
        dock.Children.Add(info);
        Child = dock;
        Tick();
    }

    private static void Detach(params FrameworkElement[] elements)
    {
        foreach (var element in elements)
            if (element.Parent is Panel panel)
                panel.Children.Remove(element);
    }

    private bool Animated => IsVisible && _media is { IsPlaying: true }
        && (_layout == OverlayLayout.Bar ? _style.ShowVisualizer : _style.ShowProgress);

    private void UpdateTimer()
    {
        if (Animated && !_timer.IsEnabled)
        {
            if (_layout == OverlayLayout.Bar && _style.Visualizer == VisualizerMode.Spectrum && Spectrum is not null)
            {
                Spectrum.Acquire();
                _spectrumHeld = true;
            }
            _timer.Start();
        }
        else if (!Animated)
            StopTimer();
    }

    private void StopTimer()
    {
        _timer.Stop();
        if (_spectrumHeld)
        {
            Spectrum?.Release();
            _spectrumHeld = false;
        }
    }

    private void Tick()
    {
        if (_media is null)
        {
            _bars.Flatten();
            return;
        }
        if (_layout == OverlayLayout.Card)
        {
            var position = _media.PositionNow;
            _elapsed.Text = Timeline.Format(position);
            _length.Text = Timeline.Format(_media.Duration);
            _fill.Width = Math.Max(0, (_track.ActualWidth > 0 ? _track.ActualWidth : 200) * Timeline.Fraction(position, _media.Duration));
            return;
        }
        if (!_media.IsPlaying)
            _bars.Flatten();
        else if (_spectrumHeld && Spectrum is not null)
        {
            var levels = Spectrum.Levels();
            for (int i = 0; i < BarCount; i++)
                _levels[i] = levels[i * levels.Length / BarCount];
            _bars.SetLevels(_levels);
        }
        else
        {
            DecorativeBars.Fill(_levels, Environment.TickCount64 / 1000.0);
            _bars.SetLevels(_levels);
        }
    }
}
