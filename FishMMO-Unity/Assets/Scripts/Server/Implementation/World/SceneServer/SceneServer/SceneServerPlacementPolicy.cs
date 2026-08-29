using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Decides how much new scene-load work a scene server takes on, given what it is already
	/// hosting.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> Scene placement is a PULL: the world server enqueues a pending scene
	/// row and every scene server races to claim it through <c>ISceneService.DequeueAsync</c>, which
	/// is a <c>FOR UPDATE SKIP LOCKED</c> take-the-oldest. Nothing in that path considers load, so
	/// whichever server's pulse timer happens to fire first wins — and it wins every race in that
	/// window, including the ones it should lose. A server that came up first, or pulses slightly
	/// out of phase with its peers, accumulates scenes while an idle peer takes none.
	/// </para>
	/// <para>
	/// <b>How this fixes it without cluster coordination.</b> Each server scales its own per-pulse
	/// dequeue budget down as its load rises. A lightly loaded server keeps claiming several scenes
	/// per pulse; a heavily loaded one claims one; a full one claims none and leaves the row for
	/// somebody with room. No server needs to see its peers, so there is no extra query on the pulse
	/// path and no way for a stale view of the cluster to make the decision wrong.
	/// </para>
	/// <para>
	/// <b>What it deliberately is not.</b> This is statistical balancing, not exact least-loaded
	/// placement. Exact placement needs every server to see every peer's live load — a per-pulse
	/// cluster query, and a decision made on data that is already out of date by the time the race
	/// is run. At a handful of scene servers hosting scenes that live for minutes to hours, the
	/// difference does not survive first contact with the stale-scene reaper, which retires idle
	/// scenes and lets placement re-level on its own.
	/// </para>
	/// <para>
	/// Pure and static so the whole decision is unit tested without a database, a pulse, or a
	/// cluster — the same shape as <c>ObserverStreamingPolicy</c>.
	/// </para>
	/// </remarks>
	public static class SceneServerPlacementPolicy
	{
		/// <summary>
		/// Scenes hosted at or below which a server keeps its full per-pulse dequeue budget.
		/// </summary>
		public static int SoftCapScenes { get; set; } = 4;

		/// <summary>
		/// Scenes hosted at or above which a server claims nothing and leaves rows for its peers.
		/// </summary>
		/// <remarks>
		/// A real capacity limit, not a balancing hint. Refusing here is correct: the row stays
		/// queued and the next server with room takes it. It cannot starve the cluster unless every
		/// server is genuinely full, which is a provisioning problem rather than a placement one —
		/// and the alternative (taking it anyway) turns that into an overloaded server instead of a
		/// visible queue.
		/// </remarks>
		public static int HardCapScenes { get; set; } = 12;

		/// <summary>
		/// Characters hosted at or below which a server keeps its full per-pulse dequeue budget.
		/// </summary>
		public static int SoftCapCharacters { get; set; } = 200;

		/// <summary>
		/// Characters hosted at or above which a server claims no new scenes.
		/// </summary>
		/// <remarks>
		/// Scene count alone is a poor proxy for load: one busy town and one empty dungeon are both
		/// "a scene". Population is what actually costs CPU and bandwidth, so both are measured and
		/// the more loaded of the two decides.
		/// </remarks>
		public static int HardCapCharacters { get; set; } = 600;

		/// <summary>
		/// Applies a named configuration value. Unknown keys are ignored.
		/// </summary>
		/// <remarks>
		/// Mirrors <c>ObserverStreamingPolicy.ApplySetting</c> so the scene server can push its
		/// configuration in without this type knowing what a configuration file is.
		/// </remarks>
		/// <param name="key">Configuration key.</param>
		/// <param name="value">Value to apply.</param>
		/// <returns>True when the key was recognised and applied.</returns>
		public static bool ApplySetting(string key, int value)
		{
			switch (key)
			{
				case "PlacementSoftCapScenes":
					SoftCapScenes = Mathf.Max(0, value);
					return true;
				case "PlacementHardCapScenes":
					HardCapScenes = Mathf.Max(1, value);
					return true;
				case "PlacementSoftCapCharacters":
					SoftCapCharacters = Mathf.Max(0, value);
					return true;
				case "PlacementHardCapCharacters":
					HardCapCharacters = Mathf.Max(1, value);
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// How many pending scenes this server should try to claim on this pulse.
		/// </summary>
		/// <param name="loadedScenes">Scenes this server currently hosts.</param>
		/// <param name="characterCount">Characters this server currently hosts.</param>
		/// <param name="maxScenesPerPulse">The configured ceiling, used when the server is idle.</param>
		/// <returns>A budget in <c>[0, maxScenesPerPulse]</c>.</returns>
		public static int ResolveDequeueBudget(int loadedScenes, int characterCount, int maxScenesPerPulse)
		{
			if (maxScenesPerPulse < 1)
			{
				return 0;
			}

			if (loadedScenes >= HardCapScenes || characterCount >= HardCapCharacters)
			{
				return 0;
			}

			/* The more loaded of the two measures decides. Taking the average would let a server
			 * with one enormously populated scene keep claiming more, because its scene count is
			 * low — which is the exact case this is meant to stop. */
			float pressure = Mathf.Max(
				ResolvePressure(loadedScenes, SoftCapScenes, HardCapScenes),
				ResolvePressure(characterCount, SoftCapCharacters, HardCapCharacters));

			if (pressure <= 0f)
			{
				return maxScenesPerPulse;
			}

			/* Linear taper from the full budget down to one. Never to zero: that is the hard cap's
			 * job, and a soft cap that could reach zero would be indistinguishable from being full
			 * while the server still had room. */
			int budget = Mathf.RoundToInt(Mathf.Lerp(maxScenesPerPulse, 1f, pressure));
			return Mathf.Clamp(budget, 1, maxScenesPerPulse);
		}

		/// <summary>
		/// Where <paramref name="value"/> sits between its soft and hard cap, as 0 to 1.
		/// </summary>
		/// <remarks>
		/// 0 at or below the soft cap, 1 at or above the hard cap. A hard cap that is not above the
		/// soft cap would divide by zero, so it answers "fully loaded" instead — a misconfiguration
		/// should throttle placement, never crash the pulse or silently disable the policy.
		/// </remarks>
		private static float ResolvePressure(int value, int softCap, int hardCap)
		{
			if (value <= softCap)
			{
				return 0f;
			}
			if (hardCap <= softCap)
			{
				return 1f;
			}
			return Mathf.Clamp01((value - softCap) / (float)(hardCap - softCap));
		}
	}
}
