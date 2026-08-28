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
	/// <b>Two terms, both real.</b> A client's view of its peers is behind the server by the time its
	/// input takes to arrive, plus the interpolation buffer it deliberately renders behind by. The
	/// first comes from FishNet's own tick bookkeeping rather than an RTT estimate:
	/// <c>NetworkConnection.ReplicateTick.LocalTickDifference</c> is how many server ticks have
	/// passed since that client's last input landed. The second is the
	/// <c>NetworkObject._spectatorInterpolation</c> setting, mirrored here because the field is
	/// private and reading it reflectively per hit would be absurd.
	/// </para>
	/// <para>
	/// <b>Server-driven characters compensate nothing.</b> An NPC's brain runs on the server against
	/// live positions, so there is no view offset to undo. Compensating one would rewind its targets
	/// away from where the brain actually aimed.
	/// </para>
	/// <para>
	/// <b>The clamp is a security boundary, not a safety net.</b> The lag term is derived from packet
	/// arrival, so a client that stops sending briefly inflates it. Capping it at the history window
	/// bounds what that buys: at worst an attacker resolves against the oldest tick the server still
	/// holds, and never further.
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
		/// Resolves the tick <paramref name="caster"/>'s client was rendering its peers at.
		/// </summary>
		/// <param name="caster">The character whose view is being reconstructed.</param>
		/// <param name="timeManager">Server time manager.</param>
		/// <param name="rewindTick">The resolved past tick.</param>
		/// <returns>
		/// False when there is nothing to compensate — a server-driven character, an unowned one, or
		/// a connection whose tick bookkeeping is not yet established.
		/// </returns>
		public static bool TryResolve(ICharacter caster, TimeManager timeManager, out uint rewindTick)
		{
			rewindTick = 0u;

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
			if (owner == null || !owner.IsValid || owner.ReplicateTick.IsUnset)
			{
				// No owning client means nothing rendered this character's view late.
				return false;
			}

			uint lagTicks = owner.ReplicateTick.LocalTickDifference(timeManager);
			if (lagTicks == TimeManager.UNSET_TICK)
			{
				return false;
			}

			uint compensation = lagTicks + SpectatorInterpolationTicks;
			uint localTick = timeManager.LocalTick;

			if (compensation >= localTick)
			{
				return false;
			}

			rewindTick = localTick - compensation;
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
