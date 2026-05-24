using System;
using System.IO;
using System.Windows;

namespace MKLink
{
    public partial class App : Application
    {
        public App()
        {
            Startup += App_Startup;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Bat loi startup som de neu app tu tat, se co log ro rang.
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrashLog(e.Exception);
            MessageBox.Show(e.Exception.Message, "MKLink crashed", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            if (exception != null)
            {
                WriteCrashLog(exception);
            }
        }

        private static void WriteCrashLog(Exception exception)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MKLink");
                Directory.CreateDirectory(folder);
                string filePath = Path.Combine(folder, "crash.log");
                File.AppendAllText(filePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
