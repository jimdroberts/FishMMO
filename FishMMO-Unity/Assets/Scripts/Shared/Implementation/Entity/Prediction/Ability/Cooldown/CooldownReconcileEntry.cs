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

			if (!forceWrite && prevCount == nextCount)
			{
				int changedCount = 0;
				for (int i = 0; i < nextCount; i++)
				{
					if (!prev[i].Equals(next[i]))
						changedCount++;
				}

				if (changedCount == 0)
					return false;

				writer.WriteInt32(-changedCount);
				for (int i = 0; i < nextCount; i++)
				{
					if (!prev[i].Equals(next[i]))
					{
						writer.WriteInt32(i);
						next[i].WriteTo(writer);
					}
				}
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
		public static CooldownReconcileEntry[] ReadArrayDelta(Reader reader, CooldownReconcileEntry[] prev)
		{
			int count = reader.ReadInt32();

			if (count < 0)
			{
				int changedCount = -count;
				if (changedCount > MaxEntries)
				{
					Log.Warning("CooldownReconcileEntry", $"Index-delta count {changedCount} exceeds limit {MaxEntries}. Preserving previous state.");
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
				Log.Warning("CooldownReconcileEntry", $"Full-array count {count} exceeds limit {MaxEntries}. Preserving previous state.");
				return prev;
			}

			CooldownReconcileEntry[] result = new CooldownReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = ReadFrom(reader);
			}
			return result;
		}
	}
}
