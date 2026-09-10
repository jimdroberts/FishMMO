using FishNet.Serializing;
using FishMMO.Logging;
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
	/// The mode and the two shape flags share one byte. Object ids and ticks travel through
	/// FishNet's packed encoding, which is genuinely variable width for them. The FULL-ENTROPY
	/// fields do not: the packed 32-bit form is a seven-bit-per-byte varint, so a value that uses
	/// the whole range costs FIVE bytes where unpacked costs four. The seed and the packed aim
	/// word are therefore written unpacked, each noted at the point it happens.
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
			/* Unpacked: the seed is a full-range 32-bit RNG value, so every one of its bits is
			 * routinely set and FishNet's signed-packed form would spend five bytes on it. Four,
			 * always, is the cheaper of the two — the same trade ObservedBuffEntry makes for a
			 * template id. Object ids and ticks below stay packed: those really are small. */
			writer.WriteInt32Unpacked(value.Seed);
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
				/* Unpacked, for the same reason the seed is. AimDirectionCompression packs a 16-bit
				 * yaw into the low half and a 16-bit pitch into the high half, so the top bits are set
				 * for any aim above the horizon — which is most of them. Packed would cost five bytes
				 * on every such cast to save one on the handful aimed at the ground. */
				writer.WriteUInt32Unpacked(value.PackedAimDirection);
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
			value.Seed = reader.ReadInt32Unpacked();
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
				value.PackedAimDirection = reader.ReadUInt32Unpacked();
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
		/// <para>
		/// Hand written for two things the generated array serializer cannot do: bound the count
		/// against a malformed message before an array is allocated for it, and treat a null list
		/// as an empty one rather than spending a sentinel on it.
		/// </para>
		/// <para>
		/// It is <b>not</b> written for the length prefix, and a remark here used to claim it was —
		/// that FishNet's generated array serializer "writes a four-byte length". It does not:
		/// <c>Writer.WriteArray</c> writes the count with <c>WriteSignedPackedWhole</c>, which is one
		/// byte for any count this message can carry. The single byte below matches that; the reasons
		/// above are what earn the hand-written form.
		/// </para>
		/// <para>
		/// The template id and every event id are unpacked. Both are
		/// <c>(typeName + assetName).GetDeterministicHashCode()</c> values that occupy the whole
		/// 32-bit range, so FishNet's signed-packed form spends five bytes where four suffice — the
		/// same trade <see cref="ObservedBuffEntry.WriteTo"/> makes.
		/// </para>
		/// </remarks>
		public static void WriteAbilityLearnedObserverBroadcast(this Writer writer, AbilityLearnedObserverBroadcast value)
		{
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.AbilityID);
			writer.WriteInt32Unpacked(value.TemplateID);

			int count = value.Events == null ? 0 : value.Events.Length;
			if (count > MAX_LEARNED_EVENTS)
			{
				Log.Warning("AbilityLearnedObserverBroadcast",
					$"Write event count {count} exceeds limit {MAX_LEARNED_EVENTS}. Truncating to preserve stream integrity.");
				count = MAX_LEARNED_EVENTS;
			}

			writer.WriteUInt8Unpacked((byte)count);
			for (int i = 0; i < count; ++i)
			{
				writer.WriteInt32Unpacked(value.Events[i]);
			}
		}

		/// <summary>Reads an <see cref="AbilityLearnedObserverBroadcast"/> written by the method above.</summary>
		/// <remarks>
		/// An over-large count is DISCARDED, not clamped. Clamping used to read
		/// <see cref="MAX_LEARNED_EVENTS"/> ids and leave the rest of the declared ones in the
		/// stream — and FishNet does not reposition the reader after a broadcast handler runs
		/// (<c>ClientManager.ParseBroadcast</c> uses the length prefix only to SKIP a key it has no
		/// handler for), so every byte left behind misaligned whatever shared the datagram. See
		/// <c>CharacterBuffsBroadcastSerializer.ReadCharacterBuffsBroadcast</c>, which spells the
		/// same trap out at length. The discard is returned with an unresolvable caster id so the
		/// handler drops it outright rather than filing an events-less ability.
		/// </remarks>
		public static AbilityLearnedObserverBroadcast ReadAbilityLearnedObserverBroadcast(this Reader reader)
		{
			AbilityLearnedObserverBroadcast value = new AbilityLearnedObserverBroadcast()
			{
				CasterObjectID = reader.ReadInt32(),
				AbilityID = reader.ReadInt64(),
				TemplateID = reader.ReadInt32Unpacked(),
				Events = System.Array.Empty<int>(),
			};

			int count = reader.ReadUInt8Unpacked();
			if (count > MAX_LEARNED_EVENTS)
			{
				/* Unreachable through the writer above, which caps the count at MAX_LEARNED_EVENTS
				 * before it writes it — so arriving here means the stream is already corrupt. Reading
				 * a clamped subset would compound that by leaving the remaining ids unread. */
				Log.Warning("AbilityLearnedObserverBroadcast",
					$"Read event count {count} exceeds limit {MAX_LEARNED_EVENTS}. Discarding this update.");
				return new AbilityLearnedObserverBroadcast()
				{
					CasterObjectID = -1,
					Events = System.Array.Empty<int>(),
				};
			}

			if (count > 0)
			{
				int[] events = new int[count];
				for (int i = 0; i < count; ++i)
				{
					events[i] = reader.ReadInt32Unpacked();
				}
				value.Events = events;
			}

			return value;
		}
	}

	/// <summary>Wire format for <see cref="CharacterCastBroadcast"/>.</summary>
	/// <remarks>
	/// <para>
	/// Hand written because a stop carries two fields the receiver never looks at.
	/// <c>ClientCastNameplateDisplay.OnCastBroadcast</c> branches on
	/// <see cref="CharacterCastBroadcast.Started"/> first and returns having read only the caster id
	/// and the reference id — so <see cref="CharacterCastBroadcast.ServerTick"/> and
	/// <see cref="CharacterCastBroadcast.IsConsumable"/> are dead weight on every cast end, and the
	/// generated serializer wrote all five fields unconditionally. That is four to five bytes per
	/// cast per observer on the reliable channel, paid by NPC auto-attacks at one per second each.
	/// </para>
	/// <para>
	/// <see cref="CharacterCastBroadcast.Started"/> therefore goes FIRST, as a header bit: it is the
	/// field that says which shape follows, in the style of
	/// <see cref="AbilityObserverBroadcastSerializers"/> and
	/// <c>AbilityObjectObserverBroadcastSerializers</c>. The consumable bit shares that byte rather
	/// than costing one of its own.
	/// </para>
	/// <para>
	/// <see cref="CharacterCastBroadcast.ReferenceID"/> travels packed and in ONE form on both
	/// shapes. It is an ability instance id — a database sequence value, and small — except on a
	/// consumable, where it is a full-range template hash; splitting the two would need the
	/// consumable bit on a stop, which is the byte this format exists to save, and the receiver
	/// compares a stop's reference against the start's, so the two must encode identically.
	/// </para>
	/// </remarks>
	public static class CharacterCastBroadcastSerializer
	{
		/// <summary>Set when this message starts an activation; clear when it ends one.</summary>
		private const byte FLAG_STARTED = 0x01;

		/// <summary>Set when a STARTING activation is an item rather than an ability.</summary>
		/// <remarks>
		/// Masked off on a stop, where the receiver does not read it — so the bit is a true
		/// statement about what follows rather than a field that happens to be ignored.
		/// </remarks>
		private const byte FLAG_CONSUMABLE = 0x02;

		/// <summary>Writes a <see cref="CharacterCastBroadcast"/> in its shape-dependent form.</summary>
		public static void WriteCharacterCastBroadcast(this Writer writer, CharacterCastBroadcast value)
		{
			byte header = 0;
			if (value.Started)
			{
				header |= FLAG_STARTED;
				if (value.IsConsumable)
				{
					header |= FLAG_CONSUMABLE;
				}
			}

			writer.WriteUInt8Unpacked(header);
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.ReferenceID);

			if (value.Started)
			{
				/* Packed: an absolute server tick is a small number for a long while, and the
				 * receiver only ever differences it against its own estimate. */
				writer.WriteUInt32(value.ServerTick);
			}
		}

		/// <summary>Reads a <see cref="CharacterCastBroadcast"/> written by the method above.</summary>
		/// <remarks>
		/// A stop comes back with <c>ServerTick</c> 0 and <c>IsConsumable</c> false, which is exactly
		/// what its handler expects: it reads neither.
		/// </remarks>
		public static CharacterCastBroadcast ReadCharacterCastBroadcast(this Reader reader)
		{
			byte header = reader.ReadUInt8Unpacked();

			CharacterCastBroadcast value = new CharacterCastBroadcast()
			{
				Started = (header & FLAG_STARTED) != 0,
				IsConsumable = (header & FLAG_CONSUMABLE) != 0,
				ServerTick = 0u,
			};

			value.CasterObjectID = reader.ReadInt32();
			value.ReferenceID = reader.ReadInt64();

			if (value.Started)
			{
				value.ServerTick = reader.ReadUInt32();
			}

			return value;
		}
	}
}
