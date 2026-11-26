using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MatrixBlack;

internal static class SelfTest
{
    public static void Run()
    {
        TestColumnDetectionAndRemoval();
        TestFallbackColumnsAndErrors();
        TestDemoRoundTrip();
        TestWaveformHitTesting();
        Console.WriteLine("Self-test passed.");
    }

    private static void TestColumnDetectionAndRemoval()
    {
        var builder = new StringBuilder();
        builder.Append("Counter,EEG 1,EEG 2,EEG 3,EEG 4,EEG 5,EEG 6,EEG 7,EEG 8,Extra\n");
        for (var i = 0; i < 750; i++)
        {
            builder.Append(i);
            for (var ch = 0; ch < 8; ch++)
                builder.Append(',').Append((i + ch).ToString(CultureInfo.InvariantCulture));
            builder.Append(",9\n");
        }

        builder.Append("bad,x,x,x,x,x,x,x,x,x\n");
        builder.Append("1,2,3\n");

        var path = WriteTemp(builder.ToString());
        var store = new EegStore();
        Pump(store.LoadCsvAsync(path));

        Expect(store.TotalSamples == 750, "expected 750 samples after skipping invalid rows");
        Expect(Math.Abs(store.Channels[0].Samples[10] - 10) < 0.0001, "EEG 1 should ignore the Counter column");
        Expect(Math.Abs(store.Channels[7].Samples[10] - 17) < 0.0001, "EEG 8 should be the eighth signal column");

        store.SelectedSecondRange = new SecondRange(1, 1);
        store.RemoveSelectedWindow();
        Expect(store.TotalSamples == 500, "removing one second should drop 250 samples");
        Expect(Math.Abs(store.Channels[0].Samples[250] - 500) < 0.0001, "samples after the cut should shift up");
        Expect(store.HasUnsavedChanges, "removal should mark the file edited");
        Expect(store.CanUndo, "removal should be undoable");

        var edited = Path.Combine(Path.GetDirectoryName(path)!, "edited.csv");
        store.ExportTo(edited);
        var reloaded = EegStore.Parse(File.ReadAllText(edited), "edited.csv");
        Expect(reloaded.RawLines.Count == 500, "exported CSV should contain the shortened recording");
        Expect(reloaded.HeaderLine.StartsWith("Counter,EEG 1", StringComparison.Ordinal), "header should be preserved");

        store.UndoLastRemoval();
        Expect(store.TotalSamples == 750, "undo should restore every sample");
        Expect(!store.HasUnsavedChanges, "undoing the only edit should clear the dirty flag");
        Expect(Math.Abs(store.Channels[3].Samples[250] - 253) < 0.0001, "undo should restore channel values");
    }

    private static void TestFallbackColumnsAndErrors()
    {
        var fallback = EegStore.Parse("1,2,3,4,5,6,7,8\n9,8,7,6,5,4,3,2\n", "plain.csv");
        Expect(fallback.RawLines.Count == 1, "without EEG headers the first row is the header");
        Expect(Math.Abs(fallback.Columns[0][0] - 9) < 0.0001, "fallback should read the first eight columns");

        ExpectThrows(() => EegStore.Parse("", "empty.csv"), "The CSV file is empty.");
        ExpectThrows(() => EegStore.Parse("a,b,c\n1,2,3\n", "short.csv"), "CSV must contain at least 8 data columns.");
        ExpectThrows(() => EegStore.Parse("EEG 1,EEG 2,EEG 3,EEG 4,EEG 5,EEG 6,EEG 7,EEG 8\nx,x,x,x,x,x,x,x\n", "nan.csv"),
            "No valid numeric EEG rows found.");
    }

    private static void TestDemoRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "matrixblack-demo.csv");
        DemoEeg.Write(path, seconds: 4);
        var store = new EegStore();
        Pump(store.LoadCsvAsync(path));

        Expect(store.TotalSamples == 1000, "demo recording should be 4 seconds at 250 Hz");
        var expected = Math.Round(DemoEeg.Value(0, 0), 3);
        Expect(Math.Abs(store.Channels[0].Samples[0] - expected) < 0.0001, "named EEG columns should win over Counter");
        Expect(Math.Abs(store.Channels[0].Samples[0] - store.Channels[1].Samples[0]) > 0.01, "channels should differ");

        store.SaveInPlace();
        Expect(!store.HasUnsavedChanges, "saving without edits should leave the file clean");
    }

    private static void TestWaveformHitTesting()
    {
        var path = Path.Combine(Path.GetTempPath(), "matrixblack-hit.csv");
        DemoEeg.Write(path, seconds: 10);
        var store = new EegStore();
        Pump(store.LoadCsvAsync(path));
        store.CurrentDurationChoice = store.DurationChoices.First(choice => !choice.IsAll && Math.Abs(choice.Value - 10) < 0.001);

        var control = new EegWaveformControl { Store = store, Width = 1000, Height = 500 };
        var window = new Window
        {
            Width = 1000,
            Height = 500,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = control
        };

        window.Show();
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var plotWidth = Math.Max(10, control.ActualWidth - 78 - 16);
        var atStart = control.SecondColumn(78, plotWidth, store);
        var atEnd = control.SecondColumn(78 + plotWidth - 1, plotWidth, store);
        Expect(atStart == 0, "left edge of the plot should be second 0");
        Expect(atEnd == 9, "right edge of a 10 second window should be second 9");

        store.SelectedSecondRange = new SecondRange(2, 4);
        control.InvalidateVisual();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        store.Channels[0].IsVisible = false;
        control.InvalidateVisual();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        window.Close();
    }

    private static void Pump(Task task)
    {
        while (!task.IsCompleted)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
    }

    private static string WriteTemp(string csv)
    {
        var path = Path.Combine(Path.GetTempPath(), $"matrixblack-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv, new UTF8Encoding(false));
        return path;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void ExpectThrows(Action action, string fragment)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex.Message.Contains(fragment, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected an error containing \"{fragment}\".");
    }
}
