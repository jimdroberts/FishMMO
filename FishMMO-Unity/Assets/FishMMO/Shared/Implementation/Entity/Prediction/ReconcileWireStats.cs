namespace FishMMO.Shared
{
	/// <summary>
	/// What the server's CharacterReconcileData writes cost on the wire: full snapshots against deltas, and each delta's
	/// bytes by section. A diagnostic for the scene server's simulate report; counters only, never read by the simulation.
	/// </summary>
	public static class ReconcileWireStats
	{
		/// <summary>The sections of a delta reconcile, in the order they are written.</summary>
		public enum Section
		{
			Header,
			Motor,
			Ability,
			Resources,
			Flags,
			Cooldowns,
			Buffs,
			Rng,
			Attributes,
			Equipment,
			ChargedHold,
			Exposure,
		}

		/// <summary>Number of <see cref="Section"/> values.</summary>
		public const int SectionCount = (int)Section.Exposure + 1;

		private static long fullCount, fullBytes, deltaCount, deltaBytes;
		private static readonly long[] sectionBytes = new long[SectionCount];

		/// <summary>
		/// Above zero while a reconcile is serialized for something other than the wire (the quiet-reconcile comparison),
		/// so that write is not counted as sent. Main thread only.
		/// </summary>
		public static int NotOnTheWire;

		/// <summary>One full snapshot of <paramref name="bytes"/> bytes was written.</summary>
		public static void AddFull(int bytes)
		{
			if (NotOnTheWire > 0)
			{
				return;
			}
			fullCount++;
			fullBytes += bytes;
		}

		/// <summary>One delta of <paramref name="bytes"/> bytes was written.</summary>
		public static void AddDelta(int bytes)
		{
			deltaCount++;
			deltaBytes += bytes;
		}

		/// <summary><paramref name="bytes"/> of a delta went to <paramref name="section"/>.</summary>
		public static void AddSection(Section section, int bytes)
		{
			sectionBytes[(int)section] += bytes;
		}

		/// <summary>A copy of the counters, to subtract from a later one.</summary>
		public struct Snapshot
		{
			public long FullCount, FullBytes, DeltaCount, DeltaBytes;
			public long[] SectionBytes;
		}

		/// <summary>The counters now.</summary>
		public static Snapshot Take()
		{
			return new Snapshot
			{
				FullCount = fullCount,
				FullBytes = fullBytes,
				DeltaCount = deltaCount,
				DeltaBytes = deltaBytes,
				SectionBytes = (long[])sectionBytes.Clone(),
			};
		}
	}
}
