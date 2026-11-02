using System.Diagnostics;
using Microsoft.Win32;

namespace ToolCupboard
{
    /// <summary>
    /// App-wide options stored for this Windows user.
    /// </summary>
    public static class AppOptions
    {
        public const string RegistryPath = @"SOFTWARE\ToolCupboard";

        private static bool Read(string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                return (key?.GetValue(name) ?? false).ToString() == "True";
            }
        }

        private static void Write(string name, bool value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue(name, value.ToString());
            }
        }

        public static bool StartOnLogin => Read("StartOnBoot");

        public static bool RestartOnCrash
        {
            get => Read("RestartOnCrash");
            set => Write("RestartOnCrash", value);
        }

        public static bool DiscordBotAutoStart
        {
            get => Read("DiscordBotAutoStart");
            set => Write("DiscordBotAutoStart", value);
        }

        public static void SetStartOnLogin(bool enable)
        {
            Write("StartOnBoot", enable);

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
    }
}
