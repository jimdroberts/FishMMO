using System;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The escalation from refused chat lines to a temporary mute: a sender whose lines the rate
	/// gate refuses too often within a short window is muted for a few minutes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="ChatRateGate"/> already bounds what any sender can put through, so a flood costs
	/// nobody else anything. It was silent, though: a flooding client kept hammering the gate for as
	/// long as it liked and was never told, and the lines it did get through — one a second at the
	/// default refill — kept arriving in everybody's chat. Past a threshold the sender is muted and
	/// told why, with a system line.
	/// </para>
	/// <para>
	/// <b>Counted from refusals, not from lines.</b> An honest player is refused now and then: two
	/// short lines typed inside the minimum gap, or several delivered at once after a network stall
	/// — a few, once. The threshold sits well above that: at the defaults, ten refusals within ten
	/// seconds takes a client sending twice the sustained rate for ten seconds once its burst is
	/// spent, or anything much faster for about a second.
	/// </para>
	/// <para>
	/// <b>The window opens at the first refusal</b> and closes a fixed time later; a refusal after it
	/// has closed opens a new one. Reaching the threshold mutes the sender and empties the window, so
	/// a sender who goes on flooding while muted reaches it again and the mute is pushed out from
	/// then: the mute ends a full term after the flooding stops.
	/// </para>
	/// <para>
	/// <b>Monotonic seconds.</b> A duration measured on the wall clock ends early or late when the
	/// host clock is stepped (see <c>FishMMO.Server.Core.MonotonicClock</c>). This is never
	/// compared with anything outside the process, so the process clock is the right one.
	/// </para>
	/// <para>
	/// Pure, so the rule can be pinned by a test without a character, a connection or a server.
	/// <see cref="ChatSystem"/> holds one <see cref="State"/> per character that has been refused.
	/// </para>
	/// </remarks>
	public static class ChatFloodMute
	{
		/// <summary>
		/// One sender's flood state.
		/// </summary>
		public struct State
		{
			/// <summary>Refusals counted in the open window.</summary>
			public int Refusals;
			/// <summary>Monotonic seconds of the refusal that opened the window.</summary>
			public double WindowStartSeconds;
			/// <summary>Monotonic seconds the mute ends at. Not muted at or after it; 0 when never muted.</summary>
			public double MutedUntilSeconds;
		}

		/// <summary>What one refusal did.</summary>
		public enum Outcome : byte
		{
			/// <summary>Counted; the threshold was not reached.</summary>
			Counted = 0,
			/// <summary>The threshold was reached by a sender who was not muted: they are now.</summary>
			Muted = 1,
			/// <summary>The threshold was reached by a sender already muted: the mute now ends later.</summary>
			Extended = 2,
			/// <summary>The flood mute is switched off; nothing was recorded.</summary>
			Disabled = 3,
		}

		/// <summary>
		/// Records one line refused by the rate gate.
		/// </summary>
		/// <param name="state">The sender's flood state; updated in place.</param>
		/// <param name="nowSeconds">Monotonic seconds now.</param>
		/// <param name="threshold">Refusals within the window that mute. Zero or less disables the flood mute.</param>
		/// <param name="windowSeconds">The window's length. Zero or less disables the flood mute.</param>
		/// <param name="muteSeconds">How long a mute lasts. Zero or less disables the flood mute.</param>
		/// <returns>What the refusal did.</returns>
		public static Outcome RecordRefusal(ref State state, double nowSeconds, int threshold, double windowSeconds, double muteSeconds)
		{
			if (threshold < 1 || windowSeconds <= 0.0 || muteSeconds <= 0.0)
			{
				return Outcome.Disabled;
			}

			if (state.Refusals < 1 || nowSeconds - state.WindowStartSeconds > windowSeconds)
			{
				state.Refusals = 0;
				state.WindowStartSeconds = nowSeconds;
			}

			++state.Refusals;
			if (state.Refusals < threshold)
			{
				return Outcome.Counted;
			}

			bool wasMuted = IsMuted(state, nowSeconds);
			state.Refusals = 0;
			double until = nowSeconds + muteSeconds;
			if (until > state.MutedUntilSeconds)
			{
				state.MutedUntilSeconds = until;
			}
			return wasMuted ? Outcome.Extended : Outcome.Muted;
		}

		/// <summary>
		/// Whether a sender is flood-muted now.
		/// </summary>
		/// <param name="state">The sender's flood state.</param>
		/// <param name="nowSeconds">Monotonic seconds now.</param>
		/// <returns>True while the mute has time left.</returns>
		public static bool IsMuted(State state, double nowSeconds)
		{
			return state.MutedUntilSeconds > nowSeconds;
		}

		/// <summary>
		/// Whether a sender's flood state has nothing left to remember, so it can be dropped.
		/// </summary>
		/// <param name="state">The sender's flood state.</param>
		/// <param name="nowSeconds">Monotonic seconds now.</param>
		/// <param name="windowSeconds">The window's length.</param>
		/// <returns>True when the sender is not muted and no window is open.</returns>
		public static bool IsSpent(State state, double nowSeconds, double windowSeconds)
		{
			return !IsMuted(state, nowSeconds) &&
				   (state.Refusals < 1 || nowSeconds - state.WindowStartSeconds > windowSeconds);
		}

		/// <summary>
		/// The system line a flood-muted sender is shown: when the mute starts, and for each line
		/// refused while it lasts.
		/// </summary>
		/// <param name="mutedUntilSeconds">Monotonic seconds the mute ends at.</param>
		/// <param name="nowSeconds">Monotonic seconds now.</param>
		/// <returns>The line; well inside <c>ChatBroadcast.MaxTextLength</c>.</returns>
		/// <remarks>
		/// Says why and for how long, and that commands still work: a player refused with no reason
		/// types the line again, and one who is not told they can still file a ticket has no way to
		/// ask. The remaining time is rounded up, so the first line of a three-minute mute says three
		/// minutes and not two.
		/// </remarks>
		public static string DescribeForPlayer(double mutedUntilSeconds, double nowSeconds)
		{
			double remaining = Math.Max(1.0, Math.Ceiling(mutedUntilSeconds - nowSeconds));
			return $"You are sending messages too quickly and are muted for {OperatorCommandParsing.DescribeDuration(TimeSpan.FromSeconds(remaining))}. Commands such as /helpme still work.";
		}
	}
}
