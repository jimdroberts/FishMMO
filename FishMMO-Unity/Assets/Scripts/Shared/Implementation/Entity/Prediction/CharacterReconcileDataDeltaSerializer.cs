using System;
using UnityEngine;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom delta serializers for <see cref="CharacterReconcileData"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 2-byte bitmask (13 bits for 13 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// The nested <see cref="KinematicCharacterController.KinematicCharacterMotorState"/> and
	/// <see cref="CharacterAttributeResourceState"/> use their own delta serializers,
	/// so savings compound. Cooldowns, buffs, and non-resource attributes use index-delta
	/// compression via <see cref="CooldownReconcileEntry"/>, <see cref="BuffReconcileEntry"/>,
	/// and <see cref="AttributeReconcileEntry"/>.
	/// </para>
	/// <para>
	/// <b>Who owns what (FishNet delta prediction, <c>FISHNET_DELTA_PREDICTION</c>).</b> FishNet
	/// writes every reconcile behind a one-byte header (a full flag plus a 7-bit id of the full
	/// reconcile a delta was written against). A FULL reconcile — the first one, one per observer
	/// added, and one every <c>min(TickRate, 60)</c> ticks — goes through the REGULAR serializer,
	/// <see cref="WriteCharacterReconcileData"/>. Every other reconcile goes through
	/// <see cref="WriteDelta"/> with <c>RootSerialize</c>, as a delta against the last FULL
	/// reconcile as readers decoded it, not against the previous reconcile. The reader only uses a
	/// delta when it holds the full reconcile that delta names, and otherwise decodes it to stay
	/// aligned and discards it. Loss detection and late-joiner bootstrap are therefore FishNet's,
	/// which is why this serializer no longer carries the mode byte and chain sequence it needed
	/// when the fork chained each delta onto the previous one.
	/// </para>
	/// <para>
	/// The contract this serializer keeps for FishNet: write bytes if and only if it returns true;
	/// always write something at <c>RootSerialize</c>; consume the same number of bytes whatever
	/// baseline the reader holds (a rejected delta is still decoded); never modify the baseline
	/// instance, which is reused for every delta until the next full reconcile — the array readers
	/// copy before they patch.
	/// </para>
	/// </summary>
	public static class CharacterReconcileDataDeltaSerializer
	{
		/// <summary>
		/// Bit flag for the motor state field in the delta bitmask.
		/// </summary>
		private const ushort MOTOR_STATE_BIT = 1 << 0;
		/// <summary>
		/// Bit flag for the ability ID field in the delta bitmask.
		/// </summary>
		private const ushort ABILITY_ID_BIT = 1 << 1;
		/// <summary>
		/// Bit flag for the remaining ticks field in the delta bitmask.
		/// </summary>
		private const ushort REMAINING_TICKS_BIT = 1 << 2;
		/// <summary>
		/// Bit flag for the seed field in the delta bitmask.
		/// </summary>
		private const ushort SEED_BIT = 1 << 3;
		/// <summary>
		/// Bit flag for the resource state field in the delta bitmask.
		/// </summary>
		private const ushort RESOURCE_BIT = 1 << 4;
		/// <summary>
		/// Bit flag for the packed flags and slot field in the delta bitmask.
		/// </summary>
		private const ushort PACKED_FLAGS_BIT = 1 << 5;
		/// <summary>
		/// Bit flag for the cooldown array field in the delta bitmask.
		/// </summary>
		private const ushort COOLDOWN_BIT = 1 << 6;
		/// <summary>
		/// Bit flag for the buff array field in the delta bitmask.
		/// </summary>
		private const ushort BUFF_BIT = 1 << 7;
		/// <summary>
		/// Bit flag for the xoshiro128** RNG state fields in the delta bitmask.
		/// </summary>
		private const ushort RNG_STATE_BIT = 1 << 8;
		/// <summary>
		/// Bit flag for the attribute array field in the delta bitmask.
		/// </summary>
		private const ushort ATTRIBUTE_BIT = 1 << 9;
		/// <summary>
		/// Bit flag for the equipment array field in the delta bitmask.
		/// </summary>
		private const ushort EQUIPMENT_BIT = 1 << 10;
		/// <summary>
		/// Bit flag for the charged-hold tick counter in the delta bitmask.
		/// </summary>
		private const ushort CHARGED_HOLD_BIT = 1 << 11;

		/// <summary>Set when the weather exposure array changed.</summary>
		private const ushort EXPOSURE_BIT = 1 << 12;
		// Bits 13..15 are reserved for future fields. The flag mask is a ushort (16 bits);
		// 13 are currently in use. When adding new fields, take the next bit and update
		// WriteDelta and ReadDelta in lock-step — the two handle the same fields in the same
		// order, and a field added to one of them only silently misaligns every field after it.

		/// <summary>
		/// Registers the custom delta serializers at runtime via <see cref="GenericDeltaWriter{T}"/> and <see cref="GenericDeltaReader{T}"/>.
		/// Full array write cap — 4096 entries covers any realistic buff/debuff/cooldown set
		/// and provides a backstop against accidental runaway allocation.
		/// </summary>
		private const ushort MaxArrayEntries = 4096;

		/// <summary>
		/// Width of the length prefix <see cref="WriteCharacterReconcileData"/> puts in front of the
		/// absolute snapshot. Fixed, so <c>InsertUInt32Unpacked</c> can backfill it.
		/// </summary>
		private const int RECONCILE_SNAPSHOT_LENGTH_BYTES = 4;

		/// <summary>
		/// Custom full serializer: writes all fields of <see cref="CharacterReconcileData"/>.
		/// Nested types use their full serializers. Arrays write count + entries.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Framed by a byte count</b>, the same shape the four <c>WritePayload</c> implementations
		/// use and for the same reason. FishNet packs every predicted behaviour's reconcile into one
		/// <c>StateUpdate</c> reader, so a reader that stops early does not merely lose its own state
		/// — every behaviour after it decodes from the wrong offset.
		/// </para>
		/// <para>
		/// <see cref="ReadCharacterReconcileData"/> has four defensive aborts for array counts that
		/// cannot be trusted, and no way to drain past them: the per-entry sizes it would need to skip
		/// are derived from the count it just rejected. The length recorded here is what lets it
		/// resynchronise instead. Four bytes, and only on the absolute form — which FishNet emits once
		/// per second per owner, not on the per-tick deltas.
		/// </para>
		/// </remarks>
		public static void WriteCharacterReconcileData(this Writer writer, CharacterReconcileData value)
		{
			writer.Skip(RECONCILE_SNAPSHOT_LENGTH_BYTES);
			int snapshotStart = writer.Position;

			KinematicCharacterMotorStateDeltaSerializer.WriteKinematicCharacterMotorState(writer, value.MotorState);
			writer.WriteInt64(value.AbilityID);
			writer.WriteUInt32(value.RemainingTicks);
			/* UNPACKED: the ability RNG seed is full entropy, so the zig-zag varint form spends five
			 * bytes on fifteen of every sixteen values where fixed-width spends four. The tick above
			 * it is a small absolute number and stays packed. */
			writer.WriteInt32Unpacked(value.Seed);
			CharacterAttributeResourceStateSerializer.WriteCharacterAttributeResourceState(writer, value.ResourceState);
			writer.WriteInt32(value.PackedFlagsAndSlot);

			// Cooldowns
			if (value.Cooldowns == null || value.Cooldowns.Length == 0)
			{
				writer.WriteUInt16(0);
			}
			else
			{
				ushort count = (ushort)Math.Min(value.Cooldowns.Length, MaxArrayEntries);
				writer.WriteUInt16(count);
				/* Layout owned by CooldownReconcileEntry.WriteTo. The absolute form and the
				 * index-delta form must read from ONE field list: a hand-copy here drifted from
				 * AttributeReconcileEntry's the moment that struct changed a field's width. */
				for (int i = 0; i < count; i++)
				{
					value.Cooldowns[i].WriteTo(writer);
				}
			}

			// Buffs
			if (value.Buffs == null || value.Buffs.Length == 0)
			{
				writer.WriteUInt16(0);
			}
			else
			{
				ushort count = (ushort)Math.Min(value.Buffs.Length, MaxArrayEntries);
				writer.WriteUInt16(count);
				// Layout owned by BuffReconcileEntry.WriteTo — see the cooldown loop above.
				for (int i = 0; i < count; i++)
				{
					value.Buffs[i].WriteTo(writer);
				}
			}

			// Equipment
			if (value.Equipment == null || value.Equipment.Length == 0)
			{
				writer.WriteUInt16(0);
			}
			else
			{
				ushort count = (ushort)Math.Min(value.Equipment.Length, EquipmentReconcileEntry.MaxEntries);
				writer.WriteUInt16(count);
				// Layout owned by EquipmentReconcileEntry.WriteTo — see the cooldown loop above.
				for (int i = 0; i < count; i++)
				{
					EquipmentReconcileEntry.WriteTo(writer, value.Equipment[i]);
				}
			}

			// Attributes
			if (value.Attributes == null || value.Attributes.Length == 0)
			{
				writer.WriteUInt16(0);
			}
			else
			{
				ushort count = (ushort)Math.Min(value.Attributes.Length, MaxArrayEntries);
				writer.WriteUInt16(count);
				// Layout owned by AttributeReconcileEntry.WriteTo — see the cooldown loop above.
				for (int i = 0; i < count; i++)
				{
					value.Attributes[i].WriteTo(writer);
				}
			}

			/* RNG state — UNPACKED. The four xoshiro128** words are high-entropy by construction, so
			 * FishNet's varint form needs five bytes for any word with its top four bits set: fifteen
			 * of every sixteen. Fixed-width is four apiece and never worse. Same reason the delta path
			 * does not difference-encode them (see WriteRngStateDelta). */
			writer.WriteUInt32Unpacked(value.RngS0);
			writer.WriteUInt32Unpacked(value.RngS1);
			writer.WriteUInt32Unpacked(value.RngS2);
			writer.WriteUInt32Unpacked(value.RngS3);

			// Charged hold counter. Appended after the fields that predate it so the frame's
			// existing layout is untouched; the length prefix is what makes appending safe.
			writer.WriteUInt32(value.ChargedHoldTicks);

			/* Weather exposure levels, appended for the same reason. ALWAYS a header, even with no
			 * exposure states: WriteArrayDelta declines when both arrays are null (correct for the
			 * delta form, where EXPOSURE_BIT records the absence), but ReadCharacterReconcileData
			 * always reads one. A character without exposure states used to write nothing here, so
			 * every absolute snapshot read two bytes past its own frame and logged a misread. An
			 * empty array takes the full-array branch and writes a zero count. */
			ExposureReconcileEntry.WriteArrayDelta(writer, null,
				value.Exposure ?? Array.Empty<ExposureReconcileEntry>(), DeltaSerializerOption.FullSerialize);

			writer.InsertUInt32Unpacked((uint)(writer.Position - snapshotStart),
				snapshotStart - RECONCILE_SNAPSHOT_LENGTH_BYTES);
		}

		/// <summary>
		/// Validates an array count read from the wire against the cap its writer enforces.
		/// </summary>
		/// <remarks>
		/// Every array in this payload is written as <c>Math.Min(length, cap)</c>, so a count above
		/// the cap cannot have been produced by <see cref="WriteCharacterReconcileData"/> and means
		/// the stream is already misaligned. The count field is a <c>ushort</c>, so an unchecked
		/// count would allocate and then attempt to read up to 65535 entries — for buffs that is
		/// 65535 × 6 reads walking off the end of the buffer. Reporting once and abandoning the
		/// rest of the read is the containable failure; the alternative is an out-of-range throw
		/// deep inside a reconcile.
		/// </remarks>
		/// <param name="count">Count read from the wire.</param>
		/// <param name="cap">Maximum the writer can emit.</param>
		/// <param name="field">Field name, for the log line.</param>
		/// <returns>True when the count is usable.</returns>
		private static bool IsValidArrayCount(int count, int cap, string field)
		{
			if (count <= cap)
			{
				return true;
			}

			Log.Error("CharacterReconcileDataDeltaSerializer",
				$"ReadCharacterReconcileData: {field} count {count} exceeds the writer's cap of {cap}. " +
				"The reconcile stream is corrupt; seeking to the end of this snapshot's frame.");
			return false;
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="CharacterReconcileData"/>.
		/// Must read in the same order as <see cref="WriteCharacterReconcileData"/>.
		/// </summary>
		public static CharacterReconcileData ReadCharacterReconcileData(this Reader reader)
		{
			/* Where this snapshot ends, whatever happens below. Every abort seeks here before
			 * returning, so the shared StateUpdate reader is left where the NEXT predicted behaviour
			 * expects it — see WritePayload-style framing in the four spawn payloads, and
			 * WriteCharacterReconcileData for why the aborts cannot simply drain.
			 *
			 * The length is validated against what the reader actually holds before it is trusted.
			 * This frame exists to survive a stream that cannot be trusted, which makes its own
			 * length the one value that has to be checked rather than believed: Reader.Position is a
			 * plain field with no bounds check, so a length that overflows int or overruns the buffer
			 * would turn a recoverable abort into an out-of-range read for whoever reads next. */
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("CharacterReconcileDataDeltaSerializer",
					$"ReadCharacterReconcileData: framed length {declaredLength} exceeds the {remainingBytes} bytes " +
					"remaining in the state reader. The stream cannot be resynchronised; discarding the remainder.");
				reader.Position += remainingBytes;
				return default;
			}
			int snapshotEnd = reader.Position + (int)declaredLength;

			var result = new CharacterReconcileData
			{
				MotorState = KinematicCharacterMotorStateDeltaSerializer.ReadKinematicCharacterMotorState(reader),
				AbilityID = reader.ReadInt64(),
				RemainingTicks = reader.ReadUInt32(),
				Seed = reader.ReadInt32Unpacked(),
				ResourceState = CharacterAttributeResourceStateSerializer.ReadCharacterAttributeResourceState(reader),
				PackedFlagsAndSlot = reader.ReadInt32(),
			};

			// Cooldowns
			ushort cdCount = reader.ReadUInt16();
			if (!IsValidArrayCount(cdCount, MaxArrayEntries, "cooldown"))
			{
				reader.Position = snapshotEnd;
				return result;
			}
			if (cdCount > 0)
			{
				result.Cooldowns = new CooldownReconcileEntry[cdCount];
				// Layout owned by CooldownReconcileEntry.ReadFrom — the mirror of the write loop.
				for (int i = 0; i < cdCount; i++)
				{
					result.Cooldowns[i] = CooldownReconcileEntry.ReadFrom(reader);
				}
			}

			// Buffs
			ushort buffCount = reader.ReadUInt16();
			if (!IsValidArrayCount(buffCount, MaxArrayEntries, "buff"))
			{
				reader.Position = snapshotEnd;
				return result;
			}
			if (buffCount > 0)
			{
				result.Buffs = new BuffReconcileEntry[buffCount];
				// Layout owned by BuffReconcileEntry.ReadFrom — the mirror of the write loop.
				for (int i = 0; i < buffCount; i++)
				{
					result.Buffs[i] = BuffReconcileEntry.ReadFrom(reader);
				}
			}

			// Equipment
			ushort equipCount = reader.ReadUInt16();
			if (!IsValidArrayCount(equipCount, EquipmentReconcileEntry.MaxEntries, "equipment"))
			{
				reader.Position = snapshotEnd;
				return result;
			}
			if (equipCount > 0)
			{
				result.Equipment = new EquipmentReconcileEntry[equipCount];
				// Layout owned by EquipmentReconcileEntry.ReadFrom — the mirror of the write loop.
				for (int i = 0; i < equipCount; i++)
				{
					result.Equipment[i] = EquipmentReconcileEntry.ReadFrom(reader);
				}
			}

			// Attributes
			ushort attrCount = reader.ReadUInt16();
			if (!IsValidArrayCount(attrCount, MaxArrayEntries, "attribute"))
			{
				reader.Position = snapshotEnd;
				return result;
			}
			if (attrCount > 0)
			{
				result.Attributes = new AttributeReconcileEntry[attrCount];
				// Layout owned by AttributeReconcileEntry.ReadFrom — the mirror of the write loop.
				for (int i = 0; i < attrCount; i++)
				{
					result.Attributes[i] = AttributeReconcileEntry.ReadFrom(reader);
				}
			}

			// RNG state — unpacked, mirroring WriteCharacterReconcileData.
			result.RngS0 = reader.ReadUInt32Unpacked();
			result.RngS1 = reader.ReadUInt32Unpacked();
			result.RngS2 = reader.ReadUInt32Unpacked();
			result.RngS3 = reader.ReadUInt32Unpacked();

			result.ChargedHoldTicks = reader.ReadUInt32();

			result.Exposure = ExposureReconcileEntry.ReadArrayDelta(reader, null);

			/* Belt and braces on the success path too. If the two sides ever disagree about the shape
			 * of this snapshot the frame absorbs it here rather than corrupting the behaviour after
			 * this one, and says so once instead of failing invisibly. */
			if (reader.Position != snapshotEnd)
			{
				Log.Error("CharacterReconcileDataDeltaSerializer",
					$"ReadCharacterReconcileData consumed {reader.Position - (snapshotEnd - (int)declaredLength)} of " +
					$"{declaredLength} framed bytes. Seeking to the end of the snapshot; the reconcile state " +
					"read above may be incomplete.");
				reader.Position = snapshotEnd;
			}

			return result;
		}

		/// <summary>
		/// Registers custom full + delta serializers. FishNet uses the delta pair only when BOTH are
		/// registered; without them the type is written in full every tick, with no header.
		/// </summary>
		/// <remarks>
		/// Registered full-then-delta. On FishNet 4.6.12 that order was load-bearing — its
		/// <c>GenericWriter.SetWrite</c> discarded a CUSTOM delta serializer registered before it —
		/// and the fix (feat/delta-prediction-beta) makes the order irrelevant; it is kept so this
		/// still holds on a FishNet without that fix.
		/// </remarks>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<CharacterReconcileData>.SetWrite(WriteCharacterReconcileData);
			GenericReader<CharacterReconcileData>.SetRead(ReadCharacterReconcileData);
			GenericDeltaWriter<CharacterReconcileData>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterReconcileData>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="CharacterReconcileData"/>.
		/// Writes a 2-byte bitmask indicating which fields changed, followed by delta-encoded values.
		/// </summary>
		/// <remarks>
		/// <para>
		/// FishNet passes <c>RootSerialize</c> and, as <paramref name="prev"/>, the last FULL
		/// reconcile as readers decoded it — not the previous reconcile. See the class remarks.
		/// </para>
		/// <para>
		/// <c>FullSerialize</c> puts every field on the wire, still relative to
		/// <paramref name="prev"/>: FishNet's scalar delta primitives are difference-based, so no
		/// delta payload can be read without the baseline. FishNet no longer passes it here — a full
		/// reconcile goes through <see cref="WriteCharacterReconcileData"/> — so the absolute-snapshot
		/// mode byte the fork needed for that case is gone.
		/// </para>
		/// </remarks>
		/// <param name="writer">The network writer.</param>
		/// <param name="prev">Baseline: the last full reconcile, as decoded.</param>
		/// <param name="next">Reconcile data to write.</param>
		/// <param name="option">Delta serializer options.</param>
		/// <returns>True if any data was written.</returns>
		private static bool WriteDelta(
			Writer writer,
			CharacterReconcileData prev,
			CharacterReconcileData next,
			DeltaSerializerOption option)
		{
			ushort flags = 0;
			// See CharacterReplicateDataDeltaSerializer.WriteDelta for why these are three separate
			// values rather than one forceWrite.
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);
			bool mustEmit = option != DeltaSerializerOption.Unset;
			DeltaSerializerOption fieldOption = fullSerialize ? option : DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			int startLength = writer.Length;
			writer.WriteUInt16(0);

			if (writer.WriteDelta(prev.MotorState, next.MotorState, fieldOption))
				flags |= MOTOR_STATE_BIT;

			if (writer.WriteDeltaInt64(prev.AbilityID, next.AbilityID, fieldOption))
				flags |= ABILITY_ID_BIT;

			if (writer.WriteDeltaUInt32(prev.RemainingTicks, next.RemainingTicks, fieldOption))
				flags |= REMAINING_TICKS_BIT;

			if (writer.WriteDeltaInt32(prev.Seed, next.Seed, fieldOption))
				flags |= SEED_BIT;

			if (writer.WriteDelta(prev.ResourceState, next.ResourceState, fieldOption))
				flags |= RESOURCE_BIT;

			if (writer.WriteDeltaInt32(prev.PackedFlagsAndSlot, next.PackedFlagsAndSlot, fieldOption))
				flags |= PACKED_FLAGS_BIT;

			if (CooldownReconcileEntry.WriteArrayDelta(writer, prev.Cooldowns, next.Cooldowns, fieldOption))
				flags |= COOLDOWN_BIT;

			if (BuffReconcileEntry.WriteArrayDelta(writer, prev.Buffs, next.Buffs, fieldOption))
				flags |= BUFF_BIT;

			if (WriteRngStateDelta(writer, prev, next, fieldOption))
				flags |= RNG_STATE_BIT;

			if (AttributeReconcileEntry.WriteArrayDelta(writer, prev.Attributes, next.Attributes, fieldOption))
				flags |= ATTRIBUTE_BIT;

			if (EquipmentReconcileEntry.WriteArrayDelta(writer, prev.Equipment, next.Equipment, fieldOption))
				flags |= EQUIPMENT_BIT;

			if (writer.WriteDeltaUInt32(prev.ChargedHoldTicks, next.ChargedHoldTicks, fieldOption))
				flags |= CHARGED_HOLD_BIT;

			if (ExposureReconcileEntry.WriteArrayDelta(writer, prev.Exposure, next.Exposure, fieldOption))
				flags |= EXPOSURE_BIT;

			if (flags != 0 || mustEmit)
			{
				/* Insert rather than seek-write-seek: the Insert* helpers are fixed width and
				 * cannot silently change size, whereas WriteUInt16 is only unpacked today
				 * because of a standing 'todo: should be using WritePackedWhole' in FishNet's
				 * Writer. A packed backfill would overrun the placeholder and corrupt the
				 * first field written after it. */
				writer.InsertUInt16Unpacked(flags, flagPos);
				return true;
			}

			/* Rewind Length as well as Position. Writer.Length only ever grows — every write does
			 * Length = Max(Length, Position) — and GetArraySegment sends 0..Length, so restoring
			 * Position alone left the placeholder inside the sent segment as trailing garbage
			 * whenever nothing was written after it. */
			writer.Position = flagPos;
			writer.Length = startLength;
			return false;
		}

		/// <summary>
		/// Compares and writes the 4 xoshiro128** state words. All 4 change together so a single bit controls writing.
		/// Delta encoding is intentionally skipped since pseudo-random state has high entropy.
		/// <para>
		/// That same entropy is why the words go out UNPACKED. FishNet's varint form spends a fifth
		/// byte on any word whose top four bits are set, which for state words is fifteen of every
		/// sixteen; fixed-width is four apiece and never worse. <see cref="ReadDelta"/> and
		/// <see cref="WriteCharacterReconcileData"/> both use the same unpacked form — they must agree or every
		/// predicted behaviour after this one decodes from the wrong offset.
		/// </para>
		/// </summary>
		/// <param name="writer">The network writer.</param>
		/// <param name="prev">Previous reconcile data snapshot.</param>
		/// <param name="next">Next reconcile data snapshot.</param>
		/// <param name="option">Delta serializer options.</param>
		/// <returns>True if RNG state was written.</returns>
		private static bool WriteRngStateDelta(
			Writer writer,
			CharacterReconcileData prev,
			CharacterReconcileData next,
			DeltaSerializerOption option)
		{
			/* Only fullSerialize forces the words out. This is a leaf writer whose presence is
			 * signalled by RNG_STATE_BIT in the caller's flags word, so on a RootSerialize it can
			 * still decline and let the caller leave the bit clear. */
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);

			if (!fullSerialize &&
				prev.RngS0 == next.RngS0 &&
				prev.RngS1 == next.RngS1 &&
				prev.RngS2 == next.RngS2 &&
				prev.RngS3 == next.RngS3)
			{
				return false;
			}

			writer.WriteUInt32Unpacked(next.RngS0);
			writer.WriteUInt32Unpacked(next.RngS1);
			writer.WriteUInt32Unpacked(next.RngS2);
			writer.WriteUInt32Unpacked(next.RngS3);
			return true;
		}

		/// <summary>
		/// Delta reader for <see cref="CharacterReconcileData"/>.
		/// Reads the bitmask and reconstructs only the changed fields.
		/// Unknown bits are silently ignored for forward compatibility.
		/// </summary>
		/// <remarks>
		/// Consumes exactly what <see cref="WriteDelta"/> wrote whatever <paramref name="prev"/> is:
		/// every field's presence comes from the bitmask and every nested reader's width from its
		/// own flags, never from the baseline. FishNet relies on that to decode — and then discard —
		/// a delta against a full reconcile this peer never received. The arrays are copied before
		/// they are patched, so <paramref name="prev"/> is never modified.
		/// </remarks>
		/// <param name="reader">The network reader.</param>
		/// <param name="prev">Baseline: the last full reconcile this peer decoded.</param>
		/// <returns>The reconstructed reconcile data with delta-applied changes.</returns>
		private static CharacterReconcileData ReadDelta(
			Reader reader,
			CharacterReconcileData prev)
		{
			ushort flags = reader.ReadUInt16();
			CharacterReconcileData result = prev;

			if ((flags & MOTOR_STATE_BIT) != 0)
				result.MotorState = reader.ReadDelta(prev.MotorState);

			if ((flags & ABILITY_ID_BIT) != 0)
				result.AbilityID = reader.ReadDeltaInt64(prev.AbilityID);

			if ((flags & REMAINING_TICKS_BIT) != 0)
				result.RemainingTicks = reader.ReadDeltaUInt32(prev.RemainingTicks);

			if ((flags & SEED_BIT) != 0)
				result.Seed = reader.ReadDeltaInt32(prev.Seed);

			if ((flags & RESOURCE_BIT) != 0)
				result.ResourceState = reader.ReadDelta(prev.ResourceState);

			if ((flags & PACKED_FLAGS_BIT) != 0)
				result.PackedFlagsAndSlot = reader.ReadDeltaInt32(prev.PackedFlagsAndSlot);

			if ((flags & COOLDOWN_BIT) != 0)
				result.Cooldowns = CooldownReconcileEntry.ReadArrayDelta(reader, prev.Cooldowns);

			if ((flags & BUFF_BIT) != 0)
				result.Buffs = BuffReconcileEntry.ReadArrayDelta(reader, prev.Buffs);

			if ((flags & RNG_STATE_BIT) != 0)
			{
				// Unpacked, matching WriteRngStateDelta.
				result.RngS0 = reader.ReadUInt32Unpacked();
				result.RngS1 = reader.ReadUInt32Unpacked();
				result.RngS2 = reader.ReadUInt32Unpacked();
				result.RngS3 = reader.ReadUInt32Unpacked();
			}

			if ((flags & ATTRIBUTE_BIT) != 0)
				result.Attributes = AttributeReconcileEntry.ReadArrayDelta(reader, prev.Attributes);

			if ((flags & EQUIPMENT_BIT) != 0)
				result.Equipment = EquipmentReconcileEntry.ReadArrayDelta(reader, prev.Equipment);

			if ((flags & CHARGED_HOLD_BIT) != 0)
				result.ChargedHoldTicks = reader.ReadDeltaUInt32(prev.ChargedHoldTicks);

			if ((flags & EXPOSURE_BIT) != 0)
				result.Exposure = ExposureReconcileEntry.ReadArrayDelta(reader, prev.Exposure);

			return result;
		}
	}
}