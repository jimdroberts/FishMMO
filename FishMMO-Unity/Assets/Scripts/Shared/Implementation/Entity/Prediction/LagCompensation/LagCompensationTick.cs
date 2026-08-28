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
	/// The first term does NOT measure latency, despite its name.
	/// <c>NetworkConnection.ReplicateTick.LocalTickDifference</c> is
	/// <c>TimeManager.LocalTick - ReplicateTick.LocalTick</c>, and <c>ReplicateTick.LocalTick</c> is
	/// stamped with the CURRENT server tick every time the server runs a created replicate for that
	/// owner (<c>NetworkBehaviour.ReplicateData</c> → <c>NetworkObject.SetReplicateTick</c> →
	/// <c>EstimatedTick.Update</c>, which assigns <c>LocalTick = tm.LocalTick</c>). That stamp
	/// happens immediately before the replicate body — including any ability that resolves a hit
	/// from inside it — so on every tick that runs real input the difference is zero, whatever the
	/// client's RTT. It only grows while a client is input-starved, which is the opposite of the
	/// quantity wanted. In practice compensation is therefore a constant
	/// <see cref="SpectatorInterpolationTicks"/>, and a 200 ms player is compensated exactly as much
	/// as a 20 ms one. Deriving the term from something that tracks arrival — the owner's packet
	/// tick or the TimeManager's RTT — is an open decision, not a bug in the rewind itself, which
	/// works correctly for whatever tick it is handed.
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

			/* The offset the CLIENT measured, not one derived server-side.
			 *
			 * ReplicateTick.LocalTickDifference used to supply the latency term and is not a latency
			 * term: it is stamped with the current server tick immediately before the replicate body
			 * runs, so it read 0 on every tick that carried real input, and every player was
			 * compensated the same fixed SpectatorInterpolationTicks regardless of ping. The client
			 * has both halves of the real number — half its round trip, plus the interpolation buffer
			 * it holds — and stamps their sum into the input, to a 1/256 of a tick.
			 *
			 * It is a claim, not a fact, so it is capped here and CharacterPositionHistory refuses
			 * anything outside its recorded window outright — an inflated claim buys no compensation
			 * rather than the deepest rewind available. */
			uint wholeTicks = SpectatorInterpolationTicks;
			float fraction = 0f;
			uint anchorTick = timeManager.LocalTick;

			if (nob.TryGetComponent(out CharacterPredictionController predictionController))
			{
				uint claimed = predictionController.CurrentViewOffsetTicks;
				wholeTicks = claimed > MaximumCompensationTicks ? MaximumCompensationTicks : claimed;
				fraction = predictionController.CurrentViewOffsetFraction / 256f;

				/* Anchor on the INPUT's tick, not the server's present one.
				 *
				 * The hit is resolved inside the replicate for a particular input, and the server
				 * runs that input at whatever LocalTick it has reached — the two differ by however
				 * many ticks of this client's input the server currently has buffered, which is
				 * exactly the per-client quantity that varies. Anchoring on LocalTick folded that
				 * queue depth straight into the rewind as error. Outside a replicate (an ability
				 * object resolving from its own OnTick subscription) the snapshot is unset and the
				 * server's present tick is the best available anchor. */
				uint replicateTick = predictionController.CurrentReplicateTickSnapshot;
				if (replicateTick != TimeManager.UNSET_TICK)
				{
					anchorTick = replicateTick;
				}
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
