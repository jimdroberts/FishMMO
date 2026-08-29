using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Works out which past tick a caster's client was actually looking at, so a hit can be resolved
	/// against that instead of against the server's present.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two terms — but only one of them currently measures anything.</b> A client's view of its
	/// peers is behind the server by the time its input takes to arrive, plus the interpolation
	/// buffer it deliberately renders behind by. The second term is the
	/// <c>NetworkObject._spectatorInterpolation</c> setting, mirrored here because the field is
	/// private and reading it reflectively per hit would be absurd.
	/// </para>
	/// <para>
	/// <b>The whole offset is measured on the client and sent in the input.</b>
	/// <see cref="KCCPlayer.ResolveViewOffset"/> adds half the round trip to
	/// <see cref="SpectatorInterpolationTicks"/> and stamps the sum into
	/// <c>CharacterReplicateData.ViewOffsetTicks</c>, to a 1/256 of a tick. The server cannot derive
	/// it: <c>NetworkConnection.ReplicateTick.LocalTickDifference</c> looks like a latency term and
	/// is not one — <c>ReplicateTick.LocalTick</c> is stamped with the current server tick
	/// immediately before the replicate body runs, so it reads 0 on every tick that carries real
	/// input, whatever the client's RTT.
	/// </para>
	/// <para>
	/// <b>The offset is a DURATION; the anchor it is subtracted from is a server tick.</b> This is
	/// the distinction the whole file turns on, so it lives in one place —
	/// <see cref="ServerTickDomain"/> — which both this type and
	/// <see cref="CharacterPositionHistory.RecordTick"/> call. Anchoring on the replicate input's
	/// own tick is WRONG and silently disables compensation: on the server a replicate carries the
	/// OWNING CLIENT'S <c>TimeManager.LocalTick</c>, an unsynchronised counter that restarts at zero
	/// when that client connects (<c>NetworkBehaviour.Replicate_Reader</c> stamps the read datas
	/// from <c>LastPacketTick.LastRemoteTick</c>, which is the sender's own <c>LocalTick</c>). The
	/// history is keyed by the SERVER'S tick, so a target built in the client's domain lands far
	/// outside the recorded window, every <c>Rewind</c> declines, and the query runs against live
	/// positions with nothing logged. <see cref="PredictionTick"/> exists to stop replicate-domain
	/// ticks leaking into raw-tick code; it cannot help here because this reads a plain
	/// <c>uint</c>, so the rule is stated instead.
	/// </para>
	/// <para>
	/// <b>Server-driven characters compensate nothing.</b> An NPC's brain runs on the server against
	/// live positions, so there is no view offset to undo. Compensating one would rewind its targets
	/// away from where the brain actually aimed.
	/// </para>
	/// <para>
	/// <b>There is no clamp, deliberately.</b> An inflated lag term is not capped to the oldest
	/// recorded tick — <c>CharacterPositionHistory.TryResolve</c> REFUSES a tick outside its window,
	/// so a client whose term is inflated buys no compensation at all rather than the maximum
	/// available. See the note at the bottom of this file about the removal of
	/// <c>TryResolveClamped</c>.
	/// </para>
	/// </remarks>
	public static class LagCompensationTick
	{
		/// <summary>
		/// Ticks a non-owned character is rendered behind the server, mirroring
		/// <c>NetworkObject._spectatorInterpolation</c> as authored on the playable prefabs.
		/// </summary>
		/// <remarks>
		/// Must be kept in step with the prefab setting by hand. If they diverge, compensation is
		/// wrong by the difference — which shows up as hits landing consistently ahead of or behind
		/// where the shooter aimed, rather than as an obvious failure.
		/// </remarks>
		public const uint SpectatorInterpolationTicks = 2;

		/// <summary>
		/// Hard ceiling on the client-claimed view offset, in ticks.
		/// </summary>
		/// <remarks>
		/// The claim arrives in the replicate input, so it is attacker-controlled. This bounds it
		/// before it reaches the history, which independently refuses any tick outside its recorded
		/// window — two limits rather than one, because the history window is a serialized field
		/// somebody may widen for a legitimate reason. 30 ticks is a second at the shipped tick rate,
		/// comfortably past any playable latency.
		/// </remarks>
		public const uint MaximumCompensationTicks = 30;

		/// <summary>
		/// The one tick domain lag compensation works in: the server's own <c>TimeManager</c> tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Exists so the recording side and the rewinding side cannot drift apart. Every sample in
		/// <see cref="CharacterPositionHistory"/> is keyed by this, and every
		/// <see cref="RewindTarget"/> is measured back from it; expressing that as one function
		/// makes a mismatch impossible to introduce by editing one side, which is exactly how the
		/// replicate-domain anchor got in.
		/// </para>
		/// <para>
		/// On the server <c>TimeManager.LocalTick</c> returns <c>TimeManager.Tick</c>, so this is
		/// the authoritative simulation tick. It is deliberately NOT the replicate input's tick —
		/// see the remarks on <see cref="LagCompensationTick"/> for why that one belongs to the
		/// owning client and cannot index this history.
		/// </para>
		/// </remarks>
		/// <param name="timeManager">The server's time manager.</param>
		/// <returns>The current server tick, or <see cref="TimeManager.UNSET_TICK"/> when there is no time manager.</returns>
		public static uint ServerTickDomain(TimeManager timeManager)
		{
			return timeManager != null ? timeManager.LocalTick : TimeManager.UNSET_TICK;
		}

		/// <summary>
		/// Resolves the tick <paramref name="caster"/>'s client was rendering its peers at.
		/// </summary>
		/// <param name="caster">The character whose view is being reconstructed.</param>
		/// <param name="timeManager">Server time manager.</param>
		/// <param name="rewindTick">The resolved past tick.</param>
		/// <returns>
		/// False when there is nothing to compensate — a server-driven character, an unowned one, or
		/// a connection whose tick bookkeeping is not yet established.
		/// </returns>
		public static bool TryResolve(ICharacter caster, TimeManager timeManager, out RewindTarget target)
		{
			target = RewindTarget.None;

			if (caster == null || timeManager == null)
			{
				return false;
			}

			NetworkObject nob = caster.NetworkObject;
			if (nob == null || !nob.IsServerStarted)
			{
				return false;
			}

			NetworkConnection owner = nob.Owner;
			if (owner == null || !owner.IsValid)
			{
				// No owning client means nothing rendered this character's view late.
				return false;
			}

			/* A server-driven character compensates nothing, and ownership alone does not identify
			 * one: a pet is owned by the connection that summoned it while a server-side AIController
			 * writes its input. The owner check above lets monsters out because they are ownerless;
			 * this lets pets out for the real reason. Today a pet also carries no KCCPlayer, so
			 * nothing ever writes its ViewOffsetTicks and the zero check below would catch it — but
			 * that is a property of the current prefabs, not a rule, and rewinding an NPC's targets
			 * away from where its brain aimed is silent when it happens. */
			if (nob.TryGetComponent(out IAIController _))
			{
				return false;
			}

			/* The offset the CLIENT measured, not one derived server-side; see the remarks on this
			 * type. It is a claim, not a fact, so it is capped here and CharacterPositionHistory
			 * refuses anything outside its recorded window outright — an inflated claim buys no
			 * compensation rather than the deepest rewind available. */
			uint wholeTicks = SpectatorInterpolationTicks;
			float fraction = 0f;

			/* ALWAYS the server's own tick. The replicate input's tick is the owning client's
			 * unsynchronised counter and cannot index a history keyed by the server's — anchoring on
			 * it put every target outside the recorded window, so nothing rewound and every hit
			 * silently resolved against live positions. See ServerTickDomain. */
			uint anchorTick = ServerTickDomain(timeManager);
			if (anchorTick == TimeManager.UNSET_TICK)
			{
				return false;
			}

			if (nob.TryGetComponent(out CharacterPredictionController predictionController))
			{
				uint claimed = predictionController.CurrentViewOffsetTicks;
				wholeTicks = claimed > MaximumCompensationTicks ? MaximumCompensationTicks : claimed;
				fraction = predictionController.CurrentViewOffsetFraction / 256f;
			}

			if (wholeTicks == 0u && fraction <= 0f)
			{
				return false;
			}
			if (wholeTicks >= anchorTick)
			{
				return false;
			}

			target = new RewindTarget(anchorTick - wholeTicks, fraction);
			return true;
		}


		/* TryResolveClamped was removed deliberately. It clamped an out-of-window tick to the
		 * oldest recorded one, which is the opposite of the security stance the history settled
		 * on: CharacterPositionHistory.TryResolve REFUSES ticks older than its window, so an
		 * inflated latency claim buys no compensation rather than the maximum. Nothing ever
		 * called the clamped variant, and keeping a public API whose semantics contradict the
		 * enforced ones is how the wrong one ends up used. */
	}
}
