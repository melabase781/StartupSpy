using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace StartupSpy
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                var msg = ex.ExceptionObject?.ToString() ?? "Unknown error";
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    msg);
                MessageBox.Show(msg, "StartupSpy Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                var msg = ex.Exception?.ToString() ?? "Unknown error";
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    msg);
                MessageBox.Show(msg, "StartupSpy Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}