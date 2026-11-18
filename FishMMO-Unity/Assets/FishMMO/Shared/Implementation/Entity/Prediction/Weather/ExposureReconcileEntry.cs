using System;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// One exposure state's level, as it rides the unified reconcile.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The level is quantised to sixteen bits rather than sent as a float. Two reasons, and the
	/// second is the important one. It is smaller on the wire; and because
	/// <see cref="Quantise"/> is applied on BOTH sides — the server quantises what it sends, the
	/// owner quantises what it restores — the two hold bit-identical levels immediately after every
	/// reconcile, instead of differing by the rounding and re-diverging from there. A state at
	/// 1/65535 resolution is far finer than any buff threshold cares about.
	/// </para>
	/// <para>
	/// Producers MUST emit entries sorted by <see cref="TemplateID"/> ascending, like every other
	/// array in <see cref="CharacterReconcileData"/>: the index-delta serializer compares position
	/// by position, so an unstable order would send every entry every tick.
	/// </para>
	/// </remarks>
	public struct ExposureReconcileEntry : IEquatable<ExposureReconcileEntry>
	{
		/// <summary>The <see cref="FishMMO.Shared.Weather.WeatherExposureTemplate"/>'s cached ID.</summary>
		public int TemplateID;

		/// <summary>The level, 0..65535 for 0..1. See the remarks on quantisation.</summary>
		public ushort Level;

		/// <summary>0..1 to the wire's sixteen bits.</summary>
		public static ushort Quantise(float level)
		{
			if (level <= 0f) return 0;
			if (level >= 1f) return ushort.MaxValue;
			return (ushort)(level * ushort.MaxValue + 0.5f);
		}

		/// <summary>The wire's sixteen bits back to 0..1.</summary>
		public static float Dequantise(ushort level) => level / (float)ushort.MaxValue;

		public bool Equals(ExposureReconcileEntry other)
		{
			return TemplateID == other.TemplateID && Level == other.Level;
		}

		public override bool Equals(object obj) => obj is ExposureReconcileEntry other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				return (TemplateID * 397) ^ Level;
			}
		}

		public void WriteTo(Writer writer)
		{
			writer.WriteInt32Unpacked(TemplateID);
			writer.WriteUInt16(Level);
		}

		public static ExposureReconcileEntry ReadFrom(Reader reader)
		{
			return new ExposureReconcileEntry
			{
				TemplateID = reader.ReadInt32Unpacked(),
				Level = reader.ReadUInt16(),
			};
		}

		/// <summary>Guards against corrupted or malicious packets allocating unbounded arrays.</summary>
		private const int MaxEntries = 4096;

		/// <summary>
		/// Compares and writes exposure arrays using index-delta compression. The wire header is a
		/// packed 16-bit value: high bit = delta mode, low 15 bits = entry count.
		/// </summary>
		/// <remarks>
		/// <para><b>Null / empty equivalence:</b> both <c>null</c> and empty arrays serialize as
		/// count == 0, and <see cref="ReadArrayDelta"/> returns <c>null</c> for both.</para>
		/// <para><b>Stable ordering required:</b> producers MUST sort by <see cref="TemplateID"/>.</para>
		/// </remarks>
		public static bool WriteArrayDelta(
			Writer writer,
			ExposureReconcileEntry[] prev,
			ExposureReconcileEntry[] next,
			DeltaSerializerOption option)
		{
			if (prev == null && next == null)
				return false;

			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);

			if (!fullSerialize && ReferenceEquals(prev, next))
				return false;

			int prevCount = prev?.Length ?? 0;
			int nextCount = next?.Length ?? 0;
			if (nextCount > MaxEntries)
			{
				Log.Warning("ExposureReconcileEntry", $"WriteArrayDelta nextCount {nextCount} exceeds limit {MaxEntries}. Truncating to preserve stream integrity.");
				nextCount = MaxEntries;
			}

			// Index-delta path: same length, single pass.
			if (!fullSerialize && prevCount == nextCount)
			{
				int countPos = writer.Position;
				int startLength = writer.Length;
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
					/* Rewind Length as well as Position: Writer.Length only ever grows, and
					 * GetArraySegment sends 0..Length, so restoring Position alone would leave this
					 * placeholder's two bytes in the sent segment as trailing garbage. */
					writer.Position = countPos;
					writer.Length = startLength;
					return false;
				}

				/* Insert rather than seek-write-seek: InsertUInt16Unpacked is fixed at two bytes and
				 * cannot silently change width, whereas WriteUInt16 is only unpacked today because
				 * of a standing todo in FishNet's Writer. */
				writer.InsertUInt16Unpacked(BuildHeader(changedCount, true), countPos);
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
		/// Reads an exposure array from the delta stream. High header bit = index-delta over prev,
		/// otherwise a full array.
		/// </summary>
		public static ExposureReconcileEntry[] ReadArrayDelta(Reader reader, ExposureReconcileEntry[] prev)
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
					Log.Warning("ExposureReconcileEntry", $"Index-delta count {changedCount} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
					DrainIndexDeltaEntries(reader, changedCount);
					return prev;
				}

				int prevLength = prev?.Length ?? 0;
				ExposureReconcileEntry[] entries = new ExposureReconcileEntry[prevLength];
				if (prevLength > 0)
					Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadUInt16();
					ExposureReconcileEntry entry = ReadFrom(reader);
					if (index < prevLength)
					{
						entries[index] = entry;
					}
					else
					{
						Log.Warning("ExposureReconcileEntry", $"Index-delta entry index {index} out of bounds [0, {prevLength}). Entry discarded.");
					}
				}
				return entries;
			}

			if (count == 0)
				return null;

			if (count > MaxEntries)
			{
				Log.Warning("ExposureReconcileEntry", $"Full-array count {count} exceeds limit {MaxEntries}. Draining entries and preserving previous state.");
				DrainFullArrayEntries(reader, count);
				return prev;
			}

			ExposureReconcileEntry[] result = new ExposureReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = ReadFrom(reader);
			}
			return result;
		}

		/// <summary>Drains index-delta entries to advance the stream past a rejected overflow.</summary>
		private static void DrainIndexDeltaEntries(Reader reader, int changedCount)
		{
			for (int i = 0; i < changedCount; i++)
			{
				reader.ReadUInt16(); // index
				ReadFrom(reader);    // entry fields
			}
		}

		/// <summary>Drains full-array entries to advance the stream past a rejected overflow.</summary>
		private static void DrainFullArrayEntries(Reader reader, int count)
		{
			for (int i = 0; i < count; i++)
			{
				ReadFrom(reader);
			}
		}

		/// <summary>Packs a count and delta-mode flag: high bit (0x8000) delta, low 15 bits count.</summary>
		private static ushort BuildHeader(int count, bool isDelta)
		{
			return (ushort)((isDelta ? 0x8000 : 0) | (count & 0x7FFF));
		}
	}
}
