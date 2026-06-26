using System;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// A single non-resource character attribute entry for reconcile serialization.
	/// Mirrors <see cref="BuffReconcileEntry"/> / <see cref="CooldownReconcileEntry"/>
	/// index-delta compression so unchanged attributes contribute zero network bytes.
	/// <para>
	/// Resource attributes (HP/MP/Stamina) are NOT carried by this entry — they ride
	/// <see cref="CharacterAttributeResourceState"/> on the reconcile payload because they
	/// also need <c>CurrentValue</c> + <c>RegenTickAccum</c> state that base attributes do not have.
	/// </para>
	/// <para>
	/// Only <see cref="Value"/> (authoritative base) and <see cref="ExternalModifier"/>
	/// (sum of buff / equipment / region contributions) are reconciled.
	/// <c>FormulaModifier</c> is intentionally recomputed locally via the dependency graph
	/// (<c>CharacterAttribute.ApplyChildren</c>): replicating it would (a) cost bandwidth for a
	/// derived value and (b) potentially overwrite a more up-to-date local computation.
	/// </para>
	/// </summary>
	public struct AttributeReconcileEntry : IEquatable<AttributeReconcileEntry>
	{
		/// <summary>The attribute template ID.</summary>
		public int TemplateID;

		/// <summary>Authoritative base value (pre-modifier).</summary>
		public int Value;

		/// <summary>Authoritative external modifier (sum of buff / equip / region contributions).</summary>
		public int ExternalModifier;

		/// <inheritdoc/>
		public bool Equals(AttributeReconcileEntry other)
		{
			return TemplateID == other.TemplateID &&
				   Value == other.Value &&
				   ExternalModifier == other.ExternalModifier;
		}

		/// <inheritdoc/>
		public override bool Equals(object obj)
		{
			return obj is AttributeReconcileEntry other && Equals(other);
		}

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = TemplateID;
				hash = (hash * 397) ^ Value;
				hash = (hash * 397) ^ ExternalModifier;
				return hash;
			}
		}

		/// <summary>Writes a single entry's fields to the network writer.</summary>
		public void WriteTo(Writer writer)
		{
			writer.WriteInt32(TemplateID);
			writer.WriteInt32(Value);
			writer.WriteInt32(ExternalModifier);
		}

		/// <summary>Reads a single entry's fields from the network reader.</summary>
		public static AttributeReconcileEntry ReadFrom(Reader reader)
		{
			return new AttributeReconcileEntry
			{
				TemplateID = reader.ReadInt32(),
				Value = reader.ReadInt32(),
				ExternalModifier = reader.ReadInt32(),
			};
		}

		/// <summary>
		/// Maximum number of entries allowed in a single reconcile snapshot.
		/// Guards against corrupted or malicious packets allocating unbounded arrays.
		/// </summary>
		private const int MaxEntries = 4096;

		/// <summary>
		/// Compares and writes attribute arrays using index-delta compression.
		/// The wire header is a packed 16-bit value: high bit = delta mode, low 15 bits = entry count.
		/// </summary>
		/// <remarks>
		/// <para><b>Null / empty equivalence:</b> both <c>null</c> and empty arrays serialize as
		/// count == 0 on the wire, and <see cref="ReadArrayDelta"/> returns <c>null</c> for both.</para>
		/// <para><b>Stable ordering required:</b> producers MUST sort entries by <see cref="TemplateID"/>
		/// ascending so index-delta comparisons remain meaningful across ticks.</para>
		/// </remarks>
		public static bool WriteArrayDelta(
			Writer writer,
			AttributeReconcileEntry[] prev,
			AttributeReconcileEntry[] next,
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
				Log.Warning("AttributeReconcileEntry", $"WriteArrayDelta nextCount {nextCount} exceeds limit {MaxEntries}. Truncating to preserve stream integrity.");
				nextCount = MaxEntries;
			}

			// Index-delta path: same length, single pass.
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
		/// Reads an attribute array from the delta stream.
		/// High header bit = index-delta over prev, otherwise full array.
		/// </summary>
		public static AttributeReconcileEntry[] ReadArrayDelta(Reader reader, AttributeReconcileEntry[] prev)
		{
			ushort header = reader.ReadUInt16();
			bool isDelta = (header & 0x8000) != 0;
			int count = header & 0x7FFF;

			if (isDelta)
			{
				int changedCount = count;
				if (changedCount == 0)
					return prev;
				if (changedCount > MaxEntries)
				{
					Log.Warning("AttributeReconcileEntry", $"Index-delta count {changedCount} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
					DrainIndexDeltaEntries(reader, changedCount);
					return prev;
				}

				int prevLength = prev?.Length ?? 0;
				AttributeReconcileEntry[] entries = new AttributeReconcileEntry[prevLength];
				if (prevLength > 0)
					Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadUInt16();
					AttributeReconcileEntry entry = ReadFrom(reader);
					// index is read from a ushort (range 0–65535), so the >= 0 check is always true.
					if (index < prevLength)
					{
						entries[index] = entry;
					}
					else
					{
						Log.Warning("AttributeReconcileEntry", $"Index-delta entry index {index} out of bounds [0, {prevLength}). Entry discarded.");
					}
				}
				return entries;
			}

			if (count == 0)
				return null;

			if (count > MaxEntries)
			{
				Log.Warning("AttributeReconcileEntry", $"Full-array count {count} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
				DrainFullArrayEntries(reader, count);
				return prev;
			}

			AttributeReconcileEntry[] result = new AttributeReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = ReadFrom(reader);
			}
			return result;
		}

		private static void DrainIndexDeltaEntries(Reader reader, int changedCount)
		{
			for (int i = 0; i < changedCount; i++)
			{
				reader.ReadUInt16(); // index
				ReadFrom(reader);   // entry fields
			}
		}

		private static void DrainFullArrayEntries(Reader reader, int count)
		{
			for (int i = 0; i < count; i++)
			{
				ReadFrom(reader);
			}
		}

		private static ushort BuildHeader(int count, bool isDelta)
		{
			return (ushort)((isDelta ? 0x8000 : 0) | (count & 0x7FFF));
		}
	}
}