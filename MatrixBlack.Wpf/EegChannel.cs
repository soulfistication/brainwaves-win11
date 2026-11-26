using System.ComponentModel;
using System.Windows.Media;

namespace MatrixBlack;

public sealed class EegChannel : INotifyPropertyChanged
{
    private bool _isVisible = true;

    public EegChannel(int id, string name, Color color)
    {
        Id = id;
        Name = name;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Brush = brush;

        var muted = new SolidColorBrush(Color.FromArgb(72, color.R, color.G, color.B));
        muted.Freeze();
        BrushMuted = muted;
        Samples = new List<double>();
    }

    public int Id { get; }
    public string Name { get; }
    public Brush Brush { get; }
    public Brush BrushMuted { get; }
    public List<double> Samples { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
                return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
