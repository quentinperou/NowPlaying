using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NowPlaying.Core;
using NowPlaying.Services;

namespace NowPlaying.Controls;

/// <summary>
/// The widget: what plays, in the style of the settings. Used in the taskbar (<see cref="TaskbarWidget"/>) and as the
/// live preview of the app window. Its timers run only while it is seen: the bars at rest while playing, the progress
/// on hover.
/// </summary>
public partial class WidgetView : UserControl
{
    private const int BarCount = 10;

    // NOWPLAYING_WIDGET_STATE=hover|volume keeps the widget in that state (pictures of the README, tests: the
    // hover and the wheel need the real mouse).
    private static readonly string? ForcedState = Environment.GetEnvironmentVariable("NOWPLAYING_WIDGET_STATE");

    private readonly BarsView _bars = new(BarCount, 3, 2, 3);
    private readonly float[] _barLevels = new float[BarCount];
    private readonly DispatcherTimer _frameTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private WidgetStyle _style = new();
    private SurfaceColors _colors = SurfaceColors.Resolve(new WidgetStyle(), null, true, false);
    private MediaSnapshot? _media;
    private bool _idle;
    private bool _hover;
    private bool _spectrumHeld;
    private BitmapSource? _greyCover;

    public WidgetView()
    {
        InitializeComponent();
        BarsPanel.Child = _bars;
        _frameTimer.Tick += (_, _) => PaintBars();
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _volumeTimer.Tick += (_, _) =>
        {
            _volumeTimer.Stop();
            UpdateState();
        };
        MouseEnter += (_, _) => { _hover = true; UpdateState(); };
        MouseLeave += (_, _) => { _hover = false; UpdateState(); };
        MouseWheel += (_, e) =>
        {
            e.Handled = true;
            VolumeWheel?.Invoke(this, e.Delta / 120);
        };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            MenuRequested?.Invoke(this, EventArgs.Empty);
        };
        IsVisibleChanged += (_, _) => UpdateTimers();
        Unloaded += (_, _) => StopFrames();
        ApplyStyle(_style, _colors);
    }

    public event EventHandler? PlayPauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;
    /// <summary>Seek to this share of the track (0 to 1).</summary>
    public event EventHandler<double>? SeekRequested;
    /// <summary>The wheel turned over the widget: notches, up positive.</summary>
    public event EventHandler<int>? VolumeWheel;
    /// <summary>A click on the volume bar: the new volume (0 to 1).</summary>
    public event EventHandler<double>? VolumeRequested;
    public event EventHandler? MenuRequested;

    /// <summary>The sound of the PC for the real spectrum; null: the bars never move (a preview without sound).</summary>
    internal AudioSpectrum? Spectrum { get; set; }

    /// <param name="frostedRadius">
    /// Over a blurred background (the floating widget, see <see cref="FloatingWidget.SetLook"/>): its corners, a light
    /// rim brighter at the top and a sheen, as light catches a pane of glass.
    /// </param>
    public void ApplyStyle(WidgetStyle style, SurfaceColors colors, double? frostedRadius = null)
    {
        bool frosted = frostedRadius is not null;
        bool visualizerChanged = style.Visualizer != _style.Visualizer;
        if (visualizerChanged)
            StopFrames(); // the next start holds the spectrum, or not
        _style = style;
        _colors = colors;

        Width = style.Width;
        Root.CornerRadius = new CornerRadius(frostedRadius ?? style.CornerRadius);
        CoverBox.CornerRadius = new CornerRadius(Math.Min(4, Math.Max(2, style.CornerRadius)));
        Root.Background = frosted ? Sheen(colors.Background) : Brush(colors.Background);
        Root.BorderThickness = new Thickness(frosted ? 1 : 0);
        Root.BorderBrush = frosted ? Rim : null;
        var text = Brush(colors.Text);
        var secondary = Brush(colors.SecondaryText);
        var track = Brush(colors.Track);
        Resources["WidgetHoverBrush"] = Brush(colors.Hover);

        FontFamily = new FontFamily(style.Font + ", Segoe UI");
        TitleText.FontSize = style.FontSize;
        ArtistText.FontSize = Math.Max(9, style.FontSize - 1);
        TitleText.Foreground = text;
        ArtistText.Foreground = secondary;
        ElapsedText.Foreground = LengthText.Foreground = VolumeText.Foreground = VolumeIcon.Foreground = secondary;
        LiveText.Foreground = text;
        SeekBack.Background = VolumeBack.Background = track;
        SeekFill.Background = VolumeFill.Background = text;
        PreviousGlyph.Fill = PlayGlyph.Fill = NextGlyph.Fill = text;
        _bars.Fill = secondary;
        CoverPlaceholder(track);

        CoverBox.Visibility = style.ShowCover ? Visibility.Visible : Visibility.Collapsed;
        Root.Padding = new Thickness(style.ShowCover ? 4 : 10, 0, 8, 0);
        ArtistText.Visibility = style.ShowArtist ? Visibility.Visible : Visibility.Collapsed;
        BarsPanel.Visibility = style.ShowVisualizer ? Visibility.Visible : Visibility.Collapsed;
        ElapsedText.Visibility = LengthText.Visibility = style.ShowTimes ? Visibility.Visible : Visibility.Collapsed;
        var controls = style.ShowControls ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.Visibility = PlayButton.Visibility = NextButton.Visibility = controls;
        UpdateState();
    }

    internal void ShowMedia(MediaSnapshot? media, bool idle)
    {
        bool coverChanged = media?.CoverRevision != _media?.CoverRevision || media?.Id != _media?.Id;
        if (ForcedState == "volume" && media is not null && media.Id != _media?.Id)
            Dispatcher.BeginInvoke(() => VolumeWheel?.Invoke(this, 0)); // reads the volume to show
        _media = media;
        _idle = idle;
        TitleText.Text = media?.Title ?? "";
        ArtistText.Text = media?.Artist ?? "";
        LiveText.Text = Strings.Get("Live");
        if (coverChanged)
            _greyCover = null;
        CoverBrush.ImageSource = media?.Cover is { } cover ? (idle ? GreyCover(cover) : cover) : null;
        CoverPlaceholder(Brush(_colors.Track));
        PlayGlyph.Data = (Geometry)FindResource(media is { IsPlaying: true } && !idle ? "GlyphPause" : "GlyphPlay");
        PreviousButton.IsEnabled = media?.CanPrevious ?? false;
        NextButton.IsEnabled = media?.CanNext ?? false;
        PlayButton.IsEnabled = media?.CanPlayPause ?? false;
        SeekTrack.IsHitTestVisible = media?.CanSeek ?? false;
        Opacity = idle ? 0.55 : 1;
        UpdateProgress();
        UpdateState();
    }

    /// <summary>Shows the volume of the app that plays for a moment (null: the player has no sound to set now).</summary>
    public void ShowVolume(float? volume)
    {
        VolumeFill.Width = volume is { } v ? Math.Max(0, VolumeTrack.ActualWidth * v) : 0;
        VolumeText.Text = volume is { } p ? $"{Math.Round(p * 100)} %" : "–";
        VolumeIcon.Text = volume is 0f ? "" : "";
        _volumeTimer.Stop();
        _volumeTimer.Start();
        UpdateState();
        // The bar may only now have its width.
        Dispatcher.BeginInvoke(() =>
        {
            if (volume is { } w)
                VolumeFill.Width = Math.Max(0, VolumeTrack.ActualWidth * w);
        }, DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- States

    private bool ShowsVolume => _volumeTimer.IsEnabled || ForcedState == "volume";

    private void UpdateState()
    {
        bool active = !_idle && _media is not null && (_hover || ShowsVolume || ForcedState == "hover");
        RestPanel.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        ControlPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        // No length: a live stream if the position goes on, else a player that tells nothing (no progress then).
        var progress = Progress(_media);
        SeekPanel.Visibility = !ShowsVolume && progress == ProgressKind.Track ? Visibility.Visible : Visibility.Collapsed;
        LivePanel.Visibility = !ShowsVolume && progress == ProgressKind.Live ? Visibility.Visible : Visibility.Collapsed;
        VolumePanel.Visibility = ShowsVolume ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimers();
    }

    private void UpdateTimers()
    {
        bool seen = IsVisible;
        bool rest = RestPanel.Visibility == Visibility.Visible;
        bool bars = seen && rest && _style.ShowVisualizer && _media is { IsPlaying: true } && !_idle;
        if (bars && !_frameTimer.IsEnabled)
        {
            if (_style.Visualizer == VisualizerMode.Spectrum && Spectrum is not null && !_spectrumHeld)
            {
                Spectrum.Acquire();
                _spectrumHeld = true;
            }
            _frameTimer.Start();
        }
        else if (!bars && _frameTimer.IsEnabled)
            StopFrames();
        if (!bars)
            FlattenBars();

        bool progress = seen && !rest && _media is not null;
        if (progress && !_progressTimer.IsEnabled)
            _progressTimer.Start();
        else if (!progress)
            _progressTimer.Stop();
    }

    private void StopFrames()
    {
        _frameTimer.Stop();
        if (_spectrumHeld)
        {
            Spectrum?.Release();
            _spectrumHeld = false;
        }
    }

    private void PaintBars()
    {
        if (_spectrumHeld && Spectrum is not null)
        {
            var levels = Spectrum.Levels();
            // 24 bands read as 10 bars: the mean of each tenth.
            for (int i = 0; i < BarCount; i++)
            {
                int from = i * levels.Length / BarCount, to = Math.Max(from + 1, (i + 1) * levels.Length / BarCount);
                float sum = 0;
                for (int k = from; k < to; k++)
                    sum += levels[k];
                _barLevels[i] = sum / (to - from);
            }
        }
        else
            DecorativeBars.Fill(_barLevels, Environment.TickCount64 / 1000.0);
        _bars.SetLevels(_barLevels);
    }

    private void FlattenBars() => _bars.Flatten();

    /// <summary>What the progress shows (see <see cref="Timeline.Kind"/>).</summary>
    internal static ProgressKind Progress(MediaSnapshot? media) =>
        media is null ? ProgressKind.None : Timeline.Kind(media.Position, media.Duration);

    private void UpdateProgress()
    {
        if (_media is null)
            return;
        // A live stream that starts reporting its position: "live" shows.
        if ((LivePanel.Visibility == Visibility.Visible) != (Progress(_media) == ProgressKind.Live && !ShowsVolume) && ControlPanel.IsVisible)
            UpdateState();
        var position = _media.PositionNow;
        ElapsedText.Text = Timeline.Format(position);
        LengthText.Text = Timeline.Format(_media.Duration);
        SeekFill.Width = Math.Max(0, SeekTrack.ActualWidth * Timeline.Fraction(position, _media.Duration));
    }

    // ---------------------------------------------------------------- Input

    private void Play_Click(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);

    private void Previous_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);

    private void Seek_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        double fraction = e.GetPosition(SeekTrack).X / Math.Max(1, SeekTrack.ActualWidth);
        SeekFill.Width = Math.Max(0, SeekTrack.ActualWidth * Math.Clamp(fraction, 0, 1));
        SeekRequested?.Invoke(this, Math.Clamp(fraction, 0, 1));
    }

    private void Volume_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        VolumeRequested?.Invoke(this, Math.Clamp(e.GetPosition(VolumeTrack).X / Math.Max(1, VolumeTrack.ActualWidth), 0, 1));
    }

    /// <summary>Whether <paramref name="element"/> is in the progress or the volume bar (they act on the press itself).</summary>
    internal bool IsOnTrack(DependencyObject? element)
    {
        for (; element is not null; element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
            if (element == SeekTrack || element == VolumeTrack)
                return true;
        return false;
    }

    // ---------------------------------------------------------------- Helpers

    private void CoverPlaceholder(Brush placeholder)
    {
        // No cover (a page without artwork): a plain tile in the track color.
        CoverBox.BorderThickness = new Thickness(0);
        CoverBox.Background = CoverBrush.ImageSource is null ? placeholder : CoverBoxImage;
    }

    private ImageBrush CoverBoxImage => CoverBrush;

    private BitmapSource GreyCover(BitmapSource cover)
    {
        if (_greyCover is null)
        {
            var grey = new FormatConvertedBitmap(cover, PixelFormats.Gray8, null, 0);
            grey.Freeze();
            _greyCover = grey;
        }
        return _greyCover;
    }

    private static readonly Brush Rim = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromArgb(120, 255, 255, 255), 0), new GradientStop(Color.FromArgb(28, 255, 255, 255), 1)], 90));

    // The background a little lighter at the top.
    private static Brush Sheen(Argb background)
    {
        var c = background.Color;
        byte Lighter(byte v) => (byte)(v + (255 - v) * 0.16);
        var top = Color.FromArgb(background.A, Lighter(c.R), Lighter(c.G), Lighter(c.B));
        var bottom = Color.FromArgb(background.A, c.R, c.G, c.B);
        return Frozen(new LinearGradientBrush([new GradientStop(top, 0), new GradientStop(bottom, 0.7)], 90));
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    internal static SolidColorBrush Brush(Argb color)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.Color.R, color.Color.G, color.Color.B));
        brush.Freeze();
        return brush;
    }
}
