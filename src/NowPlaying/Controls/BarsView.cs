using System.Windows;
using System.Windows.Media;

namespace NowPlaying.Controls;

/// <summary>
/// The bars of a visualizer, drawn by hand: changing their heights redraws this element only, with no layout pass
/// (ten rectangles resized 25 times a second cost the widget several times more).
/// </summary>
internal sealed class BarsView : FrameworkElement
{
    private readonly float[] _levels;
    private Brush _fill = Brushes.White;

    public BarsView(int count, double barWidth, double gap, double minHeight)
    {
        _levels = new float[count];
        BarWidth = barWidth;
        Gap = gap;
        MinBarHeight = minHeight;
        Width = count * barWidth + (count - 1) * gap;
        IsHitTestVisible = false;
    }

    public double BarWidth { get; }
    public double Gap { get; }
    public double MinBarHeight { get; }

    public Brush Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            InvalidateVisual();
        }
    }

    /// <summary>Sets the heights (0 to 1); redraws only when one changed.</summary>
    public void SetLevels(ReadOnlySpan<float> levels)
    {
        bool changed = false;
        for (int i = 0; i < _levels.Length; i++)
        {
            float level = i < levels.Length ? Math.Clamp(levels[i], 0, 1) : 0;
            if (Math.Abs(level - _levels[i]) > 0.004f)
            {
                _levels[i] = level;
                changed = true;
            }
        }
        if (changed)
            InvalidateVisual();
    }

    public void Flatten() => SetLevels(new float[_levels.Length]);

    protected override void OnRender(DrawingContext dc)
    {
        double height = ActualHeight, radius = BarWidth / 2;
        for (int i = 0; i < _levels.Length; i++)
        {
            double h = MinBarHeight + (height - MinBarHeight) * _levels[i];
            dc.DrawRoundedRectangle(_fill, null, new Rect(i * (BarWidth + Gap), (height - h) / 2, BarWidth, h), radius, radius);
        }
    }
}
