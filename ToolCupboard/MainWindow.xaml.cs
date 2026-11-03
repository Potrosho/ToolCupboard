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




        public static readonly string APP_VERSION = AppInfo.Version;
        public static readonly int MAX_SERVER = AppInfo.MaxServers;
        public static readonly string APP_PATH = AppInfo.RootPath;
        public static readonly string DEFAULT_THEME = "Cyan";

        private readonly NotifyIcon notifyIcon;

        private readonly ServerManager g_Servers;
        public Dictionary<int, ServerMetadata> _serverMetadata => g_Servers._serverMetadata;
        private ServerMetadata GetServerMetadata(object serverId) => g_Servers.GetServerMetadata(serverId);


        private readonly List<System.Windows.Controls.CheckBox> _checkBoxes = new List<System.Windows.Controls.CheckBox>();
        private Web.WebServer g_WebServer;
        private string g_UmodQuery = string.Empty;
        private int g_UmodPage = 1;
        private int g_UmodLastPage = 1;

        public MainWindow(bool showCrashHint)
        {
            g_Servers = new ServerManager(new System.Windows.Threading.DispatcherSynchronizationContext(Dispatcher));
            g_Servers.Logged += ShowLogLine;
            g_Servers.ServerChanged += RefreshServerRow;
            g_Servers.TableChanged += ShowServers;
            g_Servers.DiscordLogged += ShowDiscordLogLine;
            g_Servers.BotChanged += ShowBotState;
            g_Servers.OptionsChanged += ShowOptions;
            g_Servers.StatusChanged += DataGrid_RefreshElements;

            //Add SplashScreen
            var splashScreen = new SplashScreen("Images/SplashScreen.png");
            splashScreen.Show(false, true);

            InitializeComponent();
            Title = $"ToolCupboard {APP_VERSION}";

            //Close SplashScreen
            splashScreen.Close(new TimeSpan(0, 0, 1));

            // Add all themes to comboBox_Themes
            ThemeManager.Current.Themes.Select(t => Path.GetExtension(t.Name).Trim('.')).Distinct().OrderBy(x => x).ToList().ForEach(delegate (string name) { comboBox_Themes.Items.Add(name); });

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

            g_Servers.ReattachServers();

            if (showCrashHint)
            {
                string logFile = $"CRASH_{DateTime.Now:yyyyMMdd}.log";
                Log("System", $"ToolCupboard crashed unexpectedly, please view the crash log {logFile}");
            }

            g_Servers.AutoStartServer();
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

                int startedCount = g_Servers.GetStartedServerCount();
                dashboard_started_bar.Value = ServerGrid.Items.Count == 0 ? 0 : startedCount * 100.0 / ServerGrid.Items.Count;
                dashboard_started_bar.Value = (dashboard_started_bar.Value > 100.0) ? 100.0 : dashboard_started_bar.Value;
                dashboard_started_usage.Content = $"{string.Format("{0:0.00}", dashboard_started_bar.Value)}%";
                dashboard_started_ratio.Content = $"{startedCount}/{ServerGrid.Items.Count}";

                dashboard_players_count.Content = g_Servers.GetActivePlayers().ToString();

                Refresh_DashBoard_LiveChart();

                await Task.Delay(2000);
            }
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
            g_Servers.Bot.Stop().ConfigureAwait(false);

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
                switch_routerports.IsOn = GetServerMetadata(row.ID).OpenRouterPorts;
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
            if (g_Servers.InstallBusy)
            {
                await this.ShowMessageAsync("Install Server", "The web UI is already installing or importing a server.");
                return;
            }

            if (string.IsNullOrWhiteSpace(textbox_InstallServerName.Text)) { return; }

            //Installation start
            textbox_InstallServerName.IsEnabled = false;
            progressbar_InstallProgress.IsIndeterminate = true;
            textblock_InstallProgress.Text = "Installing";
            button_Install.IsEnabled = false;
            textbox_InstallLog.Text = string.Empty;

            string error = await g_Servers.InstallServer(textbox_InstallServerName.Text, line =>
            {
                textbox_InstallLog.AppendText(line + Environment.NewLine);
                textbox_InstallLog.ScrollToEnd();
            });

            textbox_InstallServerName.IsEnabled = true;
            progressbar_InstallProgress.IsIndeterminate = false;

            if (error == null)
            {
                MahAppFlyout_InstallGameServer.IsOpen = false;
            }
            else
            {
                textblock_InstallProgress.Text = error;
                button_Install.IsEnabled = true;
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
            if (g_Servers.InstallBusy)
            {
                await this.ShowMessageAsync("Import Server", "The web UI is already installing or importing a server.");
                return;
            }

            if (string.IsNullOrWhiteSpace(textbox_ImportServerName.Text)) { return; }

            //Import start
            textbox_ImportServerName.IsEnabled = false;
            textbox_ServerDir.IsEnabled = false;
            button_Browse.IsEnabled = false;
            progressbar_ImportProgress.IsIndeterminate = true;
            textblock_ImportProgress.Text = "Importing";
            label_ServerDirWarn.Content = string.Empty;

            string error = await g_Servers.ImportServer(textbox_ImportServerName.Text, textbox_ServerDir.Text);

            textbox_ImportServerName.IsEnabled = true;
            textbox_ServerDir.IsEnabled = true;
            button_Browse.IsEnabled = true;
            progressbar_ImportProgress.IsIndeterminate = false;
            textblock_ImportProgress.Text = error == null ? string.Empty : "[ERROR] Fail to import";
            label_ServerDirWarn.Content = error ?? string.Empty;

            if (error == null)
            {
                MahAppFlyout_ImportGameServer.IsOpen = false;
            }
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

            await g_Servers.GameServer_Delete(server);
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

            g_Servers.SendCommand(server, command);
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
                g_WebServer = new Web.WebServer(g_Servers);
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

        private void Button_SettingsSave_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string error = g_Servers.SaveServerSettings(server, textBox_SettingWorldSize.Text, textBox_SettingSeed.Text, textBox_SettingTickRate.Text, textBox_SettingSaveInterval.Text,
                textBox_SettingDescription.Text, textBox_SettingUrl.Text, textBox_SettingHeaderImage.Text, checkBox_SettingRustPlus.IsChecked == true);

            label_SettingsStatus.Content = error ?? "Saved. Changes apply the next time the server starts.";
        }
        #endregion

        #region Mods and Plugins
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
            string error = await g_Servers.InstallWebPlugin(server.ID, plugin.Slug);
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

            await g_Servers.RemoveFramework(server);
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
            g_Servers.SetServerStatus(server, "Wiping");

            await g_Servers.WipeServerData(server, type, checkBox_WipeNewSeed.IsChecked == true);

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            g_Servers.SetServerStatus(server, "Stopped");
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
            g_Servers.SaveServerConfigToServerMetadata(server.ID, new ServerConfig(server.ID));

            await g_Servers.GameServer_Start(server);
        }

        private async void Actions_Stop_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await g_Servers.GameServer_Stop(server);
        }

        private async void Actions_Restart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await g_Servers.GameServer_Restart(server);
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
                        g_Servers.SetServerStatus(server, "Stopped");
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
            ServerManager.ShowWindow(hWnd, ServerManager.ShowWindow(hWnd, WindowShowStyle.Hide) ? WindowShowStyle.Hide : WindowShowStyle.ShowNormal);
        }

        private async void Actions_StartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await g_Servers.GameServer_Start(server);
                }
            }
        }

        private async void Actions_StartServersWithAutoStartEnabled_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped && GetServerMetadata(server.ID).AutoStart)
                {
                    await g_Servers.GameServer_Start(server);
                }
            }
        }

        private async void Actions_StopAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await g_Servers.GameServer_Stop(server);
                }
            }
        }

        private async void Actions_RestartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await g_Servers.GameServer_Restart(server);
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

            await g_Servers.GameServer_Update(server);
        }

        private async void Actions_UpdateValidate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to validate this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await g_Servers.GameServer_Update(server, notes: " | Validate", validate: true);
        }

        private async void Actions_Backup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show("Do you want to backup on this server?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await g_Servers.GameServer_Backup(server);
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
                await g_Servers.GameServer_RestoreBackup(server, listbox_RestoreBackup.SelectedItem.ToString());
            }
        }

        #endregion

        #region Actions - Game Server
        #endregion
public void Log(string serverId, string logText) => g_Servers.Log(serverId, logText);

        public void LoadServerTable() => g_Servers.ReloadServers();

        private void ShowServers()
        {
            var selected = ServerGrid.SelectedItem as ServerTable;
            ServerGrid.Items.Clear();

            foreach (ServerTable server in g_Servers.Servers)
            {
                ServerGrid.Items.Add(server);
                if (selected != null && selected.ID == server.ID)
                {
                    ServerGrid.SelectedItem = server;
                }
            }

            grid_action.Visibility = (ServerGrid.Items.Count != 0) ? Visibility.Visible : Visibility.Hidden;
            label_select.Visibility = grid_action.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
        }

        private void ShowDiscordLogLine(string log)
        {
            textBox_DiscordBotLog.AppendText(log);
            textBox_DiscordBotLog.Text = RemovedOldLog(textBox_DiscordBotLog.Text);
            textBox_DiscordBotLog.ScrollToEnd();
        }

        private void ShowBotState()
        {
            switch_DiscordBot.Toggled -= Switch_DiscordBot_Toggled;
            switch_DiscordBot.IsOn = g_Servers.BotRunning || g_Servers.BotBusy && switch_DiscordBot.IsOn;
            switch_DiscordBot.Toggled += Switch_DiscordBot_Toggled;
            switch_DiscordBot.IsEnabled = !g_Servers.BotBusy;
            button_DiscordBotInvite.IsEnabled = g_Servers.BotRunning;
        }

        private void ShowOptions()
        {
            if (MahAppSwitch_RestartOnCrash.IsOn != AppOptions.RestartOnCrash) { MahAppSwitch_RestartOnCrash.IsOn = AppOptions.RestartOnCrash; }
            if (MahAppSwitch_StartOnBoot.IsOn != AppOptions.StartOnLogin) { MahAppSwitch_StartOnBoot.IsOn = AppOptions.StartOnLogin; }
            if (MahAppSwitch_DiscordBotAutoStart.IsOn != AppOptions.DiscordBotAutoStart) { MahAppSwitch_DiscordBotAutoStart.IsOn = AppOptions.DiscordBotAutoStart; }
            Refresh_DiscordBotAdminList();
            textBox_DiscordBotPrefix.Text = DiscordBot.Configs.GetBotPrefix();
            label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
        }

        private void ShowLogLine(string log)
        {
            textBox_applog.AppendText(log);
            textBox_applog.Text = RemovedOldLog(textBox_applog.Text);
            textBox_applog.ScrollToEnd();
        }

        private void RefreshServerRow(ServerTable server)
        {
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

            if ((ServerGrid.SelectedItem as ServerTable)?.ID == server.ID)
            {
                textBox_nextcrontab.Text = CrontabSchedule.TryParse(GetServerMetadata(server.ID).CrontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");
            }
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

            AppOptions.SetStartOnLogin(MahAppSwitch_StartOnBoot.IsOn);
        }

        private void RestartOnCrash_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue(RegistryKeyName.RestartOnCrash, MahAppSwitch_RestartOnCrash.IsOn.ToString());
            }
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

        private void Switch_RouterPorts_Toggled(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            g_Servers.SetOpenRouterPorts(server, switch_routerports.IsOn);
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
            if (!switch_DiscordBot.IsEnabled || switch_DiscordBot.IsOn == g_Servers.BotRunning) { return; }

            if (switch_DiscordBot.IsOn)
            {
                await g_Servers.StartBot();
            }
            else
            {
                await g_Servers.StopBot();
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

        private void Switch_DiscordBotAutoStart_Click(object sender, RoutedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ToolCupboard", true))
            {
                key?.SetValue("DiscordBotAutoStart", MahAppSwitch_DiscordBotAutoStart.IsOn.ToString());
            }
        }

        private void Button_DiscordBotInvite_Click(object sender, RoutedEventArgs e)
        {
            string inviteLink = g_Servers.Bot.GetInviteLink();
            if (!string.IsNullOrWhiteSpace(inviteLink))
            {
                Functions.ProcessManagement.OpenShell(g_Servers.Bot.GetInviteLink());
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
