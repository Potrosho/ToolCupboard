using System;
using System.Threading.Tasks;

namespace ToolCupboard
{
    class WindowsFirewall
    {
        private readonly string Name;
        private readonly string Path;

        public WindowsFirewall(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public async Task<bool> IsRuleExist()
        {
            return await Task.Run(() =>
            {
                try
                {
                    dynamic netFwMgr = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwMgr"));

                    foreach (dynamic app in netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications)
                    {
                        string filename = app.ProcessImageFileName;
                        if (filename.ToLower() == Path.ToLower())
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    return false;
                }

                return false;
            });
        }

        public async Task<bool> AddRule()
        {
            return await Task.Run(() =>
            {
                try
                {
                    dynamic netFwMgr = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwMgr"));
                    dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwAuthorizedApplication"));
                    app.Name = Name;
                    app.ProcessImageFileName = Path;
                    app.Enabled = true;
                    netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications.Add(app);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public void RemoveRule()
        {
            try
            {
                dynamic netFwMgr = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwMgr"));
                netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications.Remove(Path);
            }
            catch
            {
                // ignore
            }
        }

        //Remove the firewall rule by similar path
        public async void RemoveRuleEx()
        {
            await Task.Run(() =>
            {
                try
                {
                    dynamic netFwMgr = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwMgr"));

                    foreach (dynamic app in netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications)
                    {
                        string filename = app.ProcessImageFileName;
                        if (filename.ToLower().Contains(Path.ToLower()))
                        {
                            netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications.Remove(filename);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            });
        }
    }
}
