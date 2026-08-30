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
	/// <b>Three terms, and they are measured by two different peers.</b> The gap between what the
	/// caster's client had on screen and the server's present, at the instant the replicate body
	/// runs, is:
	/// </para>
	/// <list type="number">
	/// <item><b>The full round trip.</b> The state being looked at left the server one way ago, and
	/// the input built from it takes another one way to come back. Both halves are in the gap
	/// because the anchor is the tick the query RUNS on, not the tick the input was produced on.</item>
	/// <item><b>The interpolation buffer</b> the client deliberately renders its peers behind —
	/// <see cref="SpectatorInterpolationTicks"/>, mirroring <c>NetworkObject._spectatorInterpolation</c>
	/// because the field is private and reading it reflectively per hit would be absurd.</item>
	/// <item><b>The server's replicate queue.</b> FishNet holds <c>StateInterpolation</c> entries
	/// before consuming one, so an arriving input waits that long before it is run. Added by
	/// <see cref="ResolveReplicateQueueTicks"/>, because only the server knows it.</item>
	/// </list>
	/// <para>
	/// <b>The first two are measured on the client and sent in the input.</b>
	/// <see cref="KCCPlayer.ResolveViewOffset"/> adds the round trip to
	/// <see cref="SpectatorInterpolationTicks"/> and stamps the sum into
	/// <c>CharacterReplicateData.ViewOffsetTicks</c>, to a 1/256 of a tick. The server cannot derive
	/// them: FishNet measures ping on the CLIENT (<c>TimeManager.ModifyPing</c> runs from the pong
	/// the client receives), and there is no per-connection round trip server side.
	/// <c>NetworkConnection.ReplicateTick.LocalTickDifference</c> looks like a latency term and is
	/// not one — <c>ReplicateTick.LocalTick</c> is stamped with the current server tick immediately
	/// before the replicate body runs, so it reads 0 on every tick that carries real input, whatever
	/// the client's RTT.
	/// </para>
	/// <para>
	/// <b>Half the round trip was the previous formulation, and it under-compensated everybody.</b>
	/// It covered the state's trip out but not the input's trip back or the queue, so every shot
	/// resolved against a world roughly (one way + two ticks) newer than the one the shooter aimed
	/// in. At 200&#160;ms and 6&#160;m/s that is most of a character's width, and it grew with ping —
	/// the connections lag compensation exists for were the ones it served worst.
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
	/// <b>The claim is capped, and then the history clamps it.</b>
	/// <see cref="MaximumCompensationTicks"/> bounds it here as a first line, and
	/// <c>CharacterPositionHistory.TryResolve</c> resolves anything still older than its recording to
	/// the oldest sample it holds. The ceiling on how far into the past anybody can shoot is
	/// therefore <c>CharacterPositionHistory.MaximumRewindMilliseconds</c>, which is the one number
	/// to weigh when deciding how long a victim may have been behind cover and still be hit.
	/// Clamping rather than refusing costs nothing in that ceiling — an attacker maximises rewind by
	/// claiming a value just INSIDE the window, which was always accepted — and it removes the cliff
	/// that gave a 380&#160;ms player no compensation at all while a 360&#160;ms player got the lot.
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
		/// <para>
		/// The claim arrives in the replicate input, so it is attacker-controlled. This bounds it
		/// before it reaches the history, which independently clamps anything still older to the
		/// oldest sample it holds — two limits rather than one, because the history window is a
		/// serialized field somebody may widen for a legitimate reason.
		/// </para>
		/// <para>
		/// <b>Not the security ceiling.</b> That is
		/// <c>CharacterPositionHistory.MaximumRewindMilliseconds</c>, which is what actually bounds
		/// how far into the past a shot can reach; this constant sits above it (30 ticks is a second
		/// at the shipped tick rate, against a 500&#160;ms recording) and only stops an absurd claim
		/// reaching the history at all. It applies BEFORE the server's replicate queue term is added,
		/// so the queue depth is never something a client can inflate.
		/// </para>
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

			/* The server's own replicate queue, which the client cannot see and so cannot include.
			 *
			 * The client's claim ends at "the input has been sent". It has not been RUN yet:
			 * FishNet deliberately holds StateInterpolation entries in the replicates queue before
			 * consuming one (NetworkBehaviour.Prediction's leaveInBuffer), so an input waits that
			 * many ticks after arrival before the replicate body sees it — and the anchor below is
			 * the tick at which that body runs. Leaving it out compensated every player two ticks
			 * short of their real view, whatever their ping. Read live rather than assumed: the
			 * setting is authored on the PredictionManager and can differ per deployment. */
			wholeTicks += ResolveReplicateQueueTicks(nob);

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

		/// <summary>
		/// How many ticks an arriving input waits in the server's replicate queue before it is run.
		/// </summary>
		/// <remarks>
		/// <c>PredictionManager.StateInterpolation</c> is the buffer depth FishNet maintains on the
		/// receiving side: <c>Replicate_NonAuthoritative</c> only consumes past that depth, so in
		/// steady state an input sits in the queue for exactly this long. Zero when the manager
		/// cannot be reached, which loses two ticks of compensation rather than throwing.
		/// </remarks>
		/// <param name="nob">The caster's network object.</param>
		/// <returns>The queue depth in ticks.</returns>
		public static uint ResolveReplicateQueueTicks(NetworkObject nob)
		{
			FishNet.Managing.Predicting.PredictionManager predictionManager =
				nob != null ? nob.PredictionManager : null;
			return predictionManager != null ? predictionManager.StateInterpolation : 0u;
		}


		/* TryResolveClamped was removed, and stays removed — but not for the reason first given.
		 *
		 * It was deleted as contradicting a security stance the history was thought to enforce by
		 * REFUSING out-of-window ticks. That stance did not survive examination: the most rewind a
		 * claim can buy is the recorded window either way, and the way to buy it is to claim a value
		 * just inside that window, which refusal always accepted. Refusing only rejected claims that
		 * overshot — which buy strictly less — so it penalised honest high-latency clients and
		 * nobody else. The clamp now lives in CharacterPositionHistory.TryResolve itself, which is
		 * the one place that knows the window, rather than in a second entry point callers could
		 * pick between. */
	}
}
