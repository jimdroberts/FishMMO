using System;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// A single buff entry for reconcile serialization.
	/// Uses tick-based timing fields (<see cref="ExpiryTick"/>, <see cref="NextTickTick"/>)
	/// mirroring <see cref="CooldownReconcileEntry"/>'s immutable tick design.
	/// Tick values are absolute network ticks, so they are stable between structural changes
	/// (add/remove/stack), allowing the delta serializer's <c>ReferenceEquals</c> fast-path
	/// to suppress transmission on unchanged ticks with zero network overhead.
	/// Implements <see cref="IEquatable{T}"/> for efficient delta comparison
	/// in <see cref="CharacterReconcileDataDeltaSerializer"/>.
	/// </summary>
	public struct BuffReconcileEntry : IEquatable<BuffReconcileEntry>
	{
		/// <summary>The buff template ID.</summary>
		public int TemplateID;

		/// <summary>Absolute network tick at which this buff expires.</summary>
		public uint ExpiryTick;

		/// <summary>Absolute network tick at which the next OnTick fires.</summary>
		public uint NextTickTick;

		/// <summary>Current stack count for this buff.</summary>
		public int Stacks;

		/// <summary>Number of ticks remaining for periodic effects.</summary>
		public int TickCount;

		/// <summary>
		/// Compares all fields for equality. Used by the delta serializer
		/// to determine which entries changed between ticks.
		/// </summary>
		public bool Equals(BuffReconcileEntry other)
		{
			return TemplateID == other.TemplateID &&
				   ExpiryTick == other.ExpiryTick &&
				   NextTickTick == other.NextTickTick &&
				   Stacks == other.Stacks &&
				   TickCount == other.TickCount;
		}

		/// <inheritdoc/>
		public override bool Equals(object obj)
		{
			return obj is BuffReconcileEntry other && Equals(other);
		}

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = TemplateID;
				hash = (hash * 397) ^ ExpiryTick.GetHashCode();
				hash = (hash * 397) ^ NextTickTick.GetHashCode();
				hash = (hash * 397) ^ Stacks;
				hash = (hash * 397) ^ TickCount;
				return hash;
			}
		}

		/// <summary>
		/// Writes a single entry's fields to the network writer.
		/// </summary>
		public void WriteTo(Writer writer)
		{
			writer.WriteInt32(TemplateID);
			writer.WriteUInt32(ExpiryTick);
			writer.WriteUInt32(NextTickTick);
			writer.WriteInt32(Stacks);
			writer.WriteInt32(TickCount);
		}

		/// <summary>
		/// Reads a single entry's fields from the network reader.
		/// </summary>
		public static BuffReconcileEntry ReadFrom(Reader reader)
		{
			return new BuffReconcileEntry
			{
				TemplateID = reader.ReadInt32(),
				ExpiryTick = reader.ReadUInt32(),
				NextTickTick = reader.ReadUInt32(),
				Stacks = reader.ReadInt32(),
				TickCount = reader.ReadInt32(),
			};
		}

		/// <summary>
		/// Maximum number of entries allowed in a single reconcile snapshot.
		/// Guards against corrupted or malicious packets allocating unbounded arrays.
		/// </summary>
		private const int MaxEntries = 4096;

		/// <summary>
		/// Compares and writes buff arrays using index-delta compression.
		/// When the array length is unchanged, only changed entries are written
		/// (negative count signals index-delta mode). When the length differs or
		/// on a forced tick, the full array is written (positive count).
		/// </summary>
		public static bool WriteArrayDelta(
			Writer writer,
			BuffReconcileEntry[] prev,
			BuffReconcileEntry[] next,
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
		/// Reads a buff array from the delta stream.
		/// Positive count = full array. Negative count = index-delta over prev.
		/// </summary>
		public static BuffReconcileEntry[] ReadArrayDelta(Reader reader, BuffReconcileEntry[] prev)
		{
			int count = reader.ReadInt32();

			if (count < 0)
			{
				int changedCount = -count;
				if (changedCount > MaxEntries)
				{
					Log.Warning("BuffReconcileEntry", $"Index-delta count {changedCount} exceeds limit {MaxEntries}. Preserving previous state.");
					return prev;
				}

				int prevLength = prev?.Length ?? 0;
				BuffReconcileEntry[] entries = new BuffReconcileEntry[prevLength];
				if (prevLength > 0)
					Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadInt32();
					BuffReconcileEntry entry = ReadFrom(reader);
					if (index >= 0 && index < prevLength)
					{
						entries[index] = entry;
					}
					else
					{
						Log.Warning("BuffReconcileEntry", $"Index-delta entry index {index} out of bounds [0, {prevLength}). Entry discarded.");
					}
				}
				return entries;
			}

			if (count == 0)
				return null;

			if (count > MaxEntries)
			{
				Log.Warning("BuffReconcileEntry", $"Full-array count {count} exceeds limit {MaxEntries}. Preserving previous state.");
				return prev;
			}

			BuffReconcileEntry[] result = new BuffReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = ReadFrom(reader);
			}
			return result;
		}
	}
}
