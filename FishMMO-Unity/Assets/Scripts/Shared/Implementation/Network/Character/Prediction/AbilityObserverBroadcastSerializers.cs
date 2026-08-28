using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Hand written wire format for <see cref="AbilityActivatedBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A cast broadcast goes to every observer of the caster, and a channelled ability sends one per
	/// tick it is held, so its size is paid far more often than its field count suggests. The
	/// generated serializer wrote all eleven fields unconditionally — three world vectors, a
	/// quaternion and two full ticks — for every mode, about 65-73 bytes. Most of that is dead
	/// weight in any given mode.
	/// </para>
	/// <para>
	/// Two observations shape this format:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// A <see cref="AbilitySpawnTarget.Camera"/> spawn pose is a pure function of the aim origin, the
	/// aim direction and the ability's range — all of which the observer either receives or reads off
	/// the template. So Camera carries aim (12 + up to 5 bytes) and omits the pose; every other mode
	/// carries the pose (12 + 8 bytes) and omits the aim, because those poses come off transforms the
	/// observer holds interpolated and cannot reproduce.
	/// </item>
	/// <item>
	/// <see cref="AbilityActivatedBroadcast.SpawnTick"/> and
	/// <see cref="AbilityActivatedBroadcast.ServerTick"/> are close together in practice — identical
	/// for a server driven NPC, and a fraction of a second apart for a player, whose replicate tick
	/// runs a buffer ahead of the server's. The spawn tick therefore travels as a signed 16-bit
	/// offset, with a full width fallback for the case where the two domains are genuinely far apart
	/// (a client that has just resynchronised, or an unset tick).
	/// </item>
	/// </list>
	/// <para>
	/// The mode and the two shape flags share one byte. Everything else uses FishNet's packed
	/// encoding, which is already variable width for the ids.
	/// </para>
	/// <para>
	/// Discovered by FishNet's codegen through the <c>Write*</c>/<c>Read*</c> naming convention and
	/// applied across assemblies because the struct carries <c>[UseGlobalCustomSerializer]</c>.
	/// </para>
	/// </remarks>
	public static class AbilityObserverBroadcastSerializers
	{
		/// <summary>Low bits of the header byte holding the <see cref="AbilitySpawnTarget"/>.</summary>
		private const byte MODE_MASK = 0x0F;

		/// <summary>Set when the spawn tick travelled as a 16-bit offset from the server tick.</summary>
		private const byte FLAG_TICK_OFFSET = 0x10;

		/// <summary>Set when a target object id follows.</summary>
		private const byte FLAG_HAS_TARGET = 0x20;

		/// <summary>
		/// True when this mode's spawn pose can be re-derived by the observer from the aim, so the
		/// pose is omitted and the aim is sent instead.
		/// </summary>
		/// <remarks>
		/// Only <see cref="AbilitySpawnTarget.Camera"/> qualifies.
		/// <see cref="AbilitySpawnTarget.SpawnerWithCameraRotation"/> looks similar but anchors its
		/// position to the caster's spawner transform, which the observer holds behind the server.
		/// </remarks>
		private static bool DerivesPoseFromAim(byte spawnMode)
		{
			return spawnMode == (byte)AbilitySpawnTarget.Camera;
		}

		/// <summary>Writes an <see cref="AbilityActivatedBroadcast"/> in its mode-shaped form.</summary>
		public static void WriteAbilityActivatedBroadcast(this Writer writer, AbilityActivatedBroadcast value)
		{
			byte header = (byte)(value.SpawnMode & MODE_MASK);

			/* Signed difference in tick space. Computed in long space so a server tick that has
			 * wrapped past uint.MaxValue while the spawn tick has not (or the reverse) produces a
			 * huge magnitude and falls back to the full width path, rather than silently encoding a
			 * wrong small offset. */
			long tickDelta = (long)value.ServerTick - (long)value.SpawnTick;
			bool tickFits = tickDelta >= short.MinValue && tickDelta <= short.MaxValue;
			if (tickFits)
			{
				header |= FLAG_TICK_OFFSET;
			}

			bool hasTarget = value.TargetObjectID >= 0;
			if (hasTarget)
			{
				header |= FLAG_HAS_TARGET;
			}

			writer.WriteUInt8Unpacked(header);
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.AbilityID);
			writer.WriteInt32(value.Seed);
			writer.WriteUInt32(value.ServerTick);

			if (tickFits)
			{
				writer.WriteInt16((short)tickDelta);
			}
			else
			{
				writer.WriteUInt32(value.SpawnTick);
			}

			if (hasTarget)
			{
				writer.WriteInt32(value.TargetObjectID);
			}

			if (DerivesPoseFromAim(value.SpawnMode))
			{
				writer.WriteVector3(value.AimOrigin);
				writer.WriteUInt32(value.PackedAimDirection);
			}
			else
			{
				writer.WriteVector3(value.SpawnPosition);
				/* 64-bit packing, not 32-bit.
				 *
				 * Measured: FishNet's Quaternion32 is ten bits per axis, and a representative cast
				 * rotation (Euler 12, 200, 0) comes back 0.59 degrees off. On a projectile travelling
				 * 50 m that is better than half a metre of visible divergence between where an
				 * observer watches the shot go and where the server actually sent it. Quaternion64
				 * spends four more bytes for twenty-one bits per axis, which puts the error far below
				 * anything a viewer can see. This is a per-cast message, not a per-tick one, so four
				 * bytes is the right thing to spend on it. */
				writer.WriteQuaternion64(value.SpawnRotation);
			}
		}

		/// <summary>Reads an <see cref="AbilityActivatedBroadcast"/> written by the method above.</summary>
		/// <remarks>
		/// Fields that a given mode does not carry come back at their defaults — <c>Vector3.zero</c>,
		/// <c>Quaternion.identity</c>, and <c>-1</c> for an absent target — which is exactly what the
		/// receiving side expects: the handler branches on <see cref="AbilityActivatedBroadcast.SpawnMode"/>
		/// and only reads the fields that mode carries.
		/// </remarks>
		public static AbilityActivatedBroadcast ReadAbilityActivatedBroadcast(this Reader reader)
		{
			byte header = reader.ReadUInt8Unpacked();
			byte spawnMode = (byte)(header & MODE_MASK);

			AbilityActivatedBroadcast value = new AbilityActivatedBroadcast()
			{
				SpawnMode = spawnMode,
				TargetObjectID = -1,
				SpawnRotation = Quaternion.identity,
			};

			value.CasterObjectID = reader.ReadInt32();
			value.AbilityID = reader.ReadInt64();
			value.Seed = reader.ReadInt32();
			value.ServerTick = reader.ReadUInt32();

			if ((header & FLAG_TICK_OFFSET) != 0)
			{
				short tickDelta = reader.ReadInt16();
				value.SpawnTick = unchecked((uint)((long)value.ServerTick - tickDelta));
			}
			else
			{
				value.SpawnTick = reader.ReadUInt32();
			}

			if ((header & FLAG_HAS_TARGET) != 0)
			{
				value.TargetObjectID = reader.ReadInt32();
			}

			if (DerivesPoseFromAim(spawnMode))
			{
				value.AimOrigin = reader.ReadVector3();
				value.PackedAimDirection = reader.ReadUInt32();
			}
			else
			{
				value.SpawnPosition = reader.ReadVector3();
				value.SpawnRotation = reader.ReadQuaternion64();
			}

			return value;
		}

		/// <summary>
		/// Hard cap on the crafted event ids carried by one
		/// <see cref="AbilityLearnedObserverBroadcast"/>.
		/// </summary>
		/// <remarks>
		/// An ability's event count is bounded by its template's <c>AdditionalEventSlots</c> plus
		/// the events baked into the template, which is a handful. The cap exists so the count
		/// fits one byte and so a malformed or hostile message cannot make the reader allocate an
		/// arbitrarily large array before the stream runs out.
		/// </remarks>
		public const int MAX_LEARNED_EVENTS = 64;

		/// <summary>Writes an <see cref="AbilityLearnedObserverBroadcast"/>.</summary>
		/// <remarks>
		/// Hand written for the event-id count: FishNet's generated array serializer writes a
		/// four-byte length and a null sentinel, where this message's count never exceeds
		/// <see cref="MAX_LEARNED_EVENTS"/> and a null list is simply an empty one.
		/// </remarks>
		public static void WriteAbilityLearnedObserverBroadcast(this Writer writer, AbilityLearnedObserverBroadcast value)
		{
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.AbilityID);
			writer.WriteInt32(value.TemplateID);

			int count = value.Events == null ? 0 : value.Events.Length;
			if (count > MAX_LEARNED_EVENTS)
			{
				count = MAX_LEARNED_EVENTS;
			}

			writer.WriteUInt8Unpacked((byte)count);
			for (int i = 0; i < count; ++i)
			{
				writer.WriteInt32(value.Events[i]);
			}
		}

		/// <summary>Reads an <see cref="AbilityLearnedObserverBroadcast"/> written by the method above.</summary>
		public static AbilityLearnedObserverBroadcast ReadAbilityLearnedObserverBroadcast(this Reader reader)
		{
			AbilityLearnedObserverBroadcast value = new AbilityLearnedObserverBroadcast()
			{
				CasterObjectID = reader.ReadInt32(),
				AbilityID = reader.ReadInt64(),
				TemplateID = reader.ReadInt32(),
			};

			int count = reader.ReadUInt8Unpacked();
			if (count > MAX_LEARNED_EVENTS)
			{
				count = MAX_LEARNED_EVENTS;
			}

			int[] events = count > 0 ? new int[count] : System.Array.Empty<int>();
			for (int i = 0; i < count; ++i)
			{
				events[i] = reader.ReadInt32();
			}
			value.Events = events;

			return value;
		}
	}
}
