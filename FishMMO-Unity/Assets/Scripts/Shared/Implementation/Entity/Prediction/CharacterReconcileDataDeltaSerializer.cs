using System;
using UnityEngine;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom delta serializers for <see cref="CharacterReconcileData"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 2-byte bitmask (11 bits for 11 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// The nested <see cref="KinematicCharacterController.KinematicCharacterMotorState"/> and
	/// <see cref="CharacterAttributeResourceState"/> use their own delta serializers,
	/// so savings compound. Cooldowns, buffs, and non-resource attributes use index-delta
	/// compression via <see cref="CooldownReconcileEntry"/>, <see cref="BuffReconcileEntry"/>,
	/// and <see cref="AttributeReconcileEntry"/>.
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
		// Bits 11..15 are reserved for future fields. The flag mask is a ushort (16 bits);
		// 11 are currently in use. When adding new fields, take the next bit and update
		// both WriteDelta and ReadDelta in lock-step.

		/// <summary>
		/// Leading byte: the rest of the payload is a delta against the reader's previous snapshot.
		/// </summary>
		private const byte MODE_DELTA = 0;

		/// <summary>
		/// Leading byte: the rest of the payload is an absolute snapshot and does not depend on the
		/// reader's previous value. See <see cref="WriteDelta"/> for why this mode has to exist.
		/// </summary>
		private const byte MODE_FULL_SNAPSHOT = 1;

		/// <summary>
		/// Registers the custom delta serializers at runtime via <see cref="GenericDeltaWriter{T}"/> and <see cref="GenericDeltaReader{T}"/>.
		/// Full array write cap — 4096 entries covers any realistic buff/debuff/cooldown set
		/// and provides a backstop against accidental runaway allocation.
		/// </summary>
		private const ushort MaxArrayEntries = 4096;

		/// <summary>
		/// Custom full serializer: writes all fields of <see cref="CharacterReconcileData"/>.
		/// Nested types use their full serializers. Arrays write count + entries.
		/// </summary>
		public static void WriteCharacterReconcileData(this Writer writer, CharacterReconcileData value)
		{
			KinematicCharacterMotorStateDeltaSerializer.WriteKinematicCharacterMotorState(writer, value.MotorState);
			writer.WriteInt64(value.AbilityID);
			writer.WriteUInt32(value.RemainingTicks);
			writer.WriteInt32(value.Seed);
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
				for (int i = 0; i < count; i++)
				{
					writer.WriteInt64(value.Cooldowns[i].AbilityID);
					writer.WriteUInt32(value.Cooldowns[i].StartTick);
					writer.WriteUInt32(value.Cooldowns[i].DurationTicks);
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
				for (int i = 0; i < count; i++)
				{
					writer.WriteInt32(value.Buffs[i].TemplateID);
					writer.WriteUInt32(value.Buffs[i].ExpiryTick);
					writer.WriteUInt32(value.Buffs[i].NextTickTick);
					writer.WriteInt32(value.Buffs[i].Stacks);
					writer.WriteInt32(value.Buffs[i].TickCount);
					writer.WriteInt32(value.Buffs[i].CumulativeTickMultiplier);
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
				for (int i = 0; i < count; i++)
				{
					writer.WriteInt32(value.Equipment[i].TemplateID);
					writer.WriteUInt8Unpacked(value.Equipment[i].Slot);
					writer.WriteInt32(value.Equipment[i].Seed);
					writer.WriteInt64(value.Equipment[i].InstanceID);
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
				for (int i = 0; i < count; i++)
				{
					writer.WriteInt32(value.Attributes[i].TemplateID);
					writer.WriteInt32(value.Attributes[i].Value);
					writer.WriteInt32(value.Attributes[i].ExternalModifier);
				}
			}

			// RNG state
			writer.WriteUInt32(value.RngS0);
			writer.WriteUInt32(value.RngS1);
			writer.WriteUInt32(value.RngS2);
			writer.WriteUInt32(value.RngS3);

			// Chain sequence — see CharacterReconcileData.Sequence. Written last so older readers
			// of the absolute form would have read every field before reaching it.
			writer.WriteUInt8Unpacked(value.Sequence);
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
				"The reconcile stream is corrupt; abandoning the remainder of this payload.");
			return false;
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="CharacterReconcileData"/>.
		/// Must read in the same order as <see cref="WriteCharacterReconcileData"/>.
		/// </summary>
		public static CharacterReconcileData ReadCharacterReconcileData(this Reader reader)
		{
			var result = new CharacterReconcileData
			{
				MotorState = KinematicCharacterMotorStateDeltaSerializer.ReadKinematicCharacterMotorState(reader),
				AbilityID = reader.ReadInt64(),
				RemainingTicks = reader.ReadUInt32(),
				Seed = reader.ReadInt32(),
				ResourceState = CharacterAttributeResourceStateSerializer.ReadCharacterAttributeResourceState(reader),
				PackedFlagsAndSlot = reader.ReadInt32(),
			};

			// Cooldowns
			ushort cdCount = reader.ReadUInt16();
			if (!IsValidArrayCount(cdCount, MaxArrayEntries, "cooldown"))
			{
				return result;
			}
			if (cdCount > 0)
			{
				result.Cooldowns = new CooldownReconcileEntry[cdCount];
				for (int i = 0; i < cdCount; i++)
				{
					result.Cooldowns[i] = new CooldownReconcileEntry
					{
						AbilityID = reader.ReadInt64(),
						StartTick = reader.ReadUInt32(),
						DurationTicks = reader.ReadUInt32(),
					};
				}
			}

			// Buffs
			ushort buffCount = reader.ReadUInt16();
			if (!IsValidArrayCount(buffCount, MaxArrayEntries, "buff"))
			{
				return result;
			}
			if (buffCount > 0)
			{
				result.Buffs = new BuffReconcileEntry[buffCount];
				for (int i = 0; i < buffCount; i++)
				{
					result.Buffs[i] = new BuffReconcileEntry
					{
						TemplateID = reader.ReadInt32(),
						ExpiryTick = reader.ReadUInt32(),
						NextTickTick = reader.ReadUInt32(),
						Stacks = reader.ReadInt32(),
						TickCount = reader.ReadInt32(),
						CumulativeTickMultiplier = reader.ReadInt32(),
					};
				}
			}

			// Equipment
			ushort equipCount = reader.ReadUInt16();
			if (!IsValidArrayCount(equipCount, EquipmentReconcileEntry.MaxEntries, "equipment"))
			{
				return result;
			}
			if (equipCount > 0)
			{
				result.Equipment = new EquipmentReconcileEntry[equipCount];
				for (int i = 0; i < equipCount; i++)
				{
					result.Equipment[i] = new EquipmentReconcileEntry
					{
						TemplateID = reader.ReadInt32(),
						Slot = reader.ReadUInt8Unpacked(),
						Seed = reader.ReadInt32(),
						InstanceID = reader.ReadInt64(),
					};
				}
			}

			// Attributes
			ushort attrCount = reader.ReadUInt16();
			if (!IsValidArrayCount(attrCount, MaxArrayEntries, "attribute"))
			{
				return result;
			}
			if (attrCount > 0)
			{
				result.Attributes = new AttributeReconcileEntry[attrCount];
				for (int i = 0; i < attrCount; i++)
				{
					result.Attributes[i] = new AttributeReconcileEntry
					{
						TemplateID = reader.ReadInt32(),
						Value = reader.ReadInt32(),
						ExternalModifier = reader.ReadInt32(),
					};
				}
			}

			// RNG state
			result.RngS0 = reader.ReadUInt32();
			result.RngS1 = reader.ReadUInt32();
			result.RngS2 = reader.ReadUInt32();
			result.RngS3 = reader.ReadUInt32();

			result.Sequence = reader.ReadUInt8Unpacked();

			return result;
		}

		/// <summary>
		/// Registers custom full + delta serializers. Full must be registered before delta
		/// to prevent FishNet from clearing the delta registration.
		/// </summary>
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
		/// <param name="writer">The network writer.</param>
		/// <param name="prev">Previous reconcile data snapshot.</param>
		/// <param name="next">Next reconcile data snapshot.</param>
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

			/* A full serialize is written as an ABSOLUTE snapshot, not as a delta against prev.
			 *
			 * This is the difference between a delta chain that works and one that cannot. FishNet's
			 * scalar delta primitives are difference-based — Writer.WriteDifference8_16_32 writes
			 * valueB - valueA and the reader adds that onto ITS previous value — so a payload is only
			 * decodable by a peer holding the same baseline the writer used. FullSerialize was meant to
			 * be the escape hatch for a peer that has no such baseline (an observer added part-way
			 * through the object's life, whose lastReconcileData is still default while the server's has
			 * moved on), but forcing every field through a difference-based writer does not produce a
			 * self-contained payload — it just guarantees every field is present, still relative to a
			 * baseline the receiver does not have. That observer would decode garbage forever.
			 *
			 * Routing FullSerialize through the full serializer fixes it: the payload is absolute, the
			 * receiver ignores its own prev, and its baseline is correct from that point on. Because
			 * FishNet also emits FullSerialize once per second (GetDeltaSerializeOption: localTick %
			 * tickRate == 0), this doubles as a periodic resync that repairs any drift rather than
			 * letting it accumulate for the lifetime of the object.
			 *
			 * The mode byte is what lets ReadDelta tell the two apart, since delta readers receive no
			 * DeltaSerializerOption. One byte per reconcile is a cheap price for the property. */
			if (fullSerialize)
			{
				writer.WriteUInt8Unpacked(MODE_FULL_SNAPSHOT);
				WriteCharacterReconcileData(writer, next);
				return true;
			}

			/* Remembered so the nothing-changed exit below can hand back the mode and sequence
			 * bytes too. Rewinding only to the flags word left the mode byte in the stream while
			 * returning false — harmless in production, where FishNet always passes RootSerialize
			 * and the rewind is never taken, but a delta writer's contract is bytes iff true. */
			int modePos = writer.Position;
			int modeLength = writer.Length;
			writer.WriteUInt8Unpacked(MODE_DELTA);

			/* The chain sequence rides every delta, outside the flags word, so the reader can
			 * verify its baseline BEFORE it decodes anything against it. One byte per reconcile
			 * (30 B/s to the owner) buys exact loss detection on the unreliable state channel. */
			writer.WriteUInt8Unpacked(next.Sequence);

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

			/* Rewind Length as well as Position, and back past the mode and sequence bytes.
			 * Writer.Length only ever grows — every write does Length = Max(Length, Position) —
			 * and GetArraySegment sends 0..Length, so restoring Position alone left placeholder
			 * bytes inside the sent segment as trailing garbage whenever nothing was written
			 * after them. */
			writer.Position = modePos;
			writer.Length = modeLength;
			return false;
		}

		/// <summary>
		/// Compares and writes the 4 xoshiro128** state words. All 4 change together so a single bit controls writing.
		/// Delta encoding is intentionally skipped since pseudo-random state has high entropy.
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

			writer.WriteUInt32(next.RngS0);
			writer.WriteUInt32(next.RngS1);
			writer.WriteUInt32(next.RngS2);
			writer.WriteUInt32(next.RngS3);
			return true;
		}

		/// <summary>True while a sequence gap is being ridden out, so the log line fires once per gap.</summary>
		private static bool chainBreakLogged;

		/// <summary>
		/// Consumes a delta payload's remaining bytes without applying them, keeping the shared
		/// state reader aligned for whatever follows this behaviour's reconcile.
		/// </summary>
		/// <remarks>
		/// Decodes into a throwaway against <paramref name="prev"/>: the nested delta readers are
		/// the only things that know each field's wire width, and running them is cheaper than
		/// duplicating that knowledge here. The result is discarded, and the RNG words and arrays
		/// are read the same way the accepting path reads them.
		/// </remarks>
		private static void DrainDeltaPayload(Reader reader, CharacterReconcileData prev)
		{
			ushort flags = reader.ReadUInt16();
			if ((flags & MOTOR_STATE_BIT) != 0) reader.ReadDelta(prev.MotorState);
			if ((flags & ABILITY_ID_BIT) != 0) reader.ReadDeltaInt64(prev.AbilityID);
			if ((flags & REMAINING_TICKS_BIT) != 0) reader.ReadDeltaUInt32(prev.RemainingTicks);
			if ((flags & SEED_BIT) != 0) reader.ReadDeltaInt32(prev.Seed);
			if ((flags & RESOURCE_BIT) != 0) reader.ReadDelta(prev.ResourceState);
			if ((flags & PACKED_FLAGS_BIT) != 0) reader.ReadDeltaInt32(prev.PackedFlagsAndSlot);
			if ((flags & COOLDOWN_BIT) != 0) CooldownReconcileEntry.ReadArrayDelta(reader, prev.Cooldowns);
			if ((flags & BUFF_BIT) != 0) BuffReconcileEntry.ReadArrayDelta(reader, prev.Buffs);
			if ((flags & RNG_STATE_BIT) != 0) { reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); }
			if ((flags & ATTRIBUTE_BIT) != 0) AttributeReconcileEntry.ReadArrayDelta(reader, prev.Attributes);
			if ((flags & EQUIPMENT_BIT) != 0) EquipmentReconcileEntry.ReadArrayDelta(reader, prev.Equipment);
		}

		/// <summary>
		/// Delta reader for <see cref="CharacterReconcileData"/>.
		/// Reads the bitmask and reconstructs only the changed fields.
		/// Unknown bits are silently ignored for forward compatibility.
		/// </summary>
		/// <param name="reader">The network reader.</param>
		/// <param name="prev">Previous reconcile data snapshot.</param>
		/// <returns>The reconstructed reconcile data with delta-applied changes.</returns>
		private static CharacterReconcileData ReadDelta(
			Reader reader,
			CharacterReconcileData prev)
		{
			/* Mode first — see WriteDelta. A full snapshot is absolute and self-contained, so prev
			 * is deliberately ignored; a delta is relative to prev. */
			byte mode = reader.ReadUInt8Unpacked();
			if (mode == MODE_FULL_SNAPSHOT)
			{
				return ReadCharacterReconcileData(reader);
			}
			if (mode != MODE_DELTA)
			{
				Log.Error("CharacterReconcileDataDeltaSerializer",
					$"ReadDelta: unknown payload mode {mode}. The reconcile stream is corrupt; " +
					"returning the previous snapshot unchanged.");
				return prev;
			}

			byte sequence = reader.ReadUInt8Unpacked();
			if (sequence != unchecked((byte)(prev.Sequence + 1)))
			{
				/* The baseline this delta was built against is not the one this peer holds — a
				 * StateUpdate datagram was lost or reordered. Decoding would apply a wrong state,
				 * so the payload is consumed and discarded and FishNet is told not to reconcile
				 * from it (ReconcileDeltaGuard). The baseline stays where it is; every further
				 * delta is rejected the same way until the next absolute snapshot — at most one
				 * second — resynchronises the chain. Logged once per gap, not per rejected packet. */
				if (!chainBreakLogged)
				{
					chainBreakLogged = true;
					Log.Debug("CharacterReconcileDataDeltaSerializer",
						$"ReadDelta: reconcile sequence {sequence} does not follow baseline {prev.Sequence}; " +
						"a state update was lost. Ignoring reconciles until the next absolute snapshot.");
				}
				DrainDeltaPayload(reader, prev);
				FishNet.Object.ReconcileDeltaGuard.RejectLastRead();
				return prev;
			}
			chainBreakLogged = false;

			ushort flags = reader.ReadUInt16();
			CharacterReconcileData result = prev;
			result.Sequence = sequence;

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
				result.RngS0 = reader.ReadUInt32();
				result.RngS1 = reader.ReadUInt32();
				result.RngS2 = reader.ReadUInt32();
				result.RngS3 = reader.ReadUInt32();
			}

			if ((flags & ATTRIBUTE_BIT) != 0)
				result.Attributes = AttributeReconcileEntry.ReadArrayDelta(reader, prev.Attributes);

			if ((flags & EQUIPMENT_BIT) != 0)
				result.Equipment = EquipmentReconcileEntry.ReadArrayDelta(reader, prev.Equipment);

			return result;
		}
	}
}