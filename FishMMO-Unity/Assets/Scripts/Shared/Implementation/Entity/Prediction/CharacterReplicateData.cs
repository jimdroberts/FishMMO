using FishNet.CodeGenerating;
using FishNet.Object.Prediction;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Unified per-tick input data for all predicted character subsystems.
	/// Contains movement input (KCC) and ability activation input.
	/// Prediction works best when replicate data contains only input, not state.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct CharacterReplicateData : IReplicateData
	{
		// ── Movement input ──

		/// <summary>
		/// Forward movement axis value (W/S or up/down).
		/// </summary>
		public float MoveAxisForward;

		/// <summary>
		/// Right movement axis value (A/D or left/right).
		/// </summary>
		public float MoveAxisRight;

		/// <summary>
		/// Bitmask of movement flags (jump, crouch, sprint, etc).
		/// Each flag is a bit position defined by <see cref="KCCMoveFlags"/>.
		/// </summary>
		public int MoveFlags;

		/// <summary>
		/// How many ticks behind server-present this client was rendering its peers when it produced
		/// this input. Lag compensation rewinds by this much.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server cannot work this out for itself. It is one-way latency plus the interpolation
		/// buffer, and FishNet exposes no per-connection latency server-side — the value that used to
		/// be used, <c>ReplicateTick.LocalTickDifference</c>, is stamped with the current server tick
		/// immediately before the replicate body runs, so it read 0 on every tick that carried real
		/// input and every player was compensated the same fixed two ticks whatever their ping.
		/// </para>
		/// <para>
		/// The client does know: it has <c>TimeManager.RoundTripTime</c> and its own interpolation
		/// setting. It carries the FULL round trip, not half of it — the offset is subtracted from
		/// the server tick at which the replicate body RUNS, and the input's own trip to the server
		/// is part of that gap. The server adds the one term the client cannot see, its replicate
		/// queue depth; see <see cref="LagCompensationTick"/>.
		/// </para>
		/// <para>
		/// Client-supplied, so the server treats it as a claim rather than a fact:
		/// <see cref="LagCompensationTick.MaximumCompensationTicks"/> caps it, and
		/// <c>CharacterPositionHistory</c> clamps anything still older to the oldest sample it holds,
		/// so an inflated value buys the recorded window and never more than it. One byte, which at
		/// 30 Hz covers 8.5 seconds of claimed view lag.
		/// </para>
		/// </remarks>
		public byte ViewOffsetTicks;

		/// <summary>
		/// The sub-tick remainder of the view offset, in 1/256ths of a tick.
		/// </summary>
		/// <remarks>
		/// The whole-tick part alone quantises the rewind to a tick boundary, and the client's view
		/// of a peer does not sit on one: NetworkTransform interpolation blends between two received
		/// snapshots, so what it renders is a fraction of the way between two ticks. At 6 m/s one
		/// tick is 20 cm — the difference between a hit and a miss on a capsule. This byte is what
		/// lets the server resolve to the same fractional point, through
		/// <see cref="RewindTarget"/> and <c>CharacterPositionHistory</c>'s interpolating resolve.
		/// </remarks>
		public byte ViewOffsetFraction;

		/// <summary>
		/// The EXACT server tick this client was drawing its peers at, as an offset from this input's own tick: the whole
		/// ticks, low 16 bits (the server unwraps them against its present tick, which is never 65536 ticks away).
		/// </summary>
		/// <remarks>
		/// <para>
		/// Observed characters are drawn on one shared playback clock (FishNet NetworkTransform, FISHMMO EDIT: timed
		/// playback), so one tick describes every character on screen. Sending it removes every estimate the older claim
		/// above rested on: the round trip and its jitter, the batching delay, and the server's replicate queue depth. The
		/// input's own tick is exact on both sides, so input tick + offset is exactly the tick that was on screen when the
		/// input was made, and the server rewinds there.
		/// </para>
		/// <para>
		/// Meaningful only when <see cref="ViewTickFraction"/> is not 0; until the client has a clock (just connected) it
		/// sends 0 and the round-trip claim is used instead. The client changes it only when the drawn tick moves by more
		/// than a sixteenth of a tick, so in steady play it is not resent. Still a claim: the server caps how far back it
		/// reaches exactly as it capped the old one.
		/// </para>
		/// </remarks>
		public ushort ViewTickOffset;

		/// <summary>
		/// The sub-tick part of <see cref="ViewTickOffset"/>, encoded 1..255 for 0..1 of a tick (value - 1) / 254; 0 means
		/// "no exact view tick: use the round-trip claim".
		/// </summary>
		public byte ViewTickFraction;

		/// <summary>
		/// Aim direction for this input frame, as a unit vector.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Always store a value that has been through <see cref="AimDirectionCompression.Quantize"/>.
		/// This is input to a deterministic simulation, so the producer must commit to the value the
		/// wire can carry rather than to whatever it holds locally — otherwise the owner predicts
		/// with one direction while the server and observers simulate with the decoded one, and
		/// every cast diverges by the quantisation error.
		/// </para>
		/// <para>
		/// Replaced a full <c>Quaternion CameraRotation</c>. Nothing ever read the roll: movement
		/// takes <c>rotation * Vector3.forward</c> to build its planar basis and the ability path
		/// takes the same forward as its trace direction, so the rotation carried a degree of
		/// freedom that no consumer used and that could not be represented exactly.
		/// </para>
		/// </remarks>
		public Vector3 AimDirection;

		/// <summary>
		/// The yaw from <see cref="AimDirection"/>'s heading to the body's, in quarter degrees about world up
		/// (<see cref="AimConvergence.BodyHeading"/>). Movement and facing are taken from the aim turned by this; shots from
		/// the aim itself.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Needed because the shot and the body look at the crosshair's point from different places and with different
		/// limits: the shot from the eye, converging from 2 m; the body from where it is drawn, converging from 6 m so the
		/// crosshair crossing near objects cannot snap it round. One aim cannot carry both.
		/// </para>
		/// <para>
		/// Zero for an NPC, and near zero for a camera on the character's line (third and first person). Read through
		/// <see cref="AimConvergence.DecodeHeadingOffset"/>, which holds it to <see cref="AimConvergence.MaxHeadingOffsetDegrees"/>
		/// whatever was sent. On the wire it has a flag bit of its own and is written only on a tick it changes: quarter
		/// degrees is far finer than a facing can be seen to differ by, and coarse enough that it holds still while the
		/// aim sweeps.
		/// </para>
		/// </remarks>
		public short HeadingOffset;

		/// <summary>
		/// How far along <see cref="AimDirection"/> from the eye the point under the crosshair is, log-coded in one byte
		/// (<see cref="AimConvergence.EncodeDistance"/>); zero for "not given". With <see cref="AimOriginRight"/> and
		/// <see cref="AimOriginUp"/> it states the LAUNCH LINE: the line through the crosshair a projectile is simulated on.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A projectile is simulated along the line the player aims down, the camera's ray through the crosshair, and only
		/// DRAWN leaving the hand, wand or staff (AbilityObject, Presentation). The server cannot take that line from a
		/// camera it does not have, and must not take an arbitrary one from a client, so it is rebuilt from three small
		/// numbers and the eye the server derives itself: the line runs from a point beside the eye (these two offsets,
		/// held to <see cref="AimConvergence.MaxOriginOffset"/>) to the point this distance along the aim. Both peers
		/// rebuild the same line from the same input, so they fly the same projectile to the last bit, and it follows the
		/// crosshair at every range: a shot led ahead of a moving target passes where the crosshair was put, not below it.
		/// </para>
		/// <para>
		/// It used to leave the hand as the simulation had it, turned toward the crosshair's point. That is exact with the
		/// crosshair ON the target and low by most of the hand's drop below the eye everywhere short of it, which is where
		/// a led shot has to land; and a hand that follows a cast animation exists on no server at all.
		/// </para>
		/// <para>
		/// <b>One-shot.</b> Sent only on a tick that may spawn (the owner knows its own cast is about to finish), absent on
		/// every other, and absent means zero on the wire, never "the same as last tick". Zero is always safe: both peers
		/// then launch along the aim from the eye, exactly as each other (and as an NPC, which has no camera, always does).
		/// </para>
		/// </remarks>
		public byte ConvergenceDistance;

		/// <summary>
		/// Where the launch line starts, beside the eye: centimetres to the aim's right. See <see cref="ConvergenceDistance"/>.
		/// </summary>
		public sbyte AimOriginRight;

		/// <summary>Where the launch line starts, beside the eye: centimetres up, across the aim.</summary>
		public sbyte AimOriginUp;

		// ── Hit claim ──

		/// <summary>
		/// The character the owner's own copy of one of its ability objects hit, as that character's network object id
		/// plus one; zero on every tick without a claim.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The owner resolves its hits against other characters where it DRAWS them, and the server resolves the same
		/// sweep against where its history says they were drawn. The two agree to the centimetre on a steady connection,
		/// and can disagree by more when the owner's view was not the one the history reproduces: a character drawn along
		/// the straight line between two position updates while it turned, or held still while its next update was late.
		/// No rewind can recover that, because the server cannot know what a client drew.
		/// </para>
		/// <para>
		/// So the owner says what it saw. A claim names the object (by the age of its cast and its index in the cast) and
		/// the victim; the server checks that the object exists, has not already hit that character, and passed within a
		/// speed-scaled tolerance of where its own history puts the victim (AbilityObject.TryApplyClaimedHit), and applies
		/// the hit itself. A claim the server already agrees with is a no-op. One-shot, like
		/// <see cref="ConvergenceDistance"/>.
		/// </para>
		/// </remarks>
		public int HitClaimVictim;

		/// <summary>How many input ticks ago the claimed object's cast spawned it: this input's tick less the cast's spawn tick.</summary>
		public ushort HitClaimSpawnAge;

		/// <summary>The claimed object's index within its cast (0 for the cast's own object, above for forks and children).</summary>
		public byte HitClaimObject;

		// ── Ability input ──

		/// <summary>
		/// Flags representing the ability activation state.
		/// Bit positions defined by <see cref="AbilityActivationFlags"/>.
		/// </summary>
		public int ActivationFlags;

		/// <summary>
		/// The ID of the queued ability or consumable template.
		/// </summary>
		public long QueuedAbilityID;

		// ── Equipment input ──

		/// <summary>
		/// An equip or unequip the owner asked for on this tick, packed by
		/// <see cref="EquipmentReplicateInput"/>: the kind of request, the container it names and
		/// the equipment socket. Zero on every tick that carries no request, which is almost all
		/// of them.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Equipment is INPUT, not state, and this is where it belongs. It used to travel as a
		/// reliable broadcast that the server applied at whatever tick it happened to land on — a
		/// tick the owner could not know in advance — so the item's attribute bonuses reached the
		/// owner only through the next reconcile and the movement between the two was replayed at
		/// the new speed. A +15% speed item equipped mid-stride was a visible snap. Carried here,
		/// both peers apply the change on the same tick, the reconcile confirms rather than
		/// corrects, and a stale reconcile is undone and re-applied by the replay like every other
		/// predicted action.
		/// </para>
		/// <para>
		/// One request per tick. A second request in the same tick waits for the next one; see
		/// <c>EquipmentController.RequestEquip</c>.
		/// </para>
		/// </remarks>
		public byte EquipmentRequest;

		/// <summary>
		/// For an equip, the index in the named container the item is coming from. Ignored for an
		/// unequip, whose destination slot is chosen deterministically by the container on both
		/// peers.
		/// </summary>
		public short EquipmentIndex;

		/// <summary>
		/// Returns the network tick for this replicate input as a <see cref="PredictionTick"/>.
		/// This is the only sanctioned way to produce a PredictionTick — use it as the
		/// currentTick argument when applying prediction-domain buff and cooldown state.
		/// </summary>
		public PredictionTick GetPredictionTick() => new PredictionTick(tick);

		private uint tick;

		/// <inheritdoc/>
		public void Dispose() { }

		/// <inheritdoc/>
		public uint GetTick() => tick;

		/// <inheritdoc/>
		public void SetTick(uint value) => tick = value;
	}
}