namespace FishMMO.Shared
{
	/// <summary>
	/// Per-tick record of what the owning client predicted for its ability controller, so a
	/// reconcile can be compared against the client's state <b>at the reconcile tick</b> rather
	/// than against whatever the client holds now.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet only applies a reconcile once its tick is at least <c>stateInterpolation + 1</c>
	/// ticks behind the client's local tick, and the client has already simulated every tick in
	/// between. The server's seed and ability ID in that reconcile therefore describe an older
	/// tick than the client's live <c>currentSeed</c> / <c>currentAbilityID</c>. Comparing the two
	/// directly declared a "mismatch" on every cast — the client had advanced its seed for a cast
	/// the server had not yet processed — which destroyed every owner-predicted projectile about
	/// one tick after it spawned and cleared the cast bar on every reconcile for the first RTT of
	/// every timed cast.
	/// </para>
	/// <para>
	/// Recording the predicted state per tick and looking up the entry for the reconcile's own
	/// tick makes the comparison like-for-like: a real misprediction is one where the client's
	/// simulation of tick T produced a different seed than the server's simulation of tick T.
	/// </para>
	/// <para>
	/// Fixed-size ring indexed by <c>tick &amp; (capacity - 1)</c>; each slot stores the tick it
	/// holds so a stale slot is never mistaken for the requested tick. Capacity must be a power of
	/// two and comfortably larger than the client's look-ahead (a few ticks) plus a full reconcile
	/// window; 128 ticks is over four seconds at 30 Hz.
	/// </para>
	/// </remarks>
	public sealed class PredictedAbilityStateHistory
	{
		/// <summary>Default ring capacity, in ticks. Must be a power of two.</summary>
		public const int DefaultCapacity = 128;

		private struct Entry
		{
			public uint Tick;
			public int Seed;
			public long AbilityID;
			public bool Valid;
		}

		private readonly Entry[] entries;
		private readonly int mask;

		/// <summary>Ring capacity in ticks.</summary>
		public int Capacity => entries.Length;

		/// <summary>
		/// Creates a history ring.
		/// </summary>
		/// <param name="capacity">Number of ticks retained. Rounded up to a power of two, minimum 2.</param>
		public PredictedAbilityStateHistory(int capacity = DefaultCapacity)
		{
			int size = 2;
			while (size < capacity)
			{
				size <<= 1;
			}
			entries = new Entry[size];
			mask = size - 1;
		}

		/// <summary>
		/// Records the state the client holds after simulating <paramref name="tick"/>.
		/// Overwrites any earlier record for the same tick (replays re-simulate a tick and the
		/// later simulation is the one a subsequent reconcile should be compared with).
		/// </summary>
		public void Record(uint tick, int seed, long abilityID)
		{
			ref Entry e = ref entries[(int)(tick & (uint)mask)];
			e.Tick = tick;
			e.Seed = seed;
			e.AbilityID = abilityID;
			e.Valid = true;
		}

		/// <summary>
		/// Returns the recorded state for <paramref name="tick"/>, if it is still in the ring.
		/// </summary>
		public bool TryGet(uint tick, out int seed, out long abilityID)
		{
			ref Entry e = ref entries[(int)(tick & (uint)mask)];
			if (e.Valid && e.Tick == tick)
			{
				seed = e.Seed;
				abilityID = e.AbilityID;
				return true;
			}
			seed = 0;
			abilityID = 0;
			return false;
		}

		/// <summary>Forgets every recorded tick.</summary>
		public void Clear()
		{
			for (int i = 0; i < entries.Length; ++i)
			{
				entries[i].Valid = false;
			}
		}
	}
}
