using System;
using UnityEngine;
using FishNet.Serializing;

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
			bool forceWrite = option != DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			writer.WriteUInt16(0);

			if (writer.WriteDelta(prev.MotorState, next.MotorState, option))
				flags |= MOTOR_STATE_BIT;

			if (writer.WriteDeltaInt64(prev.AbilityID, next.AbilityID, option))
				flags |= ABILITY_ID_BIT;

			if (writer.WriteDeltaUInt32(prev.RemainingTicks, next.RemainingTicks, option))
				flags |= REMAINING_TICKS_BIT;

			if (writer.WriteDeltaInt32(prev.Seed, next.Seed, option))
				flags |= SEED_BIT;

			if (writer.WriteDelta(prev.ResourceState, next.ResourceState, option))
				flags |= RESOURCE_BIT;

			if (writer.WriteDeltaInt32(prev.PackedFlagsAndSlot, next.PackedFlagsAndSlot, option))
				flags |= PACKED_FLAGS_BIT;

			if (CooldownReconcileEntry.WriteArrayDelta(writer, prev.Cooldowns, next.Cooldowns, option))
				flags |= COOLDOWN_BIT;

			if (BuffReconcileEntry.WriteArrayDelta(writer, prev.Buffs, next.Buffs, option))
				flags |= BUFF_BIT;

			if (WriteRngStateDelta(writer, prev, next, option))
				flags |= RNG_STATE_BIT;

			if (AttributeReconcileEntry.WriteArrayDelta(writer, prev.Attributes, next.Attributes, option))
				flags |= ATTRIBUTE_BIT;

			if (EquipmentReconcileEntry.WriteArrayDelta(writer, prev.Equipment, next.Equipment, option))
				flags |= EQUIPMENT_BIT;

			if (flags != 0 || forceWrite)
			{
				int endPos = writer.Position;
				writer.Position = flagPos;
				writer.WriteUInt16(flags);
				writer.Position = endPos;
				return true;
			}

			writer.Position = flagPos;
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
			bool forceWrite = option != DeltaSerializerOption.Unset;

			if (!forceWrite &&
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