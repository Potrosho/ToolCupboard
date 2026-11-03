using System.Linq;
using Newtonsoft.Json;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Who has a server demo recording right now. Starting and stopping use Rust's own demo.record and demo.stop.
    /// </summary>
    static class Recording
    {
        public static string Get() => JsonConvert.SerializeObject(
            BasePlayer.activePlayerList
                .Where(p => p != null && p.net?.connection != null && p.net.connection.IsRecording)
                .Select(p => new
                {
                    steamId = p.UserIDString,
                    name = p.displayName,
                    file = p.net.connection.RecordFilename,
                    seconds = (long)p.net.connection.RecordTimeElapsed.TotalSeconds,
                    bytes = (long)p.net.connection.RecordFilesize
                })
                .ToList());
    }
}
