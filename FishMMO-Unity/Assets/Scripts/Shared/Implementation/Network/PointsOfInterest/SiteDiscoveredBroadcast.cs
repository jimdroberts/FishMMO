using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → owner: the character discovered a point of interest for the first time.
	/// </summary>
	/// <remarks>
	/// <para>Sent by <see cref="DiscoverSiteAction"/> only when the discovery bit was newly set, so it
	/// arrives once per site per character — never on a re-entry. The client announces it (a
	/// "Discovered" toast and a system chat line), the way a waypoint discovery is announced.</para>
	/// <para>Discovery is decided on the server alone, so this is the only way the owner learns of it:
	/// the region's own name toast is a client-side enter event and says nothing about whether the
	/// visit counted.</para>
	/// </remarks>
	public struct SiteDiscoveredBroadcast : IBroadcast
	{
		/// <summary>The site's display name (its region's <see cref="Region.Name"/>).</summary>
		public string SiteName;
	}
}
