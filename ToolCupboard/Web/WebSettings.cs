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
    }

    public class WebConfirmed
    {
        public string Confirm { get; set; }
        public string Type { get; set; }
        public bool NewSeed { get; set; }
        public string File { get; set; }
    }

    public class WebPluginUpload
    {
        public string FileName { get; set; }
        public string Text { get; set; }
    }
}
