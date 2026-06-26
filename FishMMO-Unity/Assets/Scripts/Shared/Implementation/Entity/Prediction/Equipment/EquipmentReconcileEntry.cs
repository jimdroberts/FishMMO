using System;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lightweight reconcile entry for a single equipped item slot.
	/// Only filled slots are serialized — empty slots are omitted.
	/// </summary>
	public struct EquipmentReconcileEntry : IEquatable<EquipmentReconcileEntry>
	{
		/// <summary>Maximum number of equipment entries per reconcile payload.</summary>
		public const int MaxEntries = 64;

		/// <summary>Item template ID (0 = empty slot, omitted from reconcile).</summary>
		public int TemplateID;
		/// <summary>Equipment slot index (byte cast of ItemSlot).</summary>
		public byte Slot;
		/// <summary>Item generation seed (0 if not generated).</summary>
		public int Seed;
		/// <summary>Item instance ID.</summary>
		public long InstanceID;

		/// <summary>Returns true if this entry matches the other entry on all fields.</summary>
		/// <param name="other">The other entry to compare.</param>
		/// <returns>True if all fields match.</returns>
		public bool Equals(EquipmentReconcileEntry other)
		{
			return TemplateID == other.TemplateID
				&& Slot == other.Slot
				&& Seed == other.Seed
				&& InstanceID == other.InstanceID;
		}

		/// <summary>Returns true if obj is an EquipmentReconcileEntry with matching fields.</summary>
		/// <param name="obj">The object to compare.</param>
		/// <returns>True if obj is an identical EquipmentReconcileEntry.</returns>
		public override bool Equals(object obj)
		{
			return obj is EquipmentReconcileEntry other && Equals(other);
		}

		/// <summary>Returns a hash code combining all fields for use in dictionaries and sets.</summary>
		/// <returns>Hash code for this entry.</returns>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + TemplateID;
				hash = hash * 31 + Slot;
				hash = hash * 31 + Seed;
				hash = hash * 31 + InstanceID.GetHashCode();
				return hash;
			}
		}

		/// <summary>Writes a single entry to a FishNet Writer.</summary>
		/// <param name="writer">The Writer to write to.</param>
		/// <param name="entry">The entry to serialize.</param>
		public static void WriteTo(Writer writer, EquipmentReconcileEntry entry)
		{
			writer.WriteInt32(entry.TemplateID);
			writer.WriteUInt8Unpacked(entry.Slot);
			writer.WriteInt32(entry.Seed);
			writer.WriteInt64(entry.InstanceID);
		}

		/// <summary>Reads a single entry from a FishNet Reader.</summary>
		/// <param name="reader">The Reader to read from.</param>
		/// <returns>The deserialized EquipmentReconcileEntry.</returns>
		public static EquipmentReconcileEntry ReadFrom(Reader reader)
		{
			return new EquipmentReconcileEntry
			{
				TemplateID = reader.ReadInt32(),
				Slot = reader.ReadUInt8Unpacked(),
				Seed = reader.ReadInt32(),
				InstanceID = reader.ReadInt64(),
			};
		}

		// ── Delta array serialization ────────────────────────────────────

		/// <summary>High bit of header = index-delta mode.</summary>
		private const ushort DELTA_FLAG = 0x8000;
		/// <summary>Remaining bits = count or changedCount.</summary>
		private const ushort COUNT_MASK = 0x7FFF;

		/// <summary>
		/// Builds the header ushort from a count and a delta flag.
		/// The high bit indicates index-delta mode; remaining bits store the count.
		/// </summary>
		/// <param name="count">Number of entries or changed entries.</param>
		/// <param name="isDelta">True if using index-delta encoding.</param>
		/// <returns>Packed header ushort.</returns>
		private static ushort BuildHeader(int count, bool isDelta)
		{
			return (ushort)((count & COUNT_MASK) | (isDelta ? DELTA_FLAG : (ushort)0));
		}

		/// <summary>
		/// Delta-encodes an array of reconcile entries by comparing against a previous snapshot.
		/// Writes a full array when lengths differ or force-write is requested; writes index-delta
		/// when lengths match. Returns true if any data was written.
		/// </summary>
		/// <param name="writer">Writer to serialize to.</param>
		/// <param name="prev">Previous reconcile snapshot, or null.</param>
		/// <param name="next">Current reconcile snapshot, or null.</param>
		/// <param name="option">Delta serializer option (Unset = auto, otherwise force).</param>
		/// <returns>True if data was written; false if unchanged.</returns>
		public static bool WriteArrayDelta(
			Writer writer,
			EquipmentReconcileEntry[] prev,
			EquipmentReconcileEntry[] next,
			DeltaSerializerOption option)
		{
			if (prev == null && next == null)
				return false;

			bool forceWrite = option != DeltaSerializerOption.Unset;

			if (!forceWrite && ReferenceEquals(prev, next))
				return false;

			int prevCount = prev?.Length ?? 0;
			int nextCount = next?.Length ?? 0;

			if (nextCount > MaxEntries)
			{
				Log.Warning("EquipmentReconcileEntry", $"WriteArrayDelta nextCount {nextCount} exceeds limit {MaxEntries}. Truncating to preserve stream integrity.");
				nextCount = MaxEntries;
			}

			// Full-array path: different length, force-write, or no previous data
			if (forceWrite || prevCount != nextCount || prev == null || prevCount == 0)
			{
				if (nextCount == 0)
				{
					writer.WriteUInt16(0);
					return forceWrite;
				}
				writer.WriteUInt16(BuildHeader(nextCount, false));
				for (int i = 0; i < nextCount; i++)
					WriteTo(writer, next[i]);
				return true;
			}

			// Index-delta path: same length, single pass
			int changedCount = 0;
			for (int i = 0; i < nextCount; i++)
			{
				if (!next[i].Equals(prev[i]))
					changedCount++;
			}

			if (changedCount == 0)
			{
				writer.WriteUInt16(0);
				return false;
			}

			writer.WriteUInt16(BuildHeader(changedCount, true));
			for (int i = 0; i < nextCount; i++)
			{
				if (!next[i].Equals(prev[i]))
				{
					writer.WriteUInt16((ushort)i);
					WriteTo(writer, next[i]);
				}
			}
			return true;
		}

		/// <summary>
		/// Reads a delta-encoded array of reconcile entries previously written by <see cref="WriteArrayDelta"/>.
		/// Handles both full-array and index-delta formats based on the header flag.
		/// </summary>
		/// <param name="reader">Reader to deserialize from.</param>
		/// <param name="prev">Previous reconcile snapshot to base deltas on, or null.</param>
		/// <returns>Reconstructed reconcile array, or null if empty.</returns>
		public static EquipmentReconcileEntry[] ReadArrayDelta(Reader reader, EquipmentReconcileEntry[] prev)
		{
			ushort header = reader.ReadUInt16();
			bool isDelta = (header & DELTA_FLAG) != 0;
			int count = header & COUNT_MASK;

			if (isDelta)
			{
				int changedCount = count;
				if (changedCount == 0)
					return prev;
				if (changedCount > MaxEntries)
				{
					DrainIndexDeltaEntries(reader, changedCount);
					return prev;
				}

				int prevLength = prev?.Length ?? 0;
				EquipmentReconcileEntry[] entries = new EquipmentReconcileEntry[prevLength];
				if (prevLength > 0)
					Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadUInt16();
					EquipmentReconcileEntry entry = ReadFrom(reader);
					if (index < prevLength)
						entries[index] = entry;
				}
				return entries;
			}

			if (count == 0)
				return null;

			if (count > MaxEntries)
			{
				DrainFullArrayEntries(reader, count);
				return prev;
			}

			EquipmentReconcileEntry[] result = new EquipmentReconcileEntry[count];
			for (int i = 0; i < count; i++)
				result[i] = ReadFrom(reader);
			return result;
		}

		/// <summary>
		/// Drains (reads and discards) index-delta entries from the reader when the count exceeds <see cref="MaxEntries"/>.
		/// Prevents stream desynchronization while rejecting the payload.
		/// </summary>
		/// <param name="reader">Reader to drain from.</param>
		/// <param name="changedCount">Number of changed entries to skip.</param>
		private static void DrainIndexDeltaEntries(Reader reader, int changedCount)
		{
			for (int i = 0; i < changedCount; i++)
			{
				reader.ReadUInt16();
				ReadFrom(reader);
			}
		}

		/// <summary>
		/// Drains (reads and discards) full-array entries from the reader when the count exceeds <see cref="MaxEntries"/>.
		/// Prevents stream desynchronization while rejecting the payload.
		/// </summary>
		/// <param name="reader">Reader to drain from.</param>
		/// <param name="count">Number of entries to skip.</param>
		private static void DrainFullArrayEntries(Reader reader, int count)
		{
			for (int i = 0; i < count; i++)
				ReadFrom(reader);
		}
	}
}