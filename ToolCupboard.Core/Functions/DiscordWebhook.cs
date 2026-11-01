using System;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

namespace ToolCupboard.Functions
{
    public class DiscordWebhook 
    {
        public const string LogoUrl = "https://raw.githubusercontent.com/Potrosho/ToolCupboard/main/ToolCupboard/Images/ToolCupboard.png";

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _webhookUrl;
        private readonly string _customMessage;

        public DiscordWebhook(string webhookurl, string customMessage)
        {
            _webhookUrl = webhookurl ?? string.Empty;
            _customMessage = customMessage ?? string.Empty;
        }

        public async Task<bool> Send(string serverid, string servergame, string serverstatus, string servername, string serverip, string serverport)
        {
            if (string.IsNullOrWhiteSpace(_webhookUrl))
            {
                return false;
            }

            string avatarUrl = LogoUrl;
            string json = @"
            {
                ""username"": ""ToolCupboard"",
                ""avatar_url"": """ + avatarUrl  + @""",
                ""content"": """ + HttpUtility.JavaScriptStringEncode(_customMessage) + @""",
                ""embeds"": [
                {
                    ""type"": ""rich"",
                    ""color"": " + GetColor(serverstatus) + @",
                    ""fields"": [
                    {
                        ""name"": ""Status"",
                        ""value"": """ + GetStatusWithEmoji(serverstatus) + @""",
                        ""inline"": true
                    },
                    {
                        ""name"": ""Game Server"",
                        ""value"": """ + servergame + @""",
                        ""inline"": true
                    },
                    {
                        ""name"": ""Server IP:Port"",
                        ""value"": """ + serverip + ":"+ serverport + @""",
                        ""inline"": true
                    }],
                    ""author"": {
                        ""name"": """ + HttpUtility.JavaScriptStringEncode(servername) + @""",
                        ""icon_url"": """ + LogoUrl + @"""
                    },
                    ""footer"": {
                        ""text"": """ + AppInfo.Version + @" - Discord Alert"",
                        ""icon_url"": """ + avatarUrl + @"""
                    },
                    ""timestamp"": """ + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.mssZ") + @"""
                }]
            }";

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(_webhookUrl, content);
                if (response.Content != null)
                {
                    return true;
                }
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Fail to send webhook ({_webhookUrl})");
            }

            return false;
        }

        private static string GetColor(string serverStatus)
        {
            if (serverStatus.Contains("Started"))
            {
                return "65280"; //Green
            }
            else if (serverStatus.Contains("Restarted"))
            {
                return "65535"; //Cyan
            }
            else if (serverStatus.Contains("Crashed"))
            {
                return "16711680"; //Red
            }
            else if (serverStatus.Contains("Updated"))
            {
                return "16564292"; //Gold
            }

            return "16711679";
        }

        private static string GetStatusWithEmoji(string serverStatus)
        {
            if (serverStatus.Contains("Started"))
            {
                return ":green_circle: " + serverStatus;
            }
            if (serverStatus.Contains("Restarted"))
            {
                return ":blue_circle: " + serverStatus;
            }
            if (serverStatus.Contains("Crashed"))
            {
                return ":red_circle: " + serverStatus;
            }
            if (serverStatus.Contains("Updated"))
            {
                return ":orange_circle: " + serverStatus;
            }

            return serverStatus;
        }

    }
}
