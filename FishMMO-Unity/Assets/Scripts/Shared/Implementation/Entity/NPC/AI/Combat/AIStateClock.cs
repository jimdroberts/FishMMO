namespace FishMMO.Shared
{
	/// <summary>
	/// Schedules a state's updates and reports how much time each one covers.
	/// </summary>
	/// <remarks>
	/// A state asks to be updated every <c>updateRate</c> seconds, and the brain ticks far more
	/// often than that. The update used to be handed the brain's tick delta — one eighth of a
	/// second — as "time elapsed", so every timer a state advanced with it (attack cooldowns,
	/// stuck detection, retreat patience, target re-evaluation) ran at the brain rate divided by
	/// the state rate: a 1.2 s attack cooldown took ten seconds of wall clock. The clock now
	/// accumulates the ticks between updates and hands the state the real interval.
	/// </remarks>
	public struct AIStateClock
	{
		/// <summary>Seconds until the next update is due.</summary>
		public float NextUpdate;

		/// <summary>Seconds accumulated since the previous update.</summary>
		public float Elapsed;

		/// <summary>
		/// Arms the clock for a state that wants its next update after <paramref name="rate"/> seconds.
		/// </summary>
		/// <param name="rate">The state's update rate, in seconds.</param>
		/// <param name="deltaTime">The brain tick being processed, already counted against the wait.</param>
		public void Rearm(float rate, float deltaTime = 0f)
		{
			NextUpdate = rate - deltaTime;
			Elapsed = 0f;
		}

		/// <summary>
		/// Advances by one brain tick.
		/// </summary>
		/// <remarks>
		/// When an update is due the wait is left alone for the caller to <see cref="Rearm"/>;
		/// otherwise the tick is counted against it.
		/// </remarks>
		/// <param name="deltaTime">Seconds in this brain tick.</param>
		/// <param name="elapsed">When true is returned, the seconds the due update covers.</param>
		/// <returns>True if an update is due now.</returns>
		public bool Advance(float deltaTime, out float elapsed)
		{
			Elapsed += deltaTime;

			if (NextUpdate < 0f)
			{
				elapsed = Elapsed;
				Elapsed = 0f;
				return true;
			}

			NextUpdate -= deltaTime;
			elapsed = 0f;
			return false;
		}
	}
}
