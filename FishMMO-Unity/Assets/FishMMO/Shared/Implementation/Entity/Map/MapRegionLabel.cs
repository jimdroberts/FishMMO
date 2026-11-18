using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks a named area of the scene. Harvested into the scene's
	/// <see cref="WorldMapDefinition"/> when the world scene details cache is rebuilt.
	/// </summary>
	/// <remarks>
	/// Authored as a component rather than as a row on the definition asset for the same reason
	/// spawn positions, teleporters and boundaries are: the value is a place, and a place is
	/// placed by dragging it onto the terrain and looking at it, not by typing three floats. The
	/// runtime never sees this component — only the baked
	/// <see cref="MapRegionLabelDetails"/> it produces.
	/// </remarks>
	public class MapRegionLabel : MonoBehaviour
	{
		/// <summary>
		/// Player-facing name of the region. Falls back to the GameObject name when empty.
		/// </summary>
		[Tooltip("Player-facing name of the region. Uses the GameObject name when empty.")]
		public string RegionName;

		/// <summary>Horizontal radius of the region in metres.</summary>
		[Tooltip("Horizontal radius of the region in metres.")]
		public float Radius = 50.0f;

		/// <summary>
		/// Zoom tier at which the label starts being drawn, lowest first. Continents at 0,
		/// provinces at 1, settlements at 2.
		/// </summary>
		[Tooltip("Zoom tier the label appears at. 0 = always, higher = only when zoomed in.")]
		public int DetailTier;

		/// <summary>Whether the name stays hidden until the player has explored the region.</summary>
		[Tooltip("Hide the name until the player has explored this region.")]
		public bool RequiresDiscovery;

		/// <summary>The name this region will be baked with.</summary>
		public string ResolvedName =>
			string.IsNullOrWhiteSpace(RegionName) ? gameObject.name : RegionName;

		/// <summary>
		/// Produces the serializable form baked into the map definition.
		/// </summary>
		/// <returns>The baked details for this region.</returns>
		public MapRegionLabelDetails ToDetails()
		{
			return new MapRegionLabelDetails()
			{
				Name = ResolvedName,
				Position = transform.position,
				Radius = Mathf.Max(0.0f, Radius),
				DetailTier = Mathf.Max(0, DetailTier),
				RequiresDiscovery = RequiresDiscovery,
			};
		}

#if UNITY_EDITOR
		/// <summary>
		/// Draws the region's footprint so it can be sized against the terrain it names.
		/// </summary>
		/// <remarks>
		/// A flat ring rather than a sphere. The containment test ignores height, so a sphere
		/// gizmo would show a volume the runtime does not use and would read as though a player
		/// on a hilltop had left the region.
		/// </remarks>
		private void OnDrawGizmos()
		{
			Gizmos.color = new Color(0.35f, 0.7f, 0.95f, 0.9f);

			const int Segments = 48;
			Vector3 center = transform.position;
			Vector3 previous = center + new Vector3(Radius, 0.0f, 0.0f);

			for (int i = 1; i <= Segments; ++i)
			{
				float angle = (i / (float)Segments) * Mathf.PI * 2.0f;
				Vector3 current = center + new Vector3(Mathf.Cos(angle) * Radius, 0.0f, Mathf.Sin(angle) * Radius);
				Gizmos.DrawLine(previous, current);
				previous = current;
			}

			UnityEditor.Handles.color = Gizmos.color;
			UnityEditor.Handles.Label(center, ResolvedName);
		}
#endif
	}
}
