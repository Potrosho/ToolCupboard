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
using Microsoft.AspNetCore.Http.Features;
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

            // Rust posts F7 reports here (server.reportsserverendpoint). The key in the address says which server it is.
            _app.MapPost("/report/{key}", async (string key, HttpRequest request) =>
            {
                if (!request.HasFormContentType) { return Results.BadRequest(); }
                IFormCollection form = await request.ReadFormAsync();
                bool saved = await _servers.Run(() => _servers.ReceiveEndpointReport(key, form["data"], form["userid"]));
                return saved ? Results.Text("ok") : Results.NotFound();
            });
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

        // The server's root folder and the full path, which is null for an unknown server or a path outside its folder
        private async Task<(string Root, string Full)> ServerFile(string id, string path)
        {
            ServerTable server = await _servers.Run(() => _servers.GetServerTableById(id));
            if (server == null) { return (null, null); }

            string root = ServerPath.GetServersServerFiles(server.ID);
            return (root, ServerFiles.Resolve(root, path));
        }

        private static bool IsFileError(Exception e) => e is IOException || e is UnauthorizedAccessException || e is BadHttpRequestException;

        private void MapFiles(IEndpointRouteBuilder api)
        {
            api.MapGet("/api/servers/{id}/files", async (string id, string path) =>
            {
                var (_, full) = await ServerFile(id, path);
                if (full == null || !Directory.Exists(full)) { return Results.NotFound(); }

                try { return Results.Json(ServerFiles.List(full)); }
                catch (Exception e) when (IsFileError(e)) { return Refused(e.Message); }
            });

            api.MapGet("/api/servers/{id}/files/content", async (string id, string path) =>
            {
                var (_, full) = await ServerFile(id, path);
                return full != null && File.Exists(full) ? Results.File(full, "application/octet-stream", Path.GetFileName(full)) : Results.NotFound();
            });

            // Saves an edited file or an upload. The body is the raw file.
            api.MapPut("/api/servers/{id}/files/content", async (string id, string path, HttpContext context) =>
            {
                var (root, full) = await ServerFile(id, path);
                if (full == null || ServerFiles.IsRoot(root, full) || Directory.Exists(full) || !Directory.Exists(Path.GetDirectoryName(full))) { return Results.NotFound(); }

                // Maps and saves can be large
                IHttpMaxRequestBodySizeFeature limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit != null && !limit.IsReadOnly) { limit.MaxRequestBodySize = 4L * 1024 * 1024 * 1024; }

                // Written beside it first, so a cancelled upload never leaves half a file
                string temp = full + ".tcupload";
                try
                {
                    using (FileStream file = File.Create(temp)) { await context.Request.Body.CopyToAsync(file); }
                    File.Move(temp, full, true);
                    return Results.Ok();
                }
                catch (Exception e) when (IsFileError(e))
                {
                    try { File.Delete(temp); } catch (Exception) { }
                    return Refused(e.Message);
                }
            });

            api.MapPost("/api/servers/{id}/files/folder", async (string id, string path) =>
            {
                var (root, full) = await ServerFile(id, path);
                if (full == null || ServerFiles.IsRoot(root, full)) { return Results.NotFound(); }
                if (File.Exists(full) || Directory.Exists(full)) { return Refused("Something with that name already exists."); }

                try { Directory.CreateDirectory(full); return Results.Ok(); }
                catch (Exception e) when (IsFileError(e)) { return Refused(e.Message); }
            });

            api.MapDelete("/api/servers/{id}/files", async (string id, string path) =>
            {
                var (root, full) = await ServerFile(id, path);
                if (full == null || ServerFiles.IsRoot(root, full)) { return Results.NotFound(); }

                try
                {
                    if (Directory.Exists(full)) { Directory.Delete(full, true); }
                    else if (File.Exists(full)) { File.Delete(full); }
                    else { return Results.NotFound(); }
                    return Results.Ok();
                }
                catch (Exception e) when (IsFileError(e)) { return Refused(e.Message); }
            });
        }

        private static void MapNotes(IEndpointRouteBuilder api)
        {
            api.MapGet("/api/app/update", async () =>
            {
                try { return Results.Json(await SelfUpdate.GetLatest()); }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is Newtonsoft.Json.JsonException) { return Results.StatusCode(StatusCodes.Status502BadGateway); }
            });

            api.MapPost("/api/app/update", async () => Refused(await SelfUpdate.Install()));

            api.MapGet("/api/notes", () => Results.Json(Notes.List()));

            api.MapGet("/api/notes/{name}", (string name) => Notes.Read(name) is string text ? Results.Json(new { text }) : Results.NotFound());

            api.MapPut("/api/notes/{name}", (string name, WebCommand body) =>
            {
                if (!Notes.IsValidName(name)) { return Refused("Use letters, numbers, spaces, - and _ for the note name, up to 80 characters."); }
                if (body?.Text == null || body.Text.Length > 1024 * 1024) { return Refused("The note is too large."); }

                try { Notes.Save(name, body.Text); return Results.Ok(); }
                catch (Exception e) when (IsFileError(e)) { return Refused(e.Message); }
            });

            api.MapDelete("/api/notes/{name}", (string name) => Notes.Delete(name) ? Results.Ok() : Results.NotFound());
        }

        private void MapApi(IEndpointRouteBuilder api)
        {
            MapFiles(api);
            MapNotes(api);

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

            api.MapGet("/api/servers/{id}/metrics", async (string id, int hours) => await _servers.Run(() => _servers.GetWebMetrics(id, hours)) is object metrics ? Results.Json(metrics) : Results.NotFound());

            api.MapGet("/api/servers/{id}/chat", async (string id, string q, string channel, int? days) => await _servers.Run(() => _servers.GetWebChat(id, q, channel, days ?? 7)) is object chat ? Results.Json(chat) : Results.NotFound());

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

            api.MapGet("/api/servers/{id}/plugins/updates", async (string id) =>
            {
                RustPlugin[] plugins = await _servers.Run(() => _servers.GetWebPlugins(id));
                if (plugins == null) { return Results.NotFound(); }

                // One at a time, so a server with many plugins does not flood uMod
                var updates = new System.Collections.Generic.List<object>();
                foreach (RustPlugin plugin in plugins)
                {
                    UmodPlugin latest;
                    try { latest = await UmodCatalog.FindByName(plugin.Name); }
                    catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is Newtonsoft.Json.JsonException) { return Results.StatusCode(StatusCodes.Status502BadGateway); }

                    updates.Add(new { plugin.Name, Installed = plugin.Version, Latest = latest?.Version, latest?.Slug, Update = latest != null && RustMods.IsNewer(latest.Version, plugin.Version) });
                }

                return Results.Json(updates);
            });

            api.MapGet("/api/servers/{id}/plugins/{name}/config", async (string id, string name) => await _servers.Run(() => _servers.GetWebPluginConfig(id, name)) is string text ? Results.Json(new { text }) : Results.NotFound());

            api.MapPut("/api/servers/{id}/plugins/{name}/config", async (string id, string name, WebCommand body) => Refused(await _servers.Run(() => _servers.SaveWebPluginConfig(id, name, body?.Text))));

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

            api.MapGet("/api/servers/{id}/roster", async (string id) => await _servers.Run(() => _servers.GetWebRoster(id)) is string roster ? Results.Content(roster, "application/json") : Results.NotFound());

            api.MapPost("/api/servers/{id}/players/{steamId}/{action}", async (string id, string steamId, string action, WebPlayerAction body) => Refused(await _servers.Run(() => _servers.RunWebPlayerAction(id, steamId, action, body))));

            api.MapGet("/api/servers/{id}/map", async (string id) => await _servers.Run(() => _servers.GetWebMap(id)) is string map ? Results.Content(map, "application/json") : Results.Conflict());

            api.MapGet("/api/servers/{id}/positions", async (string id) => await _servers.Run(() => _servers.GetWebPositions(id)) is object positions ? Results.Content(positions.ToString(), "application/json") : Results.Conflict());

            api.MapPost("/api/servers/{id}/teleport", async (string id, WebTeleport body) => Refused(await _servers.Run(() => _servers.TeleportWebPlayer(id, body))));

            api.MapGet("/api/avatars/{steamId}", async (string steamId) => await SteamAvatars.Get(steamId) is string url ? Results.Json(new { url }) : Results.NotFound());

            api.MapGet("/api/servers/{id}/groups/{list}", async (string id, string list) => await _servers.Run(() => _servers.GetWebTeams(id, list)) is object groups ? Results.Content(groups.ToString(), "application/json") : Results.Conflict());

            api.MapPost("/api/servers/{id}/groups/{kind}/{groupId}/{action}", async (string id, string kind, string groupId, string action, WebGroupAction body) => Refused(await _servers.Run(() => _servers.RunWebGroupAction(id, kind, groupId, action, body?.SteamId, body?.RoleId))));

            api.MapGet("/api/servers/{id}/reports", async (string id) => await _servers.Run(() => _servers.GetWebReports(id)) is object reports ? Results.Json(reports) : Results.NotFound());

            api.MapPut("/api/servers/{id}/reports/{reportId}", async (string id, string reportId, WebReportUpdate body) => Refused(await _servers.Run(() => _servers.UpdateWebReport(id, reportId, body?.Status, body?.Note))));

            api.MapGet("/api/servers/{id}/reports/{reportId}/screenshot", async (string id, string reportId) => await _servers.Run(() => _servers.GetWebReportScreenshot(id, reportId)) is string url ? Results.Json(new { url }) : Results.NotFound());

            api.MapGet("/api/servers/{id}/activity", async (string id, string q, string type, int? days) => await _servers.Run(() => _servers.GetWebActivity(id, q, type, days ?? 7)) is object entries ? Results.Json(entries) : Results.NotFound());

            api.MapGet("/api/servers/{id}/leaderboard", async (string id, DateTime? from, DateTime? to) => await _servers.Run(() => _servers.GetWebLeaderboard(id, from, to)) is object rows ? Results.Json(rows) : Results.NotFound());

            api.MapGet("/api/servers/{id}/build", async (string id) => await _servers.Run(() => _servers.GetWebBuild(id)) is object build ? Results.Json(build) : Results.NotFound());

            api.MapGet("/api/servers/{id}/players", async (string id) =>
            {
                RustRcon rcon = await _servers.Run(() => _servers.GetWebRcon(id));
                if (rcon == null || !rcon.IsConnected) { return Results.Conflict(); }

                var players = await rcon.GetPlayersAsync();
                return players == null ? Results.Conflict() : Results.Json(players);
            });

        }

        private sealed class WebCommand
        {
            public string Text { get; set; }
        }
    }
}
