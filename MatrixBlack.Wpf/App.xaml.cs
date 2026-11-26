using System.IO;
using System.Windows;

namespace MatrixBlack;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var selfTest = e.Args.Any(arg => arg == "--self-test");
        var writeDemo = e.Args.Any(arg => arg == "--write-demo");
        var file = e.Args.FirstOrDefault(arg => !arg.StartsWith('-'));

        if (selfTest)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                SelfTest.Run();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Shutdown(1);
            }
            return;
        }

        if (writeDemo)
        {
            var output = file ?? Path.Combine(Directory.GetCurrentDirectory(), "samples", "demo_eeg.csv");
            DemoEeg.Write(output, seconds: 20);
            Console.WriteLine(output);
            Shutdown(0);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        if (file != null && File.Exists(file))
            window.Store.LoadCsv(file);
        window.Show();
    }
}
