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