using FishNet.Broadcast;
using FishNet.CodeGenerating;
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
	/// Lifetime expiry is deterministic and needs no message, but collisions are resolved on each
	/// client against interpolated characters: a client can miss a hit the server landed and keep
	/// a ghost flying to the end of its lifetime. Sent reliably — it is one small message per
	/// collision-ended object, and a lost one is a ghost that flies on for the rest of its life.
	/// The container id is a pure function of (seed, spawn tick) on every peer, so the pair below
	/// names the same object everywhere.
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
	/// <b>Not a grant.</b> The receiving side files this in the controller's observer-only
	/// transient store, never in <c>KnownAbilities</c>, which gates activation and is populated
	/// exclusively from server-authoritative paths. Nothing a client learns from this message can
	/// let it cast anything.
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
	/// The list is filtered server side — templates flagged <c>HiddenFromOthers</c> never leave the
	/// server — so this is what observers are permitted to see rather than the character's real buff
	/// state. Remaining durations are sent in seconds because observers do not run the buff
	/// simulation and have no use for tick numbers.
	/// </para>
	/// <para>
	/// Sent only when the visible set actually changes, which is what keeps it off the per-tick
	/// budget: a character holding steady buffs sends nothing.
	/// </para>
	/// </remarks>
	public struct CharacterBuffsBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character these buffs belong to.</summary>
		public int CharacterObjectID;

		/// <summary>The buffs this character's observers are allowed to see.</summary>
		public ObservedBuffEntry[] Buffs;
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
