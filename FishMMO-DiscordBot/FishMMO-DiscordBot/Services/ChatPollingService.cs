using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Background service that polls the game database for new chat messages
	/// and forwards them to the appropriate Discord channels.
	/// Uses a reentrancy guard to prevent overlapping polls.
	/// </summary>
	public class ChatPollingService : IHostedService, IDisposable
	{
		private readonly DiscordSocketClient discordClient;
		private readonly NpgsqlDbContextFactory dbContextFactory;
		private readonly ILogger<ChatPollingService> logger;
		private readonly DynamicChannelManagerService dynamicChannelManager;
		private readonly BridgeBanService bridgeBanService;
		private readonly AccountLinkingService accountLinkingService;
		private readonly ChatRelayPolicy relayPolicy;
		private readonly SemaphoreSlim pollLock = new SemaphoreSlim(1, 1);
		private Timer? timer;
		private readonly int pollingIntervalSeconds;
		private long lastProcessedChatId;
		private readonly ulong? defaultGuildId;
		private int disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="ChatPollingService"/> class.
		/// </summary>
		/// <param name="discordClient">The Discord socket client.</param>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <param name="logger">Logger instance.</param>
		/// <param name="configuration">Application configuration.</param>
		/// <param name="dynamicChannelManager">Service managing dynamic Discord channels.</param>
		/// <param name="relayPolicy">Allowlist deciding which game channels may reach Discord.</param>
		public ChatPollingService(
			DiscordSocketClient discordClient,
			NpgsqlDbContextFactory dbContextFactory,
			ILogger<ChatPollingService> logger,
			IConfiguration configuration,
			DynamicChannelManagerService dynamicChannelManager,
			BridgeBanService bridgeBanService,
			AccountLinkingService accountLinkingService,
			ChatRelayPolicy relayPolicy)
		{
			this.discordClient = discordClient;
			this.dbContextFactory = dbContextFactory;
			this.logger = logger;
			this.dynamicChannelManager = dynamicChannelManager;
			this.bridgeBanService = bridgeBanService;
			this.accountLinkingService = accountLinkingService;
			this.relayPolicy = relayPolicy;

			if (!int.TryParse(configuration["ChatPollingIntervalSeconds"], out int parsedInterval) || parsedInterval <= 0)
			{
				parsedInterval = 5;
				logger.LogWarning(
					"ChatPollingIntervalSeconds not found or invalid in appsettings.json. Defaulting to {DefaultInterval} seconds.",
					parsedInterval);
			}
			pollingIntervalSeconds = parsedInterval;

			if (ulong.TryParse(configuration.GetSection("Discord")["DefaultGuildId"], out ulong parsedDefaultGuildId) &&
				parsedDefaultGuildId != 0)
			{
				defaultGuildId = parsedDefaultGuildId;
			}
			else
			{
				defaultGuildId = null;
				logger.LogWarning(
					"DefaultGuildId not found or invalid in appsettings.json. Dynamic channel creation from game chat may fail.");
			}

			logger.LogInformation("ChatPollingService initialized with polling interval: {PollingInterval} seconds.", pollingIntervalSeconds);
		}

		/// <inheritdoc />
		public Task StartAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("ChatPollingService is starting.");
			timer = new Timer(OnTimerElapsed, null, TimeSpan.Zero, TimeSpan.FromSeconds(pollingIntervalSeconds));
			return Task.CompletedTask;
		}

		/// <summary>
		/// Timer callback with reentrancy guard. Skips execution if a previous poll is still running.
		/// </summary>
		private async void OnTimerElapsed(object? state)
		{
			if (!pollLock.Wait(0))
			{
				return;
			}

			try
			{
				await PollAndForwardAsync();
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error occurred during chat polling.");
			}
			finally
			{
				pollLock.Release();
			}
		}

		/// <summary>
		/// Polls the database for new chat messages and forwards them to Discord.
		/// Uses batch queries for world/scene servers and character names to avoid N+1.
		/// </summary>
		private async Task PollAndForwardAsync()
		{
			if (discordClient.ConnectionState != ConnectionState.Connected)
			{
				return;
			}

			if (!defaultGuildId.HasValue)
			{
				return;
			}

			ulong guildId = defaultGuildId.Value;

			using var dbContext = dbContextFactory.CreateDbContext();

			if (lastProcessedChatId == 0)
			{
				var highestId = await dbContext.Chat
					.AsQueryable()
					.OrderByDescending(c => c.ID)
					.Select(c => c.ID)
					.FirstOrDefaultAsync();
				lastProcessedChatId = highestId;
				logger.LogInformation("Initialized lastProcessedChatId to {LastProcessedId}.", lastProcessedChatId);
			}

			var newChatMessages = await dbContext.Chat
				.AsQueryable()
				.Where(c => c.ID > lastProcessedChatId)
				// Bridged Discord messages are excluded here so the relay cannot echo its own
				// traffic back. Everything else is fetched so the cursor advances and so link
				// verification can scan it; what is actually SENT is decided by relayPolicy below.
				.Where(c => c.Channel != (byte)ChatChannel.Discord)
				.OrderBy(c => c.ID)
				.ToListAsync();

			if (newChatMessages.Count == 0)
			{
				return;
			}

			/* The cursor moves once the batch's database reads below have succeeded, not before. It
			 * used to move first, so a read that threw — or a Discord send that threw part-way through
			 * the batch — lost every message after it for good. Now a failed read leaves the batch to be
			 * read again, and each send is caught on its own, so one refusal costs one message. */
			long batchEndId = newChatMessages[newChatMessages.Count - 1].ID;

			// Check for account link verification codes in all new messages
			foreach (var msg in newChatMessages)
			{
				if (!string.IsNullOrEmpty(msg.CharacterName) && !string.IsNullOrEmpty(msg.Message))
				{
					var verification = await accountLinkingService.TryVerifyFromChatAsync(
						msg.CharacterName, msg.AccountName ?? string.Empty, msg.Message);

					if (verification != null)
					{
						await NotifyLinkOutcomeAsync(verification);
					}
				}
			}

			// Batch-collect IDs for entities we need from the DB
			var worldIdsNeeded = new HashSet<long>();
			var sceneIdsNeeded = new HashSet<long>();
			var charIdsNeeded = new HashSet<long>();

			foreach (var msg in newChatMessages)
			{
				if (dynamicChannelManager.GetManagedChannelState(guildId, msg.WorldServerID, msg.SceneServerID) == null)
				{
					worldIdsNeeded.Add(msg.WorldServerID);
					sceneIdsNeeded.Add(msg.SceneServerID);
				}
				if (msg.CharacterID != 0 && string.IsNullOrEmpty(msg.CharacterName))
				{
					charIdsNeeded.Add(msg.CharacterID);
				}
			}

			// Batch-fetch world servers
			var worldServerNames = new Dictionary<long, string>();
			if (worldIdsNeeded.Count > 0)
			{
				var worldServers = await dbContext.WorldServers.AsQueryable()
					.Where(w => worldIdsNeeded.Contains(w.ID))
					.ToListAsync();
				foreach (var ws in worldServers)
				{
					worldServerNames[ws.ID] = ws.Name;
				}
			}

			// Batch-fetch scene servers
			var sceneServerNames = new Dictionary<long, string>();
			if (sceneIdsNeeded.Count > 0)
			{
				var sceneServers = await dbContext.SceneServers.AsQueryable()
					.Where(s => sceneIdsNeeded.Contains(s.ID))
					.ToListAsync();
				foreach (var ss in sceneServers)
				{
					sceneServerNames[ss.ID] = ss.Name;
				}
			}

			// Batch-fetch character names
			var characterNames = new Dictionary<long, string>();
			if (charIdsNeeded.Count > 0)
			{
				var characters = await dbContext.Characters.AsQueryable()
					.Where(c => charIdsNeeded.Contains(c.ID))
					.ToListAsync();
				foreach (var ch in characters)
				{
					characterNames[ch.ID] = ch.Name ?? "Unknown Character";
				}
			}

			// Every read this batch needs has succeeded; nothing below reads the database.
			lastProcessedChatId = batchEndId;

			foreach (var chatMessage in newChatMessages)
			{
				try
				{
					await ForwardAsync(chatMessage, guildId, worldServerNames, sceneServerNames, characterNames);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Could not forward chat message {ChatId} to Discord; continuing with the rest of the batch.", chatMessage.ID);
				}
			}
		}

		/// <summary>Relays one chat row to its Discord channel, if it passes the relay gate.</summary>
		private async Task ForwardAsync(
			ChatEntity chatMessage,
			ulong guildId,
			Dictionary<long, string> worldServerNames,
			Dictionary<long, string> sceneServerNames,
			Dictionary<long, string> characterNames)
		{
			/* THE relay gate. Nothing reaches Discord without passing an explicit allowlist.
			 *
			 * This check used to be the `Channel != Discord` filter on the query above and
			 * nothing else, which meant every channel the game has — including whispers,
			 * guild chat and party chat — was published to a public Discord channel. See
			 * ChatRelayPolicy for what the allowlist contains and why private channels cannot
			 * be added to it from configuration.
			 *
			 * The gate is here rather than in the query on purpose: the query still has to
			 * see every row so the cursor advances past channels we do not relay, and so the
			 * account-link verification pass below can spot a verification code wherever a
			 * player typed it. Neither of those republishes anything. */
			if (!relayPolicy.IsRelayable(chatMessage.Channel))
			{
				return;
			}

			// Skip bridge-banned characters/accounts
			if (bridgeBanService.IsBridgeBanned(chatMessage.CharacterName, chatMessage.AccountName))
			{
				logger.LogDebug(
					"Skipping bridge-banned message from '{CharacterName}' (Account: '{AccountName}').",
					chatMessage.CharacterName, chatMessage.AccountName);
				return;
			}

			var channelState = dynamicChannelManager.GetManagedChannelState(
				guildId,
				chatMessage.WorldServerID,
				chatMessage.SceneServerID);

			if (channelState == null)
			{
				if (!worldServerNames.TryGetValue(chatMessage.WorldServerID, out string? worldName))
				{
					logger.LogError(
						"WorldServer with ID {WorldId} not found in database. Cannot create Discord channel.",
						chatMessage.WorldServerID);
					return;
				}
				if (!sceneServerNames.TryGetValue(chatMessage.SceneServerID, out string? sceneName))
				{
					logger.LogError(
						"SceneServer with ID {SceneId} not found in database. Cannot create Discord channel.",
						chatMessage.SceneServerID);
					return;
				}

				channelState = await dynamicChannelManager.GetOrCreateChannelState(
					guildId,
					chatMessage.WorldServerID,
					worldName,
					chatMessage.SceneServerID,
					sceneName);

				if (channelState == null)
				{
					logger.LogError(
						"Failed to create Discord channel for World {WorldId}, Scene {SceneId}.",
						chatMessage.WorldServerID, chatMessage.SceneServerID);
					return;
				}
			}

			string characterName = chatMessage.CharacterName ?? "System";
			if (chatMessage.CharacterID != 0 && string.IsNullOrEmpty(chatMessage.CharacterName))
			{
				if (characterNames.TryGetValue(chatMessage.CharacterID, out string? resolvedName))
				{
					characterName = resolvedName;
				}
				else
				{
					characterName = "Unknown Character";
				}
			}

			string messageText = chatMessage.Message ?? string.Empty;
			string worldPrefix = $"{chatMessage.WorldServerID} ";
			if (messageText.StartsWith(worldPrefix))
			{
				messageText = messageText.Substring(worldPrefix.Length).Trim();
			}

			// Sanitize for Discord: escape mentions to prevent @everyone/@here abuse from game chat
			messageText = messageText
				.Replace("@everyone", "@\u200Beveryone")
				.Replace("@here", "@\u200Bhere");
			characterName = characterName
				.Replace("@everyone", "@\u200Beveryone")
				.Replace("@here", "@\u200Bhere");

			var discordChannel = discordClient.GetChannel(channelState.DiscordChannelId) as IMessageChannel;
			if (discordChannel != null)
			{
				string channelLabel = ((ChatChannel)chatMessage.Channel).ToString();
				await discordChannel.SendMessageAsync(
					$"[{chatMessage.TimeCreated:HH:mm:ss}] [{channelLabel}] {characterName}: {messageText}",
					allowedMentions: AllowedMentions.None);

				dynamicChannelManager.UpdateChannelActivity(guildId, chatMessage.WorldServerID, chatMessage.SceneServerID);
			}
			else
			{
				logger.LogWarning(
					"Discord channel ID {ChannelId} for World {WorldId}/Scene {SceneId} not found or not a message channel.",
					channelState.DiscordChannelId, chatMessage.WorldServerID, chatMessage.SceneServerID);
			}
		}

		/// <summary>
		/// Tells the Discord user how their chat-code link ended. The link itself is already in the database.
		/// </summary>
		private async Task NotifyLinkOutcomeAsync(ChatLinkVerification verification)
		{
			string text = verification.Outcome switch
			{
				ChatLinkOutcome.Linked =>
					$"Your Discord account has been successfully linked to **{verification.CharacterName}**!",
				ChatLinkOutcome.LinkedToAnotherAccount =>
					$"Your code was typed by **{verification.CharacterName}**, but this Discord account is already linked to another game account, so nothing was changed. " +
					"Use `/unlink` first if you want to link it to a different account.",
				_ =>
					"Your code was received, but the link could not be saved. Please run `/link` again.",
			};

			try
			{
				var discordUser = discordClient.GetUser(verification.DiscordUserId);
				if (discordUser != null)
				{
					var dmChannel = await discordUser.CreateDMChannelAsync();
					await dmChannel.SendMessageAsync(text, allowedMentions: AllowedMentions.None);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Could not DM Discord user {UserId} about their account link ({Outcome}).",
					verification.DiscordUserId, verification.Outcome);
			}
		}

		/// <inheritdoc />
		public Task StopAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("ChatPollingService is stopping.");
			timer?.Change(Timeout.Infinite, 0);
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				timer?.Dispose();
				pollLock.Dispose();
			}
		}
	}
}