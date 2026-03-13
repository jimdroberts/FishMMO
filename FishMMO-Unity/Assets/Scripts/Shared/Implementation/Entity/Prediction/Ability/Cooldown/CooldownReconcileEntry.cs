using System;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// A single cooldown entry for reconcile serialization.
	/// Implements <see cref="IEquatable{T}"/> for efficient delta comparison
	/// in <see cref="CharacterReconcileDataDeltaSerializer"/>.
	/// </summary>
	public struct CooldownReconcileEntry : IEquatable<CooldownReconcileEntry>
	{
		/// <summary>The ability ID this cooldown is associated with.</summary>
		public long AbilityID;

		/// <summary>The absolute network tick at which this cooldown started.</summary>
		public uint StartTick;

		/// <summary>The duration of this cooldown in ticks.</summary>
		public uint DurationTicks;

		/// <summary>
		/// Compares all fields for equality. Used by the delta serializer
		/// to determine which entries changed between ticks.
		/// </summary>
		public bool Equals(CooldownReconcileEntry other)
		{
			return AbilityID == other.AbilityID &&
				   StartTick == other.StartTick &&
				   DurationTicks == other.DurationTicks;
		}

		/// <inheritdoc/>
		public override bool Equals(object obj)
		{
			return obj is CooldownReconcileEntry other && Equals(other);
		}

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = AbilityID.GetHashCode();
				hash = (hash * 397) ^ StartTick.GetHashCode();
				hash = (hash * 397) ^ DurationTicks.GetHashCode();
				return hash;
			}
		}

		/// <summary>
		/// Writes a single entry's fields to the network writer.
		/// </summary>
		public void WriteTo(Writer writer)
		{
			writer.WriteInt64(AbilityID);
			writer.WriteUInt32(StartTick);
			writer.WriteUInt32(DurationTicks);
		}

		/// <summary>
		/// Reads a single entry's fields from the network reader.
		/// </summary>
		public static CooldownReconcileEntry ReadFrom(Reader reader)
		{
			return new CooldownReconcileEntry
			{
				AbilityID = reader.ReadInt64(),
				StartTick = reader.ReadUInt32(),
				DurationTicks = reader.ReadUInt32(),
			};
		}

		/// <summary>
		/// Maximum number of entries allowed in a single reconcile snapshot.
		/// Guards against corrupted or malicious packets allocating unbounded arrays.
		/// </summary>
		private const int MaxEntries = 4096;

		/// <summary>
		/// Compares and writes cooldown arrays using index-delta compression.
		/// When the array length is unchanged, only changed entries are written
		/// (negative count signals index-delta mode). When the length differs or
		/// on a forced tick, the full array is written (positive count).
		/// </summary>
		/// <remarks>
		/// <para><b>Null / empty equivalence:</b> both <c>null</c> and empty arrays
		/// serialize as count == 0 on the wire, and <see cref="ReadArrayDelta"/> returns
		/// <c>null</c> for both. Callers must treat <c>null</c> and empty as identical.</para>
		/// <para><b>Position-based identity:</b> index-delta uses array position, not
		/// <see cref="AbilityID"/>. When cooldowns are added or removed, the SortedDictionary
		/// iteration order may shift entries to different positions, causing more entries
		/// to appear changed than conceptually changed. This is correct (sends more bytes)
		/// but never incorrect. When the length changes, a full array is sent.</para>
		/// </remarks>
		public static bool WriteArrayDelta(
			Writer writer,
			CooldownReconcileEntry[] prev,
			CooldownReconcileEntry[] next,
			DeltaSerializerOption option)
		{
			if (prev == null && next == null)
				return false;

			bool forceWrite = option != DeltaSerializerOption.Unset;

			if (!forceWrite && ReferenceEquals(prev, next))
				return false;

			int prevCount = prev?.Length ?? 0;
			int nextCount = next?.Length ?? 0;

			// Index-delta path: same length, single pass — reserve changedCount,
			// write entries, then patch the count. Avoids double-iterating the array.
			// FRAGILE: Relies on FishNet's Writer.Position supporting backward seeks
			// without clearing bytes between countPos and endPos. Writer.Position is
			// a plain int field (not a property) backed by a raw byte[], so rewinding
			// just moves the offset — previously written bytes remain intact.
			// Verified against FishNet 4.x. Re-validate on major FishNet upgrades.
			// FAILURE MODE: If FishNet moves to a pooled-segment writer where Position
			// is segment-relative, the patched count lands in the wrong segment,
			// silently corrupting every reconcile packet downstream. The stream
			// position after countPos is still valid but the count value is wrong.
			// Safe alternative if this breaks: two-pass (count changes first, then write).
			if (!forceWrite && prevCount == nextCount)
			{
				int countPos = writer.Position;
				writer.WriteInt32(0); // placeholder for -changedCount

				int changedCount = 0;
				for (int i = 0; i < nextCount; i++)
				{
					if (!prev[i].Equals(next[i]))
					{
						writer.WriteInt32(i);
						next[i].WriteTo(writer);
						changedCount++;
					}
				}

				if (changedCount == 0)
				{
					writer.Position = countPos; // rewind — nothing changed
					return false;
				}

				int endPos = writer.Position;
				writer.Position = countPos;
				writer.WriteInt32(-changedCount);
				writer.Position = endPos;
				return true;
			}

			writer.WriteInt32(nextCount);
			for (int i = 0; i < nextCount; i++)
			{
				next[i].WriteTo(writer);
			}
			return true;
		}

		/// <summary>
		/// Reads a cooldown array from the delta stream.
		/// Positive count = full array. Negative count = index-delta over prev.
		/// </summary>
		/// <remarks>
		/// <para>When count exceeds <see cref="MaxEntries"/>, remaining entry data is
		/// drained from the reader to keep the stream position valid for subsequent
		/// fields. The previous state is preserved.</para>
		/// <para><b>Allocation note:</b> Both full-array and index-delta paths allocate a
		/// fresh array per call (intentional — see issue #6: mutating in-place would corrupt
		/// the delta serializer’s <c>prev</c> reference). Full-array is rare (only when the
		/// cooldown count changes). Index-delta copies are small. If profiling shows GC pressure,
		/// consider <c>System.Buffers.ArrayPool&lt;CooldownReconcileEntry&gt;</c> with an explicit
		/// count sidecar, since <c>Rent</c> may return oversized arrays.</para>
		/// </remarks>
		public static CooldownReconcileEntry[] ReadArrayDelta(Reader reader, CooldownReconcileEntry[] prev)
		{
			int count = reader.ReadInt32();

			if (count < 0)
			{
				int changedCount = -count;
				if (changedCount > MaxEntries)
				{
					Log.Warning("CooldownReconcileEntry", $"Index-delta count {changedCount} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
					DrainIndexDeltaEntries(reader, changedCount);
					return prev;
				}

				int prevLength = prev?.Length ?? 0;
				CooldownReconcileEntry[] entries = new CooldownReconcileEntry[prevLength];
				if (prevLength > 0)
					Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadInt32();
					CooldownReconcileEntry entry = ReadFrom(reader);
					if (index >= 0 && index < prevLength)
					{
						entries[index] = entry;
					}
					else
					{
						Log.Warning("CooldownReconcileEntry", $"Index-delta entry index {index} out of bounds [0, {prevLength}). Entry discarded.");
					}
				}
				return entries;
			}

			if (count == 0)
				return null;

			if (count > MaxEntries)
			{
				Log.Warning("CooldownReconcileEntry", $"Full-array count {count} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
				DrainFullArrayEntries(reader, count);
				return prev;
			}

			CooldownReconcileEntry[] result = new CooldownReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = ReadFrom(reader);
			}
			return result;
		}

		/// <summary>
		/// Drains index-delta entries from the reader to keep the stream position valid.
		/// Each entry consists of an int32 index + the entry fields.
		/// </summary>
		private static void DrainIndexDeltaEntries(Reader reader, int changedCount)
		{
			for (int i = 0; i < changedCount; i++)
			{
				reader.ReadInt32(); // index
				ReadFrom(reader);   // entry fields
			}
		}

		/// <summary>
		/// Drains full-array entries from the reader to keep the stream position valid.
		/// </summary>
		private static void DrainFullArrayEntries(Reader reader, int count)
		{
			for (int i = 0; i < count; i++)
			{
				ReadFrom(reader);
			}
		}
	}
}