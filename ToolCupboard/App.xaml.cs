using System;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace ToolCupboard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        protected override void OnStartup(StartupEventArgs e)
        {
            bool forceStart = false, showCrashHint = false;
            foreach (string arg in e.Args)
            {
                switch (arg)
                {
                    case "/ForceStart": forceStart = true; break;
                    case "/ShowCrashHint": showCrashHint = true; break;
                }
            }

            if (!forceStart)
            {
                //LINQ query for windowsgsm old processes
                var instances = (from p in Process.GetProcesses()
                            where ((Predicate<Process>)(p_ =>
                            {
                                try
                                {
                                    return p_.MainModule.FileName.Equals(Process.GetCurrentProcess().MainModule.FileName);
                                }
                                catch
                                {
                                    return false;
                                }
                            }))(p)
                            select p).ToList();

                //Display the opened ToolCupboard
                foreach (var process in instances)
                {
                    if (process.Id != Process.GetCurrentProcess().Id)
                    {
                        System.Media.SystemSounds.Beep.Play();
                        MessageBox.Show("Another instance is already running", "ToolCupboard already running", MessageBoxButton.OK, MessageBoxImage.Warning);
                        SetForegroundWindow(process.MainWindowHandle);
                        Process.GetCurrentProcess().Kill();
                    }
                }
            }

            AppDomain.CurrentDomain.UnhandledException += async (s, args) =>
            {
                string version = string.Concat(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString().Reverse().Skip(2).Reverse());
                string logPath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "logs");
                Directory.CreateDirectory(logPath);

                string logFile = Path.Combine(logPath, $"CRASH_{DateTime.Now.ToString("yyyyMMdd")}.log");
                File.AppendAllText(logFile, $"ToolCupboard v{version}\n\n" + args.ExceptionObject);
#if !DEBUG
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard");
                if (key != null && (key.GetValue("RestartOnCrash") ?? false).ToString() == "True")
                {
                    Process p = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),
                            FileName = "cmd.exe",
                            Arguments = "/C echo ToolCupboard will auto restart after 10 seconds... & echo Close this windows to cancel... & ping 0 -w 1000 -n 10 > NUL & start ToolCupboard.exe /ForceStart /ShowCrashHint",
                            UseShellExecute = false
                        }
                    };
                    p.Start();
                    Process.GetCurrentProcess().Kill();
                }
#endif
            };

            MainWindow mainwindow = new MainWindow(showCrashHint);
            mainwindow.Show();
        }
    }
}
