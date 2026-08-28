using FishNet.Broadcast;
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
	/// Today that tuple reaches observers only because the owner's entire input stream is relayed to
	/// them thirty times a second. This carries the same information as one message per cast.
	/// </para>
	/// <para>
	/// <b>Server authored.</b> The client sends intent through its replicate input; the server
	/// validates it, resolves the target itself, and broadcasts what actually happened. Nothing here
	/// is taken from the client on trust — in particular <see cref="TargetObjectID"/> is the server's
	/// own resolution, never a victim the client named.
	/// </para>
	/// </remarks>
	public struct AbilityActivatedBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the casting character.</summary>
		public int CasterObjectID;

		/// <summary>Ability that was activated.</summary>
		public long AbilityID;

		/// <summary>Deterministic RNG seed the ability object was spawned with.</summary>
		/// <remarks>
		/// The whole reason an observer can reproduce the cast rather than be told about it every
		/// tick. Must match what the server used or the two simulations diverge immediately.
		/// </remarks>
		public int Seed;

		/// <summary>Replicate tick the ability was spawned on.</summary>
		public uint SpawnTick;

		/// <summary>World-space point the ability was aimed from.</summary>
		/// <remarks>
		/// Sent rather than derived. An observer could compute the caster's eye position, but it
		/// holds that caster interpolated — several hundred milliseconds behind — so deriving it
		/// would place the ability where the caster used to be.
		/// </remarks>
		public Vector3 AimOrigin;

		/// <summary>Aim direction, packed as yaw and pitch by <see cref="AimDirectionCompression"/>.</summary>
		public uint PackedAimDirection;

		/// <summary>
		/// NetworkObject id the server resolved as the target, or -1 when the ability has none.
		/// </summary>
		public int TargetObjectID;

		/// <summary>World-space point the server's target raycast hit (or the ray end when nothing was hit).</summary>
		/// <remarks>
		/// <see cref="AbilitySpawnTarget.Target"/> spawns at this point. An observer cannot derive
		/// it — its own raycast runs against interpolated colliders — and the target's root
		/// position is not the same point.
		/// </remarks>
		public Vector3 HitPosition;

		/// <summary>World position the server spawned the object at.</summary>
		/// <remarks>
		/// Sent for the same reason as <see cref="AimOrigin"/>, and for every spawn target rather
		/// than just the camera one: PointBlank, Forward and Spawner poses come off the caster's
		/// motor and spawner transforms, which an observer holds several hundred milliseconds
		/// behind. Since the trajectory is a closed form from the spawn pose, a locally resolved
		/// pose would keep the observer's object on a parallel, offset line for its whole life.
		/// </remarks>
		public Vector3 SpawnPosition;

		/// <summary>World rotation the server spawned the object with. See <see cref="SpawnPosition"/>.</summary>
		public Quaternion SpawnRotation;

		/// <summary>Server <c>TimeManager.LocalTick</c> at the moment of the spawn.</summary>
		/// <remarks>
		/// <see cref="SpawnTick"/> is in the OWNER's replicate-tick domain, which an observer has no
		/// way to map. This one is in the server's, so an observer can compare it against its
		/// estimate of the current server tick and fast-forward the object by the transit delay —
		/// otherwise every observed projectile starts one network delay behind the server's and
		/// outlives it by the same amount.
		/// </remarks>
		public uint ServerTick;
	}

	/// <summary>
	/// Tells observers that an ability object ended on the server through a collision.
	/// </summary>
	/// <remarks>
	/// Lifetime expiry is deterministic and needs no message, but collisions are resolved on each
	/// client against interpolated characters: a client can miss a hit the server landed and keep
	/// a ghost flying to the end of its lifetime. Sent unreliably — a lost message costs one
	/// observer a ghost that still expires on its own.
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
	/// Tells observers which presentation mode a character is using.
	/// </summary>
	/// <remarks>
	/// Clients need this to enable or disable their own <c>NetworkTransform</c> to match the server,
	/// since a transform left running while state is forwarded would fight the simulated position.
	/// </remarks>
	public struct PredictionModeBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character whose mode changed.</summary>
		public int CharacterObjectID;

		/// <summary>The new mode, as a <c>PredictionMode</c> value.</summary>
		public byte Mode;
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
