using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace MatrixBlack;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Store = new EegStore();
        DataContext = Store;
        InitializeComponent();
        Store.ErrorOccurred += OnError;
    }

    public EegStore Store { get; }

    private void OnError(string message)
    {
        MessageBox.Show(this, message, "CSV Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenCsv();

    private void Save_Click(object sender, RoutedEventArgs e) => Store.SaveInPlace();

    private void SaveCopy_Click(object sender, RoutedEventArgs e) => SaveCopy();

    private void Remove_Click(object sender, RoutedEventArgs e) => Store.RemoveSelectedWindow();

    private void Clear_Click(object sender, RoutedEventArgs e) => Store.ClearSelection();

    private void Undo_Click(object sender, RoutedEventArgs e) => Store.UndoLastRemoval();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.Delete && Store.HasSelection)
        {
            Store.RemoveSelectedWindow();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Store.HasSelection)
        {
            Store.ClearSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && modifiers == ModifierKeys.Control)
        {
            Store.UndoLastRemoval();
            e.Handled = true;
        }
        else if (e.Key == Key.O && modifiers == ModifierKeys.Control)
        {
            OpenCsv();
            e.Handled = true;
        }
        else if (e.Key == Key.S && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveCopy();
            e.Handled = true;
        }
        else if (e.Key == Key.S && modifiers == ModifierKeys.Control)
        {
            Store.SaveInPlace();
            e.Handled = true;
        }
    }

    private void OpenCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open EEG CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (Store.SourcePath != null)
            dialog.InitialDirectory = Path.GetDirectoryName(Store.SourcePath);

        if (dialog.ShowDialog(this) == true)
            Store.LoadCsv(dialog.FileName);
    }

    private void SaveCopy()
    {
        if (!Store.HasSamples)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save CSV Copy",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = Store.SuggestedCopyFileName,
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (Store.SourcePath != null)
            dialog.InitialDirectory = Path.GetDirectoryName(Store.SourcePath);

        if (dialog.ShowDialog(this) == true)
            Store.ExportTo(dialog.FileName);
    }
}
