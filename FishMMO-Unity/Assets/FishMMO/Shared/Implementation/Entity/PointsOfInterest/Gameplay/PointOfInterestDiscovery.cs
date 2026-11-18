using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks a point of interest's <see cref="Region"/> as discoverable: entering it the first time
	/// counts the site once for the character (<see cref="DiscoverSiteAction"/>).
	/// </summary>
	/// <remarks>
	/// <para><b>Identity is (scene, <see cref="SiteIndex"/>)</b>, the site's per-scene unlock index — the
	/// same stable index its waypoint and portal use, carried over a re-cut by nearest match. A site's
	/// discovery is one bit of the character's record under <see cref="RecordKey"/>, kept and persisted
	/// with the per-character portal activations (<see cref="PortalActivationStore"/>,
	/// <c>character_portals</c>): the same set-only bit pages, a different key, so nothing new is stored
	/// or loaded for it.</para>
	/// <para>Written by the point-of-interest generator's exploration feature onto the region it rides.</para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class PointOfInterestDiscovery : MonoBehaviour
	{
		/// <summary>Suffix that keeps discovery bits apart from a scene's portal bits.</summary>
		public const string KeySuffix = "#sites";

		[Tooltip("The site's per-scene unlock index: stable forever, the bit a character's discovery record stores.")]
		public int SiteIndex = -1;

		/// <summary>The record key a scene's site discoveries are kept under.</summary>
		public static string RecordKey(string sceneName) => string.IsNullOrEmpty(sceneName) ? null : sceneName + KeySuffix;

		/// <summary>Whether a per-character record key is a discovery key rather than a scene's portals.</summary>
		public static bool IsDiscoveryKey(string recordKey) => recordKey != null && recordKey.EndsWith(KeySuffix, System.StringComparison.Ordinal);
	}
}
