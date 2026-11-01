using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Discord;
using Discord.WebSocket;

namespace ToolCupboard.DiscordBot
{
	class Bot
	{
		private DiscordSocketClient _client;
		private CancellationTokenSource _cancellationTokenSource;

		public Bot()
		{
			Configs.CreateConfigs();
		}

		public async Task<bool> Start()
		{
			_client = new DiscordSocketClient(new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
			});
			_client.Ready += On_Bot_Ready;

			try
			{
				await _client.LoginAsync(TokenType.Bot, Configs.GetBotToken());
				await _client.StartAsync();
			}
			catch
			{
				return false;
			}

			// Listen Commands
			new Commands(_client);

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
					if (Application.Current != null)
					{
						await Application.Current.Dispatcher.Invoke(async () =>
						{
							MainWindow ToolCupboard = (MainWindow)Application.Current.MainWindow;
							int serverCount = ToolCupboard.ServerGrid.Items.Count;
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
