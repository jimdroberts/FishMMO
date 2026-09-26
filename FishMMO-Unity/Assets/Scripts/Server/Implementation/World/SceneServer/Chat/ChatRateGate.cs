using System;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The per-sender chat rate rule: a token bucket, then a minimum gap between messages.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A pure function over the sender's four rate fields, so the rule can be pinned by a test
	/// without a character, a connection or a server. <see cref="ChatSystem"/> copies the fields
	/// off the character, charges, and copies them back.
	/// </para>
	/// <para>
	/// <b>On the monotonic clock.</b> The ticks are <c>MonotonicClock.NowTicks</c>, not the
	/// message's legal receipt stamp: a bucket and a gap are local durations, and on the wall clock
	/// a host stepped back refused every sender for the size of the step. A character's fields
	/// start at zero, which reads as "never", whatever the clock.
	/// </para>
	/// <para>
	/// <b>Charged when a message is queued, not when it is processed.</b> The incoming queue used
	/// to take everything a client sent and charge the bucket only as the queue drained, so the
	/// queue's size bounded nothing any one sender did. Its global cap then kicked whichever
	/// connection happened to push it over — after a one-second main-thread hitch with a hundred
	/// busy players, the next honest player to type (hot-path audit L2). Charged at the door, a
	/// sender can occupy the queue only at the rate the bucket allows, so the cap is reached by
	/// many honest senders at once or not at all, and nobody is kicked for it.
	/// </para>
	/// </remarks>
	public static class ChatRateGate
	{
		/// <summary>
		/// One sender's rate state, as held on the character.
		/// </summary>
		public struct State
		{
			/// <summary>Tokens in the bucket. One is spent per message.</summary>
			public double Tokens;
			/// <summary>Monotonic ticks of the last refill.</summary>
			public long LastRefillTicks;
			/// <summary>True until the first message: the bucket starts full.</summary>
			public bool IsFull;
			/// <summary>Monotonic ticks before which the next message is refused by the minimum gap.</summary>
			public long NextMessageTicks;
		}

		/// <summary>
		/// Charges one message against <paramref name="state"/>.
		/// </summary>
		/// <param name="state">The sender's rate state; updated in place.</param>
		/// <param name="nowTicks">Monotonic ticks as the message was received.</param>
		/// <param name="capacity">Bucket capacity: the burst a sender may send. Zero or less disables the bucket.</param>
		/// <param name="refillPerSecond">Tokens returned per second. Zero or less disables the bucket.</param>
		/// <param name="minimumGapMilliseconds">Minimum gap between messages. Zero or less disables it.</param>
		/// <returns>True if the message may be sent.</returns>
		/// <remarks>
		/// The token is spent before the gap is tested, as it always was: a message refused by the
		/// gap still cost its token. A bucket marked full is filled on the first message whatever
		/// the elapsed time; it used to be filled only when some time had passed since the last
		/// refill, which in the same tick left a new character's first message refused against an
		/// empty bucket it had been told was full.
		/// </remarks>
		public static bool TryCharge(ref State state, long nowTicks, int capacity, double refillPerSecond, double minimumGapMilliseconds)
		{
			if (capacity > 0 && refillPerSecond > 0.0)
			{
				if (state.IsFull)
				{
					state.Tokens = capacity;
					state.IsFull = false;
					if (nowTicks > state.LastRefillTicks)
					{
						state.LastRefillTicks = nowTicks;
					}
				}
				else
				{
					double elapsedSeconds = (nowTicks - state.LastRefillTicks) / (double)TimeSpan.TicksPerSecond;
					if (elapsedSeconds > 0.0)
					{
						state.Tokens = Math.Min(capacity, state.Tokens + elapsedSeconds * refillPerSecond);
						state.LastRefillTicks = nowTicks;
					}
				}

				if (state.Tokens < 1.0)
				{
					return false;
				}
				state.Tokens -= 1.0;
			}

			if (minimumGapMilliseconds > 0.0)
			{
				if (state.NextMessageTicks > nowTicks)
				{
					return false;
				}
				state.NextMessageTicks = nowTicks + (long)(minimumGapMilliseconds * TimeSpan.TicksPerMillisecond);
			}

			return true;
		}
	}
}
