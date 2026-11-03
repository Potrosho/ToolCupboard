using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class RustPlayer
    {
        public string SteamID { get; set; }
        public string DisplayName { get; set; }
        public int Ping { get; set; }
        public int ConnectedSeconds { get; set; }
        public double Health { get; set; }

        [JsonIgnore]
        public string Connected => TimeSpan.FromSeconds(ConnectedSeconds).ToString(@"h\:mm\:ss");
    }

    /// <summary>
    /// Client for the WebRcon interface of a Rust dedicated server.
    /// </summary>
    public sealed class RustRcon : IDisposable
    {
        private const int RECEIVE_BUFFER_SIZE = 8192;
        private const int REPLY_QUIET_MS = 200;

        // A command can answer with several packets, so a reply ends when they stop arriving
        private sealed class PendingReply : IDisposable
        {
            private readonly List<string> _lines = new List<string>();
            private readonly Timer _quiet;

            public readonly TaskCompletionSource<string> Result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingReply()
            {
                _quiet = new Timer(_ => Finish());
            }

            public void Add(string text)
            {
                lock (_lines)
                {
                    if (!string.IsNullOrWhiteSpace(text)) { _lines.Add(text.TrimEnd()); }
                }

                _quiet.Change(REPLY_QUIET_MS, Timeout.Infinite);
            }

            public void Finish()
            {
                lock (_lines)
                {
                    Result.TrySetResult(string.Join("\n", _lines));
                }
            }

            public void Dispose() => _quiet.Dispose();
        }

        private readonly Uri _uri;
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly ConcurrentDictionary<int, PendingReply> _pending = new ConcurrentDictionary<int, PendingReply>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private int _lastIdentifier;

        /// <summary>Raised for server output that is not a reply to a command.</summary>
        public event Action<string> MessageReceived;

        /// <summary>Raised with the JSON of each chat message.</summary>
        public event Action<string> ChatReceived;

        /// <summary>Completes when the connection ends.</summary>
        public Task Completion { get; private set; } = Task.CompletedTask;

        public bool IsConnected => _socket.State == WebSocketState.Open;

        public RustRcon(string host, int port, string password)
        {
            _uri = new Uri($"ws://{host}:{port}/{Uri.EscapeDataString(password)}");
        }

        /// <summary>
        /// Reads the RCON port and password from a server's server.cfg.
        /// </summary>
        public static (int port, string password)? ReadSettings(string serverId)
        {
            string configPath = ServerPath.GetServersServerFiles(serverId, "server.cfg");
            if (!File.Exists(configPath)) { return null; }

            string config = File.ReadAllText(configPath);
            Match port = Regex.Match(config, @"^\s*\+rcon\.port\s+""?(\d+)""?\s*$", RegexOptions.Multiline);
            Match password = Regex.Match(config, @"^\s*\+rcon\.password\s+""?(.*?)""?\s*$", RegexOptions.Multiline);
            if (!port.Success || !password.Success || password.Groups[1].Value.Length == 0) { return null; }

            return (int.Parse(port.Groups[1].Value), password.Groups[1].Value);
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await _socket.ConnectAsync(_uri, timeout.Token);
                }
            }
            catch
            {
                return false;
            }

            Completion = Task.Run(ReceiveLoop);
            return true;
        }

        /// <summary>
        /// Sends a command and returns its reply, or null if none arrives in time.
        /// </summary>
        public async Task<string> SendAsync(string command, int timeoutSeconds = 10)
        {
            int identifier = Interlocked.Increment(ref _lastIdentifier);
            using (var reply = new PendingReply())
            {
                _pending[identifier] = reply;

                try
                {
                    await WriteAsync(command, identifier);
                    Task finished = await Task.WhenAny(reply.Result.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
                    return finished == reply.Result.Task ? await reply.Result.Task : null;
                }
                catch (Exception e) when (e is WebSocketException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    return null;
                }
                finally
                {
                    _pending.TryRemove(identifier, out _);
                }
            }
        }

        public async Task<List<RustPlayer>> GetPlayersAsync()
        {
            string reply = await SendAsync("playerlist");
            if (reply == null) { return null; }

            try
            {
                return JsonConvert.DeserializeObject<List<RustPlayer>>(reply) ?? new List<RustPlayer>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public async Task<RustServerInfo> GetServerInfoAsync()
        {
            string reply = await SendAsync("serverinfo");
            if (reply == null) { return null; }

            try
            {
                return JsonConvert.DeserializeObject<RustServerInfo>(reply);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public Task<string> SayAsync(string message) => SendAsync($"global.say {message}");

        public Task<string> KickAsync(RustPlayer player, string reason) => SendAsync($"global.kick {player.SteamID} {Quote(reason)}");

        public Task<string> BanAsync(RustPlayer player, string reason) => SendAsync($"global.banid {player.SteamID} {Quote(player.DisplayName)} {Quote(reason)}");

        /// <summary>
        /// Saves the world and asks the server to shut down.
        /// </summary>
        public async Task<bool> SaveAndQuitAsync()
        {
            await SendAsync("server.save", 30);

            try
            {
                await WriteAsync("quit", Interlocked.Increment(ref _lastIdentifier));
                return true;
            }
            catch (Exception e) when (e is WebSocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                return false;
            }
        }

        private static string Quote(string text) => $"\"{(text ?? string.Empty).Replace("\"", "'")}\"";

        private async Task WriteAsync(string command, int identifier)
        {
            string json = new JObject
            {
                { "Identifier", identifier },
                { "Message", command },
                { "Name", "ToolCupboard" }
            }.ToString(Formatting.None);

            await _sendLock.WaitAsync();
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, _closing.Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[RECEIVE_BUFFER_SIZE];
            var message = new MemoryStream();

            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _closing.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { break; }

                    message.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        Dispatch(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        message.SetLength(0);
                    }
                }
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is ObjectDisposedException)
            {
                // Connection closed
            }

            foreach (var pending in _pending.Values)
            {
                pending.Finish();
            }
        }

        private void Dispatch(string json)
        {
            JObject packet;
            try
            {
                packet = JObject.Parse(json);
            }
            catch (JsonException)
            {
                return;
            }

            int identifier = packet.Value<int?>("Identifier") ?? 0;
            string text = packet.Value<string>("Message") ?? string.Empty;

            if (identifier > 0 && _pending.TryGetValue(identifier, out var reply))
            {
                reply.Add(text);
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                bool chat = packet.Value<string>("Type") == "Chat";
                if (chat) { ChatReceived?.Invoke(text); }
                MessageReceived?.Invoke(chat ? FormatChat(text) : text);
            }
        }

        private static string FormatChat(string json)
        {
            try
            {
                JObject chat = JObject.Parse(json);
                return $"[Chat] {chat.Value<string>("Username")}: {chat.Value<string>("Message")}";
            }
            catch (JsonException)
            {
                return json;
            }
        }

        public void Dispose()
        {
            _closing.Cancel();
            _socket.Dispose();
            _closing.Dispose();
            _sendLock.Dispose();
        }
    }
}
