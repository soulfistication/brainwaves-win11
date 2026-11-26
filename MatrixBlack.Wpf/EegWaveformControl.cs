using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MatrixBlack;

public sealed class EegWaveformControl : FrameworkElement
{
    public static readonly DependencyProperty StoreProperty = DependencyProperty.Register(
        nameof(Store),
        typeof(EegStore),
        typeof(EegWaveformControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStoreChanged));

    private const double LeftMargin = 78;
    private const double RightMargin = 16;
    private const double TopMargin = 24;
    private const double BottomMargin = 26;

    private static readonly Brush PlotBackground = Freeze(Color.FromRgb(14, 14, 14));
    private static readonly Brush GridBrush = Freeze(Color.FromArgb(31, 255, 255, 255));
    private static readonly Brush SeparatorBrush = Freeze(Color.FromArgb(20, 255, 255, 255));
    private static readonly Brush MutedText = Freeze(Color.FromRgb(142, 142, 147));
    private static readonly Brush SelectionFill = Freeze(Color.FromArgb(46, 255, 214, 10));
    private static readonly Brush SelectionStroke = Freeze(Color.FromArgb(217, 255, 214, 10));
    private static readonly Pen GridPen = FreezePen(GridBrush, 1);
    private static readonly Pen SeparatorPen = FreezePen(SeparatorBrush, 1);
    private static readonly Pen SelectionPen = FreezePen(SelectionStroke, 1.5);

    private int? _dragAnchor;
    private EegStore? _subscribedStore;

    public EegWaveformControl()
    {
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    public EegStore? Store
    {
        get => (EegStore?)GetValue(StoreProperty);
        set => SetValue(StoreProperty, value);
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var store = Store;
        if (store == null || store.TotalSamples <= 0)
            return;

        _dragAnchor = SecondColumn(e.GetPosition(this).X, PlotWidth(ActualWidth), store);
        if (_dragAnchor == null)
            return;

        store.SelectedSecondRange = new SecondRange(_dragAnchor.Value, _dragAnchor.Value);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || _dragAnchor == null || Store == null)
            return;

        var current = SecondColumn(e.GetPosition(this).X, PlotWidth(ActualWidth), Store);
        if (current == null)
            return;

        var lower = Math.Min(_dragAnchor.Value, current.Value);
        var upper = Math.Max(_dragAnchor.Value, current.Value);
        Store.SelectedSecondRange = new SecondRange(lower, upper);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragAnchor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        dc.DrawRectangle(PlotBackground, null, new Rect(size));

        var store = Store;
        var visible = store?.Channels.Where(channel => channel.IsVisible).ToList() ?? new List<EegChannel>();
        if (store == null || visible.Count == 0 || store.TotalSamples <= 1)
        {
            DrawText(dc, "Open a 250 Hz CSV recording to visualize 8 EEG channels",
                size.Width / 2, size.Height / 2, MutedText, 16, FontWeights.SemiBold, TextAnchor.Center);
            return;
        }

        var plotWidth = PlotWidth(size.Width);
        var plotHeight = Math.Max(10, size.Height - TopMargin - BottomMargin);
        var startSample = Math.Max(0, (int)(store.WindowStartSeconds * EegStore.SampleRate));
        var visibleSampleCount = Math.Max(2, (int)(store.WindowDurationSeconds * EegStore.SampleRate));
        var endSample = Math.Min(store.TotalSamples - 1, startSample + visibleSampleCount);
        var actualSamples = Math.Max(1, endSample - startSample);

        DrawSelection(dc, store, plotWidth, plotHeight);
        DrawGrid(dc, store, plotWidth, plotHeight);

        var trackHeight = plotHeight / visible.Count;
        var halfTrack = trackHeight * 0.44;
        for (var index = 0; index < visible.Count; index++)
        {
            var channel = visible[index];
            var trackTop = TopMargin + index * trackHeight;
            var baselineY = trackTop + trackHeight / 2;

            dc.DrawLine(SeparatorPen,
                new Point(LeftMargin, trackTop + trackHeight),
                new Point(LeftMargin + plotWidth, trackTop + trackHeight));

            var baselinePen = BaselinePen(channel);
            dc.DrawLine(baselinePen,
                new Point(LeftMargin, baselineY),
                new Point(LeftMargin + plotWidth, baselineY));

            DrawText(dc, channel.Name, LeftMargin - 10, baselineY, channel.Brush, 12, FontWeights.SemiBold, TextAnchor.Trailing);

            var wave = BuildWaveform(channel.Samples, startSample, endSample, actualSamples, plotWidth, baselineY, halfTrack, store.MicrovoltScale);
            if (wave != null)
                dc.DrawGeometry(null, WavePen(channel), wave);
        }
    }

    internal int? SecondColumn(double x, double plotWidth, EegStore store)
    {
        if (store.WindowDurationSeconds <= 0)
            return null;

        var clampedX = Math.Min(Math.Max(x, LeftMargin), LeftMargin + plotWidth - 0.001);
        var ratio = (clampedX - LeftMargin) / plotWidth;
        var timeAtCursor = store.WindowStartSeconds + ratio * store.WindowDurationSeconds;
        var maxColumnIndex = Math.Max(0, (int)Math.Ceiling(store.TotalDurationSeconds) - 1);
        return Math.Min(Math.Max(0, (int)Math.Floor(timeAtCursor)), maxColumnIndex);
    }

    private static double PlotWidth(double totalWidth) => Math.Max(10, totalWidth - LeftMargin - RightMargin);

    private void DrawSelection(DrawingContext dc, EegStore store, double plotWidth, double plotHeight)
    {
        if (store.SelectedSecondRange is not SecondRange range || store.WindowDurationSeconds <= 0)
            return;

        var viewStart = store.WindowStartSeconds;
        var viewEnd = viewStart + store.WindowDurationSeconds;
        var visibleStart = Math.Max(range.Lower, viewStart);
        var visibleEnd = Math.Min(range.Upper + 1, viewEnd);
        if (visibleEnd <= visibleStart)
            return;

        var startX = LeftMargin + (visibleStart - viewStart) / store.WindowDurationSeconds * plotWidth;
        var endX = LeftMargin + (visibleEnd - viewStart) / store.WindowDurationSeconds * plotWidth;
        var rect = new Rect(startX, TopMargin, Math.Max(1, endX - startX), plotHeight);
        dc.DrawRectangle(SelectionFill, SelectionPen, rect);
        DrawText(dc, $"Selected: {range.Lower}s – {range.Upper + 1}s ({range.Count}s)",
            (startX + endX) / 2, TopMargin - 11, SelectionStroke, 11, FontWeights.SemiBold, TextAnchor.Center);
    }

    private void DrawGrid(DrawingContext dc, EegStore store, double plotWidth, double plotHeight)
    {
        var duration = store.WindowDurationSeconds;
        if (duration <= 0)
            return;

        var firstSecond = (int)Math.Floor(store.WindowStartSeconds);
        var lastSecond = (int)Math.Ceiling(store.WindowStartSeconds + duration);
        var labelStep = duration <= 15 ? 1 : duration <= 40 ? 2 : 5;
        var viewEnd = store.WindowStartSeconds + duration;

        for (var sec = firstSecond; sec <= lastSecond; sec++)
        {
            var t = (double)sec;
            if (t < store.WindowStartSeconds || t > viewEnd)
                continue;

            var x = LeftMargin + (t - store.WindowStartSeconds) / duration * plotWidth;
            dc.DrawLine(GridPen, new Point(x, TopMargin), new Point(x, TopMargin + plotHeight));
            if (sec % labelStep == 0)
            {
                DrawText(dc, $"{sec}s", x, TopMargin + plotHeight + 13, MutedText, 10, FontWeights.Normal, TextAnchor.Center);
            }
        }
    }

    private static StreamGeometry? BuildWaveform(
        IReadOnlyList<double> samples,
        int startSample,
        int endSample,
        int actualSamples,
        double plotWidth,
        double baselineY,
        double halfTrack,
        double microvoltScale)
    {
        if (endSample <= startSample || endSample >= samples.Count || microvoltScale == 0)
            return null;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var pixelColumns = Math.Max(1, (int)plotWidth);
            var samplesPerPixel = actualSamples / (double)pixelColumns;

            double Y(double microvolts)
            {
                var normalized = Math.Max(-1.2, Math.Min(1.2, microvolts / microvoltScale));
                return baselineY - normalized * halfTrack;
            }

            if (samplesPerPixel <= 2)
            {
                for (var idx = startSample; idx <= endSample; idx++)
                {
                    var x = LeftMargin + (idx - startSample) / (double)actualSamples * plotWidth;
                    var y = Y(samples[idx]);
                    if (idx == startSample)
                        ctx.BeginFigure(new Point(x, y), false, false);
                    else
                        ctx.LineTo(new Point(x, y), true, false);
                }
            }
            else
            {
                var started = false;
                for (var px = 0; px < pixelColumns; px++)
                {
                    var binStart = startSample + (int)(px * samplesPerPixel);
                    var binEnd = Math.Min(endSample, startSample + (int)((px + 1) * samplesPerPixel));
                    if (binStart > binEnd || binStart >= samples.Count)
                        continue;

                    var min = samples[binStart];
                    var max = min;
                    for (var idx = binStart; idx <= binEnd && idx < samples.Count; idx++)
                    {
                        var sample = samples[idx];
                        if (sample < min) min = sample;
                        if (sample > max) max = sample;
                    }

                    var x = LeftMargin + px;
                    var yTop = Y(max);
                    var yBottom = Y(min);
                    if (!started)
                    {
                        ctx.BeginFigure(new Point(x, yTop), false, false);
                        started = true;
                    }
                    else
                    {
                        ctx.LineTo(new Point(x, yTop), true, false);
                    }

                    if (Math.Abs(yBottom - yTop) > 0.5)
                        ctx.LineTo(new Point(x, yBottom), true, false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, double size, FontWeight weight, TextAnchor anchor)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var origin = anchor switch
        {
            TextAnchor.Trailing => new Point(x - formatted.Width, y - formatted.Height / 2),
            TextAnchor.Center => new Point(x - formatted.Width / 2, y - formatted.Height / 2),
            _ => new Point(x, y)
        };
        dc.DrawText(formatted, origin);
    }

    private static void OnStoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EegWaveformControl)d;
        if (control._subscribedStore != null)
        {
            control._subscribedStore.PropertyChanged -= control.OnStorePropertyChanged;
            foreach (var channel in control._subscribedStore.Channels)
                channel.PropertyChanged -= control.OnChannelPropertyChanged;
        }

        control._subscribedStore = e.NewValue as EegStore;
        if (control._subscribedStore != null)
        {
            control._subscribedStore.PropertyChanged += control.OnStorePropertyChanged;
            foreach (var channel in control._subscribedStore.Channels)
                channel.PropertyChanged += control.OnChannelPropertyChanged;
        }

        control.InvalidateVisual();
    }

    private void OnStorePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => InvalidateVisual();

    private void OnChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => InvalidateVisual();

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen WavePen(EegChannel channel)
    {
        var pen = new Pen(channel.Brush, 1.35)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Pen BaselinePen(EegChannel channel)
    {
        Color color = channel.Brush is SolidColorBrush solid
            ? solid.Color
            : Colors.White;
        var brush = new SolidColorBrush(Color.FromArgb(56, color.R, color.G, color.B));
        brush.Freeze();
        var dash = new DashStyle(new double[] { 4, 4 }, 0);
        dash.Freeze();
        var pen = new Pen(brush, 0.75) { DashStyle = dash };
        pen.Freeze();
        return pen;
    }

    private enum TextAnchor
    {
        Leading,
        Center,
        Trailing
    }
}
