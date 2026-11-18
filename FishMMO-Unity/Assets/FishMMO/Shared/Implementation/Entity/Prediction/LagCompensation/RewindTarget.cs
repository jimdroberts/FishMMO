namespace FishMMO.Shared
{
	/// <summary>
	/// A point in the past to rewind the world to: a whole tick, plus how far <b>before</b> that tick
	/// the target actually sits.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A bare tick was not enough to reproduce what a client saw. Its view of a peer is produced by
	/// NetworkTransform interpolation, which blends between two received snapshots and lands
	/// somewhere <em>between</em> two ticks — so a rewind quantised to a tick boundary reproduces a
	/// position the client never displayed. At 6 m/s one tick is 20 cm, which is the difference
	/// between a hit and a miss on a capsule.
	/// </para>
	/// <para>
	/// <see cref="SubTickFraction"/> is in [0,1) and measured backwards, so
	/// <c>Tick = 100, SubTickFraction = 0.25</c> means "a quarter of a tick before tick 100" — that
	/// is, 75% of the way from tick 99 to tick 100. Zero means exactly on the tick, which is what
	/// every caller that has no sub-tick information should use.
	/// </para>
	/// </remarks>
	public readonly struct RewindTarget
	{
		/// <summary>The whole tick the target sits on or just before.</summary>
		public readonly uint Tick;

		/// <summary>
		/// How far before <see cref="Tick"/> the target sits, in ticks, in [0,1).
		/// </summary>
		public readonly float SubTickFraction;

		/// <summary>True when this target names a real tick.</summary>
		public readonly bool IsValid;

		public RewindTarget(uint tick, float subTickFraction = 0f)
		{
			Tick = tick;
			// Clamped rather than trusted: the fraction originates on the client.
			if (subTickFraction < 0f)
			{
				subTickFraction = 0f;
			}
			else if (subTickFraction >= 1f)
			{
				subTickFraction = 0.999f;
			}
			SubTickFraction = subTickFraction;
			IsValid = true;
		}

		/// <summary>A target that names nothing; callers should not rewind.</summary>
		public static RewindTarget None => default;

		/// <summary>
		/// The two ticks this target lies between, and how far along it sits.
		/// </summary>
		/// <remarks>
		/// With a zero fraction both bounds are <see cref="Tick"/> and <paramref name="alpha"/> is 1,
		/// so a caller with no sub-tick information resolves exactly the sample it asked for.
		/// </remarks>
		/// <param name="olderTick">The tick immediately before the target (or the target's own tick).</param>
		/// <param name="newerTick">The tick immediately after the target (or the target's own tick).</param>
		/// <param name="alpha">Interpolation weight from <paramref name="olderTick"/> toward <paramref name="newerTick"/>.</param>
		public void GetBounds(out uint olderTick, out uint newerTick, out float alpha)
		{
			newerTick = Tick;
			if (SubTickFraction <= 0f || Tick == 0u)
			{
				olderTick = Tick;
				alpha = 1f;
				return;
			}

			olderTick = Tick - 1u;
			alpha = 1f - SubTickFraction;
		}

		/// <summary>The target as a fractional tick, for logging and tests.</summary>
		public double AsFractionalTick => Tick - (double)SubTickFraction;
	}
}
