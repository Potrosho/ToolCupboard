using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ToolCupboard.Functions;

namespace ToolCupboard.Web
{
    /// <summary>
    /// Serves the dashboard and its API to this PC only.
    /// </summary>
    public sealed class WebServer
    {
        public const int Port = 27080;
        private const string TOKEN_HEADER = "X-ToolCupboard-Token";

        private readonly ServerManager _servers;
        private readonly string _token;
        private WebApplication _app;

        public string Url => $"http://127.0.0.1:{Port}/#token={_token}";

        public WebServer(ServerManager servers)
        {
            _servers = servers;

            string tokenPath = ServerPath.Get(ServerPath.FolderName.Configs, "webtoken.txt");
            if (!File.Exists(tokenPath))
            {
                File.WriteAllText(tokenPath, WebAuth.CreateToken());
            }

            _token = File.ReadAllText(tokenPath).Trim();
        }

        public async Task Start()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppInfo.RootPath });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, Port));

            _app = builder.Build();
            _app.Use(CheckRequest);

            string page = ReadPage();
            _app.MapGet("/", () => Results.Content(page, "text/html; charset=utf-8"));
            MapApi(_app);

            await _app.StartAsync();
        }

        public async Task Stop()
        {
            if (_app != null)
            {
                await _app.StopAsync();
            }
        }

        private async Task CheckRequest(HttpContext context, Func<Task> next)
        {
            if (!WebAuth.IsLocalHost(context.Request.Host.Value, Port))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (context.Request.Path.StartsWithSegments("/api") && !WebAuth.IsValidToken(context.Request.Headers[TOKEN_HEADER], _token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; img-src data:; frame-ancestors 'none'; base-uri 'none'";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next();
        }

        private static string ReadPage()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ToolCupboard.Web.index.html"))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static IResult Refused(string error) => error == null ? Results.Ok() : Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);

        private void MapApi(IEndpointRouteBuilder api)
        {
            api.MapGet("/api/servers", async () => Results.Json(await _servers.Run(_servers.GetWebServers)));

            api.MapPost("/api/servers/{id}/{action}", async (string id, string action) =>
            {
                bool? done = await _servers.Run(() => _servers.RunWebAction(id, action));
                return done == null ? Results.NotFound() : done == true ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/servers/{id}/console", async (string id) =>
            {
                string[] lines = await _servers.Run(() => _servers.GetWebConsole(id));
                return lines == null ? Results.NotFound() : Results.Json(lines);
            });

            api.MapPost("/api/servers/{id}/command", async (string id, WebCommand body) =>
            {
                bool sent = !string.IsNullOrWhiteSpace(body?.Text) && await _servers.Run(() => _servers.SendWebCommand(id, body.Text));
                return sent ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/servers/{id}/settings", async (string id) =>
            {
                WebSettings settings = await _servers.Run(() => _servers.GetWebSettings(id));
                return settings == null ? Results.NotFound() : Results.Json(settings);
            });

            api.MapPut("/api/servers/{id}/settings", async (string id, WebSettings settings) => Refused(await _servers.Run(() => _servers.SaveWebSettings(id, settings))));

            api.MapGet("/api/servers/{id}/wipe", async (string id) =>
            {
                object info = await _servers.Run(() => _servers.GetWebWipeInfo(id));
                return info == null ? Results.NotFound() : Results.Json(info);
            });

            api.MapPost("/api/servers/{id}/wipe", async (string id, WebConfirmed request) => Refused(await _servers.Run(() => _servers.RunWebWipe(id, request))));

            api.MapGet("/api/servers/{id}/backups", async (string id) =>
            {
                object[] backups = await _servers.Run(() => _servers.GetWebBackups(id));
                return backups == null ? Results.NotFound() : Results.Json(backups);
            });

            api.MapPost("/api/servers/{id}/backups/restore", async (string id, WebConfirmed request) => Refused(await _servers.Run(() => _servers.RestoreWebBackup(id, request))));

            api.MapPost("/api/servers/{id}/plugins/upload", async (string id, WebPluginUpload upload) => Refused(await _servers.Run(() => _servers.AddWebPlugin(id, upload))));

            api.MapGet("/api/system", async () => Results.Json(await _servers.Run(_servers.GetWebSystem)));

            api.MapGet("/api/public-ip", async () =>
            {
                try { return Results.Json(new { ip = await Functions.RouterPorts.GetPublicIp() }); }
                catch { return Results.StatusCode(502); }
            });

            api.MapPut("/api/system/options", async (WebAppOptions options) => { await _servers.Run(() => _servers.SaveWebOptions(options)); return Results.Ok(); });

            api.MapGet("/api/discord", async () => Results.Json(await _servers.Run(_servers.GetWebDiscord)));

            api.MapPut("/api/discord", async (WebDiscord discord) => Refused(await _servers.Run(() => _servers.SaveWebDiscord(discord))));

            api.MapPost("/api/discord/{action}", async (string action) =>
            {
                if (action != "start" && action != "stop") { return Results.NotFound(); }
                return await _servers.Run(() => _servers.SetWebDiscordRunning(action == "start")) ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/job", async () => Results.Json(await _servers.Run(_servers.GetWebJob)));

            api.MapPost("/api/job", async (WebNewServer request) =>
            {
                if (request?.Kind != "install" && request?.Kind != "import") { return Results.NotFound(); }
                return Refused(await _servers.Run(() => _servers.StartWebJob(request.Kind, request.Name, request.Path)));
            });

            api.MapPost("/api/servers/{id}/delete", async (string id, WebConfirmed request) => Refused(await _servers.Run(() => _servers.DeleteWebServer(id, request))));

            api.MapGet("/api/servers/{id}/config-file", async (string id) =>
            {
                string text = await _servers.Run(() => _servers.GetWebConfigFile(id));
                return text == null ? Results.NotFound() : Results.Json(new { text });
            });

            api.MapPut("/api/servers/{id}/config-file", async (string id, WebCommand body) => Refused(await _servers.Run(() => _servers.SaveWebConfigFile(id, body?.Text))));

            api.MapGet("/api/plugins/search", async (string query, int? page) =>
            {
                try
                {
                    return Results.Json(await UmodCatalog.Search(query, page ?? 1));
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is Newtonsoft.Json.JsonException)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }
            });

            api.MapGet("/api/servers/{id}/plugins", async (string id) =>
            {
                RustPlugin[] plugins = await _servers.Run(() => _servers.GetWebPlugins(id));
                return plugins == null ? Results.NotFound() : Results.Json(plugins);
            });

            api.MapPost("/api/servers/{id}/plugins/install", async (string id, WebCommand body) =>
            {
                string error = await _servers.Run(() => _servers.InstallWebPlugin(id, body?.Text));
                return Refused(error);
            });

            api.MapPost("/api/servers/{id}/plugins/{name}/{action}", async (string id, string name, string action) =>
            {
                bool? done = await _servers.Run(() => _servers.RunWebPluginAction(id, name, action));
                return done == null ? Results.NotFound() : done == true ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/servers/{id}/players", async (string id) =>
            {
                RustRcon rcon = await _servers.Run(() => _servers.GetWebRcon(id));
                if (rcon == null || !rcon.IsConnected) { return Results.Conflict(); }

                var players = await rcon.GetPlayersAsync();
                return players == null ? Results.Conflict() : Results.Json(players);
            });

            api.MapPost("/api/servers/{id}/players/{steamId}/{action}", async (string id, string steamId, string action, WebCommand body) =>
            {
                RustRcon rcon = await _servers.Run(() => _servers.GetWebRcon(id));
                if (rcon == null || !rcon.IsConnected) { return Results.Conflict(); }

                // Checked against the live list, the id goes into a server command
                var player = (await rcon.GetPlayersAsync())?.FirstOrDefault(p => p.SteamID == steamId);
                if (player == null) { return Results.NotFound(); }

                if (action == "kick") { await rcon.KickAsync(player, body?.Text); }
                else if (action == "ban") { await rcon.BanAsync(player, body?.Text); }
                else { return Results.NotFound(); }

                await _servers.Run(() => { _servers.Log(id, $"Player: {action} {player.DisplayName} ({player.SteamID}) from the web UI"); return true; });
                return Results.Ok();
            });
        }

        private sealed class WebCommand
        {
            public string Text { get; set; }
        }
    }
}
