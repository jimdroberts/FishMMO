using FishNet.Broadcast;
using FishNet.CodeGenerating;
using FishNet.Serializing;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Carries a character's resources to everyone observing it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Observers learn a peer's health from the reconcile stream, and that stream reaches them only
	/// while state forwarding is on. Once forwarding is disabled — which is what makes 100-200
	/// players per scene affordable — a peer's <c>CharacterAttributeController</c> would never be
	/// updated on anyone else's client, and <c>UITKTarget</c> reads exactly that to draw a target's
	/// health bar. This is the replacement path.
	/// </para>
	/// <para>
	/// Rate limited and change gated by the sender, so an idle character at full health costs
	/// nothing and a fight costs a handful of updates a second rather than thirty.
	/// </para>
	/// </remarks>
	public struct CharacterResourcesBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character these resources belong to.</summary>
		/// <remarks>
		/// Required because a broadcast is not addressed to a NetworkBehaviour the way an RPC is —
		/// the handler is registered once per client and has to be told which character it is about.
		/// </remarks>
		public int CharacterObjectID;

		/// <summary>Current health, in whole units.</summary>
		/// <remarks>
		/// Integers rather than floats, for all three current values. An observer renders a bar
		/// at whole-unit precision — the sender's change gate already compares at whole units, so
		/// nothing finer was ever visible — and FishNet packs an int to one or two bytes where a
		/// float is always four.
		/// </remarks>
		public int Health;
		/// <summary>Maximum health.</summary>
		public int MaxHealth;
		/// <summary>Current mana, in whole units.</summary>
		public int Mana;
		/// <summary>Maximum mana.</summary>
		public int MaxMana;
		/// <summary>Current stamina, in whole units.</summary>
		public int Stamina;
		/// <summary>Maximum stamina.</summary>
		public int MaxStamina;
	}

	/// <summary>
	/// Tells observers that a character activated an ability, with everything needed to reproduce it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The ability simulation is deterministic — <c>AbilityObject</c> is a plain MonoBehaviour driven
	/// by the tick delta from a seeded RNG — so an observer given the same spawn tuple produces the
	/// same object the server did, and it costs nothing further on the wire for its whole lifetime.
	/// State forwarding is off for every character, so this message (and the spawn payload, for a
	/// late joiner) is the only way an observer ever learns about a cast.
	/// </para>
	/// <para>
	/// <b>Server authored.</b> The client sends intent through its replicate input; the server
	/// validates it, resolves the target itself, and broadcasts what actually happened. Nothing here
	/// is taken from the client on trust — in particular <see cref="TargetObjectID"/> is the server's
	/// own resolution, never a victim the client named.
	/// </para>
	/// <para>
	/// <b>Wire format</b> is hand written (<see cref="AbilityObserverBroadcastSerializers"/>) and
	/// shaped by <see cref="SpawnMode"/>: a Camera spawn carries the aim origin and the packed aim
	/// direction and the observer re-derives the pose with the server's own formula; every other
	/// mode carries the pose itself and no aim at all. The spawn tick travels as a 16-bit offset
	/// from <see cref="ServerTick"/> with a full-width fallback. Sent <b>reliably</b>: a cast is a
	/// rare, event-driven message and a lost one used to be a projectile an observer never saw.
	/// The only unreliable use is the second and later per-tick spawns of a channelled ability,
	/// where the first spawn of the channel is reliable and the rest are one visual each.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct AbilityActivatedBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the casting character.</summary>
		public int CasterObjectID;

		/// <summary>Ability that was activated.</summary>
		public long AbilityID;

		/// <summary>Deterministic RNG seed the ability object was spawned with.</summary>
		/// <remarks>
		/// The whole reason an observer can reproduce the cast rather than be told about it every
		/// tick. Must match what the server used or the two simulations diverge immediately. It is
		/// also half of the container id (<c>AbilityContainerAllocator</c>), which is why the
		/// destroy message below can name an object by id alone.
		/// </remarks>
		public int Seed;

		/// <summary>Replicate tick the ability was spawned on.</summary>
		public uint SpawnTick;

		/// <summary>Server <c>TimeManager.LocalTick</c> at the moment of the spawn.</summary>
		/// <remarks>
		/// <see cref="SpawnTick"/> is in the OWNER's replicate-tick domain, which an observer has no
		/// way to map. This one is in the server's, so an observer can compare it against its
		/// estimate of the current server tick and fast-forward the object by the transit delay —
		/// otherwise every observed projectile starts one network delay behind the server's and
		/// outlives it by the same amount.
		/// </remarks>
		public uint ServerTick;

		/// <summary>The template's <see cref="AbilitySpawnTarget"/>, which decides what else is carried.</summary>
		public byte SpawnMode;

		/// <summary>
		/// NetworkObject id the server resolved as the target, or -1 when the ability has none.
		/// </summary>
		public int TargetObjectID;

		/// <summary>World-space point the ability was aimed from. <b>Camera mode only.</b></summary>
		/// <remarks>
		/// Sent rather than derived. An observer could compute the caster's eye position, but it
		/// holds that caster interpolated — several hundred milliseconds behind — so deriving it
		/// would place the ability where the caster used to be. A Camera spawn's pose is a pure
		/// function of this and the aim direction, so for that mode the pose itself is omitted.
		/// </remarks>
		public Vector3 AimOrigin;

		/// <summary>Aim direction, packed by <see cref="AimDirectionCompression"/>. <b>Camera mode only.</b></summary>
		public uint PackedAimDirection;

		/// <summary>World position the server spawned the object at. <b>Every mode except Camera.</b></summary>
		/// <remarks>
		/// PointBlank, Forward and Spawner poses come off the caster's motor and spawner transforms,
		/// which an observer holds several hundred milliseconds behind, and a Target pose is the
		/// server's raycast hit, which an observer cannot reproduce against interpolated colliders.
		/// Since the trajectory is a closed form from the spawn pose, a locally resolved pose would
		/// keep the observer's object on a parallel, offset line for its whole life.
		/// </remarks>
		public Vector3 SpawnPosition;

		/// <summary>World rotation the server spawned the object with. <b>Every mode except Camera.</b></summary>
		/// <remarks>
		/// Travels through FishNet's 64-bit quaternion packing. The 32-bit form was measured at 0.59
		/// degrees of error on a representative cast rotation — half a metre of visible drift on a
		/// 50 m projectile — which is more than a viewer should see even though hits are resolved on
		/// the server and only the visual is at stake. The owner never reads this: it predicts with
		/// its own exact pose.
		/// </remarks>
		public Quaternion SpawnRotation;
	}

	/// <summary>
	/// Tells observers that an ability object ended on the server through a collision.
	/// </summary>
	/// <remarks>
	/// Lifetime expiry is deterministic and needs no message; a collision is not. Only the server
	/// and the caster's own client resolve hits — see <c>AbilityObject.ResolvesHitsLocally</c> — so
	/// for a third-party observer this is the ONLY thing that ends a collided object, and for the
	/// caster it corrects a predicted miss. Sent reliably: it is one small message per
	/// collision-ended object, and a lost one is a ghost that flies on for the rest of its life.
	/// The container id is a pure function of (seed, spawn tick) on every peer, so the pair below
	/// names the same object everywhere. Paired with <see cref="AbilityObjectHitBroadcast"/>, which
	/// carries the impact itself — this message says the object ended, not what it struck.
	/// </remarks>
	public struct AbilityObjectDestroyedBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the casting character.</summary>
		public int CasterObjectID;

		/// <summary>Ability the object belonged to.</summary>
		public long AbilityID;

		/// <summary>Deterministic container id the object lived in (identical on every peer).</summary>
		public int ContainerID;

		/// <summary>Object id within the container (identical on every peer).</summary>
		public int ObjectID;
	}

	/// <summary>
	/// Tells observers which body an ability object hit, so they can draw the impact the server
	/// resolved instead of guessing at one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why an observer is told rather than left to work it out.</b> An ability object's
	/// trajectory is deterministic and every peer agrees on it; its HIT SET is not. The server
	/// resolves hits inside a rewind to the CASTER'S view, and a third-party observer holds every
	/// character interpolated against its own latency — so an observer running the same query
	/// answered a question nobody asked. It could miss a hit the server landed (corrected, by
	/// <see cref="AbilityObjectDestroyedBroadcast"/>) and it could resolve one the server did not,
	/// which nothing corrected: its copy ended early, played its impact effect where nothing had
	/// happened, and — with a fork — carried on down a heading the server never took.
	/// </para>
	/// <para>
	/// <b>What it costs, and why that is less than it looks.</b> An observer's copy is deliberately
	/// run <c>SpectatorInterpolationTicks</c> behind the server's, so it stays consistent with the
	/// interpolated peers it is drawn against — see <c>ComputeObserverFastForwardTicks</c>. That is
	/// 66&#160;ms at the shipped tick rate, and it is a head start this message already holds: for
	/// an observer inside roughly 133&#160;ms round trip the authoritative answer arrives BEFORE
	/// the local guess would have fired. Only observers past that see the impact late, and by
	/// less than their one-way latency rather than by a round trip.
	/// </para>
	/// <para>
	/// <b>Sent to every observer of the caster, the owner included</b>, exactly like
	/// <see cref="AbilityObjectDestroyedBroadcast"/>. The owner predicts its own hits and normally
	/// has this one already, in which case the receiver's per-object hit set makes the message a
	/// no-op — but an owner that MISPREDICTED A MISS had no correction at all before this, and its
	/// impact effect simply never played.
	/// </para>
	/// <para>
	/// Reliable, and for the same reason its sibling is: there is no repeat behind it. A lost hit
	/// message is an impact nobody outside the server ever sees.
	/// </para>
	/// </remarks>
	public struct AbilityObjectHitBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the casting character.</summary>
		public int CasterObjectID;

		/// <summary>Ability the object belonged to.</summary>
		public long AbilityID;

		/// <summary>Deterministic container id the object lived in (identical on every peer).</summary>
		public int ContainerID;

		/// <summary>Object id within the container (identical on every peer).</summary>
		public int ObjectID;

		/// <summary>
		/// NetworkObject id of the character that was hit, or 0 when the hit resolved to scenery.
		/// </summary>
		/// <remarks>
		/// Zero is a real outcome rather than a failure: a projectile is free to end on a wall, and
		/// the OnHit events still run for it with no target character — which is what an authored
		/// impact decal or sound needs.
		/// </remarks>
		public int VictimObjectID;

		/// <summary>World point of impact, measured on the server inside the rewind scope.</summary>
		public Vector3 Point;

		/// <summary>Surface normal at <see cref="Point"/>.</summary>
		public Vector3 Normal;

		/// <summary>
		/// True when the victim TURNED THIS OBJECT AWAY rather than being struck by it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A deflect is a rejected hit: the server ran no OnHit events, spent no hit count and dealt
		/// no damage, but the projectile changed direction — and a trajectory change is the one
		/// thing an observer cannot reproduce on its own, because the buff that caused it is the
		/// DEFENDER's and the observer never resolves hits. One bit is all it takes, because the new
		/// heading is a pure function of the incoming one and <see cref="Normal"/>, which this
		/// message already carries. See <c>DeflectBuffTemplate.ResolveDeflectedHeading</c>.
		/// </para>
		/// <para>
		/// It is also what lets a receiver act on a hit whose VICTIM it cannot resolve. A character
		/// outside this client's streaming budget is not spawned here, so the hit itself must be
		/// dropped — running the OnHit events target-less would fire impact effects for a body that
		/// is really there — but the redirect must not be, or the observer's copy flies on down a
		/// heading the server never took until the destroy message catches up with it.
		/// </para>
		/// </remarks>
		public bool Deflected;

		/// <summary>
		/// The heading the object left on after a deflection, packed by
		/// <see cref="AimDirectionCompression"/>. Meaningless unless <see cref="Deflected"/> is set.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>An ABSOLUTE heading, not an instruction to mirror.</b> A receiver that recomputed the
		/// reflection from <see cref="Normal"/> would be correct exactly once: reflecting twice about
		/// the same normal returns the original vector, so the caster's own client — which predicts
		/// its deflections and then receives this message like everybody else — would turn the
		/// object straight back at the defender it had just been turned away from. Applying an
		/// absolute heading is idempotent, which is what makes the message safe to send to the peer
		/// that already worked it out.
		/// </para>
		/// <para>
		/// Four bytes, on a message sent once per body per object, and it also removes the
		/// receiver's need to know anything about WHY the object turned — a future deflection that
		/// is not a simple mirror needs no wire change.
		/// </para>
		/// </remarks>
		public uint PackedDeflectHeading;
	}

	/// <summary>
	/// Tells observers that a character learned an ability, so they can draw its casts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An observer's knowledge of a peer's abilities arrives in the spawn payload and nowhere
	/// else. An ability learned <i>after</i> an observer started observing was therefore invisible
	/// to it forever: <c>OnAbilityActivatedBroadcast</c> could not resolve the ability id, and
	/// every cast of that ability was dropped without drawing anything. The activation message
	/// deliberately does not carry the template — casts are frequent and learns are rare, so the
	/// bytes belong here, once, rather than on every cast.
	/// </para>
	/// <para>
	/// <b>Not a grant.</b> The receiving side files this straight into <c>KnownAbilities</c>, the
	/// same container the owner uses — a client is required to hold an observed character's real
	/// state, because Inspect and faction evaluation read it and not just the renderer. What stops
	/// it being a grant is <c>AbilityController.RegisterObservedAbility</c>'s owner check: the
	/// message only ever describes somebody else, so it refuses outright on our own character, and
	/// the dictionary an activation is gated on can never be written by it.
	/// </para>
	/// <para>
	/// This used to be enforced by filing the ability in a separate observer-only dictionary
	/// instead. It is not any more, and two comments went on claiming it was — long enough to be
	/// worth spelling out here: <b>the boundary is the owner check, not a container split.</b>
	/// Removing that check because "the parallel store is what protects it" reopens the hole.
	/// </para>
	/// <para>
	/// The event ids travel because they are what makes the reproduction move: an ability's
	/// OnTick events carry the trajectory, and an ability rebuilt without them would spawn a
	/// projectile that sits still. Sent reliably to observers except the owner, which has its own
	/// <c>AbilityAddBroadcast</c>.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct AbilityLearnedObserverBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character that learned the ability.</summary>
		public int CasterObjectID;

		/// <summary>The ability instance id, matching <c>AbilityActivatedBroadcast.AbilityID</c>.</summary>
		public long AbilityID;

		/// <summary><see cref="AbilityTemplate"/> id the ability was built from.</summary>
		public int TemplateID;

		/// <summary>Crafted event template ids attached to the ability. May be null or empty.</summary>
		public int[] Events;
	}

	/// <summary>
	/// Carries a character's publicly visible buffs to everyone observing it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The list is assembled server side — no buff is hidden from other players, so it is the
	/// server — so this is what observers are permitted to see rather than the character's real buff
	/// state. Remaining durations are sent in seconds because observers do not run the buff
	/// simulation and have no use for tick numbers.
	/// </para>
	/// <para>
	/// Sent only when the visible set actually changes, which is what keeps it off the per-tick
	/// budget: a character holding steady buffs sends nothing.
	/// </para>
	/// </remarks>
	/// <remarks>
	/// <para>
	/// <b>Delta by default.</b> A structural change carries only the buffs that were added or whose
	/// stack count moved, plus the template ids that left — one buff expiring costs one id, not the
	/// character's whole strip. <see cref="IsFullSet"/> marks the messages that are authoritative
	/// for the entire strip: the first push, a late observer's replay, and the periodic timing
	/// resync. See <c>BuffController.PushObservedBuffs</c>.
	/// </para>
	/// <para>
	/// <b>Why not FishNet's difference-encoded deltas.</b> Each entry carries its template id and
	/// its absolute values, never an index into the receiver's previous array or a difference from
	/// it. The observer set changes continuously as players move, and the sender keeps ONE baseline
	/// per character rather than one per observer — so a difference encoded against "what I last
	/// sent to anyone" is undecodable to whoever joined after that send. Absolute entries are
	/// applicable no matter what state the receiver was in, which is what makes a single serialized
	/// message safe to fan out to every observer.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct CharacterBuffsBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character these buffs belong to.</summary>
		public int CharacterObjectID;

		/// <summary>
		/// True when <see cref="Buffs"/> is the character's entire visible strip rather than the
		/// entries that changed.
		/// </summary>
		/// <remarks>
		/// A receiver REPLACES its strip on a full set and MERGES on a delta. Unlike
		/// <c>CharacterAttributesBroadcast</c> — whose sheet is fixed at spawn, so an omission means
		/// "unchanged" — a buff strip gains and loses members constantly, which is why
		/// <see cref="Removed"/> has to exist alongside this flag rather than being implied by
		/// absence.
		/// </remarks>
		public bool IsFullSet;

		/// <summary>The buffs that changed, or the whole visible strip when <see cref="IsFullSet"/>.</summary>
		public ObservedBuffEntry[] Buffs;

		/// <summary>
		/// Template ids no longer visible on this character. Always empty when
		/// <see cref="IsFullSet"/>, which states the whole strip on its own.
		/// </summary>
		public int[] Removed;
	}

	/// <summary>Wire format for <see cref="CharacterBuffsBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for the same reason <c>CharacterAttributesBroadcast</c> is: the generated array
	/// serializer spends four bytes on each length and a sentinel on each null, where both counts
	/// here are bounded by a character's buff strip and a null array is simply an empty one. The
	/// caps also stop a malformed message allocating an arbitrarily large array before the stream
	/// runs out.
	/// </remarks>
	public static class CharacterBuffsBroadcastSerializer
	{
		/// <summary>Hard cap on entries, and separately on removals, in one message.</summary>
		/// <remarks>
		/// Far above any real character's visible strip; small enough that either count fits a
		/// <c>ushort</c>.
		/// </remarks>
		public const int MAX_BUFFS = 4096;

		/// <summary>Writes a <see cref="CharacterBuffsBroadcast"/>.</summary>
		public static void WriteCharacterBuffsBroadcast(this Writer writer, CharacterBuffsBroadcast value)
		{
			writer.WriteInt32(value.CharacterObjectID);
			writer.WriteBoolean(value.IsFullSet);

			int count = value.Buffs?.Length ?? 0;
			if (count > MAX_BUFFS)
			{
				Log.Warning("CharacterBuffsBroadcast",
					$"Write buff count {count} exceeds limit {MAX_BUFFS}. Truncating to preserve stream integrity.");
				count = MAX_BUFFS;
			}
			writer.WriteUInt16((ushort)count);
			for (int i = 0; i < count; ++i)
			{
				value.Buffs[i].WriteTo(writer);
			}

			/* A full set states the whole strip, so removals would be noise — and a receiver that
			 * replaces rather than merges would never read them. Not written at all rather than
			 * written as zero, because the reader knows the flag before it gets here. */
			if (value.IsFullSet)
			{
				return;
			}

			int removedCount = value.Removed?.Length ?? 0;
			if (removedCount > MAX_BUFFS)
			{
				Log.Warning("CharacterBuffsBroadcast",
					$"Write removed count {removedCount} exceeds limit {MAX_BUFFS}. Truncating to preserve stream integrity.");
				removedCount = MAX_BUFFS;
			}
			writer.WriteUInt16((ushort)removedCount);
			for (int i = 0; i < removedCount; ++i)
			{
				// Unpacked for the same reason ObservedBuffEntry writes its id unpacked.
				writer.WriteInt32Unpacked(value.Removed[i]);
			}
		}

		/// <summary>Reads a <see cref="CharacterBuffsBroadcast"/>.</summary>
		public static CharacterBuffsBroadcast ReadCharacterBuffsBroadcast(this Reader reader)
		{
			CharacterBuffsBroadcast value = new CharacterBuffsBroadcast()
			{
				CharacterObjectID = reader.ReadInt32(),
				IsFullSet = reader.ReadBoolean(),
				Buffs = System.Array.Empty<ObservedBuffEntry>(),
				Removed = System.Array.Empty<int>(),
			};

			int count = reader.ReadUInt16();
			if (count > MAX_BUFFS)
			{
				/* Discarded rather than partially applied. Returning a FULL empty set would tell the
				 * receiver to clear the strip, so the discard is reported as a delta and the strip
				 * simply stays as it was until the next push.
				 *
				 * Note what this does NOT buy: FishNet writes a length in front of every broadcast
				 * (Utility.GetPacketLength) but ClientManager.ParseBroadcast only uses it to SKIP a
				 * key it has no handler for — it does not reposition the reader after a handler
				 * runs. So a handler that reads the wrong number of bytes still misaligns whatever
				 * shares the datagram. The cap below is what keeps this handler from being that
				 * handler; it is not a frame to recover behind. */
				Log.Warning("CharacterBuffsBroadcast",
					$"Read buff count {count} exceeds limit {MAX_BUFFS}. Discarding this update.");
				value.IsFullSet = false;
				return value;
			}

			if (count > 0)
			{
				ObservedBuffEntry[] entries = new ObservedBuffEntry[count];
				for (int i = 0; i < count; ++i)
				{
					entries[i] = ObservedBuffEntry.ReadFrom(reader);
				}
				value.Buffs = entries;
			}

			if (value.IsFullSet)
			{
				return value;
			}

			int removedCount = reader.ReadUInt16();
			if (removedCount > MAX_BUFFS)
			{
				Log.Warning("CharacterBuffsBroadcast",
					$"Read removed count {removedCount} exceeds limit {MAX_BUFFS}. Discarding this update.");
				value.Buffs = System.Array.Empty<ObservedBuffEntry>();
				return value;
			}

			if (removedCount > 0)
			{
				int[] removed = new int[removedCount];
				for (int i = 0; i < removedCount; ++i)
				{
					removed[i] = reader.ReadInt32Unpacked();
				}
				value.Removed = removed;
			}

			return value;
		}
	}

	/// <summary>
	/// Tells observers a character entered or left combat.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>CharacterFlags.IsInCombat</c> was known only to the character's own client and to the
	/// server, so nothing on anybody else's screen could react to a peer being in a fight — a
	/// nameplate indicator had no state to read. This is the smallest message that closes that:
	/// one id and one bool, sent on the transition rather than continuously.
	/// </para>
	/// <para>
	/// Reliable, and deliberately not buffered — a client that arrives later reads the flag out of
	/// the spawn payload, exactly as it does for death.
	/// </para>
	/// </remarks>
	public struct CharacterCombatStateBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character whose combat state changed.</summary>
		public int CharacterObjectID;

		/// <summary>True on entering combat, false on leaving.</summary>
		public bool InCombat;
	}

	/// <summary>
	/// Tells observers a character died or was revived.
	/// </summary>
	/// <remarks>
	/// Deliberately not a buffered message. Clients arriving after a death are served by the spawn
	/// payload, which carries <c>CharacterFlags.IsDead</c> for players and NPCs alike; buffering
	/// this instead would tie a message's lifetime to a pooled NPC's slot rather than to the
	/// creature that died in it.
	/// </remarks>
	public struct CharacterDeathStateBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character whose death state changed.</summary>
		public int CharacterObjectID;

		/// <summary>True on death, false on revive.</summary>
		public bool Dead;
	}
}
