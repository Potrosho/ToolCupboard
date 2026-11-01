using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
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

        private readonly MainWindow _window;
        private readonly Dispatcher _dispatcher;
        private readonly string _token;
        private WebApplication _app;

        public string Url => $"http://127.0.0.1:{Port}/#token={_token}";

        public WebServer(MainWindow window)
        {
            _window = window;
            _dispatcher = window.Dispatcher;

            string tokenPath = ServerPath.Get(ServerPath.FolderName.Configs, "webtoken.txt");
            if (!File.Exists(tokenPath))
            {
                File.WriteAllText(tokenPath, WebAuth.CreateToken());
            }

            _token = File.ReadAllText(tokenPath).Trim();
        }

        public async Task Start()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = MainWindow.APP_PATH });
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

        // The window owns the server state, so every read and action runs on its thread
        private Task<T> OnWindow<T>(Func<T> read) => _dispatcher.InvokeAsync(read).Task;

        private Task<T> OnWindow<T>(Func<Task<T>> action) => _dispatcher.InvokeAsync(action).Task.Unwrap();

        private static IResult Refused(string error) => error == null ? Results.Ok() : Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);

        private void MapApi(IEndpointRouteBuilder api)
        {
            api.MapGet("/api/servers", async () => Results.Json(await OnWindow(_window.GetWebServers)));

            api.MapPost("/api/servers/{id}/{action}", async (string id, string action) =>
            {
                bool? done = await OnWindow(() => _window.RunWebAction(id, action));
                return done == null ? Results.NotFound() : done == true ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/servers/{id}/console", async (string id) =>
            {
                string[] lines = await OnWindow(() => _window.GetWebConsole(id));
                return lines == null ? Results.NotFound() : Results.Json(lines);
            });

            api.MapPost("/api/servers/{id}/command", async (string id, WebCommand body) =>
            {
                bool sent = !string.IsNullOrWhiteSpace(body?.Text) && await OnWindow(() => _window.SendWebCommand(id, body.Text));
                return sent ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/servers/{id}/settings", async (string id) =>
            {
                WebSettings settings = await OnWindow(() => _window.GetWebSettings(id));
                return settings == null ? Results.NotFound() : Results.Json(settings);
            });

            api.MapPut("/api/servers/{id}/settings", async (string id, WebSettings settings) => Refused(await OnWindow(() => _window.SaveWebSettings(id, settings))));

            api.MapGet("/api/servers/{id}/wipe", async (string id) =>
            {
                object info = await OnWindow(() => _window.GetWebWipeInfo(id));
                return info == null ? Results.NotFound() : Results.Json(info);
            });

            api.MapPost("/api/servers/{id}/wipe", async (string id, WebConfirmed request) => Refused(await OnWindow(() => _window.RunWebWipe(id, request))));

            api.MapGet("/api/servers/{id}/backups", async (string id) =>
            {
                object[] backups = await OnWindow(() => _window.GetWebBackups(id));
                return backups == null ? Results.NotFound() : Results.Json(backups);
            });

            api.MapPost("/api/servers/{id}/backups/restore", async (string id, WebConfirmed request) => Refused(await OnWindow(() => _window.RestoreWebBackup(id, request))));

            api.MapPost("/api/servers/{id}/plugins/upload", async (string id, WebPluginUpload upload) => Refused(await OnWindow(() => _window.AddWebPlugin(id, upload))));

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
                RustPlugin[] plugins = await OnWindow(() => _window.GetWebPlugins(id));
                return plugins == null ? Results.NotFound() : Results.Json(plugins);
            });

            api.MapPost("/api/servers/{id}/plugins/install", async (string id, WebCommand body) =>
            {
                string error = await OnWindow(() => _window.InstallWebPlugin(id, body?.Text));
                return Refused(error);
            });

            api.MapPost("/api/servers/{id}/plugins/{name}/{action}", async (string id, string name, string action) =>
            {
                bool? done = await OnWindow(() => _window.RunWebPluginAction(id, name, action));
                return done == null ? Results.NotFound() : done == true ? Results.Ok() : Results.Conflict();
            });

            api.MapGet("/api/servers/{id}/players", async (string id) =>
            {
                RustRcon rcon = await OnWindow(() => _window.GetWebRcon(id));
                if (rcon == null || !rcon.IsConnected) { return Results.Conflict(); }

                var players = await rcon.GetPlayersAsync();
                return players == null ? Results.Conflict() : Results.Json(players);
            });

            api.MapPost("/api/servers/{id}/players/{steamId}/{action}", async (string id, string steamId, string action, WebCommand body) =>
            {
                RustRcon rcon = await OnWindow(() => _window.GetWebRcon(id));
                if (rcon == null || !rcon.IsConnected) { return Results.Conflict(); }

                // Checked against the live list, the id goes into a server command
                var player = (await rcon.GetPlayersAsync())?.FirstOrDefault(p => p.SteamID == steamId);
                if (player == null) { return Results.NotFound(); }

                if (action == "kick") { await rcon.KickAsync(player, body?.Text); }
                else if (action == "ban") { await rcon.BanAsync(player, body?.Text); }
                else { return Results.NotFound(); }

                await OnWindow(() => { _window.Log(id, $"Player: {action} {player.DisplayName} ({player.SteamID}) from the web UI"); return true; });
                return Results.Ok();
            });
        }

        private sealed class WebCommand
        {
            public string Text { get; set; }
        }
    }
}
