namespace ToolCupboard.Web
{
    /// <summary>
    /// The per-server options the dashboard reads and saves.
    /// </summary>
    public class WebSettings
    {
        public bool AutoStart { get; set; }
        public bool AutoRestart { get; set; }
        public bool AutoUpdate { get; set; }
        public bool UpdateOnStart { get; set; }
        public bool BackupOnStart { get; set; }
        public bool RestartSchedule { get; set; }
        public string RestartCrontab { get; set; }
        public bool WipeSchedule { get; set; }
        public string WipeCrontab { get; set; }
        public string WipeType { get; set; }
        public bool WipeNewSeed { get; set; }
        public string NextWipe { get; set; }
        public int MaximumBackups { get; set; }
        public string WorldSize { get; set; }
        public string Seed { get; set; }
        public string TickRate { get; set; }
        public string SaveInterval { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string HeaderImage { get; set; }
        public bool RustPlus { get; set; }
        public string Tags { get; set; }
        public string GameMode { get; set; }
        public bool Pve { get; set; }
        public bool Radiation { get; set; }
        public string IdleKick { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Port { get; set; }
        public string QueryPort { get; set; }
        public string Map { get; set; }
        public string MaxPlayers { get; set; }
        public string Parameters { get; set; }
        public int CpuPriority { get; set; }
        public string CpuAffinity { get; set; }
        public bool DiscordAlert { get; set; }
        public string DiscordMessage { get; set; }
        public bool HasWebhook { get; set; }
        public string NewWebhook { get; set; }
        public bool ClearWebhook { get; set; }
        public bool AutoStartAlert { get; set; }
        public bool AutoRestartAlert { get; set; }
        public bool AutoUpdateAlert { get; set; }
        public bool RestartCrontabAlert { get; set; }
        public bool CrashAlert { get; set; }
        public bool OpenRouterPorts { get; set; }
        public string Branch { get; set; }
        public string RouterStatus { get; set; }
    }

    public class WebAppOptions
    {
        public bool StartOnLogin { get; set; }
        public bool RestartOnCrash { get; set; }
    }

    public class WebDiscordAdmin
    {
        public string Id { get; set; }
        public string Servers { get; set; }
    }

    public class WebDiscord
    {
        public bool Running { get; set; }
        public bool Busy { get; set; }
        public bool HasToken { get; set; }
        public string NewToken { get; set; }
        public string Prefix { get; set; }
        public bool AutoStart { get; set; }
        public string Commands { get; set; }
        public string InviteLink { get; set; }
        public WebDiscordAdmin[] Admins { get; set; }
    }

    public class WebNewServer
    {
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public class WebConfirmed
    {
        public string Confirm { get; set; }
        public string Type { get; set; }
        public bool NewSeed { get; set; }
        public string File { get; set; }
    }

    public class WebPlayerAction
    {
        public string Reason { get; set; }
        public int? Minutes { get; set; }
        public string Role { get; set; }
        public string Item { get; set; }
        public int? Amount { get; set; }
    }

    public class WebTeleport
    {
        public string SteamId { get; set; }
        public string ToSteamId { get; set; }
        public double? X { get; set; }
        public double? Z { get; set; }
    }

    public class WebGroupAction
    {
        public string SteamId { get; set; }
        public string RoleId { get; set; }
    }

    public class WebReportUpdate
    {
        public string Status { get; set; }
        public string Note { get; set; }
    }

    public class WebPluginUpload
    {
        public string FileName { get; set; }
        public string Text { get; set; }
    }
}
