using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// A portal opened for everyone in the world: either for good, or until a moment.
	/// </summary>
	/// <remarks>
	/// <para>Keyed by (scene, portal index) and shared by every scene server on the database, so a
	/// portal opened on one server is open on the next that loads the scene. The index is the
	/// portal's authored, per-scene stable index (<c>PortalActivation.PortalIndex</c>).</para>
	/// <para><b>Merge, not replace.</b> <see cref="Permanent"/> ORs and <see cref="ActiveUntilUtc"/>
	/// takes the later of the stored and written moments, so the write is idempotent and two servers
	/// opening the same portal at once end with the longer opening. Nothing can close a portal
	/// through this row; an expired timed opening simply stops counting.</para>
	/// </remarks>
	public struct WorldPortalStateData
	{
		/// <summary>The scene the portal stands in.</summary>
		public readonly string SceneName;

		/// <summary>The portal's authored index within that scene.</summary>
		public readonly int PortalIndex;

		/// <summary>Open for good.</summary>
		public readonly bool Permanent;

		/// <summary>
		/// UTC moment a timed opening ends. The Unix epoch when the portal was never opened on a
		/// timer (a permanent opening carries the epoch unless it was also timed once).
		/// </summary>
		public readonly DateTime ActiveUntilUtc;

		public WorldPortalStateData(string sceneName, int portalIndex, bool permanent, DateTime activeUntilUtc)
		{
			SceneName = sceneName;
			PortalIndex = portalIndex;
			Permanent = permanent;
			ActiveUntilUtc = activeUntilUtc;
		}
	}
}
