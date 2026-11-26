using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media;

namespace MatrixBlack;

public readonly record struct SecondRange(int Lower, int Upper)
{
    public int Count => Upper - Lower + 1;
}

public sealed class EegStore : INotifyPropertyChanged
{
    public const double SampleRate = 250.0;

    private static readonly (string Name, Color Color)[] Defaults =
    [
        ("Fz", Color.FromRgb(255, 79, 163)),
        ("C3", Color.FromRgb(255, 59, 48)),
        ("Cz", Color.FromRgb(255, 214, 10)),
        ("C4", Color.FromRgb(10, 132, 255)),
        ("Pz", Color.FromRgb(48, 209, 88)),
        ("PO7", Color.FromRgb(255, 159, 10)),
        ("Oz", Color.FromRgb(191, 90, 242)),
        ("PO8", Color.FromRgb(100, 210, 255))
    ];

    private readonly List<Snapshot> _undo = new();
    private string _headerLine = "";
    private List<string> _rawLines = new();
    private int _loadGeneration;
    private string _fileName = "No file loaded";
    private string? _sourcePath;
    private int _totalSamples;
    private bool _isLoading;
    private bool _hasUnsavedChanges;
    private string? _statusMessage;
    private SecondRange? _selectedSecondRange;
    private double _windowStartSeconds;
    private double _windowDurationSeconds = 10;
    private LabeledValue _durationChoice;
    private LabeledValue _scale;

    public EegStore()
    {
        Channels = new ObservableCollection<EegChannel>(
            Defaults.Select((item, index) => new EegChannel(index, item.Name, item.Color)));

        DurationChoices = new ObservableCollection<LabeledValue>
        {
            new(5, "5 s"),
            new(10, "10 s"),
            new(15, "15 s"),
            new(30, "30 s")
        };
        _durationChoice = DurationChoices[1];

        Scales = new ObservableCollection<LabeledValue>
        {
            new(25, "±25 µV"),
            new(50, "±50 µV"),
            new(100, "±100 µV"),
            new(200, "±200 µV")
        };
        _scale = Scales[2];
    }

    public ObservableCollection<EegChannel> Channels { get; }
    public ObservableCollection<LabeledValue> DurationChoices { get; }
    public ObservableCollection<LabeledValue> Scales { get; }

    public string FileName
    {
        get => _fileName;
        private set
        {
            if (_fileName == value)
                return;
            _fileName = value;
            Raise();
            Raise(nameof(WindowTitle));
            Raise(nameof(SuggestedCopyFileName));
        }
    }

    public string? SourcePath
    {
        get => _sourcePath;
        private set
        {
            if (_sourcePath == value)
                return;
            _sourcePath = value;
            Raise();
            Raise(nameof(CanSave));
        }
    }

    public int TotalSamples
    {
        get => _totalSamples;
        private set
        {
            if (_totalSamples == value)
                return;
            _totalSamples = value;
            Raise();
            Raise(nameof(TotalDurationSeconds));
            Raise(nameof(SummaryText));
            Raise(nameof(HasSamples));
            NotifyViewport();
        }
    }

    public double TotalDurationSeconds => TotalSamples / SampleRate;

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value)
                return;
            _hasUnsavedChanges = value;
            Raise();
            Raise(nameof(CanSave));
            Raise(nameof(WindowTitle));
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanSave => HasUnsavedChanges && SourcePath != null;
    public bool HasSamples => TotalSamples > 0;
    public bool HasSelection => SelectedSecondRange != null;

    public string WindowTitle => HasUnsavedChanges
        ? $"MatrixBlack — {FileName} • Edited"
        : $"MatrixBlack — {FileName}";

    public string SummaryText => $"250 Hz  •  {TotalSamples} samples ({TotalDurationSeconds:0.0}s)";

    public string SelectionLabel
    {
        get
        {
            if (SelectedSecondRange is not SecondRange range)
                return "";
            return $"Window: {range.Lower}s – {range.Upper + 1}s ({range.Count * (int)SampleRate} samples)";
        }
    }

    public string SuggestedCopyFileName
    {
        get
        {
            if (FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return string.Concat(FileName.AsSpan(0, FileName.Length - 4), "_cleaned.csv");
            return FileName + "_cleaned.csv";
        }
    }

    public SecondRange? SelectedSecondRange
    {
        get => _selectedSecondRange;
        set
        {
            if (_selectedSecondRange == value)
                return;
            _selectedSecondRange = value;
            Raise();
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionLabel));
        }
    }

    public double WindowStartSeconds
    {
        get => _windowStartSeconds;
        set
        {
            var clamped = Math.Max(0, value);
            if (Math.Abs(_windowStartSeconds - clamped) < 0.0000001)
                return;
            _windowStartSeconds = clamped;
            Raise();
        }
    }

    public double WindowDurationSeconds => _windowDurationSeconds;

    public LabeledValue CurrentDurationChoice
    {
        get => _durationChoice;
        set
        {
            if (value == null || ReferenceEquals(value, _durationChoice))
                return;
            _durationChoice = value;
            Raise();
            if (Math.Abs(_windowDurationSeconds - value.Value) > 0.0000001)
            {
                _windowDurationSeconds = value.Value;
                Raise(nameof(WindowDurationSeconds));
                NotifyViewport();
                ClampWindowStart();
            }
        }
    }

    public LabeledValue CurrentScale
    {
        get => _scale;
        set
        {
            if (value == null || ReferenceEquals(value, _scale))
                return;
            _scale = value;
            Raise();
            Raise(nameof(MicrovoltScale));
        }
    }

    public double MicrovoltScale => _scale.Value;

    public double MaxWindowStart => Math.Max(0.01, TotalDurationSeconds - WindowDurationSeconds);

    public bool CanScrub => TotalDurationSeconds > WindowDurationSeconds + 0.0001;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? ErrorOccurred;

    public void LoadCsv(string path) => _ = LoadCsvAsync(path);

    public async Task LoadCsvAsync(string path)
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        StatusMessage = null;

        try
        {
            var parsed = await Task.Run(() =>
            {
                var text = File.ReadAllText(path);
                return Parse(text, Path.GetFileName(path));
            }).ConfigureAwait(true);

            if (generation != _loadGeneration)
                return;

            Apply(parsed, path);
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
                return;
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            if (generation == _loadGeneration)
                IsLoading = false;
        }
    }

    public void RemoveSelectedWindow()
    {
        if (SelectedSecondRange is not SecondRange range || TotalSamples <= 0)
            return;

        var startSample = Math.Max(0, (int)(range.Lower * SampleRate));
        var endSampleExclusive = Math.Min(TotalSamples, (int)((range.Upper + 1) * SampleRate));
        if (startSample >= endSampleExclusive)
            return;

        PushUndo();

        var removeCount = endSampleExclusive - startSample;
        _rawLines.RemoveRange(startSample, removeCount);
        foreach (var channel in Channels)
            channel.Samples.RemoveRange(startSample, removeCount);

        TotalSamples = _rawLines.Count;
        SelectedSecondRange = null;
        HasUnsavedChanges = true;
        StatusMessage = $"Removed {removeCount} samples ({range.Count}s window)";
        FinishTimelineEdit(preferAll: WindowDurationSeconds > TotalDurationSeconds && TotalDurationSeconds > 0);
    }

    public void UndoLastRemoval()
    {
        if (_undo.Count == 0)
            return;

        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Raise(nameof(CanUndo));

        _rawLines = previous.RawLines;
        for (var ch = 0; ch < Channels.Count; ch++)
            Channels[ch].Samples = previous.Columns[ch];

        TotalSamples = _rawLines.Count;
        SelectedSecondRange = null;
        HasUnsavedChanges = _undo.Count > 0;
        StatusMessage = "Restored removed window";
        FinishTimelineEdit(preferAll: false);
    }

    public void ClearSelection() => SelectedSecondRange = null;

    public string BuildCsvString()
    {
        var builder = new StringBuilder();
        builder.Append(_headerLine);
        builder.Append('\n');
        foreach (var line in _rawLines)
        {
            builder.Append(line);
            builder.Append('\n');
        }
        return builder.ToString();
    }

    public void SaveInPlace()
    {
        if (SourcePath == null)
            return;

        try
        {
            WriteCsvAtomic(SourcePath, BuildCsvString());
            HasUnsavedChanges = false;
            StatusMessage = $"Saved changes to {Path.GetFileName(SourcePath)}";
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Could not overwrite file: {ex.Message}. Use 'Save Copy...' instead.");
        }
    }

    public void ExportTo(string path)
    {
        try
        {
            WriteCsvAtomic(path, BuildCsvString());
            StatusMessage = $"Exported to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Could not export file: {ex.Message}");
        }
    }

    public static ParsedRecording Parse(string text, string fileName)
    {
        var lines = SplitLines(text);
        if (lines.Count == 0)
            throw new InvalidDataException("The CSV file is empty.");

        var headerLine = lines[0];
        var headers = headerLine.Split(',').Select(header => header.Trim()).ToArray();

        var columnIndices = new List<int>(8);
        for (var i = 1; i <= 8; i++)
        {
            var idx = Array.IndexOf(headers, $"EEG {i}");
            if (idx >= 0)
                columnIndices.Add(idx);
        }

        if (columnIndices.Count != 8)
        {
            if (headers.Length < 8)
                throw new InvalidDataException("CSV must contain at least 8 data columns.");
            columnIndices = Enumerable.Range(0, 8).ToList();
        }

        var maxColumn = columnIndices.Max();
        var columns = Enumerable.Range(0, 8).Select(_ => new List<double>(Math.Max(0, lines.Count - 1))).ToArray();
        var rawRows = new List<string>(Math.Max(0, lines.Count - 1));

        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var fields = line.Split(',');
            if (fields.Length <= maxColumn)
                continue;

            var row = new double[8];
            var valid = true;
            for (var ch = 0; ch < 8; ch++)
            {
                var token = fields[columnIndices[ch]].Trim();
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    !double.IsFinite(value))
                {
                    valid = false;
                    break;
                }

                row[ch] = value;
            }

            if (!valid)
                continue;

            rawRows.Add(line);
            for (var ch = 0; ch < 8; ch++)
                columns[ch].Add(row[ch]);
        }

        if (columns[0].Count == 0)
            throw new InvalidDataException("No valid numeric EEG rows found.");

        return new ParsedRecording(headerLine, rawRows, columns, fileName);
    }

    private void Apply(ParsedRecording parsed, string path)
    {
        _headerLine = parsed.HeaderLine;
        _rawLines = parsed.RawLines;
        _undo.Clear();
        Raise(nameof(CanUndo));

        for (var ch = 0; ch < 8; ch++)
            Channels[ch].Samples = parsed.Columns[ch];

        SelectedSecondRange = null;
        HasUnsavedChanges = false;
        FileName = parsed.FileName;
        SourcePath = path;
        TotalSamples = parsed.RawLines.Count;
        _windowStartSeconds = 0;
        Raise(nameof(WindowStartSeconds));
        RebuildAllChoice();
        SetDuration(Math.Min(10, TotalDurationSeconds), preferAll: false);
        StatusMessage = null;
    }

    private void FinishTimelineEdit(bool preferAll)
    {
        if (WindowDurationSeconds > TotalDurationSeconds && TotalDurationSeconds > 0)
            SetDuration(Math.Max(1, TotalDurationSeconds), preferAll: true);
        else
        {
            RebuildAllChoice();
            ReselectDuration(preferAll);
            NotifyViewport();
            ClampWindowStart();
        }
    }

    private void RebuildAllChoice()
    {
        var existing = DurationChoices.FirstOrDefault(choice => choice.IsAll);
        if (existing != null)
            DurationChoices.Remove(existing);

        if (TotalDurationSeconds > 0)
        {
            DurationChoices.Add(new LabeledValue(
                TotalDurationSeconds,
                $"All ({(int)TotalDurationSeconds}s)",
                isAll: true));
        }
    }

    private void SetDuration(double seconds, bool preferAll)
    {
        _windowDurationSeconds = seconds;
        Raise(nameof(WindowDurationSeconds));
        RebuildAllChoice();
        ReselectDuration(preferAll);
        NotifyViewport();
        ClampWindowStart();
    }

    private void ReselectDuration(bool preferAll)
    {
        LabeledValue? match = null;
        if (!preferAll)
        {
            match = DurationChoices.FirstOrDefault(choice =>
                !choice.IsAll && Math.Abs(choice.Value - _windowDurationSeconds) < 0.0001);
        }

        match ??= DurationChoices.FirstOrDefault(choice =>
            choice.IsAll && Math.Abs(choice.Value - _windowDurationSeconds) < 0.0001);
        match ??= DurationChoices.FirstOrDefault(choice => choice.IsAll);
        match ??= DurationChoices.FirstOrDefault(choice => !choice.IsAll && Math.Abs(choice.Value - _windowDurationSeconds) < 0.0001);
        match ??= _durationChoice;

        if (!ReferenceEquals(_durationChoice, match))
        {
            _durationChoice = match;
            Raise(nameof(CurrentDurationChoice));
        }
    }

    private void ClampWindowStart()
    {
        var maxStart = Math.Max(0, TotalDurationSeconds - WindowDurationSeconds);
        if (_windowStartSeconds > maxStart)
        {
            _windowStartSeconds = maxStart;
            Raise(nameof(WindowStartSeconds));
        }
    }

    private void NotifyViewport()
    {
        Raise(nameof(MaxWindowStart));
        Raise(nameof(CanScrub));
    }

    private void PushUndo()
    {
        _undo.Add(new Snapshot(
            _rawLines.ToList(),
            Channels.Select(channel => channel.Samples.ToList()).ToArray()));
        Raise(nameof(CanUndo));
    }

    private static void WriteCsvAtomic(string path, string csv)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
                File.Delete(temp);
            throw;
        }
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n' && text[i] != '\r')
                continue;

            if (i > start)
                lines.Add(text.Substring(start, i - start));

            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;

            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines;
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Raise(name);
    }

    private sealed record Snapshot(List<string> RawLines, List<double>[] Columns);
}

public sealed record ParsedRecording(
    string HeaderLine,
    List<string> RawLines,
    List<double>[] Columns,
    string FileName);
