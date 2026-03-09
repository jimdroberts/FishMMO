using FishNet.Serializing;
using FishMMO.Logging;
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

		// Bitmask bit positions for CharacterAttributeResourceState fields.
		private const byte REGEN_DELTA_BIT = 1 << 0; // 0x01
		private const byte HEALTH_BIT      = 1 << 1; // 0x02
		private const byte MAX_HEALTH_BIT  = 1 << 2; // 0x04
		private const byte MANA_BIT        = 1 << 3; // 0x08
		private const byte MAX_MANA_BIT    = 1 << 4; // 0x10
		private const byte STAMINA_BIT     = 1 << 5; // 0x20
		private const byte MAX_STAMINA_BIT = 1 << 6; // 0x40

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

			// Reserve 1 byte for the bitmask; we'll overwrite it once we know which fields changed.
			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			// Serialize changed float fields directly. FishNet's public API for float deltas
			// is not accessible in this assembly, so we gate writes via explicit comparisons.
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
				// Overwrite the placeholder bitmask byte with the actual flags.
				int endPos = writer.Position;
				writer.Position = flagPos;
				writer.WriteUInt8Unpacked(flags);
				writer.Position = endPos;
				return true;
			}

			// Nothing changed — rewind past the placeholder byte.
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

	/// <summary>
	/// Custom delta serializers for <see cref="AbilityActivationReplicateData"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 1-byte bitmask (2 bits for 2 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// On idle ticks where the player does nothing, this sends 1 byte instead of 12.
	/// </para>
	/// </summary>
	public static class AbilityActivationReplicateDataDeltaSerializer
	{
		private const byte FLAGS_BIT = 1 << 0;
		private const byte ABILITY_BIT = 1 << 1;

		/// <summary>
		/// Registers the custom delta serializers at runtime.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterDeltaSerializers()
		{
			GenericDeltaWriter<AbilityActivationReplicateData>.SetWrite(WriteDelta);
			GenericDeltaReader<AbilityActivationReplicateData>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="AbilityActivationReplicateData"/>.
		/// Writes a 1-byte bitmask indicating which fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		private static bool WriteDelta(
			Writer writer,
			AbilityActivationReplicateData prev,
			AbilityActivationReplicateData next,
			DeltaSerializerOption option)
		{
			byte flags = 0;

			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			if (writer.WriteDeltaInt32(prev.ActivationFlags, next.ActivationFlags, option))
				flags |= FLAGS_BIT;

			if (writer.WriteDeltaInt64(prev.QueuedAbilityID, next.QueuedAbilityID, option))
				flags |= ABILITY_BIT;

			bool forceWrite = option != DeltaSerializerOption.Unset;
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
		/// Delta reader for <see cref="AbilityActivationReplicateData"/>.
		/// Reads the bitmask and reconstructs only the changed fields.
		/// </summary>
		private static AbilityActivationReplicateData ReadDelta(
			Reader reader,
			AbilityActivationReplicateData prev)
		{
			byte flags = reader.ReadUInt8Unpacked();
			AbilityActivationReplicateData result = prev;

			if ((flags & FLAGS_BIT) != 0)
				result.ActivationFlags = reader.ReadDeltaInt32(prev.ActivationFlags);

			if ((flags & ABILITY_BIT) != 0)
				result.QueuedAbilityID = reader.ReadDeltaInt64(prev.QueuedAbilityID);

			return result;
		}
	}

	/// <summary>
	/// Custom delta serializers for <see cref="CharacterReconcileData"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 1-byte bitmask (8 bits for 8 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// On idle ticks where nothing changes, this sends 1 byte instead of ~33.
	/// The nested <see cref="CharacterAttributeResourceState"/> uses its own
	/// delta serializer, so the savings compound. Cooldowns are sent in full
	/// when any cooldown changes, but skipped entirely when unchanged.
	/// </para>
	/// </summary>
	public static class CharacterReconcileDataDeltaSerializer
	{
		private const byte ABILITY_ID_BIT = 1 << 0;
		private const byte REMAINING_TICKS_BIT = 1 << 1;
		private const byte SEED_BIT = 1 << 2;
		private const byte RESOURCE_BIT = 1 << 3;
		private const byte PACKED_FLAGS_BIT = 1 << 4;
		private const byte COOLDOWN_BIT = 1 << 5;
		private const byte BUFF_BIT = 1 << 6;
		private const byte RNG_STATE_BIT = 1 << 7;

		/// <summary>
		/// Registers the custom delta serializers at runtime.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterDeltaSerializers()
		{
			GenericDeltaWriter<CharacterReconcileData>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterReconcileData>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="CharacterReconcileData"/>.
		/// Writes a 1-byte bitmask indicating which of the 8 fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		private static bool WriteDelta(
			Writer writer,
			CharacterReconcileData prev,
			CharacterReconcileData next,
			DeltaSerializerOption option)
		{
			byte flags = 0;

			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			if (writer.WriteDeltaInt64(prev.AbilityID, next.AbilityID, option))
				flags |= ABILITY_ID_BIT;

			if (writer.WriteDeltaUInt32(prev.RemainingTicks, next.RemainingTicks, option))
				flags |= REMAINING_TICKS_BIT;

			if (writer.WriteDeltaInt32(prev.Seed, next.Seed, option))
				flags |= SEED_BIT;

			// ResourceState uses its own registered delta serializer via GenericDeltaWriter.
			if (writer.WriteDelta(prev.ResourceState, next.ResourceState, option))
				flags |= RESOURCE_BIT;

			if (writer.WriteDeltaInt32(prev.PackedFlagsAndSlot, next.PackedFlagsAndSlot, option))
				flags |= PACKED_FLAGS_BIT;

			if (WriteCooldownsDelta(writer, prev.Cooldowns, next.Cooldowns, option))
				flags |= COOLDOWN_BIT;

			if (WriteBuffsDelta(writer, prev.Buffs, next.Buffs, option))
				flags |= BUFF_BIT;

			if (WriteRngStateDelta(writer, prev, next, option))
				flags |= RNG_STATE_BIT;

			bool forceWrite = option != DeltaSerializerOption.Unset;
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
		/// Compares and writes the 4 xoshiro128** state words. All 4 change together,
		/// so a single bit controls whether the full 16 bytes are written.
		/// </summary>
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
		/// Compares and writes cooldown arrays using index delta compression.
		/// When the array length is unchanged, only changed entries are written
		/// (negative count signals index-delta mode). When the length differs or
		/// on a forced tick, the full array is written (positive count).
		/// </summary>
		private static bool WriteCooldownsDelta(
			Writer writer,
			CooldownReconcileEntry[] prev,
			CooldownReconcileEntry[] next,
			DeltaSerializerOption option)
		{
			// Both null — nothing to write even on a forced tick.
			if (prev == null && next == null)
				return false;

			bool forceWrite = option != DeltaSerializerOption.Unset;

			// Fast identity check: cached snapshots reuse the same array reference when unchanged.
			if (!forceWrite && ReferenceEquals(prev, next))
			{
				return false;
			}

			int prevCount = prev?.Length ?? 0;
			int nextCount = next?.Length ?? 0;

			// Same length and not forced — try index delta.
			// Two-pass scan: first counts changed entries, then writes them.
			// Safe because prev/next are immutable reconcile snapshots on a single thread.
			if (!forceWrite && prevCount == nextCount)
			{
				int changedCount = 0;
				for (int i = 0; i < nextCount; i++)
				{
					if (prev[i].AbilityID != next[i].AbilityID ||
						prev[i].StartTick != next[i].StartTick ||
						prev[i].DurationTicks != next[i].DurationTicks)
					{
						changedCount++;
					}
				}

				if (changedCount == 0)
					return false;

				// Negative count signals index-delta mode.
				writer.WriteInt32(-changedCount);
				for (int i = 0; i < nextCount; i++)
				{
					if (prev[i].AbilityID != next[i].AbilityID ||
						prev[i].StartTick != next[i].StartTick ||
						prev[i].DurationTicks != next[i].DurationTicks)
					{
						writer.WriteInt32(i);
						writer.WriteInt64(next[i].AbilityID);
						writer.WriteUInt32(next[i].StartTick);
						writer.WriteUInt32(next[i].DurationTicks);
					}
				}
				return true;
			}

			// Full write: length changed or forced.
			writer.WriteInt32(nextCount);
			for (int i = 0; i < nextCount; i++)
			{
				writer.WriteInt64(next[i].AbilityID);
				writer.WriteUInt32(next[i].StartTick);
				writer.WriteUInt32(next[i].DurationTicks);
			}
			return true;
		}

		/// <summary>
		/// Reads a cooldown array from the delta stream.
		/// Positive count = full array. Negative count = index-delta over prev.
		/// </summary>
		private static CooldownReconcileEntry[] ReadCooldowns(Reader reader, CooldownReconcileEntry[] prev)
		{
			const int maxEntries = 4096;

			int count = reader.ReadInt32();

			// Index-delta mode: -count changed entries follow.
			if (count < 0)
			{
				int changedCount = -count;
				if (changedCount > maxEntries)
				{
					Log.Warning("ReadCooldowns", $"Index-delta count {changedCount} exceeds limit {maxEntries}. Rejecting.");
					return null;
				}

				int prevLength = prev?.Length ?? 0;
				CooldownReconcileEntry[] entries = new CooldownReconcileEntry[prevLength];
				if (prevLength > 0)
					System.Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadInt32();
					var entry = new CooldownReconcileEntry
					{
						AbilityID = reader.ReadInt64(),
						StartTick = reader.ReadUInt32(),
						DurationTicks = reader.ReadUInt32(),
					};
					if (index >= 0 && index < prevLength)
						entries[index] = entry;
				}
				return entries;
			}

			// Full array mode.
			if (count == 0)
				return null;

			if (count > maxEntries)
			{
				Log.Warning("ReadCooldowns", $"Full-array count {count} exceeds limit {maxEntries}. Rejecting.");
				return null;
			}

			CooldownReconcileEntry[] result = new CooldownReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = new CooldownReconcileEntry
				{
					AbilityID = reader.ReadInt64(),
					StartTick = reader.ReadUInt32(),
					DurationTicks = reader.ReadUInt32(),
				};
			}
			return result;
		}

		/// <summary>
		/// Compares and writes buff arrays using index delta compression.
		/// When the array length is unchanged, only changed entries are written
		/// (negative count signals index-delta mode). When the length differs or
		/// on a forced tick, the full array is written (positive count).
		/// Tick-based fields (<see cref="BuffReconcileEntry.ExpiryTick"/>,
		/// <see cref="BuffReconcileEntry.NextTickTick"/>) are stable between structural
		/// changes, so the <c>ReferenceEquals</c> fast-path zero-bytes unchanged arrays.
		/// </summary>
		private static bool WriteBuffsDelta(
			Writer writer,
			BuffReconcileEntry[] prev,
			BuffReconcileEntry[] next,
			DeltaSerializerOption option)
		{
			// Both null — nothing to write even on a forced tick.
			if (prev == null && next == null)
				return false;

			bool forceWrite = option != DeltaSerializerOption.Unset;

			// Fast identity check: cached snapshots reuse the same array reference when unchanged.
			if (!forceWrite && ReferenceEquals(prev, next))
			{
				return false;
			}

			int prevCount = prev?.Length ?? 0;
			int nextCount = next?.Length ?? 0;

			// Same length and not forced — try index delta.
			// Two-pass scan: first counts changed entries, then writes them.
			// Safe because prev/next are immutable reconcile snapshots on a single thread.
			if (!forceWrite && prevCount == nextCount)
			{
				int changedCount = 0;
				for (int i = 0; i < nextCount; i++)
				{
					if (prev[i].TemplateID   != next[i].TemplateID   ||
						prev[i].ExpiryTick   != next[i].ExpiryTick   ||
						prev[i].NextTickTick != next[i].NextTickTick ||
						prev[i].Stacks       != next[i].Stacks       ||
						prev[i].TickCount    != next[i].TickCount)
					{
						changedCount++;
					}
				}

				if (changedCount == 0)
					return false;

				// Negative count signals index-delta mode.
				writer.WriteInt32(-changedCount);
				for (int i = 0; i < nextCount; i++)
				{
					if (prev[i].TemplateID   != next[i].TemplateID   ||
						prev[i].ExpiryTick   != next[i].ExpiryTick   ||
						prev[i].NextTickTick != next[i].NextTickTick ||
						prev[i].Stacks       != next[i].Stacks       ||
						prev[i].TickCount    != next[i].TickCount)
					{
						writer.WriteInt32(i);
						writer.WriteInt32(next[i].TemplateID);
						writer.WriteUInt32(next[i].ExpiryTick);
						writer.WriteUInt32(next[i].NextTickTick);
						writer.WriteInt32(next[i].Stacks);
						writer.WriteInt32(next[i].TickCount);
					}
				}
				return true;
			}

			// Full write: length changed or forced.
			writer.WriteInt32(nextCount);
			for (int i = 0; i < nextCount; i++)
			{
				writer.WriteInt32(next[i].TemplateID);
				writer.WriteUInt32(next[i].ExpiryTick);
				writer.WriteUInt32(next[i].NextTickTick);
				writer.WriteInt32(next[i].Stacks);
				writer.WriteInt32(next[i].TickCount);
			}
			return true;
		}

		/// <summary>
		/// Reads a buff array from the delta stream.
		/// Positive count = full array. Negative count = index-delta over prev.
		/// </summary>
		private static BuffReconcileEntry[] ReadBuffs(Reader reader, BuffReconcileEntry[] prev)
		{
			const int maxEntries = 4096;

			int count = reader.ReadInt32();

			// Index-delta mode: -count changed entries follow.
			if (count < 0)
			{
				int changedCount = -count;
				if (changedCount > maxEntries)
				{
					Log.Warning("ReadBuffs", $"Index-delta count {changedCount} exceeds limit {maxEntries}. Rejecting.");
					return null;
				}

				int prevLength = prev?.Length ?? 0;
				BuffReconcileEntry[] entries = new BuffReconcileEntry[prevLength];
				if (prevLength > 0)
					System.Array.Copy(prev, entries, prevLength);

				for (int i = 0; i < changedCount; i++)
				{
					int index = reader.ReadInt32();
					var entry = new BuffReconcileEntry
					{
						TemplateID   = reader.ReadInt32(),
						ExpiryTick   = reader.ReadUInt32(),
						NextTickTick = reader.ReadUInt32(),
						Stacks       = reader.ReadInt32(),
						TickCount    = reader.ReadInt32(),
					};
					if (index >= 0 && index < prevLength)
						entries[index] = entry;
				}
				return entries;
			}

			// Full array mode.
			if (count == 0)
				return null;

			if (count > maxEntries)
			{
				Log.Warning("ReadBuffs", $"Full-array count {count} exceeds limit {maxEntries}. Rejecting.");
				return null;
			}

			BuffReconcileEntry[] result = new BuffReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = new BuffReconcileEntry
				{
					TemplateID   = reader.ReadInt32(),
					ExpiryTick   = reader.ReadUInt32(),
					NextTickTick = reader.ReadUInt32(),
					Stacks       = reader.ReadInt32(),
					TickCount    = reader.ReadInt32(),
				};
			}
			return result;
		}

		/// <summary>
		/// Delta reader for <see cref="CharacterReconcileData"/>.
		/// Reads the bitmask and reconstructs only the changed fields.
		/// </summary>
		private static CharacterReconcileData ReadDelta(
			Reader reader,
			CharacterReconcileData prev)
		{
			byte flags = reader.ReadUInt8Unpacked();
			CharacterReconcileData result = prev;

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
				result.Cooldowns = ReadCooldowns(reader, prev.Cooldowns);

			if ((flags & BUFF_BIT) != 0)
				result.Buffs = ReadBuffs(reader, prev.Buffs);

			if ((flags & RNG_STATE_BIT) != 0)
			{
				result.RngS0 = reader.ReadUInt32();
				result.RngS1 = reader.ReadUInt32();
				result.RngS2 = reader.ReadUInt32();
				result.RngS3 = reader.ReadUInt32();
			}

			return result;
		}
	}
}