using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace ToolCupboard.Functions
{
    public static class ProcessManagement
    {
        // Opens a url, folder or document with its default handler
        public static void OpenShell(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

        public static async Task<string> GetCommandLineByApproximatePath(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string query = $"SELECT CommandLine FROM Win32_Process WHERE ExecutablePath LIKE '%{path.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("'", @"\'")}%'";
                    using (ManagementObjectSearcher mos = new ManagementObjectSearcher(query))
                    using (ManagementObjectCollection moc = mos.Get())
                    {
                        return (from mo in moc.Cast<ManagementObject>() select mo["CommandLine"]).First().ToString();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    return null;
                }
            });
        }
    }
}
