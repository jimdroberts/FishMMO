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
		/// <para>
		/// <b>The gap between the two is deliberate and settled.</b> 500&#160;ms RTT is the worst case
		/// this game designs for, and the shipped 500&#160;ms recording covers it; past that the
		/// history clamps to its oldest sample, which is the safe direction because it returns a pose
		/// that was actually recorded rather than falling through to a live one. Do not lower this
		/// constant to match the ring — a claim capped BELOW the recorded window would be the real
		/// defect. If the worst case moves, widen
		/// <c>CharacterPositionHistory.maximumRewindMilliseconds</c> on the prefab instead.
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
		/// <summary>
		/// Test-only claim source, consulted before the owner/AI gates. Null in production —
		/// nothing in shipping code assigns it, and it must stay that way.
		/// </summary>
		/// <remarks>
		/// The gates below are correct for the live game: an ownerless or AI-driven caster has no
		/// late-rendering client, so nothing rewinds for it. But that also means a simulation
		/// harness (which drives casters server-side by construction) can never exercise the real
		/// rewind path. This hook lets the harness say "treat this caster as a client that claims
		/// this view offset" so <see cref="ResolveAnchor"/> and <c>CharacterPositionHistory</c>
		/// run for real, with synthetic 0–500ms claims. Internal, and visible only to
		/// FishMMO.TestHarness via InternalsVisibleTo; delete alongside that folder.
		/// </remarks>
		internal static System.Func<ICharacter, (byte ticks, byte fraction)?> ClaimOverride;

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

			if (ClaimOverride != null)
			{
				(byte ticks, byte fraction)? claim = ClaimOverride(caster);
				if (claim.HasValue)
				{
					uint overrideAnchor = ServerTickDomain(timeManager);
					if (overrideAnchor == TimeManager.UNSET_TICK)
					{
						return false;
					}
					return ResolveAnchor(overrideAnchor, claim.Value.ticks, claim.Value.fraction,
						ResolveReplicateQueueTicks(nob), out target);
				}
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
			/* ALWAYS the server's own tick. The replicate input's tick is the owning client's
			 * unsynchronised counter and cannot index a history keyed by the server's — anchoring on
			 * it put every target outside the recorded window, so nothing rewound and every hit
			 * silently resolved against live positions. See ServerTickDomain. */
			uint anchorTick = ServerTickDomain(timeManager);
			if (anchorTick == TimeManager.UNSET_TICK)
			{
				return false;
			}

			byte claimedTicks = (byte)SpectatorInterpolationTicks;
			byte claimedFraction = 0;
			if (nob.TryGetComponent(out CharacterPredictionController predictionController))
			{
				claimedTicks = predictionController.CurrentViewOffsetTicks;
				claimedFraction = predictionController.CurrentViewOffsetFraction;
			}

			/* The server's own replicate queue, which the client cannot see and so cannot include,
			 * is added by ResolveAnchor. Read live rather than assumed: the setting is authored on
			 * the PredictionManager and can differ per deployment. */
			return ResolveAnchor(anchorTick, claimedTicks, claimedFraction,
				ResolveReplicateQueueTicks(nob), out target);
		}

		/// <summary>
		/// The whole server-side half of the rewind derivation, as a pure function.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Separated from <see cref="TryResolve"/> so the arithmetic can be exercised directly
		/// against production rather than re-implemented in a test — the same reason
		/// <c>CharacterPredictionController.IsTransformRedundant</c> and
		/// <c>Buff.DurationToTicks</c> are shaped this way. <see cref="TryResolve"/> supplies the
		/// three inputs from the live object and adds nothing of its own.
		/// </para>
		/// <para>
		/// The three terms and why each is here:
		/// <list type="number">
		/// <item><description><paramref name="claimedTicks"/>/<paramref name="claimedFraction"/> —
		/// the client's measured full round trip plus its interpolation buffer, capped at
		/// <see cref="MaximumCompensationTicks"/> because it is a claim rather than a fact.</description></item>
		/// <item><description><paramref name="queueTicks"/> — FishNet's replicate queue depth, which
		/// the client cannot see; the input waits this long after arrival before the replicate body
		/// runs, and <paramref name="anchorTick"/> is the tick at which that body runs.</description></item>
		/// <item><description><paramref name="anchorTick"/> — the SERVER's tick, never the replicate
		/// input's, which belongs to the owning client's unsynchronised counter.</description></item>
		/// </list>
		/// </para>
		/// <para>
		/// The cap is applied to the CLAIM alone and the queue depth is added afterwards, so a
		/// deployment that holds more states never loses the queue term to the cap.
		/// </para>
		/// </remarks>
		/// <param name="anchorTick">The server tick at which the replicate body runs.</param>
		/// <param name="claimedTicks">Whole ticks of view offset the client claimed.</param>
		/// <param name="claimedFraction">Sub-tick remainder of that claim, in 1/256ths of a tick.</param>
		/// <param name="queueTicks">The server's replicate queue depth.</param>
		/// <param name="target">The resolved rewind target, or <see cref="RewindTarget.None"/>.</param>
		/// <returns>True when there is anything to compensate.</returns>
		internal static bool ResolveAnchor(uint anchorTick, byte claimedTicks, byte claimedFraction,
			uint queueTicks, out RewindTarget target)
		{
			target = RewindTarget.None;

			uint wholeTicks = claimedTicks > MaximumCompensationTicks ? MaximumCompensationTicks : claimedTicks;
			float fraction = claimedFraction / 256f;

			wholeTicks += queueTicks;

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
		/// The whole client-side half of the derivation: how far behind server-present this client's
		/// rendered view of its peers will be BY THE TIME THE SERVER RUNS the input it is producing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The FULL round trip plus the interpolation buffer, split into whole ticks and a 1/256
		/// tick remainder. Both halves of the trip are in play and they are different halves: the
		/// state being looked at left the server one way trip ago and is rendered
		/// <see cref="SpectatorInterpolationTicks"/> behind even that, and the input built from
		/// that view takes another one way trip to reach the server.
		/// </para>
		/// <para>
		/// Lives here rather than on <c>KCCPlayer</c> so both halves of the loop sit in one file and
		/// can be composed in a test without a live <c>TimeManager</c>. <c>KCCPlayer</c> supplies
		/// the two measurements and adds nothing of its own.
		/// </para>
		/// </remarks>
		/// <param name="roundTripMilliseconds"><c>TimeManager.RoundTripTime</c>.</param>
		/// <param name="tickDelta"><c>TimeManager.TickDelta</c>, in seconds.</param>
		/// <param name="wholeTicks">Whole ticks of view offset.</param>
		/// <param name="fraction">Sub-tick remainder, in 1/256ths of a tick.</param>
		public static void ResolveViewOffset(double roundTripMilliseconds, double tickDelta,
			out byte wholeTicks, out byte fraction)
		{
			wholeTicks = (byte)SpectatorInterpolationTicks;
			fraction = 0;

			if (tickDelta <= 0d)
			{
				return;
			}

			/* RoundTripTime is milliseconds, and BOTH halves of it are in play. Kept as a REAL
			 * number rather than rounded up to a whole tick: the fractional part is carried in its
			 * own byte, because the interpolated view this is describing does not sit on a tick
			 * boundary either. */
			double roundTripSeconds = roundTripMilliseconds / 1000d;
			double ticks = (roundTripSeconds / tickDelta) + SpectatorInterpolationTicks;

			if (ticks < 0d)
			{
				ticks = 0d;
			}
			if (ticks > byte.MaxValue)
			{
				ticks = byte.MaxValue;
			}

			double whole = System.Math.Floor(ticks);
			wholeTicks = (byte)whole;

			// 1/256 of a tick is 0.13 ms at tick rate 30 — far finer than the estimate feeding it.
			int scaled = (int)System.Math.Round((ticks - whole) * 256d);
			fraction = (byte)(scaled < 0 ? 0 : scaled > 255 ? 255 : scaled);
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
