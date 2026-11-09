using UnityEngine;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom serializers for <see cref="CharacterAttributeResourceState"/>.
	/// <para>
	/// <b>Regular serializer</b> (Write/Read extension methods): Used by FishNet's codegen
	/// for RPCs, SyncVars, broadcasts, and any non-prediction serialization context.
	/// Writes every field using FishNet's built-in packed encoding (varint for ints,
	/// full precision for floats).
	/// </para>
	/// <para>
	/// <b>Delta serializer</b> (registered via <see cref="GenericDeltaWriter{T}"/>/<see cref="GenericDeltaReader{T}"/>):
	/// Used during prediction replicate/reconcile ticks. Writes a 2-byte bitmask
	/// (one bit per field) followed by delta-encoded values for only the changed fields.
	/// On a typical tick where only health regens, this sends ~4-5 bytes instead of 40.
	/// </para>
	/// <para>
	/// The delta serializer must use <see cref="GenericDeltaWriter{T}.SetWrite"/> because
	/// FishNet's <c>[DefaultDeltaWriter]</c> attribute only supports single-value signatures,
	/// not the <c>(prev, next, option)</c> signature needed for per-field delta compression.
	/// </para>
	/// </summary>
	public static class CharacterAttributeResourceStateSerializer
	{
		#region Regular Serializer (extension methods)

		/// <summary>
		/// Writes all fields of <see cref="CharacterAttributeResourceState"/>.
		/// FishNet's codegen discovers this method by naming convention and registers it
		/// via <see cref="GenericWriter{T}"/>. Combined with <c>[UseGlobalCustomSerializer]</c>
		/// on the struct, this serializer is used across all assemblies.
		/// </summary>
		public static void WriteCharacterAttributeResourceState(this Writer writer, CharacterAttributeResourceState value)
		{
			writer.WriteUInt32(value.NextRegenTick);
			writer.WriteSingle(value.Health);
			writer.WriteInt32(value.MaxHealth);
			writer.WriteSingle(value.Mana);
			writer.WriteInt32(value.MaxMana);
			writer.WriteSingle(value.Stamina);
			writer.WriteInt32(value.MaxStamina);
			writer.WriteSingle(value.Breath);
			writer.WriteUInt32(value.HealthConsumedTick);
			writer.WriteUInt32(value.ManaConsumedTick);
			writer.WriteUInt32(value.StaminaConsumedTick);
		}

		/// <summary>
		/// Reads all fields of <see cref="CharacterAttributeResourceState"/>.
		/// Must read in the same order as <see cref="WriteCharacterAttributeResourceState"/>.
		/// </summary>
		public static CharacterAttributeResourceState ReadCharacterAttributeResourceState(this Reader reader)
		{
			return new CharacterAttributeResourceState()
			{
				NextRegenTick = reader.ReadUInt32(),
				Health = reader.ReadSingle(),
				MaxHealth = reader.ReadInt32(),
				Mana = reader.ReadSingle(),
				MaxMana = reader.ReadInt32(),
				Stamina = reader.ReadSingle(),
				MaxStamina = reader.ReadInt32(),
				Breath = reader.ReadSingle(),
				HealthConsumedTick = reader.ReadUInt32(),
				ManaConsumedTick = reader.ReadUInt32(),
				StaminaConsumedTick = reader.ReadUInt32(),
			};
		}

		#endregion

		#region Delta Serializer (prediction)

		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.NextRegenTick"/>.</summary>
		private const ushort NEXT_REGEN_TICK_BIT = 1 << 0;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Health"/>.</summary>
		private const ushort HEALTH_BIT = 1 << 1;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.MaxHealth"/>.</summary>
		private const ushort MAX_HEALTH_BIT = 1 << 2;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Mana"/>.</summary>
		private const ushort MANA_BIT = 1 << 3;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.MaxMana"/>.</summary>
		private const ushort MAX_MANA_BIT = 1 << 4;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Stamina"/>.</summary>
		private const ushort STAMINA_BIT = 1 << 5;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.MaxStamina"/>.</summary>
		private const ushort MAX_STAMINA_BIT = 1 << 6;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Breath"/>.</summary>
		private const ushort BREATH_BIT = 1 << 7;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.HealthConsumedTick"/>.</summary>
		private const ushort HEALTH_CONSUMED_BIT = 1 << 8;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.ManaConsumedTick"/>.</summary>
		private const ushort MANA_CONSUMED_BIT = 1 << 9;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.StaminaConsumedTick"/>.</summary>
		private const ushort STAMINA_CONSUMED_BIT = 1 << 10;

		/// <summary>
		/// Registers the custom delta serializers at runtime, after FishNet's IL-weaved
		/// code-gen serializers. Our custom delegates take priority over generated ones.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<CharacterAttributeResourceState>.SetWrite(WriteCharacterAttributeResourceState);
			GenericReader<CharacterAttributeResourceState>.SetRead(ReadCharacterAttributeResourceState);
			GenericDeltaWriter<CharacterAttributeResourceState>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterAttributeResourceState>.SetRead(ReadDelta);
		}

		/// <summary>Epsilon for float comparisons to avoid false-positive deltas from cross-platform drift.</summary>
		private const float FLOAT_EPSILON = 0.001f;

		/// <summary>
		/// Delta writer for <see cref="CharacterAttributeResourceState"/>.
		/// Writes a 2-byte bitmask indicating which fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		/// <remarks>
		/// Float fields (Health, Mana, Stamina) use raw <c>WriteSingle</c> — four exact bytes —
		/// deliberately, not for want of an API. <c>Writer.WriteUDeltaSingle</c> and
		/// <c>Reader.ReadUDeltaSingle</c> are public, but they are LOSSY: the writer floors a scaled
		/// difference and the reader adds it onto its own copy, so a resource encoded that way
		/// accumulates error against the server's exact value for as long as the delta chain runs.
		/// These values decide death and whether an ability can be paid for. Int/uint fields
		/// (NextRegenTick, the maxima and the consumed ticks) use <c>WriteDeltaInt32</c>, which is a
		/// varint difference and exact.
		///
		/// Float comparisons use a small epsilon (<see cref="FLOAT_EPSILON"/>) to avoid
		/// false-positive deltas from cross-platform floating-point representation drift.
		/// Values differing by less than 0.001 are treated as equal.
		///
		/// A float field is only worth moving to the delta primitive if it stops being something a
		/// gameplay decision reads exactly.
		/// </remarks>
		private static bool WriteDelta(
			Writer writer,
			CharacterAttributeResourceState prev,
			CharacterAttributeResourceState next,
			DeltaSerializerOption option)
		{
			ushort flags = 0;

			int flagPos = writer.Position;
			int startLength = writer.Length;
			writer.WriteUInt16Unpacked(0);

			// See CharacterReplicateDataDeltaSerializer.WriteDelta for why these are three separate
			// values rather than one forceWrite.
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);
			bool mustEmit = option != DeltaSerializerOption.Unset;
			DeltaSerializerOption fieldOption = fullSerialize ? option : DeltaSerializerOption.Unset;

			if (writer.WriteDeltaInt32((int)prev.NextRegenTick, (int)next.NextRegenTick, fieldOption))
				flags |= NEXT_REGEN_TICK_BIT;

			if (fullSerialize || Mathf.Abs(prev.Health - next.Health) > FLOAT_EPSILON)
			{
				writer.WriteSingle(next.Health);
				flags |= HEALTH_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MaxHealth, next.MaxHealth, fieldOption))
				flags |= MAX_HEALTH_BIT;

			if (fullSerialize || Mathf.Abs(prev.Mana - next.Mana) > FLOAT_EPSILON)
			{
				writer.WriteSingle(next.Mana);
				flags |= MANA_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MaxMana, next.MaxMana, fieldOption))
				flags |= MAX_MANA_BIT;

			if (fullSerialize || Mathf.Abs(prev.Stamina - next.Stamina) > FLOAT_EPSILON)
			{
				writer.WriteSingle(next.Stamina);
				flags |= STAMINA_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MaxStamina, next.MaxStamina, fieldOption))
				flags |= MAX_STAMINA_BIT;

			// Exact, as the other resources are: running out decides when drowning starts.
			if (fullSerialize || Mathf.Abs(prev.Breath - next.Breath) > FLOAT_EPSILON)
			{
				writer.WriteSingle(next.Breath);
				flags |= BREATH_BIT;
			}

			if (writer.WriteDeltaInt32((int)prev.HealthConsumedTick, (int)next.HealthConsumedTick, fieldOption))
				flags |= HEALTH_CONSUMED_BIT;

			if (writer.WriteDeltaInt32((int)prev.ManaConsumedTick, (int)next.ManaConsumedTick, fieldOption))
				flags |= MANA_CONSUMED_BIT;

			if (writer.WriteDeltaInt32((int)prev.StaminaConsumedTick, (int)next.StaminaConsumedTick, fieldOption))
				flags |= STAMINA_CONSUMED_BIT;

			// When mustEmit is true, emit the bitmask even if flags==0 (all values equal) so the
			// reader knows a delta was written. When Unset and flags==0, rewind past the placeholder.
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

			/* Rewind Length as well as Position. Writer.Length only ever grows — every write
			 * does Length = Max(Length, Position) — and GetArraySegment sends 0..Length, so
			 * restoring Position alone left this placeholder's bytes inside the sent segment
			 * as trailing garbage whenever nothing was written after it. */
			writer.Position = flagPos;
			writer.Length = startLength;
			return false;
		}

		/// <summary>
		/// Delta reader for <see cref="CharacterAttributeResourceState"/>.
		/// Reads the bitmask and reconstructs only the changed fields from their deltas,
		/// carrying forward unchanged fields from the previous value.
		/// </summary>
		private static CharacterAttributeResourceState ReadDelta(
			Reader reader,
			CharacterAttributeResourceState prev)
		{
			ushort flags = reader.ReadUInt16Unpacked();
			CharacterAttributeResourceState result = prev;

			if ((flags & NEXT_REGEN_TICK_BIT) != 0)
				result.NextRegenTick = (uint)reader.ReadDeltaInt32((int)prev.NextRegenTick);

			if ((flags & HEALTH_BIT) != 0)
				result.Health = reader.ReadSingle();

			if ((flags & MAX_HEALTH_BIT) != 0)
				result.MaxHealth = reader.ReadDeltaInt32(prev.MaxHealth);

			if ((flags & MANA_BIT) != 0)
				result.Mana = reader.ReadSingle();

			if ((flags & MAX_MANA_BIT) != 0)
				result.MaxMana = reader.ReadDeltaInt32(prev.MaxMana);

			if ((flags & STAMINA_BIT) != 0)
				result.Stamina = reader.ReadSingle();

			if ((flags & MAX_STAMINA_BIT) != 0)
				result.MaxStamina = reader.ReadDeltaInt32(prev.MaxStamina);

			if ((flags & BREATH_BIT) != 0)
				result.Breath = reader.ReadSingle();

			if ((flags & HEALTH_CONSUMED_BIT) != 0)
				result.HealthConsumedTick = (uint)reader.ReadDeltaInt32((int)prev.HealthConsumedTick);

			if ((flags & MANA_CONSUMED_BIT) != 0)
				result.ManaConsumedTick = (uint)reader.ReadDeltaInt32((int)prev.ManaConsumedTick);

			if ((flags & STAMINA_CONSUMED_BIT) != 0)
				result.StaminaConsumedTick = (uint)reader.ReadDeltaInt32((int)prev.StaminaConsumedTick);

			return result;
		}

		#endregion
	}
}