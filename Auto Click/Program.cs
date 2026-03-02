using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FlowRunner
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // --- Global exception handlers ---
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (_, e) =>
            {
                AppLog.Exception("UI ThreadException", e.Exception);
                ShowFatal("UI ThreadException", e.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
                AppLog.Exception("AppDomain UnhandledException", ex);
                ShowFatal("UnhandledException", ex);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Exception("TaskScheduler UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            // --- app start ---
            try
            {
                ApplicationConfiguration.Initialize();
                AppLog.Info("App started.");
                Application.Run(new MainForm());
                AppLog.Info("App exited normally.");
            }
            catch (Exception ex)
            {
                AppLog.Exception("Fatal error in Main()", ex);
                ShowFatal("Fatal error", ex);
            }
        }

        private static void ShowFatal(string title, Exception ex)
        {
            try
            {
                MessageBox.Show(
                    $"{title}\n\n{ex.Message}\n\nLog:\n{AppLog.CurrentLogPath}",
                    "FlowRunner - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}