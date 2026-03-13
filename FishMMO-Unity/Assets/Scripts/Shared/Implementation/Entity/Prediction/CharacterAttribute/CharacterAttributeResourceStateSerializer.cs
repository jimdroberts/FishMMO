using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom serializers for <see cref="CharacterAttributeResourceState"/>.
	/// <para>
	/// <b>Regular serializer</b> (Write/Read extension methods): Used by FishNet's codegen
	/// for RPCs, SyncVars, broadcasts, and any non-prediction serialization context.
	/// Writes all 7 fields using FishNet's built-in packed encoding (varint for ints,
	/// full precision for floats).
	/// </para>
	/// <para>
	/// <b>Delta serializer</b> (registered via <see cref="GenericDeltaWriter{T}"/>/<see cref="GenericDeltaReader{T}"/>):
	/// Used during prediction replicate/reconcile ticks. Writes a 1-byte bitmask
	/// (7 bits for 7 fields) followed by delta-encoded values for only the changed fields.
	/// On a typical tick where only health regens, this sends ~3-4 bytes instead of 28.
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
			writer.WriteSingle(value.RegenDelta);
			writer.WriteSingle(value.Health);
			writer.WriteInt32(value.MaxHealth);
			writer.WriteSingle(value.Mana);
			writer.WriteInt32(value.MaxMana);
			writer.WriteSingle(value.Stamina);
			writer.WriteInt32(value.MaxStamina);
		}

		/// <summary>
		/// Reads all fields of <see cref="CharacterAttributeResourceState"/>.
		/// Must read in the same order as <see cref="WriteCharacterAttributeResourceState"/>.
		/// </summary>
		public static CharacterAttributeResourceState ReadCharacterAttributeResourceState(this Reader reader)
		{
			return new CharacterAttributeResourceState()
			{
				RegenDelta = reader.ReadSingle(),
				Health = reader.ReadSingle(),
				MaxHealth = reader.ReadInt32(),
				Mana = reader.ReadSingle(),
				MaxMana = reader.ReadInt32(),
				Stamina = reader.ReadSingle(),
				MaxStamina = reader.ReadInt32(),
			};
		}

		#endregion

		#region Delta Serializer (prediction)

		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.RegenDelta"/>.</summary>
		private const byte REGEN_DELTA_BIT = 1 << 0;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Health"/>.</summary>
		private const byte HEALTH_BIT      = 1 << 1;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.MaxHealth"/>.</summary>
		private const byte MAX_HEALTH_BIT  = 1 << 2;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Mana"/>.</summary>
		private const byte MANA_BIT        = 1 << 3;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.MaxMana"/>.</summary>
		private const byte MAX_MANA_BIT    = 1 << 4;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.Stamina"/>.</summary>
		private const byte STAMINA_BIT     = 1 << 5;
		/// <summary>Bitmask bit for <see cref="CharacterAttributeResourceState.MaxStamina"/>.</summary>
		private const byte MAX_STAMINA_BIT = 1 << 6;

		/// <summary>
		/// Registers the custom delta serializers at runtime, after FishNet's IL-weaved
		/// code-gen serializers. Our custom delegates take priority over generated ones.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterDeltaSerializers()
		{
			GenericDeltaWriter<CharacterAttributeResourceState>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterAttributeResourceState>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="CharacterAttributeResourceState"/>.
		/// Writes a 1-byte bitmask indicating which of the 7 fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		private static bool WriteDelta(
			Writer writer,
			CharacterAttributeResourceState prev,
			CharacterAttributeResourceState next,
			DeltaSerializerOption option)
		{
			byte flags = 0;

			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			bool forceWrite = option != DeltaSerializerOption.Unset;

			if (forceWrite || prev.RegenDelta != next.RegenDelta)
			{
				writer.WriteSingle(next.RegenDelta);
				flags |= REGEN_DELTA_BIT;
			}

			if (forceWrite || prev.Health != next.Health)
			{
				writer.WriteSingle(next.Health);
				flags |= HEALTH_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MaxHealth, next.MaxHealth, option))
				flags |= MAX_HEALTH_BIT;

			if (forceWrite || prev.Mana != next.Mana)
			{
				writer.WriteSingle(next.Mana);
				flags |= MANA_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MaxMana, next.MaxMana, option))
				flags |= MAX_MANA_BIT;

			if (forceWrite || prev.Stamina != next.Stamina)
			{
				writer.WriteSingle(next.Stamina);
				flags |= STAMINA_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MaxStamina, next.MaxStamina, option))
				flags |= MAX_STAMINA_BIT;

			if (flags != 0 || forceWrite)
			{
				int endPos = writer.Position;
				writer.Position = flagPos;
				writer.WriteUInt8Unpacked(flags);
				writer.Position = endPos;
				return true;
			}

			writer.Position = flagPos;
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
			byte flags = reader.ReadUInt8Unpacked();
			CharacterAttributeResourceState result = prev;

			if ((flags & REGEN_DELTA_BIT) != 0)
				result.RegenDelta = reader.ReadSingle();

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

			return result;
		}

		#endregion
	}
}