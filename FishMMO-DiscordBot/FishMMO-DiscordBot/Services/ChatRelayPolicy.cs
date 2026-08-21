using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Decides which in-game chat channels are allowed to be republished to Discord.
	/// </summary>
	/// <remarks>
	/// This is an <em>allowlist</em>, and it is the only thing standing between the game's chat
	/// table and a public Discord channel.
	/// <para>
	/// The relay used to be a single negation — "forward everything that is not already a Discord
	/// message" — which is a rule that grows the wrong way: every channel added to the game since
	/// became publishable by default, and nobody had to decide that it should be. What it
	/// published, in practice, was private messages. A whisper is persisted with the recipient's
	/// name at the front of the body and the sender's name on the row, so both parties and the
	/// full text of the message were posted verbatim into a channel neither of them could see and
	/// had never agreed to. Guild and party chat went the same way.
	/// </para>
	/// <para>
	/// The default is the set of channels a player can already assume is public: anyone standing
	/// nearby hears Say, and World, Trade and Region are broadcast to everyone on the shard. A
	/// server operator can widen it in configuration, but doing so is now a deliberate act with a
	/// name attached rather than the consequence of an enum getting a new member.
	/// </para>
	/// <para>
	/// Channels that carry an expectation of privacy — <see cref="ChatChannel.Tell"/>,
	/// <see cref="ChatChannel.Guild"/> and <see cref="ChatChannel.Party"/> — cannot be enabled
	/// through configuration at all. Making them relayable is a decision that needs to be taken in
	/// code, with a code review attached to it, not by editing a JSON file. Naming one in
	/// configuration is refused and logged.
	/// </para>
	/// </remarks>
	public sealed class ChatRelayPolicy
	{
		/// <summary>Configuration key holding the allowlist of relayable channel names.</summary>
		public const string ConfigurationSection = "ChatRelay:GameToDiscordChannels";

		/// <summary>
		/// Channels relayed when nothing is configured. Public channels only.
		/// </summary>
		private static readonly ChatChannel[] DefaultAllowed =
		{
			ChatChannel.Say,
			ChatChannel.World,
			ChatChannel.Trade,
			ChatChannel.Region,
		};

		/// <summary>
		/// Channels that may never be relayed, whatever the configuration says.
		/// </summary>
		private static readonly HashSet<ChatChannel> NeverRelayable = new HashSet<ChatChannel>
		{
			ChatChannel.Tell,
			ChatChannel.Guild,
			ChatChannel.Party,
			// Already-bridged Discord traffic: relaying it back would loop.
			ChatChannel.Discord,
			// Slash commands are never persisted as chat, but the enum member exists.
			ChatChannel.Command,
		};

		private readonly HashSet<byte> allowed = new HashSet<byte>();

		/// <summary>
		/// Builds the policy from configuration, falling back to the public-channel default.
		/// </summary>
		/// <param name="configuration">Application configuration.</param>
		/// <param name="logger">Logger used to report the effective policy at startup.</param>
		public ChatRelayPolicy(IConfiguration configuration, ILogger<ChatRelayPolicy> logger)
		{
			List<string>? configured = configuration
				?.GetSection(ConfigurationSection)
				?.Get<string[]>()
				?.Where(name => !string.IsNullOrWhiteSpace(name))
				.ToList();

			if (configured == null || configured.Count == 0)
			{
				foreach (ChatChannel channel in DefaultAllowed)
				{
					allowed.Add((byte)channel);
				}
				logger?.LogInformation(
					"Game-to-Discord relay using the default public-channel allowlist: {Channels}.",
					string.Join(", ", DefaultAllowed));
				return;
			}

			foreach (string name in configured)
			{
				if (!Enum.TryParse(name.Trim(), ignoreCase: true, out ChatChannel channel) ||
					!Enum.IsDefined(typeof(ChatChannel), channel))
				{
					logger?.LogWarning(
						"Ignoring unknown chat channel '{Channel}' in {Section}.", name, ConfigurationSection);
					continue;
				}

				if (NeverRelayable.Contains(channel))
				{
					logger?.LogError(
						"Refusing to relay '{Channel}' to Discord: it is a private or internal channel and " +
						"cannot be enabled from configuration. Remove it from {Section}.",
						channel, ConfigurationSection);
					continue;
				}

				allowed.Add((byte)channel);
			}

			if (allowed.Count == 0)
			{
				logger?.LogWarning(
					"{Section} named no relayable channel. Game-to-Discord relay is disabled.",
					ConfigurationSection);
				return;
			}

			logger?.LogInformation(
				"Game-to-Discord relay allowlist: {Channels}.",
				string.Join(", ", allowed.Select(b => ((ChatChannel)b).ToString())));
		}

		/// <summary>
		/// Whether a persisted chat row may be republished to Discord.
		/// </summary>
		/// <param name="channel">The stored <c>ChatEntity.Channel</c> byte.</param>
		/// <returns>True only when the channel is explicitly allowed.</returns>
		public bool IsRelayable(byte channel) => allowed.Contains(channel);
	}
}
