using System;
using System.Linq;

namespace WSJTX_Controller
{
    static class Program
    {
        // WPF migration: no App.xaml/StartupUri -- this remains the real entry point (both
        // UseWPF and UseWindowsForms are enabled in Jimmy.csproj, so "Application" and
        // "MessageBox" are ambiguous; fully qualified below to pick the WPF ones explicitly).
        [STAThread]
        static void Main()
        {
            if (System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).Count() > 1)
            {
                System.Windows.MessageBox.Show("An instance of this application is already running.");
                return;
            }
            if (System.Diagnostics.Process.GetProcessesByName("WSJTX_Controller").Count() > 0)
            {
                System.Windows.MessageBox.Show("Jimmy and Otto can't run at the same time.\n\nClose Otto before running Jimmy.");
                return;
            }

            var ctrl = new Controller();
            var app = new System.Windows.Application();
            var mainWindow = new MainWindow(ctrl);
            app.Run(mainWindow);
        }
    }
}
