using System;
using FishMMO.Database.Data;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Turns the stored mutes on a character and its account into the one number the chat path tests.
	/// </summary>
	/// <remarks>
	/// Kept apart from the chat system so the combination rule can be tested without a server:
	/// either mute silences the character, the later end wins, and a mute with no end beats both.
	/// </remarks>
	public static class ChatMutePolicy
	{
		/// <summary>The in-memory value for a mute with no end.</summary>
		public const long NoEnd = long.MaxValue;

		/// <summary>
		/// Resolves when a character's chat is silenced until, as UTC ticks.
		/// </summary>
		/// <param name="state">The character's own mute and its account's.</param>
		/// <param name="utcNow">The present, for deciding which mutes have lapsed.</param>
		/// <param name="reason">The reason recorded with whichever mute ends last, or null when not muted.</param>
		/// <returns>0 when not muted, <see cref="NoEnd"/> for a mute with no end, else the end's ticks.</returns>
		public static long ResolveUntilTicks(CharacterChatMuteState state, DateTime utcNow, out string reason)
		{
			long account = UntilTicks(state.Account, utcNow);
			long character = UntilTicks(state.Character, utcNow);

			if (account == 0 && character == 0)
			{
				reason = null;
				return 0;
			}
			if (account >= character)
			{
				reason = state.Account.Reason;
				return account;
			}
			reason = state.Character.Reason;
			return character;
		}

		/// <summary>
		/// The line a muted player is shown when chat is refused.
		/// </summary>
		/// <remarks>
		/// Says how long is left and why, and where to go. A player refused with no explanation types
		/// the message again, and one who is not told they can still file a ticket has no way to ask.
		/// </remarks>
		public static string DescribeForPlayer(long mutedUntilTicks, string reason, long nowTicks)
		{
			string span = mutedUntilTicks == NoEnd
				? "until staff lift it"
				: "for " + OperatorCommandParsing.DescribeDuration(TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, mutedUntilTicks - nowTicks))) + " more";
			/* 40, measured against the longest fixed text: "You are muted until staff lift it" plus the
			 * trailing sentence is 72 characters, so a 40-character reason leaves the line at 114,
			 * inside ChatBroadcast.MaxTextLength. At 60 it came to 134 and the client discarded it. */
			string because = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({OperatorCommandParsing.Truncate(reason, 40)})";
			return $"You are muted {span}{because}. Commands such as /helpme still work.";
		}

		private static long UntilTicks(ChatMuteData mute, DateTime utcNow)
		{
			if (!mute.IsActiveAt(utcNow))
			{
				return 0;
			}
			return mute.MutedUntilUtc == null ? NoEnd : mute.MutedUntilUtc.Value.Ticks;
		}
	}
}
