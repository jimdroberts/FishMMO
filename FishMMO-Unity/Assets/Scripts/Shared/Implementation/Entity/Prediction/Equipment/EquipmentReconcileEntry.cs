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

		public bool Equals(EquipmentReconcileEntry other)
		{
			return TemplateID == other.TemplateID
				&& Slot == other.Slot
				&& Seed == other.Seed
				&& InstanceID == other.InstanceID;
		}

		public override bool Equals(object obj)
		{
			return obj is EquipmentReconcileEntry other && Equals(other);
		}

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

		public static void WriteTo(Writer writer, EquipmentReconcileEntry entry)
		{
			writer.WriteInt32(entry.TemplateID);
			writer.WriteUInt8Unpacked(entry.Slot);
			writer.WriteInt32(entry.Seed);
			writer.WriteInt64(entry.InstanceID);
		}

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

		private static ushort BuildHeader(int count, bool isDelta)
		{
			return (ushort)((count & COUNT_MASK) | (isDelta ? DELTA_FLAG : 0u));
		}

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

		private static void DrainIndexDeltaEntries(Reader reader, int changedCount)
		{
			for (int i = 0; i < changedCount; i++)
			{
				reader.ReadUInt16();
				ReadFrom(reader);
			}
		}

		private static void DrainFullArrayEntries(Reader reader, int count)
		{
			for (int i = 0; i < count; i++)
				ReadFrom(reader);
		}
	}
}