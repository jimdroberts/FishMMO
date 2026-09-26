using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Bridges Discord messages from managed game chat channels into the game database.
	/// Enforces rate limiting, bridge ban checks, and message length validation before insertion.
	/// </summary>
	public class GameChatBridgeService
	{
		private readonly IChatService chatService;
		private readonly DynamicChannelManagerService dynamicChannelManager;
		private readonly RateLimiterService rateLimiter;
		private readonly BridgeBanService bridgeBanService;
		private readonly AccountLinkingService accountLinkingService;
		private readonly ILogger<GameChatBridgeService> logger;
		private readonly int maxMessageLength;

		/// <summary>
		/// Default cap on a bridged message body, matching <c>ChatBroadcast.MaxTextLength</c>.
		/// </summary>
		private const int DefaultMaxMessageLength = 128;

		/// <summary>
		/// Cap on the Discord display name written into the chat row's sender column.
		/// </summary>
		/// <remarks>
		/// Discord names are up to 32 characters and are chosen by the user. They share the
		/// message field with the body, so an unbounded one crowds out the message itself.
		/// </remarks>
		private const int MaxAuthorNameLength = 32;

		/// <summary>
		/// Initializes a new instance of the <see cref="GameChatBridgeService"/> class.
		/// </summary>
		/// <param name="chatService">Chat persistence, shared with the scene servers so bridged rows are stamped by the same clock.</param>
		/// <param name="dynamicChannelManager">Service managing dynamic Discord channels.</param>
		/// <param name="rateLimiter">Per-user rate limiter.</param>
		/// <param name="logger">Logger instance.</param>
		/// <param name="configuration">Application configuration.</param>
		public GameChatBridgeService(
			IChatService chatService,
			DynamicChannelManagerService dynamicChannelManager,
			RateLimiterService rateLimiter,
			BridgeBanService bridgeBanService,
			AccountLinkingService accountLinkingService,
			ILogger<GameChatBridgeService> logger,
			IConfiguration configuration)
		{
			this.chatService = chatService;
			this.dynamicChannelManager = dynamicChannelManager;
			this.rateLimiter = rateLimiter;
			this.bridgeBanService = bridgeBanService;
			this.accountLinkingService = accountLinkingService;
			this.logger = logger;

			/* Defaults to the game's own chat limit, not to something larger.
			 *
			 * This was 500 while the game client discards any chat broadcast longer than
			 * ChatBroadcast.MaxTextLength (128), so a long Discord message was written to the
			 * chat table, relayed by the scene server, and then silently dropped by every client
			 * that received it. Truncating here means a long message arrives shortened instead of
			 * not arriving at all. The scene server truncates again on the way out; this is the
			 * friendly cut, that one is the authoritative one. */
			if (!int.TryParse(configuration["BridgeMessageMaxLength"], out int parsed) || parsed <= 0)
			{
				parsed = DefaultMaxMessageLength;
			}
			maxMessageLength = parsed;

			logger.LogInformation("GameChatBridgeService initialized with max message length: {MaxLength}.", maxMessageLength);
		}

		/// <summary>
		/// Attempts to bridge a Discord message to the game database.
		/// Validates the channel is managed, the user is not rate-limited, and the message content is valid.
		/// </summary>
		/// <param name="message">The Discord user message.</param>
		/// <param name="textChannel">The Discord text channel the message was sent in.</param>
		/// <returns>The result of the bridge attempt.</returns>
		public async Task<BridgeResult> BridgeMessageAsync(SocketUserMessage message, SocketTextChannel textChannel)
		{
			if (!dynamicChannelManager.IsOurDynamicChannel(textChannel.Guild.Id, textChannel.Id))
			{
				return BridgeResult.NotManagedChannel;
			}

			if (rateLimiter.IsRateLimited(message.Author.Id))
			{
				logger.LogWarning(
					"Rate-limited bridge message from {User} (ID: {UserId}) in channel {Channel}.",
					message.Author.Username, message.Author.Id, textChannel.Name);
				return BridgeResult.RateLimited;
			}

			/* Check if the Discord user's linked game account is bridge-banned. The link lives in the
			 * database now and names an account, not a character, so only account bans apply here.
			 * A failed lookup refuses the message: a ban that cannot be checked is not lifted. */
			var linkLookup = await accountLinkingService.GetLinkedAccountAsync(message.Author.Id);
			if (!linkLookup.IsSuccess)
			{
				logger.LogWarning(
					"Could not read the account link for {User} (ID: {UserId}) to check bridge bans ({ErrorCode}). Message not bridged.",
					message.Author.Username, message.Author.Id, linkLookup.ErrorCode);
				return BridgeResult.Error;
			}
			if (linkLookup.Data.HasValue && bridgeBanService.IsBridgeBanned(null, linkLookup.Data.Value.AccountName))
			{
				logger.LogWarning(
					"Bridge-banned user {User} (linked to account {AccountName}) attempted to bridge message.",
					message.Author.Username, linkLookup.Data.Value.AccountName);
				return BridgeResult.BridgeBanned;
			}

			/* Discord is fully untrusted, and this is where it crosses into the game.
			 *
			 * Both halves of what gets written were taken raw: the author's Discord display name
			 * (which that user picks, and can change to anything at any moment) and the message
			 * body. They were concatenated into ChatEntity.Message, broadcast verbatim by the
			 * scene server, exempted from the client's tab filtering, and rendered into a Label
			 * that parses Unity rich text. A display name of "<size=500>" was therefore a
			 * client-side attack on every player in the world, launched from outside the game by
			 * somebody who does not need an account to do it.
			 *
			 * SanitizeIncoming is the same pipeline the server runs on player chat — control
			 * characters (including U+202E and newlines), rich-text markup with reassembly
			 * handled, and the FISHMMO_ control-code prefix. That last one matters here more than
			 * it does for players: a Discord user typing FISHMMO_TELL_RELAYED would otherwise
			 * have their message rendered in every recipient's log as a private whisper.
			 *
			 * The scene server sanitises this again when it relays the row. That is not
			 * redundancy for its own sake — the chat table is shared state and the server must
			 * not assume a row in it was written by a version of this bot that cleans. */
			string content = ChatSanitizer.SanitizeIncoming(message.Content, maxMessageLength);
			if (string.IsNullOrWhiteSpace(content))
			{
				return BridgeResult.EmptyMessage;
			}

			string authorName = ChatSanitizer.SanitizeIncoming(message.Author.Username, MaxAuthorNameLength);
			if (string.IsNullOrWhiteSpace(authorName))
			{
				// A display name made entirely of markup or invisible characters cleans to
				// nothing. Fall back rather than write an empty sender column.
				authorName = "DiscordUser";
			}

			var (worldId, sceneId) = dynamicChannelManager.GetWorldAndSceneIdsFromChannel(textChannel);
			if (!worldId.HasValue || !sceneId.HasValue)
			{
				logger.LogWarning(
					"Could not extract World/Scene IDs from channel '{ChannelName}' (ID: {ChannelId}). Message not bridged.",
					textChannel.Name, textChannel.Id);
				return BridgeResult.ParseFailure;
			}

			try
			{
				/* Through the scene servers' own INSERT, not an entity of the bot's making.
				 *
				 * This wrote the row with time_created taken from THIS host's clock, and every scene
				 * server's chat pump pages the table by that column. A bot host whose clock ran
				 * behind the database stamped rows the pumps had already passed, and the line never
				 * reached the game (hot-path audit H5). ChatService stamps every row with the
				 * database clock, whoever writes it. */
				DatabaseResult written = await chatService.PersistBridgedAsync(
					worldId.Value,
					sceneId.Value,
					authorName,
					$"{authorName} {content}",
					DateTime.UtcNow);
				if (!written.IsSuccess)
				{
					logger.LogError(
						"Failed to bridge Discord message from {User} in channel {Channel}: [{ErrorCode}] {ErrorMessage}",
						message.Author.Username, textChannel.Name, written.ErrorCode, written.ErrorMessage);
					return BridgeResult.Error;
				}

				dynamicChannelManager.UpdateChannelActivity(textChannel.Guild.Id, worldId.Value, sceneId.Value);

				logger.LogInformation(
					"Bridged Discord message to game DB: '{Content}' by '{User}' (World: {WorldId}, Scene: {SceneId}).",
					content, authorName, worldId.Value, sceneId.Value);

				return BridgeResult.Success;
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Failed to bridge Discord message from {User} in channel {Channel}.",
					message.Author.Username, textChannel.Name);
				return BridgeResult.Error;
			}
		}
	}

	/// <summary>
	/// Result of a Discord-to-game bridge operation.
	/// </summary>
	public enum BridgeResult : byte
	{
		/// <summary>Message was successfully bridged to the game database.</summary>
		Success = 0,
		/// <summary>Channel is not a managed game chat channel.</summary>
		NotManagedChannel,
		/// <summary>User exceeded the message rate limit.</summary>
		RateLimited,
		/// <summary>Message content was empty or whitespace.</summary>
		EmptyMessage,
		/// <summary>Could not extract World/Scene IDs from the channel.</summary>
		ParseFailure,
		/// <summary>The user's linked game account is bridge-banned.</summary>
		BridgeBanned,
		/// <summary>A database or unexpected error occurred.</summary>
		Error
	}
}