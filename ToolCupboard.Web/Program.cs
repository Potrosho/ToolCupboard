using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ToolCupboard.Functions;
using ToolCupboard.Web;

namespace ToolCupboard
{
    /// <summary>
    /// The dashboard-only edition: a tray icon, the servers, and the local website.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // One copy per install folder, the name must not contain backslashes
            string mutexName = @"Local\ToolCupboardWeb-" + AppInfo.RootPath.Replace('\\', '/').ToLowerInvariant();
            using (var mutex = new Mutex(true, mutexName, out bool isFirst))
            {
                if (!isFirst)
                {
                    MessageBox.Show("ToolCupboard is already running. Open it from the tray icon.", "ToolCupboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                {
                    string logPath = Path.Combine(AppInfo.RootPath, "logs");
                    Directory.CreateDirectory(logPath);
                    File.AppendAllText(Path.Combine(logPath, $"CRASH_{DateTime.Now:yyyyMMdd}.log"), $"ToolCupboard Web {AppInfo.Version}\n\n{args.ExceptionObject}");
                };

                Application.EnableVisualStyles();
                Application.Run(new TrayContext());
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly ServerManager _servers;
        private readonly NotifyIcon _tray;
        private WebServer _web;

        public TrayContext()
        {
            // The manager runs on this thread, the tray message loop plays the part of the window
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            _servers = new ServerManager(SynchronizationContext.Current);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open dashboard", null, (s, e) => OpenDashboard());
            menu.Items.Add("Exit", null, (s, e) => Exit());

            _tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath),
                Text = "ToolCupboard",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { OpenDashboard(); } };

            Start();
        }

        private async void Start()
        {
            ServerPath.CreateAndFixDirectories();
            _servers.ReloadServers();
            _servers.ReattachServers();
            _servers.AutoStartServer();
            if (AppOptions.DiscordBotAutoStart) { _ = _servers.StartBot(); }

            try
            {
                var web = new WebServer(_servers);
                await web.Start();
                _web = web;
                OpenDashboard();
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException)
            {
                string message = $"The dashboard could not start on port {WebServer.Port}. Is the ToolCupboard desktop app running?";
                _servers.Log("Web", $"{message} {e.Message}");
                _tray.ShowBalloonTip(10000, "ToolCupboard", message, ToolTipIcon.Warning);
            }
        }

        private void OpenDashboard()
        {
            if (_web != null) { ProcessManagement.OpenShell(_web.Url); }
        }

        private async void Exit()
        {
            _tray.Visible = false;
            _tray.Dispose();

            // Running servers keep going and are picked up again on the next start, as in the desktop app
            await _servers.Bot.Stop();
            if (_web != null) { await _web.Stop(); }
            ExitThread();
        }
    }
}
