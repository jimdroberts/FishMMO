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
		/// Running sum of <c>(1 + Stacks)</c> for each tick that has fired on this buff.
		/// See <see cref="Buff.CumulativeTickMultiplier"/> for the rationale.
		/// Carried in the reconcile entry so rollback replay and post-reconcile
		/// buff removal both reverse exactly the cumulative modifier applied.
		/// </summary>
		public int CumulativeTickMultiplier;

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
				   TickCount == other.TickCount &&
				   CumulativeTickMultiplier == other.CumulativeTickMultiplier;
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
				hash = (hash * 397) ^ CumulativeTickMultiplier;
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
			writer.WriteInt32(CumulativeTickMultiplier);
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
				CumulativeTickMultiplier = reader.ReadInt32(),
			};
		}

		/// <summary>
		/// Maximum number of entries allowed in a single reconcile snapshot.
		/// Guards against corrupted or malicious packets allocating unbounded arrays.
		/// </summary>
		private const int MaxEntries = 4096;

		/// <summary>
		/// Compares and writes buff arrays using index-delta compression.
		/// The wire header is a packed 16-bit value: high bit = delta mode,
		/// low 15 bits = entry count. <see cref="MaxEntries"/> is 4096, so this
		/// saves 2 bytes per array header and 2 bytes per changed index.
		/// </summary>
		/// <remarks>
		/// <para><b>Null / empty equivalence:</b> both <c>null</c> and empty arrays
		/// serialize as count == 0 on the wire, and <see cref="ReadArrayDelta"/> returns
		/// <c>null</c> for both. Callers must treat <c>null</c> and empty as identical.</para>
		/// </remarks>
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
			if (nextCount > MaxEntries)
			{
				Log.Warning("BuffReconcileEntry", $"WriteArrayDelta nextCount {nextCount} exceeds limit {MaxEntries}. Truncating to preserve stream integrity.");
				nextCount = MaxEntries;
			}

			// Index-delta path: same length, single pass — reserve changedCount,
			// write entries, then patch the header. Avoids double-iterating the array.
			if (!forceWrite && prevCount == nextCount)
			{
				int countPos = writer.Position;
				writer.WriteUInt16(0); // placeholder for packed header

				int changedCount = 0;
				for (int i = 0; i < nextCount; i++)
				{
					if (!prev[i].Equals(next[i]))
					{
						writer.WriteUInt16((ushort)i);
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
				writer.WriteUInt16(BuildHeader(changedCount, true));
				writer.Position = endPos;
				return true;
			}

			writer.WriteUInt16(BuildHeader(nextCount, false));
			for (int i = 0; i < nextCount; i++)
			{
				next[i].WriteTo(writer);
			}
			return true;
		}

		/// <summary>
		/// Reads a buff array from the delta stream.
		/// High header bit = index-delta over prev, otherwise full array.
		/// </summary>
		/// <remarks>
		/// <para>When count exceeds <see cref="MaxEntries"/>, remaining entry data is
		/// drained from the reader to keep the stream position valid for subsequent
		/// fields. The previous state is preserved.</para>
		/// <para><b>Allocation note:</b> Both full-array and index-delta paths allocate a
		/// fresh array per call (intentional). Full-array is rare (only when the
		/// buff count changes). Index-delta copies are small. If profiling shows GC pressure,
		/// consider <c>System.Buffers.ArrayPool&lt;BuffReconcileEntry&gt;</c> with an explicit
		/// count sidecar, since <c>Rent</c> may return oversized arrays.</para>
		/// </remarks>
		public static BuffReconcileEntry[] ReadArrayDelta(Reader reader, BuffReconcileEntry[] prev)
		{
			ushort header = reader.ReadUInt16();
			bool isDelta = (header & 0x8000) != 0;
			int count = header & 0x7FFF;

			if (isDelta)
			{
				int changedCount = count;
				// Edge case: zero changed entries in a delta. Should not occur during normal
				// operation (WriteArrayDelta returns early on changedCount==0), but guard
				// against a corrupted or unexpected header to avoid unnecessary allocation.
				if (changedCount == 0)
					return prev;
				if (changedCount > MaxEntries)
				{
					Log.Warning("BuffReconcileEntry", $"Index-delta count {changedCount} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
					DrainIndexDeltaEntries(reader, changedCount);
					return prev;
				}

				int prevLength = prev?.Length ?? 0;
				BuffReconcileEntry[] entries = new BuffReconcileEntry[prevLength];
				if (prevLength > 0)
					Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadUInt16();
					BuffReconcileEntry entry = ReadFrom(reader);
					// index is cast from ushort (range 0–65535), so >= 0 is always true.
					if (index < prevLength)
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
				Log.Warning("BuffReconcileEntry", $"Full-array count {count} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
				DrainFullArrayEntries(reader, count);
				return prev;
			}

			BuffReconcileEntry[] result = new BuffReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = ReadFrom(reader);
			}
			return result;
		}

		/// <summary>
		/// Drains index-delta entries from the reader to keep the stream position valid.
		/// Each entry consists of a uint16 index + the entry fields.
		/// </summary>
		private static void DrainIndexDeltaEntries(Reader reader, int changedCount)
		{
			for (int i = 0; i < changedCount; i++)
			{
				reader.ReadUInt16(); // index
				ReadFrom(reader);   // entry fields
			}
		}

		/// <summary>
		/// Packs the delta/full mode flag and count into a 16-bit header.
		/// </summary>
		private static ushort BuildHeader(int count, bool isDelta)
		{
			return (ushort)((isDelta ? 0x8000 : 0) | (count & 0x7FFF));
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