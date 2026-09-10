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
	/// nothing and a fight costs a handful of updates a second rather than thirty. The gate is
	/// split: health and the maxima on one schedule, mana and stamina on a far coarser one, because
	/// no observer-facing readout draws a peer's mana or stamina at all. See
	/// <see cref="ObservedResourcePushScheduler"/>.
	/// </para>
	/// <para>
	/// <b>Field gated as well as change gated.</b> <see cref="Mask"/> names which of the six values
	/// this message actually carries; the rest are absent from the wire and the receiver leaves its
	/// own copy alone. The three maxima move on an equip, a buff or a level and at no other time, so
	/// before the mask existed roughly forty per cent of every push was a number the observer had
	/// held since the spawn payload. This is the same redundancy the reconcile path solved for the
	/// same struct in <c>CharacterAttributeResourceStateSerializer.WriteDelta</c>.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct CharacterResourcesBroadcast : IBroadcast
	{
		/// <summary>
		/// Monotonic per-character counter, incremented on every send.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The stream is unreliable, so it is also UNORDERED: two updates sent a few ticks apart
		/// can arrive the wrong way round, and the receiver applies whichever landed last. During a
		/// fight that means a health bar that jumps back up after a hit, and at the end of one it
		/// meant a corpse still showing health — the reorder is most likely exactly when updates
		/// are most frequent. The receiver drops anything not newer than what it already applied.
		/// </para>
		/// <para>
		/// Compared with wrapping arithmetic, so the counter may roll over freely. It is per
		/// character and reset with the rest of the observed state when a pooled object is reused.
		/// </para>
		/// </remarks>
		public ushort Sequence;

		/// <summary>NetworkObject id of the character these resources belong to.</summary>
		/// <remarks>
		/// Required because a broadcast is not addressed to a NetworkBehaviour the way an RPC is —
		/// the handler is registered once per client and has to be told which character it is about.
		/// </remarks>
		public int CharacterObjectID;

		/// <summary>
		/// Which of the six values below are present on the wire; see <see cref="CharacterResourcesMask"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A field whose bit is clear is absent from the message entirely and its value in this
		/// struct is meaningless — the receiver must leave its own copy untouched rather than
		/// applying a default. Zero is a legal mask (a confirmation of nothing is never sent, but a
		/// corrupt or future-shaped stream can produce one) and means "apply nothing".
		/// </para>
		/// <para>
		/// <b>Every present field is an ABSOLUTE value, never a difference.</b> That is the whole
		/// reason a mask is safe on an unreliable channel: the message is self-describing for what
		/// it carries, so a receiver that missed the previous push still applies these fields
		/// exactly right. What a loss can cost is an omitted field staying at its old value, and
		/// <see cref="ObservedResourcePushScheduler.Decision.Confirm"/> — which sets every bit and
		/// rides the reliable channel — is what bounds that.
		/// </para>
		/// </remarks>
		public byte Mask;

		/// <summary>Current health, in whole units. Present when <see cref="CharacterResourcesMask.Health"/> is set.</summary>
		/// <remarks>
		/// Integers rather than floats, for all three current values. An observer renders a bar
		/// at whole-unit precision — the sender's change gate already compares at whole units, so
		/// nothing finer was ever visible — and FishNet packs an int to one or two bytes where a
		/// float is always four.
		/// <para>
		/// The whole-unit form is also what makes the mask exact. The sender sets a bit when the
		/// ROUNDED value differs from the rounded value it last sent, which is precisely what the
		/// receiver is holding, so an omitted field can never drift.
		/// </para>
		/// </remarks>
		public int Health;
		/// <summary>Maximum health. Present when <see cref="CharacterResourcesMask.MaxHealth"/> is set.</summary>
		public int MaxHealth;
		/// <summary>Current mana, in whole units. Present when <see cref="CharacterResourcesMask.Mana"/> is set.</summary>
		public int Mana;
		/// <summary>Maximum mana. Present when <see cref="CharacterResourcesMask.MaxMana"/> is set.</summary>
		public int MaxMana;
		/// <summary>Current stamina, in whole units. Present when <see cref="CharacterResourcesMask.Stamina"/> is set.</summary>
		public int Stamina;
		/// <summary>Maximum stamina. Present when <see cref="CharacterResourcesMask.MaxStamina"/> is set.</summary>
		public int MaxStamina;
	}

	/// <summary>
	/// The bits of <see cref="CharacterResourcesBroadcast.Mask"/>, and the rules that produce one.
	/// </summary>
	/// <remarks>
	/// Pure and static so the mask rules can be asserted directly — the interesting cases are a
	/// maximum changing, a confirmation, and the first push of a character's life, none of which is
	/// reachable from a test that has to spawn one first.
	/// </remarks>
	public static class CharacterResourcesMask
	{
		/// <summary><see cref="CharacterResourcesBroadcast.Health"/> is present.</summary>
		public const byte Health = 1 << 0;
		/// <summary><see cref="CharacterResourcesBroadcast.MaxHealth"/> is present.</summary>
		public const byte MaxHealth = 1 << 1;
		/// <summary><see cref="CharacterResourcesBroadcast.Mana"/> is present.</summary>
		public const byte Mana = 1 << 2;
		/// <summary><see cref="CharacterResourcesBroadcast.MaxMana"/> is present.</summary>
		public const byte MaxMana = 1 << 3;
		/// <summary><see cref="CharacterResourcesBroadcast.Stamina"/> is present.</summary>
		public const byte Stamina = 1 << 4;
		/// <summary><see cref="CharacterResourcesBroadcast.MaxStamina"/> is present.</summary>
		public const byte MaxStamina = 1 << 5;

		/// <summary>The three maxima together.</summary>
		/// <remarks>
		/// They travel as a group. Only one of them changes at a time in practice, but they are the
		/// fields a loss strands for longest, and two spare bytes on the handful of pushes that
		/// follow an equip is a better trade than three separate sticky counters.
		/// </remarks>
		public const byte Maxima = MaxHealth | MaxMana | MaxStamina;

		/// <summary>Every field. What a confirmation sends, and what the first push of a life sends.</summary>
		public const byte All = Health | MaxHealth | Mana | MaxMana | Stamina | MaxStamina;

		/// <summary>True when <paramref name="mask"/> names at least one maximum.</summary>
		public static bool IncludesMaximum(byte mask)
		{
			return (mask & Maxima) != 0;
		}

		/// <summary>
		/// Which fields of <paramref name="next"/> differ from what was last sent.
		/// </summary>
		/// <remarks>
		/// Compared against the message the observers were last GIVEN rather than against the
		/// sender's float state, so the comparison is against exactly the numbers the receiver
		/// holds. <see cref="CharacterResourcesBroadcast.Sequence"/> and
		/// <see cref="CharacterResourcesBroadcast.CharacterObjectID"/> are not fields of the
		/// resource sheet and take no part in it.
		/// </remarks>
		/// <param name="previous">The last message sent for this character.</param>
		/// <param name="next">The message about to be sent.</param>
		/// <returns>The mask of changed fields; zero when nothing moved.</returns>
		public static byte ChangedFields(in CharacterResourcesBroadcast previous, in CharacterResourcesBroadcast next)
		{
			byte mask = 0;
			if (previous.Health != next.Health) mask |= Health;
			if (previous.MaxHealth != next.MaxHealth) mask |= MaxHealth;
			if (previous.Mana != next.Mana) mask |= Mana;
			if (previous.MaxMana != next.MaxMana) mask |= MaxMana;
			if (previous.Stamina != next.Stamina) mask |= Stamina;
			if (previous.MaxStamina != next.MaxStamina) mask |= MaxStamina;
			return mask;
		}
	}

	/// <summary>
	/// Wire format for <see cref="CharacterResourcesBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Sequence, object id and the mask byte, then only the fields the mask names. Hand written
	/// because FishNet's generated serializer writes every field of a struct unconditionally, and
	/// the three maxima are unchanged for almost the whole of a character's life.
	/// </para>
	/// <para>
	/// The mask is written BEFORE the values rather than backfilled, because the sender knows it
	/// before it writes anything — there is no equivalent here of the reconcile serializer's
	/// placeholder-and-insert, which exists only because that writer discovers its flags as it
	/// goes.
	/// </para>
	/// </remarks>
	public static class CharacterResourcesBroadcastSerializer
	{
		/// <summary>Writes a <see cref="CharacterResourcesBroadcast"/>. Discovered by FishNet's codegen by name.</summary>
		public static void WriteCharacterResourcesBroadcast(this Writer writer, CharacterResourcesBroadcast value)
		{
			writer.WriteUInt16(value.Sequence);
			writer.WriteInt32(value.CharacterObjectID);
			writer.WriteUInt8Unpacked(value.Mask);

			if ((value.Mask & CharacterResourcesMask.Health) != 0) writer.WriteInt32(value.Health);
			if ((value.Mask & CharacterResourcesMask.MaxHealth) != 0) writer.WriteInt32(value.MaxHealth);
			if ((value.Mask & CharacterResourcesMask.Mana) != 0) writer.WriteInt32(value.Mana);
			if ((value.Mask & CharacterResourcesMask.MaxMana) != 0) writer.WriteInt32(value.MaxMana);
			if ((value.Mask & CharacterResourcesMask.Stamina) != 0) writer.WriteInt32(value.Stamina);
			if ((value.Mask & CharacterResourcesMask.MaxStamina) != 0) writer.WriteInt32(value.MaxStamina);
		}

		/// <summary>Reads a <see cref="CharacterResourcesBroadcast"/> in the order <see cref="WriteCharacterResourcesBroadcast"/> wrote it.</summary>
		/// <remarks>
		/// Fields the mask does not name are left at zero in the returned struct. They must never be
		/// applied — see <see cref="CharacterResourcesBroadcast.Mask"/> — and zero is deliberately
		/// not a usable value so a receiver that forgets the mask fails loudly rather than quietly
		/// emptying somebody's bar.
		/// </remarks>
		public static CharacterResourcesBroadcast ReadCharacterResourcesBroadcast(this Reader reader)
		{
			CharacterResourcesBroadcast value = new CharacterResourcesBroadcast()
			{
				Sequence = reader.ReadUInt16(),
				CharacterObjectID = reader.ReadInt32(),
			};
			value.Mask = reader.ReadUInt8Unpacked();

			if ((value.Mask & CharacterResourcesMask.Health) != 0) value.Health = reader.ReadInt32();
			if ((value.Mask & CharacterResourcesMask.MaxHealth) != 0) value.MaxHealth = reader.ReadInt32();
			if ((value.Mask & CharacterResourcesMask.Mana) != 0) value.Mana = reader.ReadInt32();
			if ((value.Mask & CharacterResourcesMask.MaxMana) != 0) value.MaxMana = reader.ReadInt32();
			if ((value.Mask & CharacterResourcesMask.Stamina) != 0) value.Stamina = reader.ReadInt32();
			if ((value.Mask & CharacterResourcesMask.MaxStamina) != 0) value.MaxStamina = reader.ReadInt32();
			return value;
		}
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
	/// an observer cannot work out that a collided object ended, and for the caster the statement
	/// corrects a predicted miss. Sent reliably: it is one small message per collision-ended
	/// object, and a lost one is a ghost that flies on for the rest of its life. The container id
	/// is a pure function of (seed, spawn tick) on every peer, so the pair below names the same
	/// object everywhere.
	/// <para>
	/// <b>Only for an end no hit message precedes.</b> A collision that published a hit states the
	/// end inline through <see cref="AbilityObjectHitBroadcast.Ended"/>, because sending both was
	/// two reliable messages for one event. What is left for this message is every OTHER end a
	/// client cannot reproduce: a shield sweeping nearby projectiles
	/// (<c>ShieldInterceptAction</c>), which destroys with observer notification and publishes no
	/// hit, and a pierce chain whose hit message could not yet know the count would run out.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
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
	[UseGlobalCustomSerializer]
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

		/// <summary>Surface normal at <see cref="Point"/>. <b>Not carried on a deflection.</b></summary>
		/// <remarks>
		/// Travels through <see cref="AimDirectionCompression"/> — four bytes rather than twelve. It
		/// is a unit vector and the only thing a receiver does with it is orient an impact effect;
		/// the packer's 0.0055&#176; of yaw and 0.0027&#176; of pitch are three orders of magnitude
		/// finer than that needs. The one caller that reasoned FROM a normal rather than drawing with
		/// it — the deflection — does not read this field at all: the heading it produced is carried
		/// absolute in <see cref="PackedDeflectHeading"/>, resolved on the server before the message
		/// was built. See <see cref="AbilityObjectObserverBroadcastSerializers"/>.
		/// </remarks>
		public Vector3 Normal;

		/// <summary>
		/// True when an ECA ACTION resolved this impact rather than the object's own sweep.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A hitscan shot and an area blast run their own lag-compensated query and execute the
		/// OnHit chain directly, so they never touch the object's per-body hit set or its hit
		/// count. Their impacts were published by nobody at all until this flag existed, and a
		/// third party watching a gunfight saw beams appear and nothing be hit by them.
		/// </para>
		/// <para>
		/// The flag is what keeps the two kinds apart on the receiver. A swept hit is applied
		/// through the hit set, which is what makes the message a free no-op for an owner that
		/// predicted it; an action impact is applied straight to the OnHit chain, because an area
		/// effect wired to OnTick pulses the same victims repeatedly and the hit set would silence
		/// every pulse after the first. These are sent to observers EXCEPT the owner for the same
		/// reason — with no hit set to absorb it, the owner would play each impact twice.
		/// </para>
		/// </remarks>
		public bool DirectImpact;

		/// <summary>
		/// True when the victim TURNED THIS OBJECT AWAY rather than being struck by it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A deflect is a rejected hit: the server ran no OnHit events, spent no hit count and dealt
		/// no damage, but the projectile changed direction — and a trajectory change is the one
		/// thing an observer cannot reproduce on its own, because the buff that caused it is the
		/// DEFENDER's and the observer never resolves hits. One bit is all this flag needs to be:
		/// it says a deflection happened, and nothing more. The new heading does NOT ride on it —
		/// re-deriving the reflection from <see cref="Normal"/> is not safe, because reflecting
		/// twice about the same normal returns the original vector and the caster's own client
		/// receives this message after predicting the deflection itself. The absolute heading
		/// therefore travels separately in <see cref="PackedDeflectHeading"/>, whose remarks carry
		/// the full argument. See <c>DeflectBuffTemplate.ResolveDeflectedHeading</c>.
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
		/// True when the victim's raised shield ATE this object rather than being struck by it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The third outcome of a collision, and until it travelled the receiver could not tell it
		/// from the first. A block is decided by the peer that resolves hits
		/// (<c>AbilityObject.ResolvesHitsLocally</c>), so the server ran NO OnHit chain for it — it
		/// destroyed the object and played its destroy events and nothing else. The message it sent
		/// was an ordinary impact, and every observer, the blocker's own client included, ran the
		/// ability's entire OnHit chain off the back of it: impact effects for a shot that never
		/// landed, <c>AbilityForkHitAction</c> redirecting a copy off a hit the server rejected, and
		/// RNG draws the server never made (<c>AbilityObject</c>'s parity contract).
		/// </para>
		/// <para>
		/// It is the block counterpart of <see cref="Deflected"/>, and the two are exclusive: a
		/// deflect gives the projectile BACK on a new heading, a block consumes it. So this carries
		/// no heading — the object ended — and rides the ordinary impact shape, because the point
		/// and the normal are the shield's and the destroy chain draws the shield impact from them.
		/// </para>
		/// </remarks>
		public bool Blocked;

		/// <summary>
		/// True when this hit ENDED the object on the server, so no separate destroy follows.
		/// </summary>
		/// <remarks>
		/// <para>
		/// One collision used to produce two reliable messages describing it: this one, and an
		/// <see cref="AbilityObjectDestroyedBroadcast"/> naming the same caster, ability, container
		/// and object. Every shipped ability carries <c>HitCount 1</c>, so the second was in
		/// practice always implied by the first — ~15-20 bytes and a second reliable slot per
		/// collision-ended object per observer, for one bit of information.
		/// </para>
		/// <para>
		/// <b>Stated, never inferred.</b> An observer must not spend <c>HitCount</c> itself — its
		/// copy is told about the hits the server resolved and never about the ones the server
		/// declined, so counting locally ends the copy early. That argument is against INFERRING
		/// the end, not against the server saying so inline, which is what this is.
		/// </para>
		/// <para>
		/// Set only where the end is certain at the moment the message is WRITTEN, which is before
		/// the OnHit chain runs (see <c>AbilityObject.ApplyHit</c>: a destroyed object cannot
		/// publish, because destruction nulls its ability and caster). A chain that can PIERCE —
		/// one carrying an <c>AbilityHitCountAction</c> — may cancel the decrement this hit would
		/// otherwise apply, so for those the standalone destroy still travels. See
		/// <c>AbilityObject.HitEndsObject</c>.
		/// </para>
		/// <para>
		/// The standalone <see cref="AbilityObjectDestroyedBroadcast"/> remains for every end that
		/// no hit message precedes: a shield sweeping nearby projectiles
		/// (<c>ShieldInterceptAction</c>) destroys with observer notification and publishes no hit
		/// at all.
		/// </para>
		/// </remarks>
		public bool Ended;

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
	/// Tells observers that an ability object's OnHit chain turned it onto a new heading — the
	/// fork case, where the hit was accepted and its events redirected the survivor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The deflect redirect rides <see cref="AbilityObjectHitBroadcast"/> because a deflection IS
	/// the whole outcome; a fork's redirect happens INSIDE the OnHit events, after that message
	/// has already gone (it is deliberately sent before the events so a hit is reported even when
	/// an authored action destroys the object handling it). So the post-chain heading travels as
	/// its own message, on the same reliable ordered channel, and therefore always lands after
	/// the hit it belongs to.
	/// </para>
	/// <para>
	/// <b>An ABSOLUTE heading, for the same reason the deflect's is.</b> A peer that ran the fork
	/// itself — the owner predicting with its reconciled RNG, or an observer that resolved the hit
	/// locally — already holds this exact heading and applies the message as a no-op (the receiver
	/// compares before re-anchoring, because <c>Redirect</c> resets the trajectory leg). The peer
	/// this exists for is the one that could NOT resolve the victim: it dropped the hit, skipped
	/// the fork's RNG draw, and without this flew the old line until the destroy caught up —
	/// re-deriving the heading there was impossible by construction, its generator having missed
	/// the draw.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct AbilityObjectRedirectBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the casting character.</summary>
		public int CasterObjectID;

		/// <summary>Ability the object belongs to.</summary>
		public long AbilityID;

		/// <summary>Deterministic container id the object lives in (identical on every peer).</summary>
		public int ContainerID;

		/// <summary>Object id within the container (identical on every peer).</summary>
		public int ObjectID;

		/// <summary>The heading the object left on, packed by <see cref="AimDirectionCompression"/>.</summary>
		public uint PackedHeading;

		/// <summary>Server tick the turn happened on.</summary>
		/// <remarks>
		/// <c>AbilityObject.Redirect</c> resets the trajectory leg to zero elapsed ticks, and the
		/// closed-form pose reads that counter — so a receiver applying this message without
		/// advancing it restarts the new leg from the corner at the moment of ARRIVAL, and its
		/// copy trails the server's by the transit delay for the rest of the object's life. The
		/// receiver subtracts its own render lag the same way every other observer clock does; see
		/// <c>AbilityController.ComputeObserverFastForwardTicks</c>.
		/// </remarks>
		public uint ServerTick;
	}

	/// <summary>
	/// Hand written wire formats for the three per-object ability broadcasts:
	/// <see cref="AbilityObjectDestroyedBroadcast"/>, <see cref="AbilityObjectHitBroadcast"/> and
	/// <see cref="AbilityObjectRedirectBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These are per-hit and per-object messages paid once per observer, and the generated
	/// serializer wrote every field of every one of them packed and unconditionally. Two things
	/// were wrong with that.
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// <b>Packing a full-entropy word costs a byte rather than saving four.</b> FishNet's packed
	/// 32-bit form is a seven-bit-per-byte varint, so a value that uses the whole range takes FIVE
	/// bytes where the unpacked form takes four. <c>ContainerID</c> is
	/// <c>seed ^ (tick * 1000003)</c> (<c>AbilityContainerAllocator.ComputeContainerId</c>) and
	/// <see cref="AbilityObjectRedirectBroadcast.PackedHeading"/> is an
	/// <see cref="AimDirectionCompression"/> word; both are unpacked here. Object ids, the ability
	/// id and the absolute tick stay packed, because those genuinely are small.
	/// </item>
	/// <item>
	/// <b>A hit message has two shapes and was written as one.</b> A deflected hit is answered by
	/// <c>AbilityController.OnAbilityObjectHitBroadcast</c> reading the heading and returning, so
	/// the point, the normal, the victim and the direct-impact flag were written and never read —
	/// twenty-five bytes per deflected hit per observer. A header byte now names the shape, in the
	/// style of <see cref="AbilityObserverBroadcastSerializers.WriteAbilityActivatedBroadcast"/>.
	/// </item>
	/// </list>
	/// <para>
	/// Discovered by FishNet's codegen through the <c>Write*</c>/<c>Read*</c> naming convention and
	/// applied across assemblies because each struct carries <c>[UseGlobalCustomSerializer]</c>.
	/// </para>
	/// </remarks>
	public static class AbilityObjectObserverBroadcastSerializers
	{
		/// <summary>Set when the hit was a deflection: a heading follows and nothing else does.</summary>
		private const byte FLAG_DEFLECTED = 0x01;

		/// <summary>Set when an ECA action resolved the impact rather than the object's own sweep.</summary>
		private const byte FLAG_DIRECT_IMPACT = 0x02;

		/// <summary>Set when a victim object id follows the point and the normal.</summary>
		/// <remarks>
		/// Absent means <c>VictimObjectID == 0</c>, which is the documented "hit scenery" outcome —
		/// NOT the <c>-1</c> that <see cref="AbilityActivatedBroadcast.TargetObjectID"/> uses for an
		/// absent target. Reading back <c>-1</c> here would turn every wall impact into a victim the
		/// receiver cannot resolve, and the handler drops those.
		/// </remarks>
		private const byte FLAG_HAS_VICTIM = 0x04;

		/// <summary>Set when the victim's shield ate the object instead of being struck by it.</summary>
		/// <remarks>
		/// An impact-shape flag, like <see cref="FLAG_DIRECT_IMPACT"/>: a block carries the shield's
		/// point and normal so the destroy chain can draw the impact on the shield face, and carries
		/// no heading because nothing was given back.
		/// </remarks>
		private const byte FLAG_BLOCKED = 0x08;

		/// <summary>Set when this hit ended the object, so no destroy message follows it.</summary>
		private const byte FLAG_ENDED = 0x10;

		/// <summary>Writes an <see cref="AbilityObjectDestroyedBroadcast"/>.</summary>
		public static void WriteAbilityObjectDestroyedBroadcast(this Writer writer, AbilityObjectDestroyedBroadcast value)
		{
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.AbilityID);
			/* Unpacked: the container id is seed ^ (tick * 1000003), which is full entropy by
			 * construction, so the packed form would spend five bytes on it. */
			writer.WriteInt32Unpacked(value.ContainerID);
			writer.WriteInt32(value.ObjectID);
		}

		/// <summary>Reads an <see cref="AbilityObjectDestroyedBroadcast"/> written by the method above.</summary>
		public static AbilityObjectDestroyedBroadcast ReadAbilityObjectDestroyedBroadcast(this Reader reader)
		{
			return new AbilityObjectDestroyedBroadcast()
			{
				CasterObjectID = reader.ReadInt32(),
				AbilityID = reader.ReadInt64(),
				ContainerID = reader.ReadInt32Unpacked(),
				ObjectID = reader.ReadInt32(),
			};
		}

		/// <summary>Writes an <see cref="AbilityObjectHitBroadcast"/> in its shape-dependent form.</summary>
		/// <remarks>
		/// <para>
		/// Two shapes, named by the header byte. A <b>deflection</b> carries the heading and nothing
		/// else: the receiver applies it and returns before it looks at the point, the normal, the
		/// victim or any of the impact flags, so writing them was pure waste on the one hit shape
		/// that occurs in bursts. An ordinary <b>impact</b> carries the point, the normal and — only
		/// when it is not a scenery hit — the victim id, and carries no heading at all. The block
		/// and the end are bits in the header rather than fields, so neither costs the impact shape
		/// anything at all.
		/// </para>
		/// <para>
		/// <see cref="AbilityObjectHitBroadcast.PackedDeflectHeading"/> is <b>unpacked</b>, like the
		/// container id above it, and the shaping is what makes that the right call. The field used
		/// to be written on every hit and was <c>0</c> on almost all of them, which is the one case
		/// the packed form is good at — a single byte. Now it is written only when a deflection
		/// actually happened, so every value that reaches the wire is a full-entropy
		/// <c>AimDirectionCompression</c> word, which the packed form spends five bytes on.
		/// </para>
		/// </remarks>
		public static void WriteAbilityObjectHitBroadcast(this Writer writer, AbilityObjectHitBroadcast value)
		{
			byte header = 0;
			if (value.Deflected)
			{
				header |= FLAG_DEFLECTED;
			}

			/* Both of these describe the impact shape only. A deflection is not an impact — the
			 * server ran no OnHit events for it — and the two producers agree: BroadcastHitToObservers
			 * never sets DirectImpact, PublishActionHit never sets Deflected. Masking them off here
			 * rather than trusting that is what keeps the header a true statement of what follows. */
			bool hasVictim = !value.Deflected && value.VictimObjectID != 0;
			if (!value.Deflected && value.DirectImpact)
			{
				header |= FLAG_DIRECT_IMPACT;
			}
			if (hasVictim)
			{
				header |= FLAG_HAS_VICTIM;
			}
			/* The block and the end are impact-shape statements too. A deflection ends nothing and
			 * strikes nobody, so neither can accompany it; masking them off here rather than trusting
			 * the producers is what keeps the header a true statement of what follows. */
			if (!value.Deflected && value.Blocked)
			{
				header |= FLAG_BLOCKED;
			}
			if (!value.Deflected && value.Ended)
			{
				header |= FLAG_ENDED;
			}

			writer.WriteUInt8Unpacked(header);
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.AbilityID);
			// Full entropy; see WriteAbilityObjectDestroyedBroadcast.
			writer.WriteInt32Unpacked(value.ContainerID);
			writer.WriteInt32(value.ObjectID);

			if (value.Deflected)
			{
				writer.WriteUInt32Unpacked(value.PackedDeflectHeading);
				return;
			}

			writer.WriteVector3(value.Point);

			/* The normal through the aim packer: four bytes instead of twelve, on a unit vector whose
			 * only consumer orients an impact effect with it. AimDirectionCompression resolves to
			 * 0.0055 degrees of yaw and 0.0027 of pitch, which is far finer than an FX transform can
			 * show, and it is already the encoding this same struct uses for its deflect heading. A
			 * degenerate normal — which a cast that begins already overlapping can report as zero —
			 * comes back as the packer's deterministic forward rather than as zero; both are arbitrary
			 * for orienting an effect, and every peer now agrees on which arbitrary vector it is.
			 * Unpacked, because an encoded direction fills all 32 bits. */
			writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(value.Normal));

			if (hasVictim)
			{
				writer.WriteInt32(value.VictimObjectID);
			}
		}

		/// <summary>Reads an <see cref="AbilityObjectHitBroadcast"/> written by the method above.</summary>
		/// <remarks>
		/// Fields a shape does not carry come back at their defaults — <c>Vector3.zero</c> for the
		/// point and the normal, <c>0</c> for the deflect heading, <c>false</c> for the impact flags,
		/// and <c>0</c> for the victim, which is this message's "hit scenery" value rather than
		/// the <c>-1</c> an activation uses for an absent target. That is exactly what the receiving
		/// side expects: <c>AbilityController.OnAbilityObjectHitBroadcast</c> branches on
		/// <see cref="AbilityObjectHitBroadcast.Deflected"/> first and only reads the fields the
		/// remaining shape carries.
		/// </remarks>
		public static AbilityObjectHitBroadcast ReadAbilityObjectHitBroadcast(this Reader reader)
		{
			byte header = reader.ReadUInt8Unpacked();

			AbilityObjectHitBroadcast value = new AbilityObjectHitBroadcast()
			{
				Deflected = (header & FLAG_DEFLECTED) != 0,
				DirectImpact = (header & FLAG_DIRECT_IMPACT) != 0,
				Blocked = (header & FLAG_BLOCKED) != 0,
				Ended = (header & FLAG_ENDED) != 0,
				VictimObjectID = 0,
				Point = Vector3.zero,
				Normal = Vector3.zero,
			};

			value.CasterObjectID = reader.ReadInt32();
			value.AbilityID = reader.ReadInt64();
			value.ContainerID = reader.ReadInt32Unpacked();
			value.ObjectID = reader.ReadInt32();

			if (value.Deflected)
			{
				value.PackedDeflectHeading = reader.ReadUInt32Unpacked();
				return value;
			}

			value.Point = reader.ReadVector3();
			value.Normal = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked());

			if ((header & FLAG_HAS_VICTIM) != 0)
			{
				value.VictimObjectID = reader.ReadInt32();
			}

			return value;
		}

		/// <summary>Writes an <see cref="AbilityObjectRedirectBroadcast"/>.</summary>
		public static void WriteAbilityObjectRedirectBroadcast(this Writer writer, AbilityObjectRedirectBroadcast value)
		{
			writer.WriteInt32(value.CasterObjectID);
			writer.WriteInt64(value.AbilityID);
			// Full entropy; see WriteAbilityObjectDestroyedBroadcast.
			writer.WriteInt32Unpacked(value.ContainerID);
			writer.WriteInt32(value.ObjectID);
			/* Unpacked: unlike the hit message's deflect heading, this one is only ever sent when a
			 * fork actually turned the object, so it is always a real AimDirectionCompression word
			 * with its high bits set — five bytes packed against four unpacked. */
			writer.WriteUInt32Unpacked(value.PackedHeading);
			writer.WriteUInt32(value.ServerTick);
		}

		/// <summary>Reads an <see cref="AbilityObjectRedirectBroadcast"/> written by the method above.</summary>
		public static AbilityObjectRedirectBroadcast ReadAbilityObjectRedirectBroadcast(this Reader reader)
		{
			return new AbilityObjectRedirectBroadcast()
			{
				CasterObjectID = reader.ReadInt32(),
				AbilityID = reader.ReadInt64(),
				ContainerID = reader.ReadInt32Unpacked(),
				ObjectID = reader.ReadInt32(),
				PackedHeading = reader.ReadUInt32Unpacked(),
				ServerTick = reader.ReadUInt32(),
			};
		}
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
	/// <para>
	/// Hand written for the same reason <c>CharacterAttributesBroadcast</c> is: a null array is
	/// simply an empty one here rather than a sentinel, the removals are omitted entirely on a full
	/// set, and the caps stop a malformed message allocating an arbitrarily large array before the
	/// stream runs out.
	/// </para>
	/// <para>
	/// It is <b>not</b> written for the length prefix, and this remark used to claim it was — that
	/// the generated array serializer "spends four bytes on each length". It does not:
	/// <c>Writer.WriteArray</c> writes the count with <c>WriteSignedPackedWhole</c>, one byte for
	/// any count seen here. Worse, the counts below used to be written with
	/// <c>WriteUInt16</c>, which is <b>always</b> two unpacked bytes (<c>Writer.WriteUInt16</c>
	/// forwards straight to <c>WriteUInt16Unpacked</c>) — so the hand-written form was costing one
	/// byte MORE per count than the generated one it was justified against. They are packed
	/// <c>WriteInt32</c>/<c>ReadInt32</c> now, which is one byte for a strip of up to 63 buffs.
	/// </para>
	/// </remarks>
	public static class CharacterBuffsBroadcastSerializer
	{
		/// <summary>Hard cap on entries, and separately on removals, in one message.</summary>
		/// <remarks>
		/// Far above any real character's visible strip; small enough that either count still costs
		/// two packed bytes at the very worst, and one for anything a character actually carries.
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
			// Packed, not WriteUInt16: see the class remark. One byte for any real strip.
			writer.WriteInt32(count);
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
			writer.WriteInt32(removedCount);
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

			/* Packed and SIGNED, so a corrupt stream can hand back a negative here where the ushort
			 * form could not. Rejected by the same branch, for the same reason. */
			int count = reader.ReadInt32();
			if (count < 0 || count > MAX_BUFFS)
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
					$"Read buff count {count} is outside 0-{MAX_BUFFS}. Discarding this update.");
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

			int removedCount = reader.ReadInt32();
			if (removedCount < 0 || removedCount > MAX_BUFFS)
			{
				Log.Warning("CharacterBuffsBroadcast",
					$"Read removed count {removedCount} is outside 0-{MAX_BUFFS}. Discarding this update.");
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
