using FishNet.Managing.Timing;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Converts a prediction replicate's tick into the tick the weather timeline is evaluated at.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two clocks, and they are not the same one.</b> The weather timeline is anchored in the
	/// SYNCHRONISED tick (<c>TimeManager.Tick</c>): <see cref="WeatherTimeline.WorldSecondsAt"/>
	/// measures from <c>WorldSecondsTick</c>, which the scene server stamps from its own
	/// <c>TimeManager.Tick</c> and broadcasts. A replicate's <c>input.GetTick()</c>, however, is the
	/// OWNING CLIENT's unsynchronised <c>LocalTick</c> — a counter that restarts at zero on every
	/// connect — so on the server the two differ by an arbitrary per-connection constant. Feeding an
	/// input tick straight to the weather would read the forecast for some other hour entirely, and
	/// would read a DIFFERENT wrong hour on the client than on the server, so the reconcile would
	/// fight the prediction for ever.
	/// </para>
	/// <para>
	/// <b>The mapping.</b> FishNet hands us the pairing it reconciled on: <c>ClientStateTick</c> is
	/// the local tick of the last reconcile and <c>ServerStateTick</c> is the server tick of that
	/// same reconcile. The difference between them is the constant, so
	/// </para>
	/// <code>weatherTick = ServerStateTick + (inputTick − ClientStateTick)</code>
	/// <para>
	/// and this one line is correct both live and in replay. In replay it is provably so: FishNet
	/// walks <c>ClientReplayTick</c> and <c>ServerReplayTick</c> forward together from
	/// <c>ClientStateTick + 1</c> and <c>ServerStateTick + 1</c> (PredictionManager, the
	/// <c>while (ClientReplayTick &lt; localTick)</c> loop), so for the replicate being replayed at
	/// client tick <c>t</c> the server's tick is exactly what the formula returns. That identity is
	/// what <see cref="Resolve"/> asserts against when a replay tick is supplied, and what the
	/// closed-loop tests pin.
	/// </para>
	/// <para>
	/// <b>On the server the input tick is not used at all.</b> The server IS the authority on when
	/// "now" is; its own <c>TimeManager.Tick</c> is the weather's tick, and the client's counter is
	/// only a label for matching inputs to reconciles.
	/// </para>
	/// <para>
	/// Pure arithmetic on purpose: everything here takes plain numbers, so the whole mapping can be
	/// tested without a NetworkManager, a connection or a running scene.
	/// </para>
	/// </remarks>
	public static class WeatherExposureTick
	{
		/// <summary>FishNet's "no tick" sentinel, repeated so callers need not reference TimeManager.</summary>
		public const uint Unset = TimeManager.UNSET_TICK;

		/// <summary>
		/// The synchronised tick to evaluate the weather at for one replicate step.
		/// </summary>
		/// <param name="isServer">True when this peer is the server, which owns the clock.</param>
		/// <param name="serverTick">The server's own <c>TimeManager.Tick</c>. Used only when <paramref name="isServer"/>.</param>
		/// <param name="clientSyncTick">
		/// The client's estimate of the synchronised tick (<c>TimeManager.Tick</c>), used only before
		/// a first reconcile has established the pairing.
		/// </param>
		/// <param name="clientStateTick">PredictionManager.ClientStateTick, or <see cref="Unset"/>.</param>
		/// <param name="serverStateTick">PredictionManager.ServerStateTick, or <see cref="Unset"/>.</param>
		/// <param name="inputTick">The replicate's own tick, in the owning client's domain.</param>
		/// <returns>
		/// A tick in the weather timeline's (synchronised) domain. Zero is a real answer — it is the
		/// first tick of the timeline, which is where a mapping that would fall below it is clamped —
		/// and is also what <see cref="Unset"/> happens to be, since FishNet's sentinel is literally
		/// zero. Nothing downstream needs to tell the two apart: both mean "read the oldest weather".
		/// </returns>
		public static uint Resolve(
			bool isServer,
			uint serverTick,
			uint clientSyncTick,
			uint clientStateTick,
			uint serverStateTick,
			uint inputTick)
		{
			if (isServer)
			{
				// The server's replicate runs during its own tick; the queued input's label is the
				// owner's counter and means nothing here.
				return serverTick == Unset ? 0u : serverTick;
			}

			// No reconcile yet (the first seconds after spawn), or a replicate with no real input:
			// the client's own synchronised estimate is the best available answer, and it is the
			// same quantity the server will be using.
			if (clientStateTick == Unset || serverStateTick == Unset || inputTick == Unset)
			{
				return clientSyncTick == Unset ? 0u : clientSyncTick;
			}

			/* The pairing, extrapolated.
			 *
			 * The difference is taken in WRAPPED 32-bit arithmetic and then read as signed, which is
			 * the standard way to difference sequence numbers and is not the same as subtracting two
			 * longs. It has to be, for two reasons. An input tick may be either side of the
			 * reconciled one — behind it while replaying, ahead of it while predicting forward — so
			 * the result must be able to go negative. And a counter that has wrapped past
			 * uint.MaxValue is only a few ticks after one that has not, yet a plain subtraction of
			 * the two calls it four billion ticks BEFORE: at thirty ticks a second that is a reading
			 * of the weather four years out, or, once clamped, of the world's first moment. Wrapped,
			 * the same subtraction gives the few ticks it really is. */
			int elapsed = unchecked((int)(inputTick - clientStateTick));
			long mapped = (long)serverStateTick + elapsed;
			return mapped <= 0L ? 0u : (uint)mapped;
		}

		/// <summary>
		/// What <see cref="Resolve"/> must return while FishNet is replaying, from FishNet's own
		/// replay counters. Equal to the formula by construction — see the remarks on this class —
		/// so it exists to be asserted against rather than to be used in its place.
		/// </summary>
		/// <remarks>
		/// Not used by the controller. Taking the replay counter directly would work, but it is only
		/// valid inside a replay, so the controller would need two code paths where one will do —
		/// and a second path is a second thing to get wrong.
		/// </remarks>
		public static uint FromReplayCounters(uint serverReplayTick) => serverReplayTick;

		/// <summary>
		/// True when the formula and FishNet's replay counter agree for one replayed step. The
		/// closed-loop check, in one place, for tests and for a debug assertion.
		/// </summary>
		public static bool ReplayAgrees(
			uint clientStateTick,
			uint serverStateTick,
			uint clientReplayTick,
			uint serverReplayTick)
		{
			if (clientStateTick == Unset || serverStateTick == Unset ||
				clientReplayTick == Unset || serverReplayTick == Unset)
			{
				return true;
			}

			uint predicted = Resolve(false, Unset, Unset, clientStateTick, serverStateTick, clientReplayTick);
			return predicted == serverReplayTick;
		}
	}
}
