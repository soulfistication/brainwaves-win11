# MatrixBlack

Eight-channel EEG viewer for 250 Hz CSV recordings from a g.tec wireless headset. Open a file, scrub the timeline, drag across one-second columns, and delete those rows from the CSV. Undo, Save, and Save Copy write the original columns back out.

The macOS app is the SwiftUI project in `MatrixBlack/`. The Windows app is a C# / WPF port with the same workflow.

## Windows 11

Open `MatrixBlack.sln` in Visual Studio 2022 and press F5.

From a terminal:

```
dotnet run --project MatrixBlack.Wpf -- samples/demo_eeg.csv
```

`samples/demo_eeg.csv` is a synthetic 20 second recording (named `EEG 1` … `EEG 8` columns, plus a counter and accelerometer fields) so the window can be tried without a headset file. A real export works the same way: columns named `EEG 1` through `EEG 8` are used when present, otherwise the first eight columns are read.

Shortcuts: Ctrl+O open, Ctrl+S save, Ctrl+Shift+S save a copy, Ctrl+Z undo, Delete remove the selected window, Esc clear the selection.

<img width="992" height="760" alt="matrixblack" src="https://github.com/user-attachments/assets/716c6645-72d4-4c97-9750-8b611b2125fe" />
