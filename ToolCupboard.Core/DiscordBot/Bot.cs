using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using ToolCupboard.Functions;

namespace ToolCupboard.DiscordBot
{
	public class Bot
	{
		private const int READY_TIMEOUT_SECONDS = 15;

		private readonly ServerManager _servers;
		private DiscordSocketClient _client;
		private CancellationTokenSource _cancellationTokenSource;

		public Bot(ServerManager servers)
		{
			_servers = servers;
			Configs.CreateConfigs();
		}

		public async Task<bool> Start()
		{
			_client = new DiscordSocketClient(new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
			});
			_client.Ready += On_Bot_Ready;

			var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_client.Ready += () => { ready.TrySetResult(true); return Task.CompletedTask; };

			try
			{
				await _client.LoginAsync(TokenType.Bot, Configs.GetBotToken());
				await _client.StartAsync();
			}
			catch
			{
				return false;
			}

			// A bad token or a missing intent only shows up as never becoming ready
			if (await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(READY_TIMEOUT_SECONDS))) != ready.Task)
			{
				await _client.StopAsync();
				return false;
			}

			// Listen Commands
			new Commands(_client, _servers);

			return true;
		}

		// Ready fires on every reconnect, so only the first one starts the presence update
		private Task On_Bot_Ready()
		{
			if (_cancellationTokenSource == null)
			{
				_cancellationTokenSource = new CancellationTokenSource();
				_ = StartDiscordPresenceUpdate(_cancellationTokenSource.Token);
			}

			return Task.CompletedTask;
		}

		private async Task StartDiscordPresenceUpdate(CancellationToken token)
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					if (_servers != null)
					{
						await _servers.Run(async () =>
						{
							ServerManager ToolCupboard = _servers;
							int serverCount = ToolCupboard.Servers.Count;
							await _client.SetGameAsync($"{serverCount} Rust server{(serverCount > 1 ? "s" : string.Empty)}");
						});
					}

					await Task.Delay(900000, token);
				}
			}
			catch (OperationCanceledException)
			{
				// Bot stopped
			}
		}

		public async Task Stop()
		{
			if (_client != null)
			{
				_cancellationTokenSource?.Cancel();
				_cancellationTokenSource = null;

				await _client.StopAsync();
			}
		}

		public string GetInviteLink()
		{
			return (_client == null || _client.CurrentUser == null) ? string.Empty : $"https://discordapp.com/api/oauth2/authorize?client_id={_client.CurrentUser.Id}&permissions=67497024&scope=bot";
		}
	}
}
