using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Newtonsoft.Json.Linq;
using NCrontab;
using System.Collections.Generic;
using System.Collections;
using LiveCharts;
using LiveCharts.Wpf;
using System.Management;
using System.Windows.Media.Imaging;
using ControlzEx.Theming;
using ToolCupboard.Functions;
using Label = System.Windows.Controls.Label;
using Orientation = System.Windows.Controls.Orientation;
using System.Windows.Documents;
using MessageBox = System.Windows.MessageBox;

namespace ToolCupboard
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

        [DllImport("user32.dll")]
        private static extern int SetWindowText(IntPtr hWnd, string windowName);

        private static class RegistryKeyName
        {
            public const string HardWareAcceleration = "HardWareAcceleration";
            public const string UIAnimation = "UIAnimation";
            public const string DarkTheme = "DarkTheme";
            public const string StartOnBoot = "StartOnBoot";
            public const string RestartOnCrash = "RestartOnCrash";
            public const string ThemeColor = "DonorColor";
            public const string Height = "Height";
            public const string Width = "Width";
            public const string DiscordBotAutoStart = "DiscordBotAutoStart";
        }

        public class ServerMetadata
        {
            public ServerStatus ServerStatus = ServerStatus.Stopped;
            public Process Process;
            public IntPtr MainWindow;
            public ServerConsole ServerConsole;
            public RustRcon Rcon;

            // Basic Game Server Settings
            public bool AutoRestart;
            public bool AutoStart;
            public bool AutoUpdate;
            public bool UpdateOnStart;
            public bool BackupOnStart;

            // Discord Alert Settings
            public bool DiscordAlert;
            public string DiscordMessage;
            public string DiscordWebhook;
            public bool AutoRestartAlert;
            public bool AutoStartAlert;
            public bool AutoUpdateAlert;
            public bool RestartCrontabAlert;
            public bool CrashAlert;

            // Restart Crontab Settings
            public bool RestartCrontab;
            public bool WipeCrontab;
            public string WipeCrontabFormat;
            public RustWipeType WipeType;
            public bool WipeNewSeed;
            public DateTime WipeLast;
            public bool ModUpdatePending;
            public string CrontabFormat;

            // Game Server Start Priority and Affinity
            public string CPUPriority;
            public string CPUAffinity;

            public bool EmbedConsole;
            public bool AutoScroll;
        }

        private enum WindowShowStyle : uint
        {
            Hide = 0,
            ShowNormal = 1,
            Show = 5,
            Minimize = 6,
            ShowMinNoActivate = 7
        }

        public enum ServerStatus
        {
            Started = 0,
            Starting = 1,
            Stopped = 2,
            Stopping = 3,
            Restarted = 4,
            Restarting = 5,
            Updated = 6,
            Updating = 7,
            Backuped = 8,
            Backuping = 9,
            Restored = 10,
            Restoring = 11,
            Deleting = 12
        }

        public static readonly string APP_VERSION = "v" + string.Concat(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString().Reverse().Skip(2).Reverse());
        public static readonly int MAX_SERVER = 50;
        public static readonly string APP_PATH = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        public static readonly string DEFAULT_THEME = "Cyan";

        private readonly NotifyIcon notifyIcon;
        private Process Installer;

        public static readonly Dictionary<int, ServerMetadata> _serverMetadata = new Dictionary<int, ServerMetadata>();
        private ServerMetadata GetServerMetadata(object serverId) => _serverMetadata.TryGetValue(int.Parse(serverId.ToString()), out var s) ? s : null;


        private readonly List<System.Windows.Controls.CheckBox> _checkBoxes = new List<System.Windows.Controls.CheckBox>();


        private readonly DiscordBot.Bot g_DiscordBot = new DiscordBot.Bot();
        private Web.WebServer g_WebServer;
        private string g_UmodQuery = string.Empty;
        private int g_UmodPage = 1;
        private int g_UmodLastPage = 1;

        public MainWindow(bool showCrashHint)
        {
            //Add SplashScreen
            var splashScreen = new SplashScreen("Images/SplashScreen.png");
            splashScreen.Show(false, true);

            InitializeComponent();
            Title = $"ToolCupboard {APP_VERSION}";

            //Close SplashScreen
            splashScreen.Close(new TimeSpan(0, 0, 1));

            // Add all themes to comboBox_Themes
            ThemeManager.Current.Themes.Select(t => Path.GetExtension(t.Name).Trim('.')).Distinct().OrderBy(x => x).ToList().ForEach(delegate (string name) { comboBox_Themes.Items.Add(name); });

            // Set up _serverMetadata
            for (int i = 0; i < MAX_SERVER; i++)
            {
                _serverMetadata[i] = new ServerMetadata
                {
                    ServerStatus = ServerStatus.Stopped,
                    ServerConsole = new ServerConsole(i)
                };
            }

            var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard");
            if (key == null)
            {
                key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\ToolCupboard");
                key.SetValue(RegistryKeyName.HardWareAcceleration, "True");
                key.SetValue(RegistryKeyName.UIAnimation, "True");
                key.SetValue(RegistryKeyName.DarkTheme, "False");
                key.SetValue(RegistryKeyName.StartOnBoot, "False");
                key.SetValue(RegistryKeyName.RestartOnCrash, "False");
                key.SetValue(RegistryKeyName.ThemeColor, DEFAULT_THEME);
                key.SetValue(RegistryKeyName.Height, Height);
                key.SetValue(RegistryKeyName.Width, Width);
                key.SetValue(RegistryKeyName.DiscordBotAutoStart, "False");
            }

            MahAppSwitch_HardWareAcceleration.IsOn = (key.GetValue(RegistryKeyName.HardWareAcceleration) ?? true).ToString() == "True";
            MahAppSwitch_UIAnimation.IsOn = (key.GetValue(RegistryKeyName.UIAnimation) ?? true).ToString() == "True";
            MahAppSwitch_DarkTheme.IsOn = (key.GetValue(RegistryKeyName.DarkTheme) ?? false).ToString() == "True";
            MahAppSwitch_StartOnBoot.IsOn = (key.GetValue(RegistryKeyName.StartOnBoot) ?? false).ToString() == "True";
            MahAppSwitch_RestartOnCrash.IsOn = (key.GetValue(RegistryKeyName.RestartOnCrash) ?? false).ToString() == "True";
            MahAppSwitch_DiscordBotAutoStart.IsOn = (key.GetValue(RegistryKeyName.DiscordBotAutoStart) ?? false).ToString() == "True";
            string color = (key.GetValue(RegistryKeyName.ThemeColor) ?? string.Empty).ToString();
            comboBox_Themes.SelectionChanged -= ComboBox_Themes_SelectionChanged;
            comboBox_Themes.SelectedItem = comboBox_Themes.Items.Contains(color) ? color : DEFAULT_THEME;
            comboBox_Themes.SelectionChanged += ComboBox_Themes_SelectionChanged;

            Height = (key.GetValue(RegistryKeyName.Height) == null) ? Height : double.Parse(key.GetValue(RegistryKeyName.Height).ToString());
            Width = (key.GetValue(RegistryKeyName.Width) == null) ? Width : double.Parse(key.GetValue(RegistryKeyName.Width).ToString());
            key.Close();

            RenderOptions.ProcessRenderMode = MahAppSwitch_HardWareAcceleration.IsOn ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
            WindowTransitionsEnabled = MahAppSwitch_UIAnimation.IsOn;
            ThemeManager.Current.ChangeTheme(this, $"{(MahAppSwitch_DarkTheme.IsOn ? "Dark" : "Light")}.{comboBox_Themes.SelectedItem}");
            //Not required - it is set in windows settings
            //SetStartOnBoot(MahAppSwitch_StartOnBoot.IsChecked ?? false);
            if (MahAppSwitch_DiscordBotAutoStart.IsOn)
            {
                switch_DiscordBot.IsOn = true;
            }

            // Add items to Set Affinity Flyout
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                StackPanel stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(15, 0, 0, 0)
                };

                _checkBoxes.Add(new System.Windows.Controls.CheckBox());
                _checkBoxes[i].Focusable = false;
                var label = new Label
                {
                    Content = $"CPU {i}",
                    Padding = new Thickness(0, 5, 0, 5)
                };

                stackPanel.Children.Add(_checkBoxes[i]);
                stackPanel.Children.Add(label);
                StackPanel_SetAffinity.Children.Add(stackPanel);
            }

            // Add click listener on each checkBox
            foreach (var checkBox in _checkBoxes)
            {
                checkBox.Click += (sender, e) =>
                {
                    var server = (ServerTable)ServerGrid.SelectedItem;
                    if (server == null) { return; }

                CheckPrioity:
                    string priority = string.Empty;
                    for (int i = _checkBoxes.Count - 1; i >= 0; i--)
                    {
                        priority += (_checkBoxes[i].IsChecked ?? false) ? "1" : "0";
                    }

                    if (!priority.Contains("1"))
                    {
                        checkBox.IsChecked = true;
                        goto CheckPrioity;
                    }

                    textBox_SetAffinity.Text = Functions.CPU.Affinity.GetAffinityValidatedString(priority);

                    _serverMetadata[int.Parse(server.ID)].CPUAffinity = priority;
                    ServerConfig.SetSetting(server.ID, "cpuaffinity", priority);

                    if (GetServerMetadata(server.ID).Process != null && !GetServerMetadata(server.ID).Process.HasExited)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(priority);
                    }
                };
            }

            notifyIcon = new NotifyIcon
            {
                BalloonTipTitle = "ToolCupboard",
                BalloonTipText = "ToolCupboard is running in the background",
                Text = "ToolCupboard",
                BalloonTipIcon = ToolTipIcon.Info,
                Visible = true
            };

            notifyIcon.Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Images/ToolCupboard-Icon.ico")).Stream);
            notifyIcon.BalloonTipClicked += OnBalloonTipClick;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;

            ServerPath.CreateAndFixDirectories();


            LoadServerTable();

            if (ServerGrid.Items.Count > 0)
            {
                ServerGrid.SelectedItem = ServerGrid.Items[0];
            }

            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                int pid = ServerCache.GetPID(server.ID);
                if (pid != -1)
                {
                    Process p = null;
                    try
                    {
                        p = Process.GetProcessById(pid);
                    }
                    catch
                    {
                        continue;
                    }

                    string pName = ServerCache.GetProcessName(server.ID);
                    if (!string.IsNullOrWhiteSpace(pName) && p.ProcessName == pName)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process = p;
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        SetServerStatus(server, "Started");

                        /*// Get Console process - untested
                        Process console = GetConsoleProcess(pid);
                        if (console != null)
                        {
                            ReadConsoleOutput(server.ID, console);
                        }
                        */

                        _serverMetadata[int.Parse(server.ID)].MainWindow = ServerCache.GetWindowsIntPtr(server.ID);
                        p.Exited += (sender, e) => OnGameServerExited(server);

                        StartAutoUpdateCheck(server);
                        StartRestartCrontabCheck(server);
                        StartWipeCrontabCheck(server);
                        StartQuery(server);
                        StartRcon(server, p);
                    }
                }
            }

            if (showCrashHint)
            {
                string logFile = $"CRASH_{DateTime.Now:yyyyMMdd}.log";
                Log("System", $"ToolCupboard crashed unexpectedly, please view the crash log {logFile}");
            }

            AutoStartServer();

            StartConsoleRefresh();

            StartWebServer();

            StartServerTableRefresh();

            StartDashBoardRefresh();
        }

        private Process GetConsoleProcess(int processId)
        {
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={processId}");
                foreach (ManagementObject mo in mos.Get())
                {
                    Process p = Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]));
                    if (Equals(p, "conhost"))
                    {
                        return p;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        // Read console redirect output - not tested
        private async void ReadConsoleOutput(string serverId, Process p)
        {
            await Task.Run(() =>
            {
                var reader = p.StandardOutput;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        GetServerMetadata(serverId).ServerConsole.Add(line);
                    });
                }
            });
        }

        public void LoadServerTable()
        {
            string[] livePlayerData = new string[MAX_SERVER + 1];
            foreach (ServerTable item in ServerGrid.Items)
            {
                livePlayerData[int.Parse(item.ID)] = item.Maxplayers;
            }

            var selectedrow = (ServerTable)ServerGrid.SelectedItem;
            ServerGrid.Items.Clear();

            //Add server to datagrid
            for (int i = 1; i <= MAX_SERVER; i++)
            {
                string serverid_path = Path.Combine(APP_PATH, "servers", i.ToString());
                if (!Directory.Exists(serverid_path)) { continue; }

                string configpath = ServerPath.GetServersConfigs(i.ToString(), "ToolCupboard.cfg");
                if (!File.Exists(configpath)) { continue; }

                var serverConfig = new ServerConfig(i.ToString());

                //If Game server not exist return
                if (GameServer.Data.Class.Get(serverConfig.ServerGame) == null) { continue; }

                string status;
                switch (GetServerMetadata(i).ServerStatus)
                {
                    case ServerStatus.Started: status = "Started"; break;
                    case ServerStatus.Starting: status = "Starting"; break;
                    case ServerStatus.Stopped: status = "Stopped"; break;
                    case ServerStatus.Stopping: status = "Stopping"; break;
                    case ServerStatus.Restarted: status = "Restarted"; break;
                    case ServerStatus.Restarting: status = "Restarting"; break;
                    case ServerStatus.Updated: status = "Updated"; break;
                    case ServerStatus.Updating: status = "Updating"; break;
                    case ServerStatus.Backuped: status = "Backuped"; break;
                    case ServerStatus.Backuping: status = "Backuping"; break;
                    case ServerStatus.Restored: status = "Restored"; break;
                    case ServerStatus.Restoring: status = "Restoring"; break;
                    case ServerStatus.Deleting: status = "Deleting"; break;
                    default:
                        {
                            _serverMetadata[i].ServerStatus = ServerStatus.Stopped;
                            status = "Stopped";
                            break;
                        }
                }

                try
                {
                    string icon = "/ToolCupboard;component/Images/ToolCupboard.png";
                    string serverId = i.ToString();
                    string pidString = string.Empty;

                    try
                    {
                        pidString = Process.GetProcessById(ServerCache.GetPID(serverId)).Id.ToString();
                    }
                    catch { }

                    var server = new ServerTable
                    {
                        ID = i.ToString(),
                        PID = pidString,
                        Game = serverConfig.ServerGame,
                        Icon = icon,
                        Status = status,
                        Framework = RustMods.Detect(i.ToString()).ToString(),
                        Name = serverConfig.ServerName,
                        IP = serverConfig.ServerIP,
                        Port = serverConfig.ServerPort,
                        QueryPort = serverConfig.ServerQueryPort,
                        Defaultmap = serverConfig.ServerMap,
                        Maxplayers = (GetServerMetadata(i).ServerStatus != ServerStatus.Started) ? serverConfig.ServerMaxPlayer : livePlayerData[i]
                    };

                    SaveServerConfigToServerMetadata(i, serverConfig);
                    ServerGrid.Items.Add(server);

                    if (selectedrow != null)
                    {
                        if (selectedrow.ID == server.ID)
                        {
                            ServerGrid.SelectedItem = server;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            grid_action.Visibility = (ServerGrid.Items.Count != 0) ? Visibility.Visible : Visibility.Hidden;
            label_select.Visibility = grid_action.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
        }

        private void SaveServerConfigToServerMetadata(object serverId, ServerConfig serverConfig)
        {
            int i = int.Parse(serverId.ToString());

            // Basic Game Server Settings
            _serverMetadata[i].AutoRestart = serverConfig.AutoRestart;
            _serverMetadata[i].AutoStart = serverConfig.AutoStart;
            _serverMetadata[i].AutoUpdate = serverConfig.AutoUpdate;
            _serverMetadata[i].UpdateOnStart = serverConfig.UpdateOnStart;
            _serverMetadata[i].BackupOnStart = serverConfig.BackupOnStart;

            // Discord Alert Settings
            _serverMetadata[i].DiscordAlert = serverConfig.DiscordAlert;
            _serverMetadata[i].DiscordMessage = serverConfig.DiscordMessage;
            _serverMetadata[i].DiscordWebhook = serverConfig.DiscordWebhook;
            _serverMetadata[i].AutoRestartAlert = serverConfig.AutoRestartAlert;
            _serverMetadata[i].AutoStartAlert = serverConfig.AutoStartAlert;
            _serverMetadata[i].AutoUpdateAlert = serverConfig.AutoUpdateAlert;
            _serverMetadata[i].RestartCrontabAlert = serverConfig.RestartCrontabAlert;
            _serverMetadata[i].CrashAlert = serverConfig.CrashAlert;

            // Restart Crontab Settings
            _serverMetadata[i].RestartCrontab = serverConfig.RestartCrontab;
            _serverMetadata[i].WipeCrontab = serverConfig.WipeCrontab;
            _serverMetadata[i].WipeCrontabFormat = serverConfig.WipeCrontabFormat;
            _serverMetadata[i].WipeType = serverConfig.WipeType;
            _serverMetadata[i].WipeNewSeed = serverConfig.WipeNewSeed;
            _serverMetadata[i].WipeLast = serverConfig.WipeLast;
            _serverMetadata[i].CrontabFormat = serverConfig.CrontabFormat;

            // Game Server Start Priority and Affinity
            _serverMetadata[i].CPUPriority = serverConfig.CPUPriority;
            _serverMetadata[i].CPUAffinity = serverConfig.CPUAffinity;

            _serverMetadata[i].EmbedConsole = serverConfig.EmbedConsole;
            _serverMetadata[i].AutoScroll = serverConfig.AutoScroll;
        }

        private async void AutoStartServer()
        {
            foreach (ServerTable server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(serverId).AutoStart && GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server, " | Auto Start");

                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoStartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Started | Auto Start", server.Name, server.IP, server.Port);
                        }
                    }
                }
            }
        }

        private async void StartServerTableRefresh()
        {
            while (true)
            {
                await Task.Delay(60 * 1000);
                ServerGrid.Items.Refresh();
            }
        }

        private async void StartConsoleRefresh()
        {
            string lastServerId = null;

            while (true)
            {
                await Task.Delay(100);
                var row = (ServerTable)ServerGrid.SelectedItem;
                if (row != null)
                {
                    string text = GetServerMetadata(int.Parse(row.ID)).ServerConsole.Get();
                    if (text != console.Text)
                    {
                        // Setting Text resets the scroll position, follow new output only from the bottom
                        double offset = console.VerticalOffset;
                        bool atBottom = row.ID != lastServerId || offset + console.ViewportHeight >= console.ExtentHeight - 1;
                        lastServerId = row.ID;

                        console.Text = text;
                        console.UpdateLayout();

                        if (GetServerMetadata(row.ID).AutoScroll && atBottom)
                        {
                            console.ScrollToEnd();
                        }
                        else
                        {
                            console.ScrollToVerticalOffset(offset);
                        }
                    }
                }
            }
        }

        private async void StartDashBoardRefresh()
        {
            var system = new SystemMetrics();

            // Get CPU info and Set
            await Task.Run(() => system.GetCPUStaticInfo());
            dashboard_cpu_type.Content = system.CPUType;

            // Get RAM info and Set
            await Task.Run(() => system.GetRAMStaticInfo());
            dashboard_ram_type.Content = system.RAMType;

            // Get Disk info and Set
            await Task.Run(() => system.GetDiskStaticInfo());
            dashboard_disk_name.Content = $"({system.DiskName})";
            dashboard_disk_type.Content = system.DiskType;

            while (true)
            {
                dashboard_cpu_bar.Value = await Task.Run(() => system.GetCPUUsage());
                dashboard_cpu_bar.Value = (dashboard_cpu_bar.Value > 100.0) ? 100.0 : dashboard_cpu_bar.Value;
                dashboard_cpu_usage.Content = $"{dashboard_cpu_bar.Value}%";

                dashboard_ram_bar.Value = await Task.Run(() => system.GetRAMUsage());
                dashboard_ram_bar.Value = (dashboard_ram_bar.Value > 100.0) ? 100.0 : dashboard_ram_bar.Value;
                dashboard_ram_usage.Content = $"{string.Format("{0:0.00}", dashboard_ram_bar.Value)}%";
                dashboard_ram_ratio.Content = SystemMetrics.GetMemoryRatioString(dashboard_ram_bar.Value, system.RAMTotalSize);

                dashboard_disk_bar.Value = await Task.Run(() => system.GetDiskUsage());
                dashboard_disk_bar.Value = (dashboard_disk_bar.Value > 100.0) ? 100.0 : dashboard_disk_bar.Value;
                dashboard_disk_usage.Content = $"{string.Format("{0:0.00}", dashboard_disk_bar.Value)}%";
                dashboard_disk_ratio.Content = SystemMetrics.GetDiskRatioString(dashboard_disk_bar.Value, system.DiskTotalSize);

                dashboard_servers_bar.Value = ServerGrid.Items.Count * 100.0 / MAX_SERVER;
                dashboard_servers_bar.Value = (dashboard_servers_bar.Value > 100.0) ? 100.0 : dashboard_servers_bar.Value;
                dashboard_servers_usage.Content = $"{string.Format("{0:0.00}", dashboard_servers_bar.Value)}%";
                dashboard_servers_ratio.Content = $"{ServerGrid.Items.Count}/{MAX_SERVER}";

                int startedCount = GetStartedServerCount();
                dashboard_started_bar.Value = ServerGrid.Items.Count == 0 ? 0 : startedCount * 100.0 / ServerGrid.Items.Count;
                dashboard_started_bar.Value = (dashboard_started_bar.Value > 100.0) ? 100.0 : dashboard_started_bar.Value;
                dashboard_started_usage.Content = $"{string.Format("{0:0.00}", dashboard_started_bar.Value)}%";
                dashboard_started_ratio.Content = $"{startedCount}/{ServerGrid.Items.Count}";

                dashboard_players_count.Content = GetActivePlayers().ToString();

                Refresh_DashBoard_LiveChart();

                await Task.Delay(2000);
            }
        }

        public int GetStartedServerCount()
        {
            return ServerGrid.Items.Cast<ServerTable>().Where(s => s.Status == "Started").Count();
        }

        public int GetActivePlayers()
        {
            return ServerGrid.Items.Cast<ServerTable>().Where(s => s.Maxplayers != null && s.Maxplayers.Contains('/')).Sum(s => int.TryParse(s.Maxplayers.Split('/')[0], out int count) ? count : 0);
        }

        private void Refresh_DashBoard_LiveChart()
        {
            // List<(ServerType, PlayerCount)> Example: ("Ricochet Dedicated Server", 0)
            List<(string, int)> typePlayers = ServerGrid.Items.Cast<ServerTable>()
                .Where(s => s.Status == "Started" && s.Maxplayers != null && s.Maxplayers.Contains("/"))
                .Select(s => (type: s.Game, players: int.Parse(s.Maxplayers.Split('/')[0])))
                .GroupBy(s => s.type)
                .Select(s => (type: s.Key, players: s.Sum(p => p.players)))
                .ToList();

            // Ajust the maxvalue of axis Y base on PlayerCount
            if (typePlayers.Count > 0)
            {
                int maxValue = typePlayers.Select(s => s.Item2).Max() + 5;
                livechart_players_axisY.MaxValue = (maxValue > 10) ? maxValue : 10;
            }

            // Update the column data if updated, if ServerType doesn't exist remove
            for (int i = 0; i < livechart_players.Series.Count; i++)
            {
                if (typePlayers.Select(t => t.Item1).Contains(livechart_players.Series[i].Title))
                {
                    int currentPlayers = typePlayers.Where(t => t.Item1 == livechart_players.Series[i].Title).Select(t => t.Item2).FirstOrDefault();
                    if (((ChartValues<int>)livechart_players.Series[i].Values)[0] != currentPlayers)
                    {
                        livechart_players.Series[i].Values[0] = currentPlayers;
                    }

                    typePlayers.Remove((livechart_players.Series[i].Title, currentPlayers));
                }
                else
                {
                    livechart_players.Series.RemoveAt(i--);
                }
            }

            // Add ServerType Series if not exist
            foreach (var item in typePlayers)
            {
                livechart_players.Series.Add(new ColumnSeries
                {
                    Title = item.Item1,
                    Values = new ChartValues<int> { item.Item2 }
                });
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save height and width
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue("Height", Height.ToString());
                key?.SetValue("Width", Width.ToString());
            }

            // Get rid of system tray icon
            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            // Stop Discord Bot
            g_DiscordBot.Stop().ConfigureAwait(false);

            g_WebServer?.Stop().ConfigureAwait(false);
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerGrid.SelectedIndex != -1)
            {
                DataGrid_RefreshElements();
            }
        }

        private void DataGrid_RefreshElements()
        {
            var row = (ServerTable)ServerGrid.SelectedItem;

            if (row != null)
            {
                Console.WriteLine("Datagrid Changed");

                if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Stopped)
                {
                    button_Start.IsEnabled = true;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = true;
                    button_Backup.IsEnabled = true;
                    button_Players.IsEnabled = false;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }
                else if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Started)
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = true;
                    button_Restart.IsEnabled = true;
                    Process p = GetServerMetadata(row.ID).Process;
                    button_Console.IsEnabled = (p == null || p.HasExited) ? false : !(p.StartInfo.CreateNoWindow || p.StartInfo.RedirectStandardOutput);
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;
                    button_Players.IsEnabled = true;

                    textbox_servercommand.IsEnabled = true;
                    button_servercommand.IsEnabled = true;
                }
                else
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;
                    button_Players.IsEnabled = false;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }

                switch (GetServerMetadata(row.ID).ServerStatus)
                {
                    case ServerStatus.Restarting:
                    case ServerStatus.Restarted:
                    case ServerStatus.Started:
                    case ServerStatus.Starting:
                    case ServerStatus.Stopping:
                        button_Kill.IsEnabled = true;
                        break;
                    default: button_Kill.IsEnabled = false; break;
                }

                slider_ProcessPriority.Value = Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(row.ID).CPUPriority);
                textBox_ProcessPriority.Text = Functions.CPU.Priority.GetPriorityByInteger((int)slider_ProcessPriority.Value);

                textBox_SetAffinity.Text = Functions.CPU.Affinity.GetAffinityValidatedString(GetServerMetadata(row.ID).CPUAffinity);
                string affinity = new string(textBox_SetAffinity.Text.Reverse().ToArray());
                for (int i = 0; i < _checkBoxes.Count; i++)
                {
                    _checkBoxes[i].IsChecked = affinity[i] == '1';
                }

                button_Status.Content = row.Status.ToUpper();
                button_Status.Background = (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Started) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Orange;

                var gameServer = GameServer.Data.Class.Get(row.Game);
                switch_embedconsole.IsEnabled = gameServer.AllowsEmbedConsole;
                switch_embedconsole.IsOn = gameServer.AllowsEmbedConsole ? GetServerMetadata(row.ID).EmbedConsole : false;
                Button_AutoScroll.Content = GetServerMetadata(row.ID).AutoScroll ? "✔️ AUTO SCROLL" : "❌ AUTO SCROLL";

                switch_autorestart.IsOn = GetServerMetadata(row.ID).AutoRestart;
                switch_restartcrontab.IsOn = GetServerMetadata(row.ID).RestartCrontab;
                switch_autostart.IsOn = GetServerMetadata(row.ID).AutoStart;
                switch_autoupdate.IsOn = GetServerMetadata(row.ID).AutoUpdate;
                switch_updateonstart.IsOn = GetServerMetadata(row.ID).UpdateOnStart;
                switch_backuponstart.IsOn = GetServerMetadata(row.ID).BackupOnStart;
                switch_discordalert.IsOn = GetServerMetadata(row.ID).DiscordAlert;
                button_discordtest.IsEnabled = switch_discordalert.IsOn;

                textBox_restartcrontab.Text = GetServerMetadata(row.ID).CrontabFormat;
                textBox_nextcrontab.Text = CrontabSchedule.TryParse(textBox_restartcrontab.Text)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");

                MahAppSwitch_AutoStartAlert.IsOn = GetServerMetadata(row.ID).AutoStartAlert;
                MahAppSwitch_AutoRestartAlert.IsOn = GetServerMetadata(row.ID).AutoRestartAlert;
                MahAppSwitch_AutoUpdateAlert.IsOn = GetServerMetadata(row.ID).AutoUpdateAlert;
                MahAppSwitch_RestartCrontabAlert.IsOn = GetServerMetadata(row.ID).RestartCrontabAlert;
                MahAppSwitch_CrashAlert.IsOn = GetServerMetadata(row.ID).CrashAlert;
            }
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGrid.Items.Count >= MAX_SERVER)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            MahAppFlyout_InstallGameServer.IsOpen = true;

            if (!progressbar_InstallProgress.IsIndeterminate)
            {
                textbox_InstallServerName.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;
                textblock_InstallProgress.Text = string.Empty;
                button_Install.IsEnabled = true;

                var newServerConfig = new ServerConfig(null);
                textbox_InstallServerName.Text = $"Rust Server #{newServerConfig.ServerID}";
            }
        }

        private async void Button_Install_Click(object sender, RoutedEventArgs e)
        {
            if (Installer != null)
            {
                if (!Installer.HasExited) { Installer.Kill(); }
                Installer = null;
            }

            if (string.IsNullOrWhiteSpace(textbox_InstallServerName.Text)) { return; }

            var newServerConfig = new ServerConfig(null);
            string installPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(installPath))
            {
                try
                {
                    Directory.Delete(installPath, true);
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show(installPath + " is not accessible!", "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            Directory.CreateDirectory(installPath);

            //Installation start
            textbox_InstallServerName.IsEnabled = false;
            progressbar_InstallProgress.IsIndeterminate = true;
            textblock_InstallProgress.Text = "Installing";
            button_Install.IsEnabled = false;
            textbox_InstallLog.Text = string.Empty;

            string servername = textbox_InstallServerName.Text;
            string servergame = GameServer.RUST.FullName;

            newServerConfig.CreateServerDirectory();

            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig);
            Installer = await gameServer.Install();

            if (Installer != null)
            {
                //Wait installer exit. Example: steamcmd.exe
                await Task.Run(() =>
                {
                    var reader = Installer.StandardOutput;
                    while (!reader.EndOfStream)
                    {
                        var nextLine = reader.ReadLine();
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            textbox_InstallLog.AppendText(nextLine + Environment.NewLine);
                            textbox_InstallLog.ScrollToEnd();
                        });
                    }

                    Installer?.WaitForExit();
                });
            }

            if (gameServer.IsInstallValid())
            {
                newServerConfig.ServerIP = newServerConfig.GetIPAddress();
                newServerConfig.ServerPort = newServerConfig.GetAvailablePort(gameServer.Port, gameServer.PortIncrements);

                // Create ToolCupboard.cfg
                newServerConfig.SetData(servergame, servername, gameServer);
                newServerConfig.CreateToolCupboardConfig();

                // Create ToolCupboard.cfg and game server config
                try
                {
                    gameServer = GameServer.Data.Class.Get(servergame, newServerConfig);
                    gameServer.CreateServerCFG();
                }
                catch
                {
                    // ignore
                }

                LoadServerTable();
                Log(newServerConfig.ServerID, "Install: Success");

                MahAppFlyout_InstallGameServer.IsOpen = false;
                textbox_InstallServerName.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;

            }
            else
            {
                textbox_InstallServerName.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;
                textblock_InstallProgress.Text = "Install";
                button_Install.IsEnabled = true;

                if (Installer != null)
                {
                    textblock_InstallProgress.Text = "Fail to install [ERROR] Exit code: " + Installer.ExitCode;
                }
                else
                {
                    textblock_InstallProgress.Text = $"Fail to install [ERROR] {gameServer.Error}";
                }
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGrid.Items.Count >= MAX_SERVER)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            MahAppFlyout_ImportGameServer.IsOpen = true;

            if (!progressbar_ImportProgress.IsIndeterminate)
            {
                textbox_ImportServerName.IsEnabled = true;
                progressbar_ImportProgress.IsIndeterminate = false;
                textblock_ImportProgress.Text = string.Empty;
                button_Import.Content = "Import";

                var newServerConfig = new ServerConfig(null);
                textbox_ImportServerName.Text = $"Rust Server #{newServerConfig.ServerID}";
            }
        }

        private async void Button_Import_Click(object sender, RoutedEventArgs e)
        {
            label_ServerDirWarn.Content = Directory.Exists(textbox_ServerDir.Text) ? string.Empty : "Server Dir is invalid";
            if (string.IsNullOrWhiteSpace(textbox_ImportServerName.Text)) { return; }

            string servername = textbox_ImportServerName.Text;
            string servergame = GameServer.RUST.FullName;

            var newServerConfig = new ServerConfig(null);
            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig);

            if (!gameServer.IsImportValid(textbox_ServerDir.Text))
            {
                label_ServerDirWarn.Content = gameServer.Error;
                return;
            }

            string importPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(importPath))
            {
                try
                {
                    Directory.Delete(importPath, true);
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show(importPath + " is not accessible!", "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            //Import start
            textbox_ImportServerName.IsEnabled = false;
            textbox_ServerDir.IsEnabled = false;
            button_Browse.IsEnabled = false;
            progressbar_ImportProgress.IsIndeterminate = true;
            textblock_ImportProgress.Text = "Importing";

            string sourcePath = textbox_ServerDir.Text;
            string importLog = await Task.Run(() =>
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(sourcePath, importPath);

                    // Scary error while moving the directory, some files may lost - Risky
                    //Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourcePath, importPath);

                    // This doesn't work on cross drive - Not working on cross drive
                    //Directory.Move(sourcePath, importPath);

                    return null;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            });

            if (importLog != null)
            {
                textbox_ImportServerName.IsEnabled = true;
                textbox_ServerDir.IsEnabled = true;
                button_Browse.IsEnabled = true;
                progressbar_ImportProgress.IsIndeterminate = false;
                textblock_ImportProgress.Text = "[ERROR] Fail to import";
                MessageBox.Show($"Fail to copy the directory.\n{textbox_ServerDir.Text}\nto\n{importPath}\n\nYou may install a new server and copy the old servers file to the new server.\n\nException: {importLog}", "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Create ToolCupboard.cfg
            newServerConfig.SetData(servergame, servername, gameServer);
            newServerConfig.CreateToolCupboardConfig();

            LoadServerTable();
            Log(newServerConfig.ServerID, "Import: Success");

            MahAppFlyout_ImportGameServer.IsOpen = false;
            textbox_ImportServerName.IsEnabled = true;
            textbox_ServerDir.IsEnabled = true;
            button_Browse.IsEnabled = true;
            progressbar_ImportProgress.IsIndeterminate = false;
            textblock_ImportProgress.Text = string.Empty;
        }

        private void Button_Browse_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.ShowDialog();

            if (!string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
            {
                textbox_ServerDir.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = MessageBox.Show("Do you want to delete this server?\n(There is no comeback)", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Delete(server);
        }

        private async void Button_DiscordEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string webhookUrl = ServerConfig.GetSetting(server.ID, Functions.ServerConfig.SettingName.DiscordWebhook);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Save",
                DefaultText = webhookUrl
            };

            webhookUrl = await this.ShowInputAsync("Discord Webhook URL", "Please enter the discord webhook url.", settings);
            if (webhookUrl == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].DiscordWebhook = webhookUrl;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordWebhook, webhookUrl);
        }

        private async void Button_DiscordSetMessage_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var message = ServerConfig.GetSetting(server.ID, ServerConfig.SettingName.DiscordMessage);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Save",
                DefaultText = message
            };

            message = await this.ShowInputAsync("Discord Custom Message", "Please enter the custom message.\n\nExample ping message <@discorduserid>:\n<@348921660361146380>", settings);
            if (message == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].DiscordMessage = message;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordMessage, message);
        }

        private async void Button_DiscordWebhookTest_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            int serverId = int.Parse(server.ID);
            if (!GetServerMetadata(serverId).DiscordAlert) { return; }

            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
            await webhook.Send(server.ID, server.Game, "Webhook Test Alert", server.Name, server.IP, server.Port);
        }

        private void Button_ServerCommand_Click(object sender, RoutedEventArgs e)
        {
            string command = textbox_servercommand.Text;
            textbox_servercommand.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(command)) { return; }

            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            SendCommand(server, command);
        }

        private void Textbox_ServerCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (textbox_servercommand.Text.Length != 0)
                {
                    GetServerMetadata(0).ServerConsole.Add(textbox_servercommand.Text);
                }

                Button_ServerCommand_Click(this, new RoutedEventArgs());
            }
        }

        private void Textbox_ServerCommand_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.IsDown && e.Key == Key.Up)
            {
                e.Handled = true;
                textbox_servercommand.Text = GetServerMetadata(0).ServerConsole.GetPreviousCommand();
            }
            else if (e.IsDown && e.Key == Key.Down)
            {
                e.Handled = true;
                textbox_servercommand.Text = GetServerMetadata(0).ServerConsole.GetNextCommand();
            }
        }

        #region Web UI
        private async void StartWebServer()
        {
            try
            {
                g_WebServer = new Web.WebServer(this);
                await g_WebServer.Start();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                g_WebServer = null;
                Log("System", $"[NOTICE] The web UI is unavailable: {e.Message}");
            }
        }

        private async void Button_WebUI_Click(object sender, RoutedEventArgs e)
        {
            if (g_WebServer == null)
            {
                await this.ShowMessageAsync("Web UI", "The web UI did not start. Check the log for the reason.");
                return;
            }

            ProcessManagement.OpenShell(g_WebServer.Url);
        }

        public object[] GetWebServers()
        {
            return ServerGrid.Items.Cast<ServerTable>().Select(server => (object)new
            {
                id = server.ID,
                name = server.Name,
                status = server.Status,
                framework = RustMods.Detect(server.ID).ToString(),
                ip = server.IP,
                port = server.Port,
                map = server.Defaultmap,
                players = server.Maxplayers,
                uptime = server.Uptime
            }).ToArray();
        }

        // Returns null for an unknown server or action, and false if the server is in the wrong state
        public async Task<bool?> RunWebAction(string serverId, string action)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            ServerStatus status = GetServerMetadata(server.ID).ServerStatus;
            bool stopped = status == ServerStatus.Stopped;
            bool started = status == ServerStatus.Started;

            switch (action)
            {
                case "start": if (!stopped) { return false; } await GameServer_Start(server); return true;
                case "stop": if (!started) { return false; } await GameServer_Stop(server); return true;
                case "restart": if (!started) { return false; } await GameServer_Restart(server); return true;
                case "update": if (!stopped) { return false; } await GameServer_Update(server); return true;
                case "backup": if (!stopped) { return false; } await GameServer_Backup(server); return true;
                case "install-oxide": return stopped && await RunWebInstall(server, RustModFramework.Oxide);
                case "install-carbon": return stopped && await RunWebInstall(server, RustModFramework.Carbon);
                default: return null;
            }
        }

        private async Task<bool> RunWebInstall(ServerTable server, RustModFramework framework)
        {
            string error = await RustMods.Install(server.ID, framework);
            Log(server.ID, error == null ? $"Mods: {framework} installed" : $"[ERROR] Fail to install {framework}: {error}");
            server.Framework = RustMods.Detect(server.ID).ToString();
            ServerGrid.Items.Refresh();
            return error == null;
        }

        public RustPlugin[] GetWebPlugins(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            RustModFramework framework = RustMods.Detect(server.ID);
            return framework == RustModFramework.None ? Array.Empty<RustPlugin>() : RustMods.GetPlugins(server.ID, framework).ToArray();
        }

        public async Task<string> InstallWebPlugin(string serverId, string slug)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }

            string error = await UmodCatalog.Install(server.ID, RustMods.Detect(server.ID), slug);
            Log(server.ID, error == null ? $"Mods: Installed {slug} from uMod" : $"[ERROR] Fail to install {slug}: {error}");
            return error;
        }

        // The name comes from the page, so it must match a plugin that is on disk
        public async Task<bool?> RunWebPluginAction(string serverId, string name, string action)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            RustModFramework framework = RustMods.Detect(server.ID);
            RustPlugin plugin = framework == RustModFramework.None ? null : RustMods.GetPlugins(server.ID, framework).FirstOrDefault(p => p.Name == name);
            if (plugin == null) { return null; }

            switch (action)
            {
                case "enable": RustMods.SetEnabled(server.ID, framework, plugin, true); break;
                case "disable": RustMods.SetEnabled(server.ID, framework, plugin, false); break;
                case "delete": File.Delete(plugin.Path); break;
                case "reload":
                    RustRcon rcon = GetServerMetadata(server.ID).Rcon;
                    if (rcon == null || !rcon.IsConnected) { return false; }
                    await rcon.SendAsync(RustMods.GetReloadCommand(framework, plugin.Name));
                    break;
                default: return null;
            }

            Log(server.ID, $"Mods: {action} plugin {plugin.Name} from the web UI");
            return true;
        }

        public string[] GetWebConsole(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            return server == null ? null : GetServerMetadata(server.ID).ServerConsole.Get().Split(Environment.NewLine);
        }

        public bool SendWebCommand(string serverId, string command)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null || GetServerMetadata(server.ID).Process == null) { return false; }

            SendCommand(server, command);
            return true;
        }

        public RustRcon GetWebRcon(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            return server == null ? null : GetServerMetadata(server.ID).Rcon;
        }
        #endregion

        #region Server Settings
        private void Tools_ServerSettings_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            textBox_SettingWorldSize.Text = RustConfig.Get(server.ID, "server.worldsize");
            textBox_SettingSeed.Text = RustConfig.Get(server.ID, "server.seed");
            textBox_SettingTickRate.Text = RustConfig.Get(server.ID, "server.tickrate");
            textBox_SettingSaveInterval.Text = RustConfig.Get(server.ID, "server.saveinterval");
            textBox_SettingDescription.Text = RustConfig.Get(server.ID, "server.description");
            textBox_SettingUrl.Text = RustConfig.Get(server.ID, "server.url");
            textBox_SettingHeaderImage.Text = RustConfig.Get(server.ID, "server.headerimage");
            checkBox_SettingRustPlus.IsChecked = RustConfig.Get(server.ID, "app.port") != "-1";
            label_SettingsStatus.Content = string.Empty;

            ToggleMahappFlyout(MahAppFlyout_ServerSettings);
        }

        private static bool IsNumberInRange(string text, int min, int max)
        {
            return int.TryParse(text, out int value) && value >= min && value <= max;
        }

        private void Button_SettingsSave_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string error = null;
            if (!IsNumberInRange(textBox_SettingWorldSize.Text, 1000, 6000)) { error = "World size must be between 1000 and 6000"; }
            else if (!IsNumberInRange(textBox_SettingSeed.Text, 0, int.MaxValue)) { error = "Seed must be a positive number"; }
            else if (!IsNumberInRange(textBox_SettingTickRate.Text, 10, 100)) { error = "Tick rate must be between 10 and 100"; }
            else if (!IsNumberInRange(textBox_SettingSaveInterval.Text, 60, 3600)) { error = "Save interval must be between 60 and 3600 seconds"; }

            if (error != null)
            {
                label_SettingsStatus.Content = error;
                return;
            }

            RustConfig.Set(server.ID, "server.worldsize", textBox_SettingWorldSize.Text);
            RustConfig.Set(server.ID, "server.seed", textBox_SettingSeed.Text);
            RustConfig.Set(server.ID, "server.tickrate", textBox_SettingTickRate.Text);
            RustConfig.Set(server.ID, "server.saveinterval", textBox_SettingSaveInterval.Text);
            RustConfig.Set(server.ID, "server.description", textBox_SettingDescription.Text);
            RustConfig.Set(server.ID, "server.url", textBox_SettingUrl.Text);
            RustConfig.Set(server.ID, "server.headerimage", textBox_SettingHeaderImage.Text);

            // The app port defaults to the highest server port + 67, so only the disabled state is stored
            bool rustPlus = checkBox_SettingRustPlus.IsChecked == true;
            if (!rustPlus || RustConfig.Get(server.ID, "app.port") == "-1")
            {
                RustConfig.Set(server.ID, "app.port", rustPlus ? (int.Parse(server.Port) + 68).ToString() : "-1");
            }

            label_SettingsStatus.Content = "Saved. Changes apply the next time the server starts.";
            Log(server.ID, "Settings: server.cfg updated");
        }
        #endregion

        #region Mods and Plugins
        private async Task UpdateModFramework(ServerTable server)
        {
            RustModFramework framework = RustMods.Detect(server.ID);
            if (framework == RustModFramework.None) { return; }

            // The Oxide zip contains game files, so an older release would replace the new ones
            if (framework == RustModFramework.Oxide)
            {
                var steamCMD = new Installer.SteamCMD();
                await steamCMD.GetRemoteBuild(new GameServer.RUST(null).AppId);

                if (steamCMD.RemoteBuildTime == null || !await RustMods.IsOxideCurrent(steamCMD.RemoteBuildTime.Value))
                {
                    _serverMetadata[int.Parse(server.ID)].ModUpdatePending = true;
                    Log(server.ID, "[NOTICE] Oxide has no release for this Rust update yet. Plugins stay off until a restart after Oxide releases.");
                    return;
                }
            }

            _serverMetadata[int.Parse(server.ID)].ModUpdatePending = false;
            string error = await RustMods.Install(server.ID, framework);
            Log(server.ID, error == null ? $"Mods: {framework} updated" : $"[ERROR] Fail to update {framework}: {error}");
        }

        private async void Tools_ModsAndPlugins_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGrid.SelectedItem == null) { return; }

            RefreshMods();
            ToggleMahappFlyout(MahAppFlyout_Mods);

            if (dataGrid_Umod.ItemsSource == null)
            {
                await SearchUmod(1);
            }
        }

        private void DataGrid_Plugins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool selected = dataGrid_Plugins.SelectedItem != null;
            button_PluginToggle.IsEnabled = selected;
            button_PluginReload.IsEnabled = selected;
            button_PluginConfig.IsEnabled = selected;
            button_PluginDelete.IsEnabled = selected;
        }

        private void DataGrid_Umod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            button_UmodInstall.IsEnabled = dataGrid_Umod.SelectedItem != null;
        }

        private async Task SearchUmod(int page)
        {
            label_UmodStatus.Content = "Searching uMod";
            button_UmodSearch.IsEnabled = false;

            try
            {
                UmodPage result = await UmodCatalog.Search(g_UmodQuery, page);
                g_UmodPage = result.Page;
                g_UmodLastPage = result.LastPage;
                dataGrid_Umod.ItemsSource = result.Items;
                MarkInstalledUmodPlugins();
                label_UmodStatus.Content = result.Items.Count == 0 ? "No Rust plugins matched that search" : $"Page {g_UmodPage} of {g_UmodLastPage}";
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is TaskCanceledException || e is Newtonsoft.Json.JsonException)
            {
                dataGrid_Umod.ItemsSource = new List<UmodPlugin>();
                g_UmodLastPage = 1;
                label_UmodStatus.Content = "Could not reach uMod. Try again in a moment.";
            }

            button_UmodSearch.IsEnabled = true;
            button_UmodPrevious.IsEnabled = g_UmodPage > 1;
            button_UmodNext.IsEnabled = g_UmodPage < g_UmodLastPage;
        }

        private void MarkInstalledUmodPlugins()
        {
            var server = ServerGrid.SelectedItem as ServerTable;
            if (server == null || !(dataGrid_Umod.ItemsSource is List<UmodPlugin> results)) { return; }

            RustModFramework framework = RustMods.Detect(server.ID);
            var installed = framework == RustModFramework.None ? new List<RustPlugin>() : RustMods.GetPlugins(server.ID, framework);
            foreach (UmodPlugin plugin in results)
            {
                plugin.Installed = installed.Any(p => p.Name == plugin.Name);
            }

            dataGrid_Umod.Items.Refresh();
        }

        private async void Button_UmodSearch_Click(object sender, RoutedEventArgs e)
        {
            g_UmodQuery = textBox_UmodSearch.Text.Trim();
            await SearchUmod(1);
        }

        private void TextBox_UmodSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Button_UmodSearch_Click(sender, e);
            }
        }

        private async void Button_UmodPrevious_Click(object sender, RoutedEventArgs e)
        {
            await SearchUmod(g_UmodPage - 1);
        }

        private async void Button_UmodNext_Click(object sender, RoutedEventArgs e)
        {
            await SearchUmod(g_UmodPage + 1);
        }

        private async void Button_UmodInstall_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            var plugin = dataGrid_Umod.SelectedItem as UmodPlugin;
            if (server == null || plugin == null) { return; }

            string note = plugin.Unmaintained ? "\n\nuMod marks this plugin as unmaintained." : string.Empty;
            var result = await this.ShowMessageAsync("Install Plugin", $"Install {plugin.Title}? Plugins are code that runs on your server. It may need other plugins, so check its uMod page.{note}", MessageDialogStyle.AffirmativeAndNegative);
            if (result != MessageDialogResult.Affirmative) { return; }

            ProgressDialogController controller = await this.ShowProgressAsync($"Installing {plugin.Title}...", "Please wait...");
            controller.SetIndeterminate();
            string error = await InstallWebPlugin(server.ID, plugin.Slug);
            await controller.CloseAsync();

            if (error != null)
            {
                await this.ShowMessageAsync("Install Plugin", error);
            }

            RefreshMods();
        }

        private void RefreshMods()
        {
            var server = ServerGrid.SelectedItem as ServerTable;
            if (server == null) { return; }

            RustModFramework framework = RustMods.Detect(server.ID);
            server.Framework = framework.ToString();
            ServerGrid.Items.Refresh();
            label_ModFramework.Content = framework == RustModFramework.None ? "No mod framework installed" : $"{framework} installed";
            button_RemoveFramework.IsEnabled = framework != RustModFramework.None;
            button_PluginAdd.IsEnabled = framework != RustModFramework.None;
            dataGrid_Plugins.ItemsSource = framework == RustModFramework.None ? null : RustMods.GetPlugins(server.ID, framework);
            MarkInstalledUmodPlugins();
        }

        private async Task<bool> RequireStoppedForMods(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped) { return true; }

            await this.ShowMessageAsync("Mods and Plugins", "Stop the server first.");
            return false;
        }

        private async void Button_InstallOxide_Click(object sender, RoutedEventArgs e)
        {
            await InstallFramework(RustModFramework.Oxide);
        }

        private async void Button_InstallCarbon_Click(object sender, RoutedEventArgs e)
        {
            await InstallFramework(RustModFramework.Carbon);
        }

        private async Task InstallFramework(RustModFramework framework)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null || !await RequireStoppedForMods(server)) { return; }

            ProgressDialogController controller = await this.ShowProgressAsync($"Installing {framework}...", "Please wait...");
            controller.SetIndeterminate();
            string error = await RustMods.Install(server.ID, framework);
            await controller.CloseAsync();

            Log(server.ID, error == null ? $"Mods: {framework} installed" : $"[ERROR] Fail to install {framework}: {error}");
            if (error != null)
            {
                await this.ShowMessageAsync("Mods and Plugins", error);
            }

            RefreshMods();
        }

        private async void Button_RemoveFramework_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null || !await RequireStoppedForMods(server)) { return; }

            RustModFramework framework = RustMods.Detect(server.ID);
            string note = framework == RustModFramework.Oxide ? " An Update Validate will run to restore the game files." : string.Empty;
            var result = await this.ShowMessageAsync("Mods and Plugins", $"Remove {framework}? Plugins and their configs are kept.{note}", MessageDialogStyle.AffirmativeAndNegative);
            if (result != MessageDialogResult.Affirmative) { return; }

            if (framework == RustModFramework.Carbon)
            {
                RustMods.RemoveCarbon(server.ID);
            }
            else
            {
                RustMods.RemoveOxide(server.ID);
                await GameServer_Update(server, validate: true);
            }

            Log(server.ID, $"Mods: {framework} removed");
            RefreshMods();
        }

        private void Button_PluginAdd_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "Rust plugins (*.cs)|*.cs", Multiselect = true };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) { return; }

            RustModFramework framework = RustMods.Detect(server.ID);
            foreach (string file in dialog.FileNames)
            {
                RustMods.AddPlugin(server.ID, framework, file);
                Log(server.ID, $"Mods: Added plugin {Path.GetFileName(file)}");
            }

            RefreshMods();
        }

        private void Button_PluginToggle_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            var plugin = dataGrid_Plugins.SelectedItem as RustPlugin;
            if (server == null || plugin == null) { return; }

            RustMods.SetEnabled(server.ID, RustMods.Detect(server.ID), plugin, !plugin.Enabled);
            Log(server.ID, $"Mods: {(plugin.Enabled ? "Disabled" : "Enabled")} plugin {plugin.Name}");
            RefreshMods();
        }

        private async void Button_PluginDelete_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            var plugin = dataGrid_Plugins.SelectedItem as RustPlugin;
            if (server == null || plugin == null) { return; }

            var result = await this.ShowMessageAsync("Mods and Plugins", $"Delete {plugin.Name}? Its config is kept.", MessageDialogStyle.AffirmativeAndNegative);
            if (result != MessageDialogResult.Affirmative) { return; }

            File.Delete(plugin.Path);
            Log(server.ID, $"Mods: Deleted plugin {plugin.Name}");
            RefreshMods();
        }

        private async void Button_PluginConfig_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            var plugin = dataGrid_Plugins.SelectedItem as RustPlugin;
            if (server == null || plugin == null) { return; }

            if (!plugin.HasConfig)
            {
                await this.ShowMessageAsync("Open Config", $"{plugin.Name} has no config file yet. Plugins create one the first time they load on a running server, and some plugins never need one.");
                return;
            }

            ProcessManagement.OpenShell(RustMods.GetConfigPath(server.ID, RustMods.Detect(server.ID), plugin.Name));
        }

        private async void Button_PluginReload_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            var plugin = dataGrid_Plugins.SelectedItem as RustPlugin;
            if (server == null || plugin == null) { return; }

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon == null || !rcon.IsConnected)
            {
                await this.ShowMessageAsync("Mods and Plugins", "RCON is not connected.");
                return;
            }

            string reply = await rcon.SendAsync(RustMods.GetReloadCommand(RustMods.Detect(server.ID), plugin.Name));
            Log(server.ID, $"Mods: Reload {plugin.Name}: {reply}");
        }
        #endregion

        #region Wipe
        private void Tools_WipeServer_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ServerMetadata metadata = GetServerMetadata(server.ID);
            radio_WipeMap.IsChecked = metadata.WipeType == RustWipeType.Map;
            radio_WipeBlueprints.IsChecked = metadata.WipeType == RustWipeType.Blueprints;
            radio_WipeFull.IsChecked = metadata.WipeType == RustWipeType.Full;
            checkBox_WipeNewSeed.IsChecked = metadata.WipeNewSeed;
            switch_WipeSchedule.IsOn = metadata.WipeCrontab;
            textBox_WipeCrontab.Text = metadata.WipeCrontabFormat;
            label_WipeSaved.Content = string.Empty;

            RefreshWipeSummary();
            ToggleMahappFlyout(MahAppFlyout_Wipe);
        }

        private RustWipeType GetSelectedWipeType()
        {
            if (radio_WipeFull.IsChecked == true) { return RustWipeType.Full; }
            return radio_WipeBlueprints.IsChecked == true ? RustWipeType.Blueprints : RustWipeType.Map;
        }

        private void WipeType_Checked(object sender, RoutedEventArgs e)
        {
            RefreshWipeSummary();
        }

        private void TextBox_WipeCrontab_TextChanged(object sender, TextChangedEventArgs e)
        {
            DateTime? next = CrontabSchedule.TryParse(textBox_WipeCrontab.Text)?.GetNextOccurrence(DateTime.Now);
            label_WipeNext.Content = next == null ? "Invalid schedule" : $"Next wipe: {next:ddd, MM/dd/yyyy HH:mm}";
        }

        private void RefreshWipeSummary()
        {
            var server = ServerGrid.SelectedItem as ServerTable;
            if (server == null) { return; }

            label_WipeFiles.Content = $"{RustWipe.GetFiles(server.ID, GetSelectedWipeType()).Count} files will be wiped";
        }

        private void Button_WipeSave_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (CrontabSchedule.TryParse(textBox_WipeCrontab.Text) == null)
            {
                label_WipeSaved.Content = "Invalid schedule";
                return;
            }

            ServerMetadata metadata = _serverMetadata[int.Parse(server.ID)];
            metadata.WipeType = GetSelectedWipeType();
            metadata.WipeNewSeed = checkBox_WipeNewSeed.IsChecked == true;
            metadata.WipeCrontab = switch_WipeSchedule.IsOn;
            metadata.WipeCrontabFormat = textBox_WipeCrontab.Text;
            metadata.WipeLast = DateTime.Now;

            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeType, metadata.WipeType.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeNewSeed, metadata.WipeNewSeed ? "1" : "0");
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeCrontab, metadata.WipeCrontab ? "1" : "0");
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeCrontabFormat, metadata.WipeCrontabFormat);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeLast, metadata.WipeLast.ToString("o"));
            label_WipeSaved.Content = "Saved";
        }

        private async void Button_WipeNow_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                await this.ShowMessageAsync("Wipe Server", "Stop the server before wiping it.");
                return;
            }

            RustWipeType type = GetSelectedWipeType();
            int fileCount = RustWipe.GetFiles(server.ID, type).Count;
            var result = await this.ShowMessageAsync("Wipe Server", $"Run a {type} wipe on {server.Name}? {fileCount} files will be moved to a backup folder.", MessageDialogStyle.AffirmativeAndNegative);
            if (result != MessageDialogResult.Affirmative || GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            SetServerStatus(server, "Wiping");

            await WipeServerData(server, type, checkBox_WipeNewSeed.IsChecked == true);

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");
            RefreshWipeSummary();
        }
        #endregion

        #region Players
        private async void Actions_Players_Click(object sender, RoutedEventArgs e)
        {
            ToggleMahappFlyout(MahAppFlyout_Players);
            await RefreshPlayers();
        }

        private async void Button_PlayersRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPlayers();
        }

        private async Task RefreshPlayers()
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon == null || !rcon.IsConnected)
            {
                dataGrid_Players.ItemsSource = null;
                label_PlayersStatus.Content = "RCON is not connected yet";
                return;
            }

            List<RustPlayer> players = await rcon.GetPlayersAsync();
            dataGrid_Players.ItemsSource = players;
            label_PlayersStatus.Content = players == null ? "Fail to get the player list" : $"{players.Count} online";
        }

        private async void Button_PlayerKick_Click(object sender, RoutedEventArgs e)
        {
            await ApplyToSelectedPlayer("Kick", (rcon, player, reason) => rcon.KickAsync(player, reason));
        }

        private async void Button_PlayerBan_Click(object sender, RoutedEventArgs e)
        {
            await ApplyToSelectedPlayer("Ban", (rcon, player, reason) => rcon.BanAsync(player, reason));
        }

        private async Task ApplyToSelectedPlayer(string action, Func<RustRcon, RustPlayer, string, Task<string>> apply)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            var player = (RustPlayer)dataGrid_Players.SelectedItem;
            if (server == null || player == null) { return; }

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon == null || !rcon.IsConnected) { return; }

            var result = await this.ShowMessageAsync($"{action} Player", $"{action} {player.DisplayName}?", MessageDialogStyle.AffirmativeAndNegative);
            if (result != MessageDialogResult.Affirmative) { return; }

            await apply(rcon, player, textbox_PlayerReason.Text.Trim());
            Log(server.ID, $"Player: {action} {player.DisplayName} ({player.SteamID})");
            textbox_PlayerReason.Text = string.Empty;
            await RefreshPlayers();
        }
        #endregion

        #region Actions - Button Click
        private void Actions_Crash_Click(object sender, RoutedEventArgs e)
        {
            int test = 0;
            _ = 0 / test; // Crash
        }

        private async void Actions_Start_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            // Reload ToolCupboard.cfg on start
            SaveServerConfigToServerMetadata(server.ID, new ServerConfig(server.ID));

            await GameServer_Start(server);
        }

        private async void Actions_Stop_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await GameServer_Stop(server);
        }

        private async void Actions_Restart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await GameServer_Restart(server);
        }

        private async void Actions_Kill_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            switch (GetServerMetadata(server.ID).ServerStatus)
            {
                case ServerStatus.Restarting:
                case ServerStatus.Restarted:
                case ServerStatus.Started:
                case ServerStatus.Starting:
                case ServerStatus.Stopping:
                    Process p = GetServerMetadata(server.ID).Process;
                    if (p != null && !p.HasExited)
                    {
                        Log(server.ID, "Actions: Kill");
                        p.Kill();

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                        Log(server.ID, "Server: Killed");
                        SetServerStatus(server, "Stopped");
                        _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
                        _serverMetadata[int.Parse(server.ID)].Process = null;
                    }

                    break;
            }
        }

        private void Actions_ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            //If console is useless, return
            if (p.StartInfo.RedirectStandardOutput) { return; }

            IntPtr hWnd = GetServerMetadata(server.ID).MainWindow;
            ShowWindow(hWnd, ShowWindow(hWnd, WindowShowStyle.Hide) ? WindowShowStyle.Hide : WindowShowStyle.ShowNormal);
        }

        private async void Actions_StartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server);
                }
            }
        }

        private async void Actions_StartServersWithAutoStartEnabled_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped && GetServerMetadata(server.ID).AutoStart)
                {
                    await GameServer_Start(server);
                }
            }
        }

        private async void Actions_StopAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await GameServer_Stop(server);
                }
            }
        }

        private async void Actions_RestartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await GameServer_Restart(server);
                }
            }
        }

        private async void Actions_Update_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to update this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Update(server);
        }

        private async void Actions_UpdateValidate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to validate this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Update(server, notes: " | Validate", validate: true);
        }

        private async void Actions_Backup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to backup on this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Backup(server);
        }

        private async void Actions_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            listbox_RestoreBackup.Items.Clear();
            var backupConfig = new BackupConfig(server.ID);
            if (Directory.Exists(backupConfig.BackupLocation))
            {
                string zipFileName = $"ToolCupboard-Backup-Server-{server.ID}-";
                foreach (var fi in new DirectoryInfo(backupConfig.BackupLocation).GetFiles("*.zip").Where(x => x.Name.Contains(zipFileName)).OrderByDescending(x => x.LastWriteTime))
                {
                    listbox_RestoreBackup.Items.Add(fi.Name);
                }
            }

            if (listbox_RestoreBackup.Items.Count > 0)
            {
                listbox_RestoreBackup.SelectedIndex = 0;
            }

            label_RestoreBackupServerName.Content = server.Name;
            MahAppFlyout_RestoreBackup.IsOpen = true;
        }

        private async void Button_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            if (listbox_RestoreBackup.SelectedIndex >= 0)
            {
                MahAppFlyout_RestoreBackup.IsOpen = false;
                await GameServer_RestoreBackup(server, listbox_RestoreBackup.SelectedItem.ToString());
            }
        }

        #endregion

        private async Task<dynamic> Server_BeginStart(ServerTable server)
        {
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID));
            if (gameServer == null) { return null; }

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(500);

            if (GetServerMetadata(server.ID).ModUpdatePending)
            {
                await UpdateModFramework(server);
            }

            if (IsWipeDue(server.ID) && !await WipeServerData(server, GetServerMetadata(server.ID).WipeType, GetServerMetadata(server.ID).WipeNewSeed))
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] The scheduled wipe failed, so the server was left stopped");
                SetServerStatus(server, "Stopped");

                return null;
            }

            //Add Start File to WindowsFirewall before start
            string startPath = ServerPath.GetServersServerFiles(server.ID, gameServer.StartPath);
            if (!string.IsNullOrWhiteSpace(gameServer.StartPath))
            {
                WindowsFirewall firewall = new WindowsFirewall(Path.GetFileName(startPath), startPath);
                if (!await firewall.IsRuleExist())
                {
                    await firewall.AddRule();
                }
            }

            gameServer.AllowsEmbedConsole = GetServerMetadata(server.ID).EmbedConsole;
            Process p = await gameServer.Start();

            //Fail to start
            if (p == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + gameServer.Error);
                SetServerStatus(server, "Stopped");

                return null;
            }

            _serverMetadata[int.Parse(server.ID)].Process = p;
            p.Exited += (sender, e) => OnGameServerExited(server);

            await Task.Run(() =>
            {
                try
                {
                    if (!p.StartInfo.CreateNoWindow)
                    {
                        while (!p.HasExited && !ShowWindow(p.MainWindowHandle, WindowShowStyle.Minimize))
                        {
                            //Debug.WriteLine("Try Setting ShowMinNoActivate Console Window");
                        }

                        Debug.WriteLine("Set ShowMinNoActivate Console Window");

                        //Save MainWindow
                        _serverMetadata[int.Parse(server.ID)].MainWindow = p.MainWindowHandle;
                    }

                    //Fix for Factorio - The WaitForInputIdle never returns my guess is because Factorio is built
                    //  without a message loop seems to work fine with this commented out
                    //p.WaitForInputIdle();

                    if (!p.StartInfo.CreateNoWindow)
                    {
                        ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);
                    }
                }
                catch
                {
                    Debug.WriteLine("No Window require to hide");
                }
            });

            //An error may occur on ShowWindow if not adding this
            if (p == null || p.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = null;

                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] Exit Code: " + p.ExitCode.ToString());
                SetServerStatus(server, "Stopped");

                return null;
            }

            // Set Priority
            p = Functions.CPU.Priority.SetProcessWithPriority(p, Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(server.ID).CPUPriority));

            // Set Affinity
            try
            {
                p.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(GetServerMetadata(server.ID).CPUAffinity);
            }
            catch (Exception e)
            {
                Log(server.ID, $"[NOTICE] Fail to set affinity. ({e.Message})");
            }

            // Save Cache
            ServerCache.SavePID(server.ID, p.Id);
            ServerCache.SaveProcessName(server.ID, p.ProcessName);
            ServerCache.SaveWindowsIntPtr(server.ID, GetServerMetadata(server.ID).MainWindow);

            SetWindowText(p.MainWindowHandle, server.Name);

            ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);

            StartAutoUpdateCheck(server);

            StartRestartCrontabCheck(server);

            StartWipeCrontabCheck(server);


            StartQuery(server);

            StartRcon(server, p);

            return gameServer;
        }

        private async Task<bool> Server_BeginStop(ServerTable server, Process p)
        {
            _serverMetadata[int.Parse(server.ID)].Process = null;

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon == null || !rcon.IsConnected || !await rcon.SaveAndQuitAsync())
            {
                dynamic gameServer = GameServer.Data.Class.Get(server.Game);
                await gameServer.Stop(p);
            }

            // Saving a large map can take a while
            for (int i = 0; i < 60; i++)
            {
                if (p == null || p.HasExited) { break; }
                await Task.Delay(1000);
            }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();

            // Save Cache
            ServerCache.SavePID(server.ID, -1);
            ServerCache.SaveProcessName(server.ID, string.Empty);
            ServerCache.SaveWindowsIntPtr(server.ID, (IntPtr)0);

            if (p != null && !p.HasExited)
            {
                p.Kill();
                return false;
            }

            return true;
        }

        private async Task<(Process, string, dynamic)> Server_BeginUpdate(ServerTable server, bool silenceCheck, bool forceUpdate, bool validate = false, string custum = null)
        {
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID));

            string localVersion = gameServer.GetLocalBuild();
            if (string.IsNullOrWhiteSpace(localVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            string remoteVersion = await gameServer.GetRemoteBuild();
            if (string.IsNullOrWhiteSpace(remoteVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            if (!silenceCheck)
            {
                Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");
            }

            if ((!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion) && localVersion != remoteVersion) || forceUpdate)
            {
                try
                {
                    return (await gameServer.Update(validate, custum), remoteVersion, gameServer);
                }
                catch
                {
                    return (await gameServer.Update(), remoteVersion, gameServer);
                }
            }

            return (null, remoteVersion, gameServer);
        }

        #region Actions - Game Server
        private async Task GameServer_Start(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            string error = string.Empty;
            if (!string.IsNullOrWhiteSpace(server.IP) && !IsValidIPAddress(server.IP))
            {
                error += " IP address is not valid.";
            }

            if (!string.IsNullOrWhiteSpace(server.Port) && !IsValidPort(server.Port))
            {
                error += " Port number is not valid.";
            }

            if (!string.IsNullOrWhiteSpace(server.QueryPort) && !IsValidPort(server.QueryPort))
            {
                error += " Query port number is not valid.";
            }

            if (error != string.Empty)
            {
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR]" + error);

                return;
            }

            Process p = GetServerMetadata(server.ID).Process;
            if (p != null) { return; }

            if (GetServerMetadata(server.ID).BackupOnStart)
            {
                await GameServer_Backup(server, " | Backup on Start");
            }

            if (GetServerMetadata(server.ID).UpdateOnStart)
            {
                await GameServer_Update(server, " | Update on Start");
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
            Log(server.ID, "Action: Start" + notes);
            SetServerStatus(server, "Starting");

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Started");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        private async Task GameServer_Stop(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            //Begin stop
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
            Log(server.ID, "Action: Stop");
            SetServerStatus(server, "Stopping");

            bool stopGracefully = await Server_BeginStop(server, p);

            Log(server.ID, "Server: Stopped");
            if (!stopGracefully)
            {
                Log(server.ID, "[NOTICE] Server fail to stop gracefully");
            }
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");
        }

        private async Task GameServer_Restart(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            _serverMetadata[int.Parse(server.ID)].Process = null;

            //Begin Restart
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
            Log(server.ID, "Action: Restart");
            SetServerStatus(server, "Restarting");

            await Server_BeginStop(server, p);

            await Task.Delay(1000);

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Restarted");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        private async Task<bool> GameServer_Update(ServerTable server, string notes = "", bool validate = false)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin Update
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
            Log(server.ID, "Action: Update" + notes);
            SetServerStatus(server, "Updating");

            var (p, remoteVersion, gameServer) = await Server_BeginUpdate(server, silenceCheck: validate, forceUpdate: true, validate: validate);

            if (p == null && string.IsNullOrEmpty(gameServer.Error)) // Update success (non-steamcmd server)
            {
                Log(server.ID, $"Server: Updated {(validate ? "Validate " : string.Empty)}({remoteVersion})");
                await UpdateModFramework(server);
            }
            else if (p != null) // p stores process of steamcmd
            {
                await Task.Run(() => { p.WaitForExit(); });
                Log(server.ID, $"Server: Updated {(validate ? "Validate " : string.Empty)}({remoteVersion})");
                await UpdateModFramework(server);
            }
            else
            {
                Log(server.ID, "Server: Fail to update");
                Log(server.ID, "[ERROR] " + gameServer.Error);
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_Backup(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin backup
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Backuping;
            Log(server.ID, "Action: Backup" + notes);
            SetServerStatus(server, "Backuping");

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string backupLocation = ServerPath.GetBackups(server.ID);
            if (!Directory.Exists(backupLocation))
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, "[ERROR] Backup location not found");
                SetServerStatus(server, "Stopped");
                return false;
            }

            string zipFileName = $"ToolCupboard-Backup-Server-{server.ID}-";

            string startPath = ServerPath.GetServers(server.ID);
            string zipFile = Path.Combine(ServerPath.GetBackups(server.ID), $"{zipFileName}{DateTime.Now.ToString("yyyyMMddHHmmss")}.zip");

            string error = string.Empty;
            await Task.Run(() =>
            {
                string tempFile = zipFile + ".tmp";

                try
                {
                    ZipFile.CreateFromDirectory(startPath, tempFile);
                    File.Move(tempFile, zipFile);

                    // Remove the oldest Backup files once the new one is safe
                    var backupConfig = new BackupConfig(server.ID);
                    var oldBackups = new DirectoryInfo(backupLocation).GetFiles("*.zip")
                        .Where(x => x.Name.Contains(zipFileName))
                        .OrderByDescending(x => x.LastWriteTime)
                        .Skip(Math.Max(1, backupConfig.MaximumBackups));

                    foreach (var fi in oldBackups)
                    {
                        fi.Delete();
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    File.Delete(tempFile);
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");

                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Backuped");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_RestoreBackup(ServerTable server, string backupFile)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            string backupLocation = ServerPath.GetBackups(server.ID);
            string backupPath = Path.Combine(backupLocation, backupFile);
            if (!File.Exists(backupPath))
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] Backup not found");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            Log(server.ID, "Action: Restore Backup");
            SetServerStatus(server, "Restoring");

            string extractPath = ServerPath.GetServers(server.ID);
            string restorePath = extractPath + ".restoring";
            string oldPath = extractPath + ".old";

            string error = string.Empty;
            await Task.Run(() =>
            {
                try
                {
                    // Keep the current server until the whole backup is unpacked
                    if (Directory.Exists(restorePath)) { Directory.Delete(restorePath, true); }
                    ZipFile.ExtractToDirectory(backupPath, restorePath);

                    if (Directory.Exists(oldPath)) { Directory.Delete(oldPath, true); }
                    if (Directory.Exists(extractPath)) { Directory.Move(extractPath, oldPath); }
                    Directory.Move(restorePath, extractPath);
                    Directory.Delete(oldPath, true);
                }
                catch (Exception e)
                {
                    error = e.Message;

                    try
                    {
                        if (Directory.Exists(restorePath)) { Directory.Delete(restorePath, true); }
                        if (!Directory.Exists(extractPath) && Directory.Exists(oldPath)) { Directory.Move(oldPath, extractPath); }
                    }
                    catch (Exception cleanup)
                    {
                        error += $" ({cleanup.Message})";
                    }
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Restored");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_Delete(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin delete
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Deleting;
            Log(server.ID, "Action: Delete");
            SetServerStatus(server, "Deleting");

            //Remove firewall rule
            var firewall = new WindowsFirewall(null, ServerPath.GetServers(server.ID));
            firewall.RemoveRuleEx();

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string serverPath = ServerPath.GetServers(server.ID);

            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(serverPath))
                    {
                        Directory.Delete(serverPath, true);
                    }
                }
                catch
                {

                }
            });

            await Task.Delay(1000);

            if (Directory.Exists(serverPath))
            {
                string appCfgPath = ServerPath.GetServersConfigs(server.ID, "ToolCupboard.cfg");
                if (File.Exists(appCfgPath))
                {
                    Log(server.ID, "Server: Fail to delete server");
                    Log(server.ID, "[ERROR] Directory is not accessible");

                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    SetServerStatus(server, "Stopped");

                    return false;
                }
            }

            Log(server.ID, "Server: Deleted server");

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            LoadServerTable();

            return true;
        }
        #endregion

        private async void OnGameServerExited(ServerTable server)
        {
            if (System.Windows.Application.Current == null) { return; }

            await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    bool autoRestart = GetServerMetadata(serverId).AutoRestart;
                    _serverMetadata[int.Parse(server.ID)].ServerStatus = autoRestart ? ServerStatus.Restarting : ServerStatus.Stopped;
                    Log(server.ID, "Server: Crashed");
                    SetServerStatus(server, autoRestart ? "Restarting" : "Stopped");

                    if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).CrashAlert)
                    {
                        var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                        await webhook.Send(server.ID, server.Game, "Crashed", server.Name, server.IP, server.Port);
                    }

                    _serverMetadata[int.Parse(server.ID)].Process = null;

                    if (autoRestart)
                    {
                        if (GetServerMetadata(server.ID).BackupOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Backup(server, " | Backup on Start");
                        }

                        if (GetServerMetadata(server.ID).UpdateOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Update(server, " | Update on Start");
                        }

                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            return;
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Started | Auto Restart");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoRestartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Restarted | Auto Restart", server.Name, server.IP, server.Port);
                        }
                    }
                }
            });
        }

        const int UPDATE_INTERVAL_MINUTE = 30;
        private async void StartAutoUpdateCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID));

            string localVersion = gameServer.GetLocalBuild();

            while (p != null && !p.HasExited)
            {
                await Task.Delay(60000 * UPDATE_INTERVAL_MINUTE);

                if (!GetServerMetadata(server.ID).AutoUpdate || GetServerMetadata(server.ID).ServerStatus == ServerStatus.Updating)
                {
                    continue;
                }

                if (p == null || p.HasExited) { break; }

                //Try to get local build again if not found just now
                if (string.IsNullOrWhiteSpace(localVersion))
                {
                    localVersion = gameServer.GetLocalBuild();
                }

                //Get remote build
                string remoteVersion = await gameServer.GetRemoteBuild();

                //Continue if success to get localVersion and remoteVersion
                if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion))
                {
                    if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started)
                    {
                        break;
                    }

                    Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");

                    if (localVersion != remoteVersion)
                    {
                        if (!await AnnounceRestart(server, p, "for an update")) { return; }

                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin stop
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
                        SetServerStatus(server, "Stopping");

                        //Stop the server
                        await Server_BeginStop(server, p);

                        if (p != null && !p.HasExited)
                        {
                            p.Kill();
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
                        SetServerStatus(server, "Updating");

                        //Update the server
                        Process updater = await gameServer.Update();
                        if (updater != null)
                        {
                            await Task.Run(() => updater.WaitForExit());
                        }

                        if (string.IsNullOrWhiteSpace(gameServer.Error))
                        {
                            Log(server.ID, $"Server: Updated ({remoteVersion})");
                            await UpdateModFramework(server);

                            if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoUpdateAlert)
                            {
                                var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                                await webhook.Send(server.ID, server.Game, "Updated | Auto Update", server.Name, server.IP, server.Port);
                            }
                        }
                        else
                        {
                            Log(server.ID, "Server: Fail to update");
                            Log(server.ID, "[ERROR] " + gameServer.Error);
                        }

                        //Start the server
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
                        SetServerStatus(server, "Starting");

                        var gameServerStart = await Server_BeginStart(server);
                        if (gameServerStart == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        break;
                    }
                }
                else if (string.IsNullOrWhiteSpace(localVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get local build.");
                }
                else if (string.IsNullOrWhiteSpace(remoteVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get remote build.");
                }
            }
        }

        private async void StartRestartCrontabCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                //If not enable return
                if (!GetServerMetadata(serverId).RestartCrontab)
                {
                    await Task.Delay(1000);

                    continue;
                }

                //Try get next DataTime restart
                DateTime? crontabTime = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now);

                //Delay 1 second for later compare
                await Task.Delay(1000);

                //Return if crontab expression is invalid
                if (crontabTime == null) { continue; }

                //If now >= crontab time
                if (DateTime.Compare(DateTime.Now, crontabTime ?? DateTime.Now) >= 0)
                {
                    //Update the next crontab
                    var currentRow = (ServerTable)ServerGrid.SelectedItem;
                    if (currentRow.ID == server.ID)
                    {
                        textBox_nextcrontab.Text = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");
                    }

                    if (p == null || p.HasExited)
                    {
                        break;
                    }

                    //Restart the server
                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        if (!await AnnounceRestart(server, p, "as scheduled")) { return; }

                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin Restart
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
                        Log(server.ID, "Action: Restart");
                        SetServerStatus(server, "Restarting");

                        await Server_BeginStop(server, p);
                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Restarted | Restart Crontab");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).RestartCrontabAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Restarted | Restart Crontab", server.Name, server.IP, server.Port);
                        }

                        break;
                    }
                }
            }
        }

        private bool IsWipeDue(string serverId)
        {
            ServerMetadata metadata = GetServerMetadata(serverId);
            if (!metadata.WipeCrontab) { return false; }

            DateTime? next = CrontabSchedule.TryParse(metadata.WipeCrontabFormat)?.GetNextOccurrence(metadata.WipeLast);
            return next != null && DateTime.Now >= next;
        }

        // Server_BeginStart runs a due wipe, so a wipe missed here still happens at the next start
        private async void StartWipeCrontabCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                await Task.Delay(1000);

                if (!IsWipeDue(server.ID) || p.HasExited || GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started)
                {
                    continue;
                }

                if (!await AnnounceRestart(server, p, "for a wipe")) { return; }

                _serverMetadata[serverId].Process = null;

                _serverMetadata[serverId].ServerStatus = ServerStatus.Restarting;
                Log(server.ID, "Action: Scheduled Wipe");
                SetServerStatus(server, "Restarting");

                await Server_BeginStop(server, p);

                var gameServer = await Server_BeginStart(server);
                if (gameServer == null) { return; }

                _serverMetadata[serverId].ServerStatus = ServerStatus.Started;
                Log(server.ID, "Server: Restarted | Scheduled Wipe");
                SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                break;
            }
        }

        private async Task<bool> WipeServerData(ServerTable server, RustWipeType type, bool newSeed)
        {
            try
            {
                string backupPath = await Task.Run(() => RustWipe.Wipe(server.ID, type));
                Log(server.ID, $"Wipe: {type} wipe complete. Wiped files were moved to {backupPath.Replace(APP_PATH, string.Empty)}");

                if (newSeed && type != RustWipeType.Blueprints)
                {
                    Log(server.ID, $"Wipe: New seed {RustWipe.SetRandomSeed(server.ID)}");
                }

                _serverMetadata[int.Parse(server.ID)].WipeLast = DateTime.Now;
                ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeLast, DateTime.Now.ToString("o"));
                return true;
            }
            catch (Exception e)
            {
                Log(server.ID, $"[ERROR] Wipe failed: {e.Message}");
                return false;
            }
        }

        // Keeps an RCON connection open while the server runs. The port opens late in the boot, so it retries.
        private async void StartRcon(ServerTable server, Process p)
        {
            int serverId = int.Parse(server.ID);

            while (p != null && !p.HasExited)
            {
                (int port, string password)? settings = null;
                RustRcon client = null;

                try
                {
                    settings = RustRcon.ReadSettings(server.ID);
                    if (settings != null)
                    {
                        client = new RustRcon("127.0.0.1", settings.Value.port, settings.Value.password);
                    }
                }
                catch (Exception e) when (e is IOException || e is FormatException || e is OverflowException)
                {
                    Log(server.ID, $"[NOTICE] RCON is unavailable: {e.Message}");
                    return;
                }

                if (client == null)
                {
                    Log(server.ID, "[NOTICE] RCON is unavailable: rcon.port or rcon.password is missing from server.cfg");
                    return;
                }

                using (RustRcon rcon = client)
                {
                    rcon.MessageReceived += text => Dispatcher.Invoke(() => AddConsoleOutput(serverId, text));

                    if (await rcon.ConnectAsync())
                    {
                        _serverMetadata[serverId].Rcon = rcon;
                        Log(server.ID, "RCON: Connected");
                        await rcon.Completion;
                        _serverMetadata[serverId].Rcon = null;
                        Log(server.ID, "RCON: Disconnected");
                    }
                }

                await Task.Delay(5000);
            }
        }

        private void AddConsoleOutput(int serverId, string text)
        {
            foreach (string line in text.Split('\n'))
            {
                _serverMetadata[serverId].ServerConsole.Add(line.TrimEnd());
            }
        }

        // Warns players in chat before an automatic restart. Returns false if the server stopped during the countdown.
        private async Task<bool> AnnounceRestart(ServerTable server, Process p, string reason)
        {
            (int seconds, string label)[] warnings = { (240, "5 minutes"), (50, "1 minute"), (10, "10 seconds") };

            foreach (var (seconds, label) in warnings)
            {
                RustRcon rcon = GetServerMetadata(server.ID).Rcon;
                if (rcon == null || !rcon.IsConnected) { return true; }

                await rcon.SayAsync($"Server restarting {reason} in {label}");
                await Task.Delay(seconds * 1000);

                if (p.HasExited || GetServerMetadata(server.ID).Process != p) { return false; }
            }

            return true;
        }

        private async void StartQuery(ServerTable server)
        {
            if (string.IsNullOrWhiteSpace(server.IP) || string.IsNullOrWhiteSpace(server.QueryPort)) { return; }

            // Check the server support Query Method
            dynamic gameServer = GameServer.Data.Class.Get(server.Game);
            if (gameServer == null) { return; }
            if (gameServer.QueryMethod == null) { return; }

            // Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            // Query server every 5 seconds
            while (p != null && !p.HasExited)
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    break;
                }

                if (!IsValidIPAddress(server.IP) || !IsValidPort(server.QueryPort))
                {
                    return;
                }

                dynamic query = gameServer.QueryMethod;
                query.SetAddressPort(server.IP, int.Parse(server.QueryPort));
                string players = await query.GetPlayersAndMaxPlayers();

                if (players != null)
                {
                    server.Maxplayers = players;

                    for (int i = 0; i < ServerGrid.Items.Count; i++)
                    {
                        if (server.ID == ((ServerTable)ServerGrid.Items[i]).ID)
                        {
                            int selectedIndex = ServerGrid.SelectedIndex;
                            ServerGrid.Items[i] = server;
                            ServerGrid.SelectedIndex = selectedIndex;
                            ServerGrid.Items.Refresh();
                            break;
                        }
                    }
                }

                await Task.Delay(5000);
            }
        }

        private async Task EndAllRunningProcess(string serverId)
        {
            await Task.Run(() =>
            {
                //LINQ query for windowsgsm old processes
                var processes = (from p in Process.GetProcesses()
                                 where ((Predicate<Process>)(p_ =>
                                 {
                                     try
                                     {
                                         return p_.MainModule.FileName.Contains(Path.Combine(APP_PATH, "servers", serverId) + "\\");
                                     }
                                     catch
                                     {
                                         return false;
                                     }
                                 }))(p)
                                 select p).ToList();

                // Kill all processes
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        //ignore
                    }
                }
            });
        }

        private void SetServerStatus(ServerTable server, string status, string pid = null)
        {
            server.Status = status;
            if (pid != null)
            {
                server.PID = pid;
            }
            if (status == "Stopped")
            {
                server.PID = string.Empty;
            }

            if (server.Status != "Started" && server.Maxplayers.Contains('/'))
            {
                var serverConfig = new ServerConfig(server.ID);
                server.Maxplayers = serverConfig.ServerMaxPlayer;
            }

            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                if (server.ID == ((ServerTable)ServerGrid.Items[i]).ID)
                {
                    int selectedIndex = ServerGrid.SelectedIndex;
                    ServerGrid.Items[i] = server;
                    ServerGrid.SelectedIndex = selectedIndex;
                    ServerGrid.Items.Refresh();
                    break;
                }
            }

            DataGrid_RefreshElements();
        }

        public void Log(string serverId, string logText)
        {
            string title = int.TryParse(serverId, out int i) ? $"#{i.ToString()}" : serverId;
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}][{title}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}.log");
            File.AppendAllText(logFile, log);

            textBox_applog.AppendText(log);
            textBox_applog.Text = RemovedOldLog(textBox_applog.Text);
            textBox_applog.ScrollToEnd();
        }

        public void DiscordBotLog(string logText)
        {
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}-DiscordBot.log");
            File.AppendAllText(logFile, log);

            textBox_DiscordBotLog.AppendText(log);
            textBox_DiscordBotLog.Text = RemovedOldLog(textBox_DiscordBotLog.Text);
            textBox_DiscordBotLog.ScrollToEnd();
        }

        private string RemovedOldLog(string logText)
        {
            const int MAX_LOG_LINE = 50;
            int lineCount = logText.Count(f => f == '\n');
            return (lineCount > MAX_LOG_LINE) ? string.Join("\n", logText.Split('\n').Skip(lineCount - MAX_LOG_LINE).ToArray()) : logText;
        }

        private void Button_ClearServerConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
            console.Clear();
        }

        private void Button_ClearAppLog_Click(object sender, RoutedEventArgs e)
        {
            textBox_applog.Clear();
        }

        private async void SendCommand(ServerTable server, string command)
        {
            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon != null && rcon.IsConnected)
            {
                int serverId = int.Parse(server.ID);
                AddConsoleOutput(serverId, $"> {command}");
                string reply = await rcon.SendAsync(command);
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    AddConsoleOutput(serverId, reply);
                }
                return;
            }

            textbox_servercommand.Focusable = false;
            _serverMetadata[int.Parse(server.ID)].ServerConsole.Input(p, command, GetServerMetadata(server.ID).MainWindow);
            textbox_servercommand.Focusable = true;
        }

        private static bool IsValidIPAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            string[] splitValues = ip.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            return splitValues.All(r => byte.TryParse(r, out byte tempForParsing));
        }

        private static bool IsValidPort(string port)
        {
            if (!int.TryParse(port, out int portnum))
            {
                return false;
            }

            return portnum > 1 && portnum < 65535;
        }

        #region Menu - Browse
        private void Browse_ServerBackups_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Functions.ProcessManagement.OpenShell(Functions.ServerPath.GetBackups(server.ID));
        }

        private void Browse_BackupFiles_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var backupConfig = new Functions.BackupConfig(server.ID);
            backupConfig.Open();
        }

        private void Browse_ServerConfigs_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string path = Functions.ServerPath.GetServersConfigs(server.ID);
            if (Directory.Exists(path))
            {
                Functions.ProcessManagement.OpenShell(path);
            }
        }

        private void Browse_ServerFiles_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string path = Functions.ServerPath.GetServersServerFiles(server.ID);
            if (Directory.Exists(path))
            {
                Functions.ProcessManagement.OpenShell(path);
            }
        }
        #endregion

        #region Top Bar Button
        private void Button_Settings_Click(object sender, RoutedEventArgs e)
        {
            ToggleMahappFlyout(MahAppFlyout_Settings);
        }

        private void Button_Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(0);
            notifyIcon.Visible = false;
            notifyIcon.Visible = true;
        }
        #endregion

        #region Settings Flyout
        private void HardWareAcceleration_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.HardWareAcceleration, MahAppSwitch_HardWareAcceleration.IsOn.ToString());
            }

            RenderOptions.ProcessRenderMode = MahAppSwitch_HardWareAcceleration.IsOn ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
        }

        private void UIAnimation_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.UIAnimation, MahAppSwitch_UIAnimation.IsOn.ToString());
            }

            WindowTransitionsEnabled = MahAppSwitch_UIAnimation.IsOn;
        }

        private void DarkTheme_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.DarkTheme, MahAppSwitch_DarkTheme.IsOn.ToString());
            }

            ThemeManager.Current.ChangeTheme(this, $"{(MahAppSwitch_DarkTheme.IsOn ? "Dark" : "Light")}.{comboBox_Themes.SelectedItem ?? DEFAULT_THEME}");
        }

        private void StartOnLogin_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.StartOnBoot, MahAppSwitch_StartOnBoot.IsOn.ToString());
            }

            SetStartOnBoot(MahAppSwitch_StartOnBoot.IsOn);
        }

        private void RestartOnCrash_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.RestartOnCrash, MahAppSwitch_RestartOnCrash.IsOn.ToString());
            }
        }

        private void SetStartOnBoot(bool enable)
        {
            string taskName = "ToolCupboard";
            string appPath = Process.GetCurrentProcess().MainModule.FileName;

            Process schtasks = new Process
            {
                StartInfo =
                {
                    FileName = "schtasks",
                    Arguments = enable ? $"/create /tn {taskName} /tr \"{appPath}\" /sc onlogon /rl HIGHEST /f" : $"/delete /tn {taskName} /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            schtasks.Start();
        }
        #endregion

        #region Theme
        private void ComboBox_Themes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.ThemeColor, comboBox_Themes.SelectedItem.ToString());
            }

            //Set theme
            ThemeManager.Current.ChangeTheme(this, $"{(MahAppSwitch_DarkTheme.IsOn ? "Dark" : "Light")}.{comboBox_Themes.SelectedItem}");
        }
        #endregion

        #region Menu - Help
        private async void Help_AboutToolCupboard_Click(object sender, RoutedEventArgs e)
        {
            await this.ShowMessageAsync("About ToolCupboard", $"Product:\t\tToolCupboard\nVersion:\t\t{APP_VERSION.Substring(1)}\n\nBased on ToolCupboard by TatLead (MIT License)");        }
        #endregion

        #region Menu - Tools
        private void Tools_GlobalServerListCheck_Click(object sender, RoutedEventArgs e)
        {
            var row = (ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            string publicIP = GetPublicIP();
            if (publicIP == null)
            {
                Log(row.ID, "Fail to check. Reason: Fail to get the public ip.");
                return;
            }

            string messageText = $"Server Name: {row.Name}\nPublic IP: {publicIP}\nQuery Port: {row.QueryPort}";
            if (GlobalServerList.IsServerOnSteamServerList(publicIP, row.QueryPort))
            {
                MessageBox.Show(messageText + "\n\nResult: Online\n\nYour server is on the global server list!", "Global Server List Check", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(messageText + "\n\nResult: Offline\n\nYour server is not on the global server list.", "Global Server List Check", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        private string GetPublicIP()
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString("https://ipinfo.io/ip").Replace("\n", string.Empty);
                }
            }
            catch
            {
                return null;
            }
        }

        private void OnBalloonTipClick(object sender, EventArgs e)
        {
        }

        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Normal;
                Show();
            }
        }

        #region Left Buttom Grid
        private void Slider_ProcessPriority_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _serverMetadata[int.Parse(server.ID)].CPUPriority = ((int)slider_ProcessPriority.Value).ToString();
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CPUPriority, GetServerMetadata(server.ID).CPUPriority);
            textBox_ProcessPriority.Text = Functions.CPU.Priority.GetPriorityByInteger((int)slider_ProcessPriority.Value);

            if (GetServerMetadata(server.ID).Process != null && !GetServerMetadata(server.ID).Process.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = Functions.CPU.Priority.SetProcessWithPriority(GetServerMetadata(server.ID).Process, (int)slider_ProcessPriority.Value);
            }
        }

        private void Button_SetAffinity_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ToggleMahappFlyout(MahAppFlyout_SetAffinity);
        }

        private void Button_EditConfig_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (Refresh_EditConfig_Data(server.ID))
            {
                ToggleMahappFlyout(MahAppFlyout_EditConfig);
            }
            else
            {
                MahAppFlyout_EditConfig.IsOpen = false;
            }
        }

        private bool Refresh_EditConfig_Data(string serverId)
        {
            var serverConfig = new ServerConfig(serverId);
            if (string.IsNullOrWhiteSpace(serverConfig.ServerGame)) { return false; }
            var gameServer = GameServer.Data.Class.Get(serverConfig.ServerGame);
            if (gameServer == null) { return false; }

            textbox_EC_ServerID.Text = serverConfig.ServerID;
            textbox_EC_ServerGame.Text = serverConfig.ServerGame;
            textbox_EC_ServerName.Text = serverConfig.ServerName;
            textbox_EC_ServerIP.Text = serverConfig.ServerIP;
            numericUpDown_EC_ServerMaxplayer.Value = int.TryParse(serverConfig.ServerMaxPlayer, out var maxplayer) ? maxplayer : int.Parse(gameServer.Maxplayers);
            numericUpDown_EC_ServerPort.Value = int.TryParse(serverConfig.ServerPort, out var port) ? port : int.Parse(gameServer.Port);
            numericUpDown_EC_ServerQueryPort.Value = int.TryParse(serverConfig.ServerQueryPort, out var queryPort) ? queryPort : int.Parse(gameServer.QueryPort);
            textbox_EC_ServerMap.Text = serverConfig.ServerMap;
            textbox_EC_ServerGSLT.Text = serverConfig.ServerGSLT;
            textbox_EC_ServerParam.Text = serverConfig.ServerParam;
            return true;
        }

        private void Button_EditConfig_Save_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerGame, textbox_EC_ServerGame.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerName, textbox_EC_ServerName.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerIP, textbox_EC_ServerIP.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerMaxPlayer, numericUpDown_EC_ServerMaxplayer.Value.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerPort, numericUpDown_EC_ServerPort.Value.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerQueryPort, numericUpDown_EC_ServerQueryPort.Value.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerMap, textbox_EC_ServerMap.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerGSLT, textbox_EC_ServerGSLT.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerParam, textbox_EC_ServerParam.Text.Trim());

            LoadServerTable();
            MahAppFlyout_EditConfig.IsOpen = false;
        }

        private void Button_RestartCrontab_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].RestartCrontab = switch_restartcrontab.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.RestartCrontab, GetServerMetadata(server.ID).RestartCrontab ? "1" : "0");
        }

        private void Button_EmbedConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].EmbedConsole = switch_embedconsole.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.EmbedConsole, GetServerMetadata(server.ID).EmbedConsole ? "1" : "0");
        }

        private void Button_AutoRestart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoRestart = switch_autorestart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoRestart, GetServerMetadata(server.ID).AutoRestart ? "1" : "0");
        }

        private void Button_AutoStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoStart = switch_autostart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoStart, GetServerMetadata(server.ID).AutoStart ? "1" : "0");
        }

        private void Button_AutoUpdate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoUpdate = switch_autoupdate.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoUpdate, GetServerMetadata(server.ID).AutoUpdate ? "1" : "0");
        }

        private async void Button_DiscordAlertSettings_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            ToggleMahappFlyout(MahAppFlyout_DiscordAlert);
        }

        private void Button_UpdateOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].UpdateOnStart = switch_updateonstart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.UpdateOnStart, GetServerMetadata(server.ID).UpdateOnStart ? "1" : "0");
        }

        private void Button_BackupOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].BackupOnStart = switch_backuponstart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.BackupOnStart, GetServerMetadata(server.ID).BackupOnStart ? "1" : "0");
        }

        private void Button_DiscordAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].DiscordAlert = switch_discordalert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordAlert, GetServerMetadata(server.ID).DiscordAlert ? "1" : "0");
            button_discordtest.IsEnabled = GetServerMetadata(server.ID).DiscordAlert;
        }

        private async void Button_CrontabEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string crontabFormat = ServerConfig.GetSetting(server.ID, ServerConfig.SettingName.CrontabFormat);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Save",
                DefaultText = crontabFormat
            };

            crontabFormat = await this.ShowInputAsync("Crontab Format", "Please enter the crontab expressions", settings);
            if (crontabFormat == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].CrontabFormat = crontabFormat;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrontabFormat, crontabFormat);

            textBox_restartcrontab.Text = crontabFormat;
            textBox_nextcrontab.Text = CrontabSchedule.TryParse(crontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss") ?? string.Empty;
        }
        #endregion

        #region Switches
        private void Switch_AutoStartAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoStartAlert = MahAppSwitch_AutoStartAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoStartAlert, GetServerMetadata(server.ID).AutoStartAlert ? "1" : "0");
        }

        private void Switch_AutoRestartAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoRestartAlert = MahAppSwitch_AutoRestartAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoRestartAlert, GetServerMetadata(server.ID).AutoRestartAlert ? "1" : "0");
        }

        private void Switch_AutoUpdateAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoUpdateAlert = MahAppSwitch_AutoUpdateAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoUpdateAlert, GetServerMetadata(server.ID).AutoUpdateAlert ? "1" : "0");
        }

        private void Switch_RestartCrontabAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].RestartCrontabAlert = MahAppSwitch_RestartCrontabAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.RestartCrontabAlert, GetServerMetadata(server.ID).RestartCrontabAlert ? "1" : "0");
        }

        private void Switch_CrashAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].CrashAlert = MahAppSwitch_CrashAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrashAlert, GetServerMetadata(server.ID).CrashAlert ? "1" : "0");
        }
        #endregion

        private async void Window_Activated(object sender, EventArgs e)
        {
            // Fix the windows cannot toggle issue because of LoadServerTable
            await Task.Delay(1);

            if (ShowActivated)
            {
                LoadServerTable();
            }
        }

        #region Discord Bot
        private async void Switch_DiscordBot_Toggled(object sender, RoutedEventArgs e)
        {
            if (!switch_DiscordBot.IsEnabled) { return; }

            if (switch_DiscordBot.IsOn)
            {
                switch_DiscordBot.IsEnabled = false;
                button_DiscordBotInvite.IsEnabled = switch_DiscordBot.IsOn = await g_DiscordBot.Start();
                DiscordBotLog("Discord Bot " + (switch_DiscordBot.IsOn ? "started." : "fail to start. Reason: Bot Token is invalid."));
                switch_DiscordBot.IsEnabled = true;
            }
            else
            {
                button_DiscordBotInvite.IsEnabled = switch_DiscordBot.IsEnabled = false;
                await g_DiscordBot.Stop();
                DiscordBotLog("Discord Bot stopped.");
                switch_DiscordBot.IsEnabled = true;
            }
        }

        private void Button_DiscordBotPrefixEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotPrefixEdit.Content.ToString() == "Edit")
            {
                button_DiscordBotPrefixEdit.Content = "Save";
                textBox_DiscordBotPrefix.IsEnabled = true;
                textBox_DiscordBotPrefix.Focus();
                textBox_DiscordBotPrefix.SelectAll();
            }
            else
            {
                button_DiscordBotPrefixEdit.Content = "Edit";
                textBox_DiscordBotPrefix.IsEnabled = false;
                DiscordBot.Configs.SetBotPrefix(textBox_DiscordBotPrefix.Text);
                label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
            }
        }

        private void Button_DiscordBotTokenEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotTokenEdit.Content.ToString() == "Edit")
            {
                rectangle_DiscordBotTokenSpoiler.Visibility = Visibility.Hidden;
                button_DiscordBotTokenEdit.Content = "Save";
                textBox_DiscordBotToken.IsEnabled = true;
                textBox_DiscordBotToken.Focus();
                textBox_DiscordBotToken.SelectAll();
            }
            else
            {
                rectangle_DiscordBotTokenSpoiler.Visibility = Visibility.Visible;
                button_DiscordBotTokenEdit.Content = "Edit";
                textBox_DiscordBotToken.IsEnabled = false;
                DiscordBot.Configs.SetBotToken(textBox_DiscordBotToken.Text);
            }
        }


        private async void Button_DiscordBotAddID_Click(object sender, RoutedEventArgs e)
        {
            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Add"
            };

            string newAdminID = await this.ShowInputAsync("Add Admin ID", "Please enter the discord user ID.", settings);
            if (newAdminID == null) { return; } //If pressed cancel

            var adminList = DiscordBot.Configs.GetBotAdminList();
            adminList.Add((newAdminID, "0"));
            DiscordBot.Configs.SetBotAdminList(adminList);
            Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);
        }

        private async void Button_DiscordBotEditServerID_Click(object sender, RoutedEventArgs e)
        {
            var adminListItem = (DiscordBot.AdminListItem)listBox_DiscordBotAdminList.SelectedItem;
            if (adminListItem == null) { return; }

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = "Save",
                DefaultText = adminListItem.ServerIds
            };

            string example = "0 - Grant All servers Permission.\n\nExamples:\n0\n1,2,3,4,5\n";
            string newServerIds = await this.ShowInputAsync($"Edit Server IDs ({adminListItem.AdminId})", $"Please enter the server Ids where admin has access to the server.\n{example}", settings);
            if (newServerIds == null) { return; } //If pressed cancel

            var adminList = DiscordBot.Configs.GetBotAdminList();
            for (int i = 0; i < adminList.Count; i++)
            {
                if (adminList[i].Item1 == adminListItem.AdminId)
                {
                    adminList.RemoveAt(i);
                    adminList.Insert(i, (adminListItem.AdminId, newServerIds.Trim()));
                    break;
                }
            }
            DiscordBot.Configs.SetBotAdminList(adminList);
            Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);
        }

        private void Button_DiscordBotRemoveID_Click(object sender, RoutedEventArgs e)
        {
            if (listBox_DiscordBotAdminList.SelectedIndex >= 0)
            {
                var adminList = DiscordBot.Configs.GetBotAdminList();
                try
                {
                    adminList.RemoveAt(listBox_DiscordBotAdminList.SelectedIndex);
                }
                catch
                {
                    Console.WriteLine($"Fail to delete item {listBox_DiscordBotAdminList.SelectedIndex} in adminIDs.txt");
                }
                DiscordBot.Configs.SetBotAdminList(adminList);

                listBox_DiscordBotAdminList.Items.Remove(listBox_DiscordBotAdminList.Items[listBox_DiscordBotAdminList.SelectedIndex]);
            }
        }

        public void Refresh_DiscordBotAdminList(int selectIndex = 0)
        {
            listBox_DiscordBotAdminList.Items.Clear();
            foreach (var (adminID, serverIDs) in DiscordBot.Configs.GetBotAdminList())
            {
                listBox_DiscordBotAdminList.Items.Add(new DiscordBot.AdminListItem { AdminId = adminID, ServerIds = serverIDs });
            }
            listBox_DiscordBotAdminList.SelectedIndex = listBox_DiscordBotAdminList.Items.Count >= 0 ? selectIndex : -1;
        }

        public int GetServerCount()
        {
            return ServerGrid.Items.Count;
        }

        public List<(string, string, string)> GetServerList()
        {
            var list = new List<(string, string, string)>();

            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var server = (ServerTable)ServerGrid.Items[i];
                list.Add((server.ID, server.Status, server.Name));
            }

            return list;
        }

        public bool IsServerExist(string serverId)
        {
            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var server = (ServerTable)ServerGrid.Items[i];
                if (server.ID == serverId) { return true; }
            }

            return false;
        }

        public ServerStatus GetServerStatus(string serverId)
        {
            return GetServerMetadata(serverId).ServerStatus;
        }

        public string GetServerName(string serverId)
        {
            var server = GetServerTableById(serverId);
            return server?.Name ?? string.Empty;
        }

        private ServerTable GetServerTableById(string serverId)
        {
            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var server = (ServerTable)ServerGrid.Items[i];
                if (server.ID == serverId) { return server; }
            }

            return null;
        }

        public async Task<bool> StartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive START action | {adminName} ({adminID})");
            await GameServer_Start(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> StopServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive STOP action | {adminName} ({adminID})");
            await GameServer_Stop(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> RestartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive RESTART action | {adminName} ({adminID})");
            await GameServer_Restart(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> SendCommandById(string serverId, string command, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive SEND action | {adminName} ({adminID}) | {command}");
            SendCommand(server, command);
            return true;
        }

        public async Task<bool> BackupServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive BACKUP action | {adminName} ({adminID})");
            await GameServer_Backup(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> UpdateServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive UPDATE action | {adminName} ({adminID})");
            await GameServer_Update(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        private void Switch_DiscordBotAutoStart_Click(object sender, RoutedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue("DiscordBotAutoStart", MahAppSwitch_DiscordBotAutoStart.IsOn.ToString());
            }
        }

        private void Button_DiscordBotInvite_Click(object sender, RoutedEventArgs e)
        {
            string inviteLink = g_DiscordBot.GetInviteLink();
            if (!string.IsNullOrWhiteSpace(inviteLink))
            {
                Functions.ProcessManagement.OpenShell(g_DiscordBot.GetInviteLink());
            }
        }
        #endregion

        /// <summary>Hide others Flyout and toggle the flyout</summary>
        /// <param name="flyout"></param>
        private void ToggleMahappFlyout(Flyout flyout)
        {
            MahAppFlyout_DiscordAlert.IsOpen = flyout == MahAppFlyout_DiscordAlert && !MahAppFlyout_DiscordAlert.IsOpen;
            MahAppFlyout_EditConfig.IsOpen = flyout == MahAppFlyout_EditConfig && !MahAppFlyout_EditConfig.IsOpen;
            MahAppFlyout_ImportGameServer.IsOpen = flyout == MahAppFlyout_ImportGameServer && !MahAppFlyout_ImportGameServer.IsOpen;
            MahAppFlyout_Players.IsOpen = flyout == MahAppFlyout_Players && !MahAppFlyout_Players.IsOpen;
            MahAppFlyout_Wipe.IsOpen = flyout == MahAppFlyout_Wipe && !MahAppFlyout_Wipe.IsOpen;
            MahAppFlyout_Mods.IsOpen = flyout == MahAppFlyout_Mods && !MahAppFlyout_Mods.IsOpen;
            MahAppFlyout_ServerSettings.IsOpen = flyout == MahAppFlyout_ServerSettings && !MahAppFlyout_ServerSettings.IsOpen;
            MahAppFlyout_InstallGameServer.IsOpen = flyout == MahAppFlyout_InstallGameServer && !MahAppFlyout_InstallGameServer.IsOpen;
            MahAppFlyout_RestoreBackup.IsOpen = flyout == MahAppFlyout_RestoreBackup && !MahAppFlyout_RestoreBackup.IsOpen;
            MahAppFlyout_SetAffinity.IsOpen = flyout == MahAppFlyout_SetAffinity && !MahAppFlyout_SetAffinity.IsOpen;
            MahAppFlyout_Settings.IsOpen = flyout == MahAppFlyout_Settings && !MahAppFlyout_Settings.IsOpen;
        }

        private void HamburgerMenu_ItemClick(object sender, ItemClickEventArgs e)
        {
            HamburgerMenuControl.IsPaneOpen = false;

            hMenu_Home.Visibility = (HamburgerMenuControl.SelectedIndex == 0) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Dashboard.Visibility = (HamburgerMenuControl.SelectedIndex == 1) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Discordbot.Visibility = (HamburgerMenuControl.SelectedIndex == 2) ? Visibility.Visible : Visibility.Hidden;

            if (HamburgerMenuControl.SelectedIndex == 2)
            {
                label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
                button_DiscordBotPrefixEdit.Content = "Edit";
                textBox_DiscordBotPrefix.IsEnabled = false;
                textBox_DiscordBotPrefix.Text = DiscordBot.Configs.GetBotPrefix();

                button_DiscordBotTokenEdit.Content = "Edit";
                textBox_DiscordBotToken.IsEnabled = false;
                textBox_DiscordBotToken.Text = DiscordBot.Configs.GetBotToken();

                Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);

                if (listBox_DiscordBotAdminList.Items.Count > 0 && listBox_DiscordBotAdminList.SelectedItem == null)
                {
                    listBox_DiscordBotAdminList.SelectedItem = listBox_DiscordBotAdminList.Items[0];
                }
            }
        }

        private async void HamburgerMenu_OptionsItemClick(object sender, ItemClickEventArgs e)
        {
            if (HamburgerMenuControl.SelectedOptionsIndex == 0)
            {
                ToggleMahappFlyout(MahAppFlyout_Settings);
            }

            HamburgerMenuControl.SelectedOptionsIndex = -1;

            await Task.Delay(1); // Delay 0.001 sec due to UI not sync
            if (hMenu_Home.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 0;
            }
            else if (hMenu_Dashboard.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 1;
            }
            else if (hMenu_Discordbot.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 2;
            }
        }

        private async void HamburgerMenu_Loaded(object sender, RoutedEventArgs e)
        {
            HamburgerMenuControl.Visibility = Visibility.Visible;
            hMenu_Home.Visibility = Visibility.Visible;
            hMenu_Dashboard.Visibility = Visibility.Hidden;
            hMenu_Discordbot.Visibility = Visibility.Hidden;

            await Task.Delay(1); // Delay 0.001 sec due to a bug
            HamburgerMenuControl.SelectedIndex = 0;
        }

        private void Button_AutoScroll_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Button_AutoScroll.Content = Button_AutoScroll.Content.ToString() == "✔️ AUTO SCROLL" ? "❌ AUTO SCROLL" : "✔️ AUTO SCROLL";
            _serverMetadata[int.Parse(server.ID)].AutoScroll = Button_AutoScroll.Content.ToString().Contains("✔️");
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoScroll, GetServerMetadata(server.ID).AutoScroll ? "1" : "0");
        }
    }
}
